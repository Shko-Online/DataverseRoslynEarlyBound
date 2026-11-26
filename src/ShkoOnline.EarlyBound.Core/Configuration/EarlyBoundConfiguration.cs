namespace ShkoOnline.EarlyBound.Configuration;

/// <summary>The contents of <c>earlybound.yaml</c>.</summary>
public sealed class EarlyBoundConfiguration
{
    /// <summary>URL of the Dataverse environment metadata is pulled from.</summary>
    public string? Environment { get; set; }

    public PullSettings Pull { get; set; } = new();

    public GeneratorSettings Generator { get; set; } = new();
}

/// <summary>What <c>earlybound pull</c> downloads and where it writes it. Read from the <c>pull:</c> section.</summary>
public sealed class PullSettings
{
    public const string LayoutPerEntity = "perEntity";
    public const string LayoutSingleFile = "singleFile";
    public const string RelationshipsIncluded = "included";
    public const string RelationshipsAll = "all";
    public const string RelationshipsNone = "none";

    /// <summary>Table logical names to pull. <c>*</c> matches any run of characters.</summary>
    public List<string> Entities { get; set; } = new();

    /// <summary>Unique names of solutions whose tables are pulled as well.</summary>
    public List<string> Solutions { get; set; } = new();

    /// <summary>Table logical names (or patterns) to leave out.</summary>
    public List<string> Exclude { get; set; } = new();

    /// <summary>Language of labels and descriptions. Defaults to the environment's base language.</summary>
    public int? LanguageCode { get; set; }

    /// <summary>
    /// <c>included</c>: only relationships between pulled tables; <c>all</c>: every relationship;
    /// <c>none</c>: no relationships.
    /// </summary>
    public string Relationships { get; set; } = RelationshipsIncluded;

    /// <summary>Folder the metadata files are written to, relative to <c>earlybound.yaml</c>.</summary>
    public string Output { get; set; } = "Metadata";

    /// <summary><c>perEntity</c>: one file per table and per global choice; <c>singleFile</c>: one file.</summary>
    public string Layout { get; set; } = LayoutPerEntity;
}
