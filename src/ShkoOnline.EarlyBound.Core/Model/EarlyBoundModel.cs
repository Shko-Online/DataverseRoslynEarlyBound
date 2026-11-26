using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Model;

/// <summary>
/// The metadata with every C# name resolved: what will be generated, and under which names.
/// Shared by the generator (to emit code) and the autocomplete provider (to refer to it).
/// </summary>
public sealed class EarlyBoundModel
{
    private readonly Dictionary<string, EntityModel> _entities = new(StringComparer.Ordinal);

    public EarlyBoundModel(MetadataSet metadata, GeneratorSettings settings, string @namespace)
    {
        Metadata = metadata;
        Settings = settings;
        Namespace = @namespace;
    }

    public MetadataSet Metadata { get; }

    public GeneratorSettings Settings { get; }

    public string Namespace { get; }

    /// <summary>Tables ordered by logical name.</summary>
    public List<EntityModel> Entities { get; } = new();

    /// <summary>Every enum to generate, ordered by name.</summary>
    public List<OptionSetModel> OptionSets { get; } = new();

    public List<MetadataDiagnostic> Diagnostics { get; } = new();

    public EntityModel? FindEntity(string logicalName) =>
        _entities.TryGetValue(logicalName, out var entity) ? entity : null;

    internal void Add(EntityModel entity)
    {
        Entities.Add(entity);
        _entities[entity.Definition.LogicalName] = entity;
    }
}

public sealed class EntityModel
{
    private readonly Dictionary<string, AttributeModel> _attributes = new(StringComparer.Ordinal);

    public EntityModel(EntityDefinition definition, string className)
    {
        Definition = definition;
        ClassName = className;
    }

    public EntityDefinition Definition { get; }

    public string ClassName { get; internal set; }

    /// <summary>Name of the IQueryable property on the service context.</summary>
    public string SetName => ClassName + "Set";

    /// <summary>Columns that get a property, ordered by logical name.</summary>
    public List<AttributeModel> Attributes { get; } = new();

    public List<RelationshipModel> Relationships { get; } = new();

    public AttributeModel? FindAttribute(string logicalName) =>
        _attributes.TryGetValue(logicalName, out var attribute) ? attribute : null;

    internal void Add(AttributeModel attribute)
    {
        Attributes.Add(attribute);
        _attributes[attribute.Definition.LogicalName] = attribute;
    }
}

public enum AttributeKind
{
    /// <summary>A plain value read and written through the attribute collection.</summary>
    Value,

    /// <summary>The table's primary key, kept in sync with <c>Entity.Id</c>.</summary>
    PrimaryId,

    /// <summary>A single choice typed as an enum.</summary>
    Enum,

    /// <summary>A multi-select choice typed as a sequence of enum values.</summary>
    MultiEnum,

    /// <summary>A party list or calendar rules: a collection of related rows.</summary>
    EntityCollection,

    /// <summary>The read-only display text of a lookup or choice, read from FormattedValues.</summary>
    FormattedValue,
}

public sealed class AttributeModel
{
    public AttributeModel(AttributeDefinition definition, string propertyName)
    {
        Definition = definition;
        PropertyName = propertyName;
    }

    public AttributeDefinition Definition { get; }

    public string PropertyName { get; internal set; }

    public AttributeKind Kind { get; internal set; } = AttributeKind.Value;

    /// <summary>C# type of the property, fully qualified where it is not a generated type.</summary>
    public string TypeName { get; internal set; } = "object";

    /// <summary>The enum the property is typed as, when it has one.</summary>
    public OptionSetModel? OptionSet { get; internal set; }

    /// <summary>For <see cref="AttributeKind.EntityCollection"/>: the class of the rows.</summary>
    public EntityModel? ElementEntity { get; internal set; }

    public bool HasSetter { get; internal set; } = true;

    public bool IsObsolete { get; internal set; }
}

public sealed class OptionSetModel
{
    public OptionSetModel(OptionSetDefinition definition, string enumName)
    {
        Definition = definition;
        EnumName = enumName;
    }

    public OptionSetDefinition Definition { get; }

    public string EnumName { get; internal set; }

    public List<OptionModel> Options { get; } = new();

    public OptionModel? FindOption(int value)
    {
        foreach (var option in Options)
        {
            if (option.Definition.Value == value)
            {
                return option;
            }
        }

        return null;
    }
}

public sealed class OptionModel
{
    public OptionModel(OptionDefinition definition, string memberName, int displayIndex)
    {
        Definition = definition;
        MemberName = memberName;
        DisplayIndex = displayIndex;
    }

    public OptionDefinition Definition { get; }

    public string MemberName { get; }

    public int DisplayIndex { get; }
}

public enum RelationshipKind
{
    ManyToOne,
    OneToMany,
    ManyToMany,
}

/// <summary>Which side of a self-referencing relationship a property represents.</summary>
public enum RelationshipRole
{
    None,
    Referencing,
    Referenced,
}

public sealed class RelationshipModel
{
    public RelationshipModel(RelationshipDefinition definition, RelationshipKind kind, RelationshipRole role, EntityModel related, string propertyName)
    {
        Definition = definition;
        Kind = kind;
        Role = role;
        Related = related;
        PropertyName = propertyName;
    }

    public RelationshipDefinition Definition { get; }

    public RelationshipKind Kind { get; }

    public RelationshipRole Role { get; }

    public EntityModel Related { get; }

    public string PropertyName { get; internal set; }

    public bool HasSetter { get; internal set; } = true;
}
