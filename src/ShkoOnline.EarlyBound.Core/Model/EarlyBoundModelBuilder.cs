using System.Text.RegularExpressions;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Naming;

namespace ShkoOnline.EarlyBound.Model;

/// <summary>Decides what is generated from a <see cref="MetadataSet"/> and names every piece of it.</summary>
public static class EarlyBoundModelBuilder
{
    public const string DefaultNamespace = "DataverseModel";

    /// <summary>Names of the helper types generated next to the tables and choices.</summary>
    public const string OptionSetEnumHelperName = "EntityOptionSetEnum";
    public const string OptionSetMetadataAttributeName = "OptionSetMetadataAttribute";
    public const string OptionSetExtensionName = "OptionSetExtension";

    private static readonly HashSet<string> AuditColumns = new(StringComparer.Ordinal)
    {
        "createdby", "createdon", "modifiedby", "modifiedon", "owningbusinessunit", "owningteam", "owninguser",
    };

    /// <param name="rootNamespace">The project's root namespace, used when the settings name none.</param>
    /// <param name="namespaceOverride">A namespace that wins over the one in the settings.</param>
    public static EarlyBoundModel Build(MetadataSet metadata, string? rootNamespace = null, string? namespaceOverride = null)
    {
        var settings = metadata.Settings;
        var @namespace = FirstNonEmpty(namespaceOverride, settings.Namespace, rootNamespace) ?? DefaultNamespace;
        var model = new EarlyBoundModel(metadata, settings, @namespace);
        var naming = new NamingService(settings);
        var obsoleteMatcher = CreateObsoleteMatcher(settings);

        // Type names are compared case-insensitively: each type becomes a file, and file
        // systems on Windows and macOS do not tell "Account" from "account".
        var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            OptionSetEnumHelperName, OptionSetMetadataAttributeName, OptionSetExtensionName,
        };
        if (!string.IsNullOrWhiteSpace(settings.ServiceContextName))
        {
            typeNames.Add(settings.ServiceContextName!.Trim());
        }

        foreach (var definition in metadata.Entities)
        {
            var className = MakeUnique(naming.GetNameForEntity(definition), typeNames, model, definition, "class");
            model.Add(new EntityModel(definition, className));
        }

        var classNames = new HashSet<string>(model.Entities.Select(e => e.ClassName), StringComparer.Ordinal);
        var globalEnums = new Dictionary<string, OptionSetModel>(StringComparer.Ordinal);
        var memberNamesByEntity = new Dictionary<EntityModel, HashSet<string>>();

        foreach (var entity in model.Entities)
        {
            var memberNames = ReservedMemberNames(entity, settings);
            memberNamesByEntity[entity] = memberNames;

            foreach (var definition in entity.Definition.Attributes.OrderBy(a => a.LogicalName, StringComparer.Ordinal))
            {
                if (entity.FindAttribute(definition.LogicalName) != null || !ShouldGenerate(definition, entity.Definition, settings))
                {
                    continue;
                }

                var propertyName = naming.GetNameForAttribute(entity.Definition, definition);
                propertyName = MakeUniqueMember(propertyName, memberNames, model, entity, definition.LogicalName);
                var attribute = new AttributeModel(definition, propertyName)
                {
                    IsObsolete = settings.ObsoleteDeprecated
                        && (definition.IsDeprecated || (definition.DisplayName != null && obsoleteMatcher(definition.DisplayName))),
                };

                ResolveType(attribute, entity, model, naming, classNames, typeNames, globalEnums);
                attribute.HasSetter = HasSetter(attribute, settings);
                entity.Add(attribute);
            }
        }

        if (settings.GenerateEntityRelationships)
        {
            foreach (var entity in model.Entities)
            {
                AddRelationships(entity, model, naming, memberNamesByEntity[entity]);
            }
        }

