namespace ShkoOnline.EarlyBound.Metadata;

/// <summary>Where in a YAML file a definition was read from. Lines and columns are zero-based.</summary>
public readonly struct SourcePosition : IEquatable<SourcePosition>
{
    public SourcePosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int Line { get; }

    public int Column { get; }

    public bool Equals(SourcePosition other) => Line == other.Line && Column == other.Column;

    public override bool Equals(object? obj) => obj is SourcePosition other && Equals(other);

    public override int GetHashCode() => unchecked((Line * 397) ^ Column);
}

/// <summary>A Dataverse table as stored in the YAML metadata files.</summary>
public sealed class EntityDefinition
{
    public string LogicalName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? DisplayCollectionName { get; set; }

    public string? Description { get; set; }

    public string? EntitySetName { get; set; }

    public string? LogicalCollectionName { get; set; }

    public string? PrimaryIdAttribute { get; set; }

    public string? PrimaryNameAttribute { get; set; }

    public string? PrimaryImageAttribute { get; set; }

    public int? ObjectTypeCode { get; set; }

    /// <summary>UserOwned, TeamOwned, BusinessOwned, OrganizationOwned, BusinessParented or None.</summary>
    public string? OwnershipType { get; set; }

    public bool IsCustomEntity { get; set; }

    public bool IsActivity { get; set; }

    public bool IsIntersect { get; set; }

    public List<AttributeDefinition> Attributes { get; set; } = new();

    /// <summary>Relationships where this table holds the lookup column (N:1).</summary>
    public List<RelationshipDefinition> ManyToOne { get; set; } = new();

    /// <summary>Relationships where another table holds a lookup to this one (1:N).</summary>
    public List<RelationshipDefinition> OneToMany { get; set; } = new();

    public List<RelationshipDefinition> ManyToMany { get; set; } = new();

    /// <summary>File the definition was read from; not written to YAML.</summary>
    public string? SourcePath { get; set; }

    public SourcePosition Position { get; set; }

    public AttributeDefinition? FindAttribute(string logicalName)
    {
        foreach (var attribute in Attributes)
        {
            if (string.Equals(attribute.LogicalName, logicalName, StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }
}

/// <summary>A column of a Dataverse table.</summary>
public sealed class AttributeDefinition
{
    public string LogicalName { get; set; } = string.Empty;

    public string SchemaName { get; set; } = string.Empty;

    /// <summary>One of the <see cref="AttributeTypes"/> names.</summary>
    public string Type { get; set; } = AttributeTypes.String;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>None, Recommended, ApplicationRequired or SystemRequired.</summary>
    public string? RequiredLevel { get; set; }

    public bool IsValidForCreate { get; set; } = true;

    public bool IsValidForUpdate { get; set; } = true;

    public bool IsValidForRead { get; set; } = true;

    /// <summary>Logical name of the column this one is a computed companion of, e.g. <c>primarycontactidname</c> → <c>primarycontactid</c>.</summary>
    public string? AttributeOf { get; set; }

    /// <summary>True when the platform marks the column as deprecated.</summary>
    public bool IsDeprecated { get; set; }

    public int? MaxLength { get; set; }

    public string? Format { get; set; }

    public string? MinValue { get; set; }

    public string? MaxValue { get; set; }

    public int? Precision { get; set; }

    /// <summary>UserLocal, DateOnly or TimeZoneIndependent.</summary>
    public string? DateTimeBehavior { get; set; }

    /// <summary>Tables a lookup column can point to.</summary>
    public List<string> Targets { get; set; } = new();

    /// <summary>Name of the global choice the column uses, when it uses one.</summary>
    public string? GlobalOptionSet { get; set; }

    /// <summary>The column's own choice, when it does not use a global one.</summary>
    public OptionSetDefinition? OptionSet { get; set; }

    public SourcePosition Position { get; set; }

    public bool HasChoices => GlobalOptionSet != null || OptionSet != null;
}

/// <summary>A choice (option set): either global, or owned by a single column.</summary>
public sealed class OptionSetDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool IsGlobal { get; set; }

    /// <summary>Picklist, State, Status or Boolean.</summary>
    public string? OptionSetType { get; set; }

    public List<OptionDefinition> Options { get; set; } = new();

    public string? SourcePath { get; set; }

    public SourcePosition Position { get; set; }
}

public sealed class OptionDefinition
{
    public int Value { get; set; }

    public string? Label { get; set; }

    public string? Description { get; set; }

    public string? Color { get; set; }

    public string? ExternalValue { get; set; }

    /// <summary>State options only: the language-independent name, e.g. <c>Active</c>.</summary>
    public string? InvariantName { get; set; }

    /// <summary>Status options only: the state value this status belongs to.</summary>
    public int? State { get; set; }

    /// <summary>State options only: the status assigned when a row enters this state.</summary>
    public int? DefaultStatus { get; set; }
}

public sealed class RelationshipDefinition
{
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>
    /// The table on the other side: the referenced table for N:1, the referencing table for 1:N,
    /// the other table for N:N.
    /// </summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>The lookup column: on this table for N:1, on <see cref="Entity"/> for 1:N. Unused for N:N.</summary>
    public string? Attribute { get; set; }

    /// <summary>N:N only: the intersect table.</summary>
    public string? IntersectEntity { get; set; }
}

/// <summary>Column type names used in the YAML files. They match Dataverse's AttributeTypeName without the "Type" suffix.</summary>
public static class AttributeTypes
{
    public const string BigInt = "BigInt";
    public const string Boolean = "Boolean";
    public const string CalendarRules = "CalendarRules";
    public const string Customer = "Customer";
    public const string DateTime = "DateTime";
    public const string Decimal = "Decimal";
    public const string Double = "Double";
    public const string EntityName = "EntityName";
    public const string File = "File";
    public const string Image = "Image";
    public const string Integer = "Integer";
    public const string Lookup = "Lookup";
    public const string ManagedProperty = "ManagedProperty";
    public const string Memo = "Memo";
    public const string Money = "Money";
    public const string MultiSelectPicklist = "MultiSelectPicklist";
    public const string Owner = "Owner";
    public const string PartyList = "PartyList";
    public const string Picklist = "Picklist";
    public const string State = "State";
    public const string Status = "Status";
    public const string String = "String";
    public const string Uniqueidentifier = "Uniqueidentifier";
    public const string Virtual = "Virtual";

    public static bool IsLookup(string type) =>
        type == Lookup || type == Customer || type == Owner;

    public static bool IsChoice(string type) =>
        type == Picklist || type == State || type == Status || type == MultiSelectPicklist;
}
