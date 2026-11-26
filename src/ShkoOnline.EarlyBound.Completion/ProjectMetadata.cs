using Microsoft.CodeAnalysis;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Model;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>The Early Bound metadata of a project, as the IDE sees it.</summary>
internal sealed class ProjectMetadata
{
    private const string ItemMetadataKey = "build_metadata.AdditionalFiles.EarlyBound";
    private const string NamespacePropertyKey = "build_property.EarlyBoundNamespace";
    private const string RootNamespacePropertyKey = "build_property.RootNamespace";
    private const int MaxCached = 8;

    private static readonly object Gate = new();
    private static readonly LinkedList<ProjectMetadata> Recent = new();

    private readonly MetadataFile[] _files;
    private readonly string? _rootNamespace;
    private readonly string? _explicitNamespace;
    private readonly Lazy<EarlyBoundModel> _model;

    private ProjectMetadata(MetadataFile[] files, string? rootNamespace, string? explicitNamespace)
    {
        _files = files;
        _rootNamespace = rootNamespace;
        _explicitNamespace = explicitNamespace;
        Set = MetadataSet.Create(files);
        _model = new Lazy<EarlyBoundModel>(() => EarlyBoundModelBuilder.Build(Set, _rootNamespace, _explicitNamespace));
    }

    public MetadataSet Set { get; }

    /// <summary>The metadata with generated names resolved; built on first use.</summary>
    public EarlyBoundModel Model => _model.Value;

    /// <summary>Loads the metadata files of a project. Returns null when it has none.</summary>
    public static ProjectMetadata? Get(Project project, CancellationToken cancellationToken)
    {
        var analyzerOptions = project.AnalyzerOptions;
        var provider = analyzerOptions.AnalyzerConfigOptionsProvider;
        List<MetadataFile>? files = null;

        foreach (var additional in analyzerOptions.AdditionalFiles)
        {
            var path = additional.Path;
            if (!MetadataFileCache.HasYamlExtension(path))
            {
                continue;
            }

            var isMetadata = provider.GetOptions(additional).TryGetValue(ItemMetadataKey, out var flag)
                ? string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)
                : MetadataFileCache.IsWellKnownFileName(path);
            if (!isMetadata)
            {
                continue;
            }

            var text = additional.GetText(cancellationToken);
            if (text == null)
            {
                continue;
            }

            (files ??= new List<MetadataFile>()).Add(MetadataFileCache.GetOrRead(text, text.ToString, path));
        }

        if (files == null)
        {
            return null;
        }

        provider.GlobalOptions.TryGetValue(NamespacePropertyKey, out var explicitNamespace);
        provider.GlobalOptions.TryGetValue(RootNamespacePropertyKey, out var rootNamespace);
        explicitNamespace = string.IsNullOrWhiteSpace(explicitNamespace) ? null : explicitNamespace!.Trim();
        rootNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? project.DefaultNamespace : rootNamespace!.Trim();

        var array = files.ToArray();
        lock (Gate)
        {
            for (var node = Recent.First; node != null; node = node.Next)
            {
                if (node.Value.Matches(array, rootNamespace, explicitNamespace))
                {
                    if (node != Recent.First)
                    {
                        Recent.Remove(node);
                        Recent.AddFirst(node);
                    }

                    return node.Value;
                }
            }
        }

        var created = new ProjectMetadata(array, rootNamespace, explicitNamespace);
        if (created.Set.IsEmpty)
        {
            return null;
        }

        lock (Gate)
        {
            Recent.AddFirst(created);
            while (Recent.Count > MaxCached)
            {
                Recent.RemoveLast();
            }
        }

        return created;
    }

    private bool Matches(MetadataFile[] files, string? rootNamespace, string? explicitNamespace)
    {
        if (files.Length != _files.Length
            || !string.Equals(rootNamespace, _rootNamespace, StringComparison.Ordinal)
            || !string.Equals(explicitNamespace, _explicitNamespace, StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = 0; i < files.Length; i++)
        {
            if (!ReferenceEquals(files[i], _files[i]))
            {
                return false;
            }
        }

        return true;
    }
}