        model.OptionSets.Sort((x, y) => string.CompareOrdinal(x.EnumName, y.EnumName));
        return model;
    }

    private static HashSet<string> ReservedMemberNames(EntityModel entity, GeneratorSettings settings) =>
        new(StringComparer.Ordinal)
        {
            entity.ClassName, settings.AttributeConstsClassName, "EntityLogicalName", "EntitySchemaName",
            "EntityLogicalCollectionName", "EntitySetName", "EntityTypeCode", "PrimaryIdAttribute",
            "PrimaryNameAttribute", "Id",
        };

    private static bool ShouldGenerate(AttributeDefinition attribute, EntityDefinition entity, GeneratorSettings settings)
    {
        if (!attribute.IsValidForCreate && !attribute.IsValidForRead && !attribute.IsValidForUpdate)
        {
            return false;
        }

        if (string.IsNullOrEmpty(attribute.AttributeOf))
        {
            return true;
        }

        // Companion columns are computed by the platform; only a few are worth a property.
        if (attribute.Type == AttributeTypes.Image
            || attribute.LogicalName.EndsWith("_url", StringComparison.OrdinalIgnoreCase)
            || attribute.LogicalName.EndsWith("_timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return settings.EmitVirtualAttributes && IsFormattedValueCompanion(attribute, entity);
    }

    /// <summary>True for <c>primarycontactidname</c>-style columns: the display text of a lookup or choice.</summary>
    internal static bool IsFormattedValueCompanion(AttributeDefinition attribute, EntityDefinition entity)
    {
        if (string.IsNullOrEmpty(attribute.AttributeOf)
            || !string.Equals(attribute.LogicalName, attribute.AttributeOf + "name", StringComparison.Ordinal))
        {
            return false;
        }

        var parent = entity.FindAttribute(attribute.AttributeOf!);
        return parent != null
            && (AttributeTypes.IsLookup(parent.Type)
                || parent.Type == AttributeTypes.Picklist
                || parent.Type == AttributeTypes.State
                || parent.Type == AttributeTypes.Status
                || parent.Type == AttributeTypes.MultiSelectPicklist
                || parent.Type == AttributeTypes.Boolean);
    }

    private static void ResolveType(
        AttributeModel attribute,
        EntityModel entity,
        EarlyBoundModel model,
        NamingService naming,
        HashSet<string> classNames,
        HashSet<string> typeNames,
        Dictionary<string, OptionSetModel> globalEnums)
    {
        var definition = attribute.Definition;
        if (!string.IsNullOrEmpty(definition.AttributeOf) && IsFormattedValueCompanion(definition, entity.Definition))
        {
            attribute.Kind = AttributeKind.FormattedValue;
            attribute.TypeName = "string";
            return;
        }

        switch (definition.Type)
        {
            case AttributeTypes.Uniqueidentifier:
                attribute.TypeName = "System.Nullable<System.Guid>";
                if (string.Equals(definition.LogicalName, entity.Definition.PrimaryIdAttribute, StringComparison.Ordinal))
                {
                    attribute.Kind = AttributeKind.PrimaryId;
                }

                break;
            case AttributeTypes.String:
            case AttributeTypes.Memo:
            case AttributeTypes.EntityName:
                attribute.TypeName = "string";
                break;
            case AttributeTypes.Integer:
                attribute.TypeName = "System.Nullable<int>";
                break;
            case AttributeTypes.BigInt:
                attribute.TypeName = "System.Nullable<long>";
                break;
            case AttributeTypes.Decimal:
                attribute.TypeName = "System.Nullable<decimal>";
                break;
            case AttributeTypes.Double:
                attribute.TypeName = "System.Nullable<double>";
                break;
            case AttributeTypes.Boolean:
                attribute.TypeName = "System.Nullable<bool>";
                break;
            case AttributeTypes.DateTime:
                attribute.TypeName = "System.Nullable<System.DateTime>";
                break;
            case AttributeTypes.Money:
                attribute.TypeName = "Microsoft.Xrm.Sdk.Money";
                break;
            case AttributeTypes.Lookup:
            case AttributeTypes.Customer:
            case AttributeTypes.Owner:
                attribute.TypeName = "Microsoft.Xrm.Sdk.EntityReference";
                break;
            case AttributeTypes.ManagedProperty:
                attribute.TypeName = "Microsoft.Xrm.Sdk.BooleanManagedProperty";
                break;
            case AttributeTypes.Image:
                attribute.TypeName = "byte[]";
                break;
            case AttributeTypes.File:
                attribute.TypeName = "System.Guid";
                break;
            case AttributeTypes.Picklist:
            case AttributeTypes.State:
            case AttributeTypes.Status:
                attribute.TypeName = "Microsoft.Xrm.Sdk.OptionSetValue";
                attribute.OptionSet = ResolveOptionSet(attribute, entity, model, naming, classNames, typeNames, globalEnums);
                if (attribute.OptionSet != null && !model.Settings.ReplaceEnumPropertiesWithOptionSet)
                {
                    attribute.Kind = AttributeKind.Enum;
                    attribute.TypeName = "System.Nullable<" + attribute.OptionSet.EnumName + ">";
                }

                break;
            case AttributeTypes.MultiSelectPicklist:
                attribute.TypeName = "Microsoft.Xrm.Sdk.OptionSetValueCollection";
                attribute.OptionSet = ResolveOptionSet(attribute, entity, model, naming, classNames, typeNames, globalEnums);
                if (attribute.OptionSet != null && !model.Settings.ReplaceEnumPropertiesWithOptionSet)
                {
                    attribute.Kind = AttributeKind.MultiEnum;
                    attribute.TypeName = "System.Collections.Generic.IEnumerable<" + attribute.OptionSet.EnumName + ">";
                }

                break;
            case AttributeTypes.PartyList:
                ResolveCollection(attribute, model, "activityparty");
                break;
            case AttributeTypes.CalendarRules:
                ResolveCollection(attribute, model, "calendarrule");
                break;
            default:
                attribute.TypeName = "object";
                break;
        }
    }

    private static void ResolveCollection(AttributeModel attribute, EarlyBoundModel model, string elementEntity)
    {
        var element = model.FindEntity(elementEntity);
        if (element == null)
        {
            attribute.TypeName = "Microsoft.Xrm.Sdk.EntityCollection";
            return;
        }

        attribute.Kind = AttributeKind.EntityCollection;
        attribute.ElementEntity = element;
        attribute.TypeName = "System.Collections.Generic.IEnumerable<" + element.ClassName + ">";
    }

    private static OptionSetModel? ResolveOptionSet(
        AttributeModel attribute,
        EntityModel entity,
        EarlyBoundModel model,
        NamingService naming,
        HashSet<string> classNames,
        HashSet<string> typeNames,
        Dictionary<string, OptionSetModel> globalEnums)
    {
        var definition = attribute.Definition;
        OptionSetDefinition? optionSet;
        if (definition.OptionSet != null)
        {
            optionSet = definition.OptionSet;
        }
        else if (definition.GlobalOptionSet != null)
        {
            if (globalEnums.TryGetValue(definition.GlobalOptionSet, out var known))
            {
                return known;
            }

            optionSet = model.Metadata.FindGlobalOptionSet(definition.GlobalOptionSet);
            if (optionSet == null)
            {
                model.Diagnostics.Add(new MetadataDiagnostic(
                    DiagnosticIds.UnknownOptionSet,
                    MetadataSeverity.Warning,
                    $"Column '{entity.Definition.LogicalName}.{definition.LogicalName}' uses the global choice '{definition.GlobalOptionSet}', which no metadata file defines. The property is typed as OptionSetValue. Pull the metadata again to include it.",
                    entity.Definition.SourcePath,
                    definition.Position));
                return null;
            }
        }
        else
        {
            return null;
        }

        var enumName = naming.GetNameForOptionSet(entity.Definition, definition, optionSet, classNames);
        enumName = MakeUnique(enumName, typeNames, model, entity.Definition, "enum");
        var result = new OptionSetModel(optionSet, enumName);

        var isStatus = definition.Type == AttributeTypes.Status
            || string.Equals(optionSet.OptionSetType, AttributeTypes.Status, StringComparison.OrdinalIgnoreCase);
        var names = naming.GetNamesForOptions(optionSet, isStatus);
        var seen = new HashSet<int>();
        var index = 0;
        foreach (var option in optionSet.Options)
        {
            if (seen.Add(option.Value) && names.TryGetValue(option.Value, out var memberName))
            {
                result.Options.Add(new OptionModel(option, memberName, index));
            }

            index++;
        }

        model.OptionSets.Add(result);
        if (optionSet.IsGlobal)
        {
            globalEnums[optionSet.Name] = result;
        }

        return result;
    }

    private static bool HasSetter(AttributeModel attribute, GeneratorSettings settings)
    {
        if (attribute.Kind == AttributeKind.FormattedValue)
        {
            return false;
        }

        var definition = attribute.Definition;
        return attribute.Kind == AttributeKind.PrimaryId
            || definition.IsValidForCreate
            || definition.IsValidForUpdate
            || settings.MakeAllFieldsEditable
            || (settings.MakeReadonlyFieldsEditable && AuditColumns.Contains(definition.LogicalName));
    }

    private static void AddRelationships(EntityModel entity, EarlyBoundModel model, NamingService naming, HashSet<string> memberNames)
    {
        var attributeNames = new HashSet<string>(entity.Attributes.Select(a => a.PropertyName), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(RelationshipDefinition definition, RelationshipKind kind, RelationshipRole role)
        {
            var related = model.FindEntity(definition.Entity);
            if (related == null || !seen.Add(kind + "|" + role + "|" + definition.SchemaName))
            {
                return;
            }

            var rolePrefix = role == RelationshipRole.None ? null : role.ToString();
            var bpfEntity = NamingService.IsBpfName(related.Definition.SchemaName)
                ? related.Definition
                : NamingService.IsBpfName(entity.Definition.SchemaName) ? entity.Definition : null;
            var name = naming.GetNameForRelationship(entity.Definition, definition, rolePrefix, attributeNames, bpfEntity);
            name = MakeUniqueMember(name, memberNames, model, entity, definition.SchemaName);

            var relationship = new RelationshipModel(definition, kind, role, related, name);
            if (kind == RelationshipKind.ManyToOne && definition.Attribute != null)
            {
                var lookup = entity.Definition.FindAttribute(definition.Attribute);
                relationship.HasSetter = lookup == null
                    || lookup.IsValidForCreate
                    || lookup.IsValidForUpdate
                    || model.Settings.MakeAllFieldsEditable;
            }

            entity.Relationships.Add(relationship);
        }

        var self = entity.Definition.LogicalName;
        foreach (var definition in entity.Definition.OneToMany.OrderBy(r => r.SchemaName, StringComparer.Ordinal))
        {
            Add(definition, RelationshipKind.OneToMany, definition.Entity == self ? RelationshipRole.Referenced : RelationshipRole.None);
        }

        foreach (var definition in entity.Definition.ManyToOne.OrderBy(r => r.SchemaName, StringComparer.Ordinal))
        {
            Add(definition, RelationshipKind.ManyToOne, definition.Entity == self ? RelationshipRole.Referencing : RelationshipRole.None);
        }

        foreach (var definition in entity.Definition.ManyToMany.OrderBy(r => r.SchemaName, StringComparer.Ordinal))
        {
            if (definition.Entity == self)
            {
                Add(definition, RelationshipKind.ManyToMany, RelationshipRole.Referencing);
                Add(definition, RelationshipKind.ManyToMany, RelationshipRole.Referenced);
            }
            else
            {
                Add(definition, RelationshipKind.ManyToMany, RelationshipRole.None);
            }
        }
    }

    private static string MakeUnique(string name, HashSet<string> typeNames, EarlyBoundModel model, EntityDefinition source, string what)
    {
        if (typeNames.Add(name))
        {
            return name;
        }

        var counter = 1;
        string candidate;
        do
        {
            candidate = name + counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            counter++;
        }
        while (!typeNames.Add(candidate));

        model.Diagnostics.Add(new MetadataDiagnostic(
            DiagnosticIds.NameCollision,
            MetadataSeverity.Warning,
            $"The {what} name '{name}' derived for '{source.LogicalName}' is already in use, so '{candidate}' is generated instead. Set an explicit name in the 'generator' section of earlybound.yaml to choose your own.",
            source.SourcePath,
            source.Position));
        return candidate;
    }

    private static string MakeUniqueMember(string name, HashSet<string> memberNames, EarlyBoundModel model, EntityModel entity, string source)
    {
        if (memberNames.Add(name))
        {
            return name;
        }

        var candidate = name;
        do
        {
            candidate += "1";
        }
        while (!memberNames.Add(candidate));

        model.Diagnostics.Add(new MetadataDiagnostic(
            DiagnosticIds.NameCollision,
            MetadataSeverity.Info,
            $"The member name '{name}' derived for '{source}' on '{entity.Definition.LogicalName}' is already in use, so '{candidate}' is generated instead.",
            entity.Definition.SourcePath,
            entity.Definition.Position));
        return candidate;
    }

    private static Func<string, bool> CreateObsoleteMatcher(GeneratorSettings settings)
    {
        var patterns = settings.ObsoleteTokens
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => new Regex(
                "^" + Regex.Escape(t.Trim()).Replace("\\*", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline))
            .ToList();

        if (patterns.Count == 0)
        {
            return _ => false;
        }

        return displayName =>
        {
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(displayName))
                {
                    return true;
                }
            }

            return false;
        };
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return null;
    }
}
