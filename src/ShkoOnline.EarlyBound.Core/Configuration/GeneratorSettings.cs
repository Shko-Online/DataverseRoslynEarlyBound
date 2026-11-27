namespace ShkoOnline.EarlyBound.Configuration;

/// <summary>
/// How C# is generated from the metadata. Read from the <c>generator:</c> section of
/// <c>earlybound.yaml</c>. Names and defaults follow the Early Bound Generator's settings so
/// that an existing configuration can be carried over key by key.
/// </summary>
public sealed class GeneratorSettings
{
    /// <summary>Namespace of the generated types. Defaults to the project's root namespace.</summary>
    public string? Namespace { get; set; }

    /// <summary>Name of the generated OrganizationServiceContext. Empty to generate none.</summary>
    public string? ServiceContextName { get; set; } = "DataverseContext";

    public bool GenerateTypesAsInternal { get; set; }

    /// <summary>
    /// Generate members only for the columns and relationships the project's own code uses,
    /// instead of for everything in the metadata.
    /// </summary>
    public bool GenerateOnlyUsedAttributes { get; set; } = true;

    /// <summary>
    /// Members generated whether they are used or not, as <c>table.column</c> or
    /// <c>table.relationship</c> with <c>*</c> as a wildcard. A table name alone means all of its members.
    /// </summary>
    public List<string> AlwaysGenerate { get; set; } = new();

    // ---- Naming -------------------------------------------------------------------------

    public bool CamelCaseClassNames { get; set; } = true;

    public bool CamelCaseMemberNames { get; set; } = true;

    public bool CamelCaseOptionSetNames { get; set; }

    /// <summary>Extra words the PascalCaser recognises, typically publisher prefixes.</summary>
    public List<string> CamelCaseCustomWords { get; set; } = new();

    /// <summary>Tokens that are always written exactly as listed.</summary>
    public List<string> TokenCapitalizationOverrides { get; set; } = new(DefaultTokenCapitalizationOverrides);

    public bool UseLogicalNames { get; set; }

    /// <summary>Format of a column-owned choice's enum name: {0} is the class name, {1} the property name.</summary>
    public string LocalOptionSetFormat { get; set; } = "{0}_{1}";

    /// <summary>Name the state enum <c>AccountState</c> instead of <c>Account_StateCode</c>.</summary>
    public bool UseCrmSvcUtilStateEnumNamingConvention { get; set; }

    public bool UseDisplayNameForBpfName { get; set; } = true;

    public string InvalidCSharpNamePrefix { get; set; } = "_";

    public bool AdjustCasingForEnumOptions { get; set; } = true;

    /// <summary>Text removed or replaced in a choice label before a member name is derived from it.</summary>
    public List<KeyValuePair<string, string>> LabelTextReplacement { get; set; } = new()
    {
        new("$", string.Empty),
        new("(", string.Empty),
    };

    /// <summary>Fixes applied to enum member names, matched case-insensitively.</summary>
    public List<KeyValuePair<string, string>> OptionNameOverrides { get; set; } = new(DefaultOptionNameOverrides);

    /// <summary>Class name per table logical name.</summary>
    public Dictionary<string, string> EntityClassNameOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per table: property names to use verbatim for the columns they match case-insensitively.</summary>
    public Dictionary<string, List<string>> EntityAttributeSpecifiedNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Enum name per generated enum name (matched case-insensitively).</summary>
    public Dictionary<string, string> OptionSetNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- Shape of the generated code ------------------------------------------------------

    public bool GenerateAttributeNameConsts { get; set; } = true;

    public string AttributeConstsClassName { get; set; } = "Fields";

    public bool GenerateAnonymousTypeConstructor { get; set; } = true;

    public bool GenerateConstructorsSansLogicalName { get; set; } = true;

    public bool GenerateEntityRelationships { get; set; } = true;

    public bool EmitEntityEtc { get; set; }

    /// <summary>Generate the read-only <c>...Name</c> companions of lookup and choice columns.</summary>
    public bool EmitVirtualAttributes { get; set; } = true;

    public bool GenerateOptionSetMetadataAttribute { get; set; } = true;

    public bool AddOptionSetMetadataAttribute { get; set; } = true;

    /// <summary>Type choice properties as OptionSetValue instead of the generated enum.</summary>
    public bool ReplaceEnumPropertiesWithOptionSet { get; set; }

    public bool MakeReadonlyFieldsEditable { get; set; }

    public bool MakeAllFieldsEditable { get; set; }

    public bool AddDebuggerNonUserCode { get; set; } = true;

    public bool ObsoleteDeprecated { get; set; } = true;

    /// <summary>Display-name patterns (<c>*</c> wildcard) that mark a column as obsolete.</summary>
    public List<string> ObsoleteTokens { get; set; } = new() { "*(Deprecated)*" };

    public bool SuppressGeneratedCodeAttribute { get; set; } = true;

    /// <summary>Emit <c>[assembly: ProxyTypesAssembly]</c> unless the project already declares it.</summary>
    public bool GenerateProxyTypesAssemblyAttribute { get; set; } = true;

    public static readonly IReadOnlyList<string> DefaultTokenCapitalizationOverrides = new[]
    {
        "AccessTeam", "ActiveState", "AssignedTo", "BusinessAs", "CardUci", "DefaultOnCase", "EmailAnd",
        "EmailSend", "EmailSender", "ExchangeRate", "FeatureSet", "FedEx", "ForAn", "Geronimo", "IsMsTeams",
        "IsPaiEnabled", "IsSopIntegration", "MsDynCe_", "MsDynMkt_", "MsDyUsd", "O365Admin",
        "OcSkillIdentMlModel", "OnHold", "OrderId", "OwnerOnAssign", "PauseStates", "PredictiveAddress",
        "PartiesOnEmail", "ParticipatesIn", "SentOn", "SettingsAndSummary", "SlaId", "SlaKpi", "SyncOptIn",
        "Timeout", "TradeShow", "UserPuid", "VoiceMail",
    };

    public static readonly IReadOnlyList<KeyValuePair<string, string>> DefaultOptionNameOverrides = new KeyValuePair<string, string>[]
    {
        new("1st", "1st"), new("2nd", "2nd"), new("3rd", "3rd"), new("4th", "4th"), new("5th", "5th"),
        new("6th", "6th"), new("7th", "7th"), new("8th", "8th"), new("9th", "9th"), new("0th", "0th"),
        new("conversationindex", "ConversationIndex"), new("customcorrelation", "CustomCorrelation"),
        new("fedex", "FedEx"), new("groupme", "GroupMe"), new("linkedin", "LinkedIn"),
        new("nontransient", "NonTransient"), new("notset", "NotSet"),
        new("reminderexpired", "ReminderExpired"), new("reminderinvalid", "ReminderInvalid"),
        new("reminderset", "ReminderSet"), new("smartmatching", "SmartMatching"),
        new("trackingtoken", "TrackingToken"), new("xheader", "XHeader"),
    };
}
