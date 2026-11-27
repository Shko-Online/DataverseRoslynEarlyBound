using System.Text.RegularExpressions;

namespace ShkoOnline.EarlyBound.Model;

/// <summary>A table class this project generates, with the members it gets.</summary>
public sealed class EntityPlan
{
    public EntityPlan(EntityModel entity, List<AttributeModel> attributes, List<RelationshipModel> relationships)
    {
        Entity = entity;
        Attributes = attributes;
        Relationships = relationships;
    }

    public EntityModel Entity { get; }

    public List<AttributeModel> Attributes { get; }

    public List<RelationshipModel> Relationships { get; }
}

/// <summary>
/// A class derived from a table class that a referenced assembly provides, holding the members
/// that class does not have: <c>AccountTestEntity : Account</c>.
/// </summary>
public sealed class TestEntityPlan
{
    public TestEntityPlan(
        EntityModel entity,
        ReferencedEntity @base,
        string className,
        List<AttributeModel> attributes,
        List<RelationshipModel> relationships,
        List<AttributeModel> fields)
    {
        Entity = entity;
        Base = @base;
        ClassName = className;
        Attributes = attributes;
        Relationships = relationships;
        Fields = fields;
    }

    public EntityModel Entity { get; }

    /// <summary>The class it derives from, as the referenced assembly has it.</summary>
    public ReferencedEntity Base { get; }

    public string ClassName { get; }

    /// <summary>Columns that get a property here because the base class has none for them.</summary>
    public List<AttributeModel> Attributes { get; }

    public List<RelationshipModel> Relationships { get; }

    /// <summary>Columns that get a name constant here because the base class has none for them.</summary>
    public List<AttributeModel> Fields { get; }
}

/// <summary>
/// What a project generates from its model: which classes, with which members.
/// </summary>
/// <remarks>
/// <para>
/// With only used members wanted, "used" is decided by name. A member is generated when its C#
/// name appears as an identifier in the project's source, wherever that is. A generator cannot
/// ask what <c>account.Name</c> binds to before <c>Name</c> exists, so the name is all there is to
/// go by; the result can only err on the side of generating a little more than needed.
/// </para>
/// <para>
/// A table class that a referenced assembly already provides is not generated again. Members
/// can only be added to a class in the assembly that declares it, so what such a class lacks
/// goes into a derived class, named after it with <see cref="TestEntitySuffix"/>, which is
/// generated when the project's code names it.
/// </para>
/// </remarks>
public sealed class GenerationPlan
{
    /// <summary>Appended to a class name to name the class derived from it: <c>AccountTestEntity</c>.</summary>
    public const string TestEntitySuffix = "TestEntity";

    /// <summary>Names of generated helpers whose use is tracked like that of a member.</summary>
    private static readonly string[] OptionSetMetadataNames =
    {
        EarlyBoundModelBuilder.OptionSetMetadataAttributeName,
        "OptionSetMetadata", // the attribute, written without its suffix
        EarlyBoundModelBuilder.OptionSetExtensionName,
        "GetMetadata",
    };

    private GenerationPlan()
    {
    }

    /// <summary>The table classes generated in this project.</summary>
    public List<EntityPlan> Entities { get; } = new();

    /// <summary>The classes derived from table classes of referenced assemblies.</summary>
    public List<TestEntityPlan> TestEntities { get; } = new();

    /// <summary>The enums generated in this project.</summary>
    public List<OptionSetModel> OptionSets { get; } = new();

    /// <summary>True when a generated property reads or writes a choice as an enum.</summary>
    public bool NeedsOptionSetEnumHelper { get; private set; }

    /// <summary>True when OptionSetMetadataAttribute and its extension are generated here.</summary>
    public bool IncludesOptionSetMetadata { get; private set; }

    /// <summary>True unless a referenced assembly already provides the service context.</summary>
    public bool IncludesServiceContext { get; private set; }

