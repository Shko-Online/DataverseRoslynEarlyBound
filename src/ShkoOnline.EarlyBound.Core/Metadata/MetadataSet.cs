using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Metadata;

/// <summary>The metadata of a project: every YAML file merged into one view.</summary>
public sealed class MetadataSet
{
    private readonly Dictionary<string, EntityDefinition> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OptionSetDefinition> _globalOptionSets = new(StringComparer.Ordinal);

    private MetadataSet()
    {
    }

    /// <summary>Tables ordered by logical name.</summary>
    public IReadOnlyList<EntityDefinition> Entities { get; private set; } = Array.Empty<EntityDefinition>();

    public IReadOnlyDictionary<string, OptionSetDefinition> GlobalOptionSets => _globalOptionSets;

    public GeneratorSettings Settings { get; private set; } = new();

    /// <summary>Problems found in the files and while merging them.</summary>
    public List<MetadataDiagnostic> Diagnostics { get; } = new();

    public bool IsEmpty => _entities.Count == 0 && _globalOptionSets.Count == 0;

    public EntityDefinition? FindEntity(string logicalName) =>
        _entities.TryGetValue(logicalName, out var entity) ? entity : null;

    public OptionSetDefinition? FindGlobalOptionSet(string name) =>
        _globalOptionSets.TryGetValue(name, out var optionSet) ? optionSet : null;

    /// <summary>The choice a column uses, whether its own or a global one.</summary>
    public OptionSetDefinition? ResolveOptionSet(AttributeDefinition attribute)
    {
        if (attribute.OptionSet != null)
        {
            return attribute.OptionSet;
        }

        return attribute.GlobalOptionSet != null ? FindGlobalOptionSet(attribute.GlobalOptionSet) : null;
    }

    public static MetadataSet Create(IEnumerable<MetadataFile> files)
    {
        var set = new MetadataSet();
        var settingsFound = false;

        // Path order makes the result independent of the order the host hands files over in.
        foreach (var file in files.OrderBy(f => f.Path ?? string.Empty, StringComparer.Ordinal))
        {
            set.Diagnostics.AddRange(file.Diagnostics);

            if (file.HasGeneratorSettings && file.Configuration != null)
            {
                if (settingsFound)
                {
                    set.Diagnostics.Add(new MetadataDiagnostic(
                        DiagnosticIds.DuplicateDefinition,
                        MetadataSeverity.Warning,
                        "More than one file has a 'generator' section; only the first one is used.",
                        file.Path));
                }
                else
                {
                    set.Settings = file.Configuration.Generator;
                    settingsFound = true;
                }
            }

            foreach (var entity in file.Entities)
            {
                if (set._entities.TryGetValue(entity.LogicalName, out var existing))
                {
                    set.Diagnostics.Add(new MetadataDiagnostic(
                        DiagnosticIds.DuplicateDefinition,
                        MetadataSeverity.Warning,
                        $"Table '{entity.LogicalName}' is already defined in '{existing.SourcePath}'; this definition is ignored.",
                        file.Path,
                        entity.Position));
                    continue;
                }

                set._entities.Add(entity.LogicalName, entity);
            }

            foreach (var optionSet in file.OptionSets)
            {
                if (set._globalOptionSets.TryGetValue(optionSet.Name, out var existing))
                {
                    set.Diagnostics.Add(new MetadataDiagnostic(
                        DiagnosticIds.DuplicateDefinition,
                        MetadataSeverity.Warning,
                        $"Choice '{optionSet.Name}' is already defined in '{existing.SourcePath}'; this definition is ignored.",
                        file.Path,
                        optionSet.Position));
                    continue;
                }

                set._globalOptionSets.Add(optionSet.Name, optionSet);
            }
        }

        set.Entities = set._entities.Values.OrderBy(e => e.LogicalName, StringComparer.Ordinal).ToList();
        return set;
    }
}
