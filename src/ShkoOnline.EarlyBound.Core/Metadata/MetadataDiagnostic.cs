namespace ShkoOnline.EarlyBound.Metadata;

public enum MetadataSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>A problem found while reading metadata or building the model, located in a YAML file where possible.</summary>
public sealed class MetadataDiagnostic
{
    public MetadataDiagnostic(string id, MetadataSeverity severity, string message, string? path = null, SourcePosition position = default)
    {
        Id = id;
        Severity = severity;
        Message = message;
        Path = path;
        Position = position;
    }

    public string Id { get; }

    public MetadataSeverity Severity { get; }

    public string Message { get; }

    public string? Path { get; }

    public SourcePosition Position { get; }

    public override string ToString() =>
        Path == null
            ? $"{Id}: {Message}"
            : $"{Path}({Position.Line + 1},{Position.Column + 1}): {Id}: {Message}";
}

/// <summary>Identifiers of every diagnostic the product reports.</summary>
public static class DiagnosticIds
{
    /// <summary>The project does not reference Microsoft.Xrm.Sdk.</summary>
    public const string SdkNotReferenced = "SOEB001";

    /// <summary>The package is installed but no metadata file was found.</summary>
    public const string NoMetadata = "SOEB002";

    /// <summary>A file is not valid YAML.</summary>
    public const string InvalidYaml = "SOEB100";

    /// <summary>A value is missing or has the wrong shape.</summary>
    public const string InvalidValue = "SOEB101";

    /// <summary>A table or global choice is defined more than once.</summary>
    public const string DuplicateDefinition = "SOEB102";

    /// <summary>A column refers to a global choice that no file defines.</summary>
    public const string UnknownOptionSet = "SOEB103";

    /// <summary>Two generated names collided and one was renamed.</summary>
    public const string NameCollision = "SOEB104";

    /// <summary>A setting key is not recognised.</summary>
    public const string UnknownSetting = "SOEB105";

    /// <summary>The generator failed unexpectedly.</summary>
    public const string InternalError = "SOEB900";
}