    /// <param name="usedNames">
    /// Identifiers found in the project's code, limited to <see cref="TrackedNames"/>.
    /// Null to generate every member, whether it is used or not.
    /// </param>
    /// <param name="referenced">What the project's references provide already.</param>
    public static GenerationPlan Create(EarlyBoundModel model, ICollection<string>? usedNames, ReferencedTypes? referenced = null)
    {
        referenced ??= ReferencedTypes.None;
        var settings = model.Settings;
        var everything = usedNames == null;
        var always = everything ? null : CreateAlwaysMatcher(settings.AlwaysGenerate);
        var plan = new GenerationPlan();
        var emitted = new List<AttributeModel>();
        var modelTypeNames = new HashSet<string>(
            model.Entities.Select(e => Unescape(e.ClassName)).Concat(model.OptionSets.Select(o => Unescape(o.EnumName))),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entity in model.Entities)
        {
            var table = entity.Definition.LogicalName;
            bool Wanted(string memberName, string metadataName) =>
                everything || usedNames!.Contains(Unescape(memberName)) || always!(table, metadataName);

            if (!referenced.TryGetEntity(table, out var @base))
            {
                // The primary key is always there: Entity.Id is implemented on top of it.
                var attributes = entity.Attributes
                    .Where(a => a.Kind == AttributeKind.PrimaryId || Wanted(a.PropertyName, a.Definition.LogicalName))
                    .ToList();
                var relationships = entity.Relationships
                    .Where(r => Wanted(r.PropertyName, r.Definition.SchemaName))
                    .ToList();
                plan.Entities.Add(new EntityPlan(entity, attributes, relationships));
                emitted.AddRange(attributes);
                continue;
            }

            // The class comes from a reference. What it lacks can only go into a class derived from it.
            var className = TestEntityName(entity);
            var missing = entity.Attributes
                .Where(a => a.Kind != AttributeKind.PrimaryId
                    && Wanted(a.PropertyName, a.Definition.LogicalName)
                    && !@base.MemberNames.Contains(Unescape(a.PropertyName)))
                .ToList();
            var missingRelationships = entity.Relationships
                .Where(r => Wanted(r.PropertyName, r.Definition.SchemaName) && !@base.MemberNames.Contains(Unescape(r.PropertyName)))
                .ToList();
            var missingFields = settings.GenerateAttributeNameConsts
                ? entity.Attributes
                    .Where(a => Wanted(a.PropertyName, a.Definition.LogicalName) && !@base.FieldNames.Contains(Unescape(a.PropertyName)))
                    .ToList()
                : new List<AttributeModel>();

            // Generated when the code names it. When everything is generated no code is looked at,
            // so it is generated whenever there is something for it to hold.
            var generate = everything
                ? missing.Count + missingRelationships.Count + missingFields.Count > 0
                : usedNames!.Contains(className);
            if (generate && !modelTypeNames.Contains(className))
            {
                plan.TestEntities.Add(new TestEntityPlan(entity, @base, className, missing, missingRelationships, missingFields));
                emitted.AddRange(missing);
            }
        }

        // An enum is needed by the properties typed as it, and can be used on its own:
        // (int)Account_StatusCode.Active in a query, for example.
        var optionSets = new HashSet<OptionSetModel>(emitted.Where(IsEnumProperty).Select(a => a.OptionSet!));
        foreach (var optionSet in model.OptionSets)
        {
            if (everything || usedNames!.Contains(Unescape(optionSet.EnumName)))
            {
                optionSets.Add(optionSet);
            }
        }

        plan.OptionSets.AddRange(model.OptionSets.Where(o => optionSets.Contains(o) && !referenced.Provides(o.EnumName)));
        plan.NeedsOptionSetEnumHelper = emitted.Any(IsEnumProperty);
        plan.IncludesOptionSetMetadata = settings.GenerateOptionSetMetadataAttribute
            && !referenced.Provides(EarlyBoundModelBuilder.OptionSetMetadataAttributeName)
            && (plan.OptionSets.Count > 0 || (!everything && OptionSetMetadataNames.Any(usedNames!.Contains)));
        plan.IncludesServiceContext = !string.IsNullOrWhiteSpace(settings.ServiceContextName)
            && !referenced.Provides(settings.ServiceContextName!.Trim());
        return plan;
    }

    /// <summary>
    /// The names whose appearance in code decides what is generated: every property, relationship
    /// and enum name of the model, and the name of every class that can be derived from a
    /// referenced one. Anything else in the code is of no interest.
    /// </summary>
    public static HashSet<string> TrackedNames(EarlyBoundModel model)
    {
        var names = new HashSet<string>(OptionSetMetadataNames, StringComparer.Ordinal);
        foreach (var entity in model.Entities)
        {
            names.Add(TestEntityName(entity));
            foreach (var attribute in entity.Attributes)
            {
                names.Add(Unescape(attribute.PropertyName));
            }

            foreach (var relationship in entity.Relationships)
            {
                names.Add(Unescape(relationship.PropertyName));
            }
        }

        foreach (var optionSet in model.OptionSets)
        {
            names.Add(Unescape(optionSet.EnumName));
        }

        return names;
    }

    /// <summary>The name of the class derived from a table class that a reference provides.</summary>
    public static string TestEntityName(EntityModel entity) => Unescape(entity.ClassName) + TestEntitySuffix;

    private static bool IsEnumProperty(AttributeModel attribute) =>
        attribute.OptionSet != null && (attribute.Kind == AttributeKind.Enum || attribute.Kind == AttributeKind.MultiEnum);

    /// <summary>An identifier as it appears in a token: <c>@event</c> is the name <c>event</c>.</summary>
    private static string Unescape(string identifier) => identifier.TrimStart('@');

    private static Func<string, string, bool> CreateAlwaysMatcher(IEnumerable<string> patterns)
    {
        var matchers = new List<(Regex Table, Regex Member)>();
        foreach (var raw in patterns)
        {
            var pattern = raw?.Trim();
            if (string.IsNullOrEmpty(pattern))
            {
                continue;
            }

            // "table.member"; a table on its own stands for all of its members.
            var dot = pattern!.IndexOf('.');
            var table = dot < 0 ? pattern : pattern.Substring(0, dot);
            var member = dot < 0 ? "*" : pattern.Substring(dot + 1);
            matchers.Add((Wildcard(table), Wildcard(member)));
        }

        if (matchers.Count == 0)
        {
            return static (_, _) => false;
        }

        return (table, member) =>
        {
            foreach (var matcher in matchers)
            {
                if (matcher.Table.IsMatch(table) && matcher.Member.IsMatch(member))
                {
                    return true;
                }
            }

            return false;
        };
    }

    private static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
