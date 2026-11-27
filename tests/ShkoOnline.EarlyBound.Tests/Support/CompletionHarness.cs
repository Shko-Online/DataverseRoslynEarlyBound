using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using ShkoOnline.EarlyBound.Completion;
using ShkoOnline.EarlyBound.Generator;

namespace ShkoOnline.EarlyBound.Tests.Support;

internal sealed record CompletionResult(Document Document, CompletionService Service, ImmutableArray<CompletionItem> Items, TextSpan Span, string Source)
{
    public IEnumerable<string> Names => Items.Select(i => i.DisplayText);

    public CompletionItem Item(string displayText) => Items.Single(i => i.DisplayText == displayText);

    /// <summary>The source after committing an item, the way an IDE would apply it.</summary>
    public async Task<string> CommitAsync(string displayText)
    {
        var change = await Service.GetChangeAsync(Document, Item(displayText));
        return SourceText.From(Source).WithChanges(change.TextChange).ToString();
    }

    public async Task<string> DescribeAsync(string displayText)
    {
        var description = await Service.GetDescriptionAsync(Document, Item(displayText));
        return description?.Text ?? string.Empty;
    }
}

/// <summary>
/// Asks Roslyn's completion service for completions in a workspace project that references the
/// provider and the generator as analyzers, which is how Visual Studio and VS Code load them.
/// </summary>
internal static class CompletionHarness
{
    private const string ProjectDirectory = "/consumer";

    private static readonly Lazy<MefHostServices> Host = new(() => MefHostServices.Create(MefHostServices.DefaultAssemblies));

    /// <param name="source">C# with <c>$$</c> marking the caret.</param>
    /// <param name="typed">The character whose typing triggers completion; null for an explicit invoke (Ctrl+Space).</param>
    public static async Task<CompletionResult> CompleteAsync(
        string source,
        char? typed = null,
        IEnumerable<(string Path, string Text)>? files = null,
        bool withGenerator = true,
        MetadataReference? reference = null)
    {
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        Assert.True(position >= 0, "The source needs a $$ caret marker.");
        source = source.Remove(position, 2);

        // Loading the project and warming the provider up is the slow part, so cases share it
        // and only swap the text of one document, like an editor does while you type.
        var project = files != null
            ? await CreateProjectAsync(files, withGenerator, reference)
            : reference != null
                ? await ProjectsWithReference.GetValue(reference, static r => CreateProjectAsync(null, true, r))
                : await SharedProjects.GetOrAdd(withGenerator, static generator => CreateProjectAsync(null, generator, null));

        var document = project.Solution.WithDocumentText(project.DocumentId, SourceText.From(source)).GetDocument(project.DocumentId)!;
        var trigger = typed.HasValue ? CompletionTrigger.CreateInsertionTrigger(typed.Value) : CompletionTrigger.Invoke;
        var list = await project.Service.GetCompletionsAsync(document, position, trigger);
        var items = list == null
            ? ImmutableArray<CompletionItem>.Empty
            : list.ItemsList.Where(IsOurs).ToImmutableArray();
        return new CompletionResult(document, project.Service, items, list?.Span ?? default, source);
    }

    private sealed record LoadedProject(AdhocWorkspace Workspace, Solution Solution, DocumentId DocumentId, CompletionService Service);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<bool, Task<LoadedProject>> SharedProjects = new();

    // A project per referenced assembly: the test project of a given plug-in assembly.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MetadataReference, Task<LoadedProject>> ProjectsWithReference = new();

    private static async Task<LoadedProject> CreateProjectAsync(IEnumerable<(string Path, string Text)>? files, bool withGenerator, MetadataReference? reference)
    {
        // Kept alive for the lifetime of the test run when shared.
        var workspace = new AdhocWorkspace(Host.Value);
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Consumer",
                "Consumer",
                LanguageNames.CSharp,
                filePath: ProjectDirectory + "/Consumer.csproj",
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
                metadataReferences: reference == null ? GeneratorHarness.References() : GeneratorHarness.References().Add(reference)));

        var loader = new InProcessAnalyzerLoader();
        solution = solution.AddAnalyzerReference(projectId, new AnalyzerFileReference(typeof(EarlyBoundCompletionProvider).Assembly.Location, loader));
        if (withGenerator)
        {
            solution = solution.AddAnalyzerReference(projectId, new AnalyzerFileReference(typeof(EarlyBoundGenerator).Assembly.Location, loader));
        }

        // The same global config MSBuild writes: build properties plus the item metadata of each additional file.
        var config = new System.Text.StringBuilder("is_global = true\nbuild_property.RootNamespace = Consumer\n");
        foreach (var (path, text) in files ?? Fixtures.MetadataFilesWith((ProjectDirectory + "/earlybound.yaml", Fixtures.Configuration)))
        {
            var normalized = path.Replace('\\', '/');
            solution = solution.AddAdditionalDocument(DocumentId.CreateNewId(projectId), Path.GetFileName(path), SourceText.From(text), filePath: normalized);
            config.Append('[').Append(normalized).Append("]\nbuild_metadata.AdditionalFiles.EarlyBound = true\n");
        }

        solution = solution.AddAnalyzerConfigDocument(
            DocumentId.CreateNewId(projectId), "Consumer.GeneratedMSBuildEditorConfig.editorconfig", SourceText.From(config.ToString()),
            filePath: ProjectDirectory + "/obj/Consumer.GeneratedMSBuildEditorConfig.editorconfig");

        var documentId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(documentId, "Code.cs", SourceText.From(string.Empty), filePath: ProjectDirectory + "/Code.cs");

        // Roslyn loads completion providers that come from analyzer references in the background:
        // the first request queues the load and gets nothing from them. An IDE session gets past
        // that on its own; here a probe request is repeated until the provider answers.
        var probeId = DocumentId.CreateNewId(projectId);
        solution = solution.AddDocument(probeId, "Probe.cs", SourceText.From(Probe), filePath: ProjectDirectory + "/Probe.cs");
        var probe = solution.GetDocument(probeId)!;
        var service = CompletionService.GetService(probe)!;

        var probePosition = Probe.IndexOf("\"\"", StringComparison.Ordinal) + 1;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var warmUp = await service.GetCompletionsAsync(probe, probePosition, CompletionTrigger.Invoke);
            if (warmUp != null && warmUp.ItemsList.Any(IsOurs))
            {
                break;
            }

            Assert.True(DateTime.UtcNow < deadline, "The completion provider was not loaded from the analyzer reference.");
            await Task.Delay(25);
        }

        return new LoadedProject(workspace, solution, documentId, service);
    }

    private const string Probe = "class Probe { object M() => new Microsoft.Xrm.Sdk.Entity(\"\"); }";

    private static bool IsOurs(CompletionItem item) => item.Properties.ContainsKey(CompletionItems.DescriptionKey);

    private sealed class InProcessAnalyzerLoader : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }
}
