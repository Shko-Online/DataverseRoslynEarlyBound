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
/// The pipeline is built so that typing in a C# file costs nothing: YAML is parsed once per file
/// version, and code is only regenerated when a metadata file, the namespace or the referenced
/// SDK changes.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class EarlyBoundGenerator : IIncrementalGenerator
{
    /// <summary>Item metadata the package's MSBuild targets put on the files this generator owns.</summary>
    internal const string ItemMetadataKey = "build_metadata.AdditionalFiles.EarlyBound";

    internal const string NamespacePropertyKey = "build_property.EarlyBoundNamespace";
    internal const string RootNamespacePropertyKey = "build_property.RootNamespace";

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

        var rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => ReadNamespace(options.GlobalOptions));

        var project = context.CompilationProvider
            .Select(static (compilation, _) => ProjectFacts.From(compilation));

        context.RegisterSourceOutput(
            files.Combine(rootNamespace).Combine(project),
            static (output, input) => Execute(output, input.Left.Left!, input.Left.Right, input.Right));
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

    private static (string? Explicit, string? Root) ReadNamespace(AnalyzerConfigOptions options)
    {
        options.TryGetValue(NamespacePropertyKey, out var explicitNamespace);
        options.TryGetValue(RootNamespacePropertyKey, out var rootNamespace);
        return (
            string.IsNullOrWhiteSpace(explicitNamespace) ? null : explicitNamespace!.Trim(),
            string.IsNullOrWhiteSpace(rootNamespace) ? null : rootNamespace!.Trim());
    }

    private static void Execute(SourceProductionContext context, ImmutableArray<MetadataFile?> files, (string? Explicit, string? Root) namespaces, ProjectFacts project)
    {
        try
        {
            var set = MetadataSet.Create(files.Where(f => f != null)!);
            foreach (var diagnostic in set.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostics.ToDiagnostic(diagnostic));
            }

            if (set.IsEmpty)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoMetadata, Location.None));
                return;
            }

            if (!project.HasSdk)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SdkNotReferenced, Location.None));
                return;
            }

            // The EarlyBoundNamespace MSBuild property wins over earlybound.yaml, which wins over RootNamespace.
            var model = EarlyBoundModelBuilder.Build(set, namespaces.Root, namespaces.Explicit);
            foreach (var diagnostic in model.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostics.ToDiagnostic(diagnostic));
            }

            var options = new EmitOptions
            {
                HasOrganizationServiceContext = project.HasServiceContext,
                EmitProxyTypesAssemblyAttribute = !project.HasProxyTypesAssemblyAttribute,
                ToolVersion = ToolVersion,
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
