using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using ShkoOnline.EarlyBound.Generator;

namespace ShkoOnline.EarlyBound.Tests.Support;

internal sealed record GeneratorRun(
    Compilation InputCompilation,
    Compilation Compilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    GeneratorDriverRunResult Result,
    GeneratorDriver Driver)
{
    public IEnumerable<Diagnostic> CompilationErrors =>
        Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>The compiled assembly: what another project would reference.</summary>
    public byte[] EmitImage()
    {
        using var stream = new MemoryStream();
        var result = Compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return stream.ToArray();
    }

    public string Source(string hintName) =>
        Result.GeneratedTrees.Single(t => Path.GetFileName(t.FilePath) == hintName).ToString();

    public IEnumerable<string> HintNames => Result.GeneratedTrees.Select(t => Path.GetFileName(t.FilePath));
}

/// <summary>Runs the real generator over in-memory files and compiles the result against the real SDK.</summary>
internal static class GeneratorHarness
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> AllReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToImmutableArray());

    /// <summary>The framework plus the Dataverse SDK the test project references.</summary>
    public static ImmutableArray<MetadataReference> References(bool includeSdk = true) =>
        includeSdk
            ? AllReferences.Value
            : AllReferences.Value.Where(r => !IsSdk(r.Display!)).ToImmutableArray();

    private static bool IsSdk(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("Microsoft.Xrm.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.Crm.", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Microsoft.PowerPlatform.", StringComparison.OrdinalIgnoreCase);
    }

    public static CSharpCompilation CreateCompilation(
        string source,
        LanguageVersion languageVersion = LanguageVersion.Latest,
        bool includeSdk = true,
        IEnumerable<(string Path, string Text)>? moreSources = null,
        string assemblyName = "Consumer",
        IEnumerable<MetadataReference>? moreReferences = null) =>
        CSharpCompilation.Create(
            assemblyName,
            new[] { ("Consumer.cs", source) }
                .Concat(moreSources ?? Array.Empty<(string, string)>())
                .Select(file => CSharpSyntaxTree.ParseText(file.Item2, new CSharpParseOptions(languageVersion), path: file.Item1)),
            References(includeSdk).AddRange(moreReferences ?? Array.Empty<MetadataReference>()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));

    public static GeneratorRun Run(
        IEnumerable<(string Path, string Text)> files,
        string userSource = "",
        LanguageVersion languageVersion = LanguageVersion.Latest,
        bool includeSdk = true,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        bool tagFiles = true,
        IEnumerable<(string Path, string Text)>? moreSources = null,
        string assemblyName = "Consumer",
        IEnumerable<MetadataReference>? moreReferences = null)
    {
        var compilation = CreateCompilation(userSource, languageVersion, includeSdk, moreSources, assemblyName, moreReferences);
        var texts = files.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToImmutableArray();
        var options = new TestOptionsProvider(buildProperties, tagFiles);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new EarlyBoundGenerator().AsSourceGenerator() },
            texts,
            new CSharpParseOptions(languageVersion),
            options,
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new GeneratorRun(compilation, output, diagnostics, driver.GetRunResult(), driver);
    }
}

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = SourceText.From(text);
    }

    public override string Path { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}

/// <summary>Stands in for the options MSBuild hands the compiler: build properties and item metadata.</summary>
internal sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly bool _tagFiles;

    public TestOptionsProvider(IReadOnlyDictionary<string, string>? buildProperties = null, bool tagFiles = true)
    {
        _tagFiles = tagFiles;
        GlobalOptions = new DictionaryOptions(buildProperties ?? new Dictionary<string, string>());
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        _tagFiles
            ? new DictionaryOptions(new Dictionary<string, string> { ["build_metadata.AdditionalFiles.EarlyBound"] = "true" })
            : DictionaryOptions.Empty;

    private sealed class DictionaryOptions : AnalyzerConfigOptions
    {
        public static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());

        private readonly IReadOnlyDictionary<string, string> _values;

        public DictionaryOptions(IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
