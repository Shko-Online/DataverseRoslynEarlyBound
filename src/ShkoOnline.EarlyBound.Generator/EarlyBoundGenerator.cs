using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using ShkoOnline.EarlyBound.CodeGeneration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Model;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Generator;

/// <summary>
/// Generates early-bound Dataverse classes from the YAML metadata files of a project.
/// </summary>
/// <remarks>
/// The pipeline is built so that typing in a C# file costs next to nothing:
/// <list type="bullet">
/// <item>YAML is parsed once per file version, and the model is built once per set of files.</item>
/// <item>Only the file that changed is scanned for the member names it uses.</item>
/// <item>Code is regenerated only when a metadata file, a setting, the referenced SDK, the
/// generated classes a reference provides or the set of used member names changes; not when
/// code merely changes.</item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EarlyBoundGenerator : IIncrementalGenerator
{
    /// <summary>Item metadata the package's MSBuild targets put on the files this generator owns.</summary>
    internal const string ItemMetadataKey = "build_metadata.AdditionalFiles.EarlyBound";

    internal const string NamespacePropertyKey = "build_property.EarlyBoundNamespace";
    internal const string RootNamespacePropertyKey = "build_property.RootNamespace";
    internal const string OnlyUsedPropertyKey = "build_property.EarlyBoundGenerateOnlyUsedAttributes";

    // Names of pipeline steps, for tests that check what is and is not recomputed.
    internal const string ModelStep = "Model";
    internal const string UsedNamesPerFileStep = "UsedNamesPerFile";
    internal const string UsedNamesStep = "UsedNames";
    internal const string ReferencedTypesStep = "ReferencedTypes";

    private static readonly string ToolVersion =
        typeof(EarlyBoundGenerator).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Which additional files are ours. The answer is a plain value, so everything after it
        // is skipped when only the options object was replaced.
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) => (Text: pair.Left, IsMetadata: IsMetadataFile(pair.Left, pair.Right)))
            .Where(static item => item.IsMetadata)
            .Select(static (item, cancellationToken) => Parse(item.Text, cancellationToken))
            .Where(static file => file != null)
            .Collect();

        var buildOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => BuildOptions.Read(options.GlobalOptions));

        // The names of everything that could be generated. Rebuilt only when metadata or settings change.
        var model = files.Combine(buildOptions)
            .Select(static (pair, cancellationToken) => GeneratorModel.Create(pair.Left, pair.Right, cancellationToken))
            .WithTrackingName(ModelStep);

        // Which of those names each C# file mentions. Only the file being edited is looked at again,
        // and its answer rarely changes.
        var usedNames = context.CompilationProvider
            .SelectMany(static (compilation, _) => compilation.SyntaxTrees)
            .Combine(model)
            .Select(static (pair, cancellationToken) => UsageScanner.Scan(pair.Left, pair.Right.TrackedNames, cancellationToken))
            .WithTrackingName(UsedNamesPerFileStep)
            .Collect()
            .Select(static (perTree, _) => UsageScanner.Merge(perTree))
            .WithTrackingName(UsedNamesStep);

        var project = context.CompilationProvider
            .Select(static (compilation, _) => ProjectFacts.From(compilation));

        // The generated classes a referenced assembly already has, as a test project gets them
        // from the plug-in project. Compared by content, so an unchanged reference changes nothing.
        var referenced = context.CompilationProvider
            .Combine(model)
            .Select(static (pair, cancellationToken) => ReferencedTypesBuilder.From(pair.Left, pair.Right.Model, cancellationToken))
            .WithTrackingName(ReferencedTypesStep);

        context.RegisterSourceOutput(
            model.Combine(usedNames).Combine(project).Combine(referenced),
            static (output, input) => Execute(output, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    internal static bool IsMetadataFile(AdditionalText text, AnalyzerConfigOptionsProvider options)
    {
        var path = text.Path;
        if (!MetadataFileCache.HasYamlExtension(path))
        {
            return false;
        }

        if (options.GetOptions(text).TryGetValue(ItemMetadataKey, out var flag))
        {
            return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        }

        // Files added by hand without the item metadata are recognised by name.
        return MetadataFileCache.IsWellKnownFileName(path);
    }

    private static MetadataFile? Parse(AdditionalText text, CancellationToken cancellationToken)
    {
        var source = text.GetText(cancellationToken);
        if (source == null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return MetadataFileCache.GetOrRead(source, source.ToString, text.Path);
    }

    private static void Execute(SourceProductionContext context, GeneratorModel generatorModel, EquatableArray<string> usedNames, ProjectFacts project, ReferencedTypes referenced)
    {
        try
        {
            if (generatorModel.Failure != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InternalError, Location.None, generatorModel.Failure));
                return;
            }

            foreach (var diagnostic in generatorModel.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostics.ToDiagnostic(diagnostic));
            }

            var model = generatorModel.Model;
            if (model == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoMetadata, Location.None));
                return;
            }

            if (!project.HasSdk)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SdkNotReferenced, Location.None));
                return;
            }

            var options = new EmitOptions
            {
                HasOrganizationServiceContext = project.HasServiceContext,
                EmitProxyTypesAssemblyAttribute = !project.HasProxyTypesAssemblyAttribute,
                ToolVersion = ToolVersion,
                UsedNames = generatorModel.OnlyUsed ? new HashSet<string>(usedNames, StringComparer.Ordinal) : null,
                Referenced = referenced,
            };

            foreach (var source in CSharpEmitter.Emit(model, options))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                context.AddSource(source.HintName, SourceText.From(source.Text, Encoding.UTF8));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InternalError, Location.None, ex.GetType().Name + ": " + ex.Message));
        }
    }

    /// <summary>The MSBuild properties the generator reads; a value, so unchanged properties skip everything.</summary>
    internal readonly struct BuildOptions : IEquatable<BuildOptions>
    {
        public BuildOptions(string? explicitNamespace, string? rootNamespace, bool? onlyUsed)
        {
            ExplicitNamespace = explicitNamespace;
            RootNamespace = rootNamespace;
            OnlyUsed = onlyUsed;
        }

        /// <summary>EarlyBoundNamespace: wins over earlybound.yaml.</summary>
        public string? ExplicitNamespace { get; }

        /// <summary>RootNamespace: used when nothing else names a namespace.</summary>
        public string? RootNamespace { get; }

        /// <summary>EarlyBoundGenerateOnlyUsedAttributes: wins over earlybound.yaml when set.</summary>
        public bool? OnlyUsed { get; }

        public static BuildOptions Read(AnalyzerConfigOptions options)
        {
            options.TryGetValue(NamespacePropertyKey, out var explicitNamespace);
            options.TryGetValue(RootNamespacePropertyKey, out var rootNamespace);
            options.TryGetValue(OnlyUsedPropertyKey, out var onlyUsed);
            return new BuildOptions(
                string.IsNullOrWhiteSpace(explicitNamespace) ? null : explicitNamespace!.Trim(),
                string.IsNullOrWhiteSpace(rootNamespace) ? null : rootNamespace!.Trim(),
                bool.TryParse(onlyUsed?.Trim(), out var parsed) ? parsed : null);
        }

        public bool Equals(BuildOptions other) =>
            string.Equals(ExplicitNamespace, other.ExplicitNamespace, StringComparison.Ordinal)
            && string.Equals(RootNamespace, other.RootNamespace, StringComparison.Ordinal)
            && OnlyUsed == other.OnlyUsed;

        public override bool Equals(object? obj) => obj is BuildOptions other && Equals(other);

        public override int GetHashCode() =>
            unchecked(((ExplicitNamespace?.GetHashCode() ?? 0) * 397) ^ (RootNamespace?.GetHashCode() ?? 0) ^ (OnlyUsed.HasValue ? (OnlyUsed.Value ? 1 : 2) : 0));
    }

    /// <summary>
    /// The metadata of a project with its names resolved. One instance lives for as long as the
    /// metadata files and build options stay the same, which is what lets later steps be skipped.
    /// </summary>
    internal sealed class GeneratorModel
    {
        private static readonly HashSet<string> NoNames = new();

        private GeneratorModel(EarlyBoundModel? model, IReadOnlyList<MetadataDiagnostic> diagnostics, bool onlyUsed, HashSet<string> trackedNames, string? failure)
        {
            Model = model;
            Diagnostics = diagnostics;
            OnlyUsed = onlyUsed;
            TrackedNames = trackedNames;
            Failure = failure;
        }

        /// <summary>Null when the project has no metadata.</summary>
        public EarlyBoundModel? Model { get; }

        public IReadOnlyList<MetadataDiagnostic> Diagnostics { get; }

        /// <summary>True when only used members are generated.</summary>
        public bool OnlyUsed { get; }

        /// <summary>The names to look for in code. Empty when everything is generated anyway.</summary>
        public HashSet<string> TrackedNames { get; }

        /// <summary>Set when building the model failed unexpectedly.</summary>
        public string? Failure { get; }

        public static GeneratorModel Create(ImmutableArray<MetadataFile?> files, BuildOptions options, CancellationToken cancellationToken)
        {
            try
            {
                var set = MetadataSet.Create(files.Where(f => f != null)!);
                if (set.IsEmpty)
                {
                    return new GeneratorModel(null, set.Diagnostics, false, NoNames, null);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // The MSBuild properties win over earlybound.yaml, which wins over the project's root namespace.
                var model = EarlyBoundModelBuilder.Build(set, options.RootNamespace, options.ExplicitNamespace);
                var onlyUsed = options.OnlyUsed ?? set.Settings.GenerateOnlyUsedAttributes;
                return new GeneratorModel(
                    model,
                    set.Diagnostics.Concat(model.Diagnostics).ToList(),
                    onlyUsed,
                    onlyUsed ? GenerationPlan.TrackedNames(model) : NoNames,
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new GeneratorModel(null, Array.Empty<MetadataDiagnostic>(), false, NoNames, ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>What the generator needs to know about the project; a value, so unchanged facts skip regeneration.</summary>
    internal readonly struct ProjectFacts : IEquatable<ProjectFacts>
    {
        public ProjectFacts(bool hasSdk, bool hasServiceContext, bool hasProxyTypesAssemblyAttribute)
        {
            HasSdk = hasSdk;
            HasServiceContext = hasServiceContext;
            HasProxyTypesAssemblyAttribute = hasProxyTypesAssemblyAttribute;
        }

        public bool HasSdk { get; }

        public bool HasServiceContext { get; }

        public bool HasProxyTypesAssemblyAttribute { get; }

        public static ProjectFacts From(Compilation compilation)
        {
            var hasSdk = compilation.GetTypeByMetadataName("Microsoft.Xrm.Sdk.Entity") != null;
            var hasContext = compilation.GetTypeByMetadataName("Microsoft.Xrm.Sdk.Client.OrganizationServiceContext") != null;
            var hasAttribute = false;
            if (hasSdk)
            {
                foreach (var attribute in compilation.Assembly.GetAttributes())
                {
                    if (attribute.AttributeClass?.Name == "ProxyTypesAssemblyAttribute"
                        && attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == "Microsoft.Xrm.Sdk.Client")
                    {
                        hasAttribute = true;
                        break;
                    }
                }
            }

            return new ProjectFacts(hasSdk, hasContext, hasAttribute);
        }

        public bool Equals(ProjectFacts other) =>
            HasSdk == other.HasSdk
            && HasServiceContext == other.HasServiceContext
            && HasProxyTypesAssemblyAttribute == other.HasProxyTypesAssemblyAttribute;

        public override bool Equals(object? obj) => obj is ProjectFacts other && Equals(other);

        public override int GetHashCode() =>
            (HasSdk ? 1 : 0) | (HasServiceContext ? 2 : 0) | (HasProxyTypesAssemblyAttribute ? 4 : 0);
    }
}
