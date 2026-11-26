using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Yaml;

/// <summary>Everything read from one YAML file.</summary>
public sealed class MetadataFile
{
    public MetadataFile(string? path)
    {
        Path = path;
    }

    public string? Path { get; }

    public List<EntityDefinition> Entities { get; } = new();

    /// <summary>Global choices defined by the file.</summary>
    public List<OptionSetDefinition> OptionSets { get; } = new();

    /// <summary>Set when the file has an <c>environment</c>, <c>pull</c> or <c>generator</c> key.</summary>
    public EarlyBoundConfiguration? Configuration { get; set; }

    /// <summary>True when the file has a <c>generator:</c> section.</summary>
    public bool HasGeneratorSettings { get; set; }

    public List<MetadataDiagnostic> Diagnostics { get; } = new();

    public bool IsEmpty => Entities.Count == 0 && OptionSets.Count == 0 && Configuration == null;
}
