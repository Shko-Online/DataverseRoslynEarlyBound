using System.Globalization;
using System.Text.Json;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Tool.Dataverse;

/// <summary>Turns Web API metadata JSON into the definitions stored in the YAML files.</summary>
internal static class MetadataMapper
{
    /// <param name="entity">An <c>EntityDefinitions</c> record with <c>Attributes</c> and the relationships expanded.</param>
    /// <param name="optionSets">The option set of every choice column, keyed by the column's logical name.</param>
    /// <param name="languageCode">LCID of the labels to keep.</param>
    public static EntityDefinition MapEntity(JsonElement entity, IReadOnlyDictionary<string, JsonElement> optionSets, int languageCode)
    {
        var definition = new EntityDefinition
        {
            LogicalName = String(entity, "LogicalName") ?? string.Empty,
            SchemaName = String(entity, "SchemaName") ?? string.Empty,
            DisplayName = Label(entity, "DisplayName", languageCode),
            DisplayCollectionName = Label(entity, "DisplayCollectionName", languageCode),
            Description = Label(entity, "Description", languageCode),
            EntitySetName = String(entity, "EntitySetName"),
            LogicalCollectionName = String(entity, "LogicalCollectionName"),
            PrimaryIdAttribute = String(entity, "PrimaryIdAttribute"),
            PrimaryNameAttribute = String(entity, "PrimaryNameAttribute"),
            PrimaryImageAttribute = String(entity, "PrimaryImageAttribute"),
            ObjectTypeCode = Int(entity, "ObjectTypeCode"),
            OwnershipType = String(entity, "OwnershipType"),
            IsCustomEntity = Bool(entity, "IsCustomEntity") ?? false,
            IsActivity = Bool(entity, "IsActivity") ?? false,
            IsIntersect = Bool(entity, "IsIntersect") ?? false,
        };

        foreach (var attribute in Array(entity, "Attributes"))
        {
            var mapped = MapAttribute(attribute, optionSets, languageCode);
            if (mapped != null)
            {
                definition.Attributes.Add(mapped);
            }
        }

        var self = definition.LogicalName;
        foreach (var relationship in Array(entity, "ManyToOneRelationships"))
        {
            Add(definition.ManyToOne, String(relationship, "SchemaName"), String(relationship, "ReferencedEntity"), String(relationship, "ReferencingAttribute"), null);
        }

        foreach (var relationship in Array(entity, "OneToManyRelationships"))
        {
            Add(definition.OneToMany, String(relationship, "SchemaName"), String(relationship, "ReferencingEntity"), String(relationship, "ReferencingAttribute"), null);
        }

        foreach (var relationship in Array(entity, "ManyToManyRelationships"))
        {
            var first = String(relationship, "Entity1LogicalName");
            var second = String(relationship, "Entity2LogicalName");
            var other = string.Equals(first, self, StringComparison.Ordinal) ? second : first;
            Add(definition.ManyToMany, String(relationship, "SchemaName"), other, null, String(relationship, "IntersectEntityName"));
        }

        return definition;
    }

    private static void Add(List<RelationshipDefinition> target, string? schemaName, string? entity, string? attribute, string? intersect)
    {
        if (string.IsNullOrEmpty(schemaName) || string.IsNullOrEmpty(entity))
        {
            return;
        }

        target.Add(new RelationshipDefinition
        {
            SchemaName = schemaName!,
            Entity = entity!,
            Attribute = attribute,
            IntersectEntity = intersect,
        });
    }

    private static AttributeDefinition? MapAttribute(JsonElement attribute, IReadOnlyDictionary<string, JsonElement> optionSets, int languageCode)
    {
        var logicalName = String(attribute, "LogicalName");
        if (string.IsNullOrEmpty(logicalName))
        {
            return null;
        }

        var definition = new AttributeDefinition
        {
            LogicalName = logicalName!,
            SchemaName = String(attribute, "SchemaName") ?? logicalName!,
            Type = AttributeType(attribute),
            DisplayName = Label(attribute, "DisplayName", languageCode),
            Description = Label(attribute, "Description", languageCode),
            RequiredLevel = ManagedValue(attribute, "RequiredLevel"),
            IsValidForCreate = Bool(attribute, "IsValidForCreate") ?? true,
            IsValidForUpdate = Bool(attribute, "IsValidForUpdate") ?? true,
            IsValidForRead = Bool(attribute, "IsValidForRead") ?? true,
            AttributeOf = String(attribute, "AttributeOf"),
            IsDeprecated = !string.IsNullOrEmpty(String(attribute, "DeprecatedVersion")),
            MaxLength = Int(attribute, "MaxLength"),
            Format = Format(attribute),
            MinValue = Number(attribute, "MinValue"),
            MaxValue = Number(attribute, "MaxValue"),
            Precision = Int(attribute, "Precision"),
            DateTimeBehavior = ManagedValue(attribute, "DateTimeBehavior"),
        };

        foreach (var target in Array(attribute, "Targets"))
        {
            if (target.ValueKind == JsonValueKind.String && target.GetString() is { Length: > 0 } name)
            {
                definition.Targets.Add(name);
            }
        }

        if (AttributeTypes.IsChoice(definition.Type) && optionSets.TryGetValue(definition.LogicalName, out var optionSet))
        {
            var mapped = MapOptionSet(optionSet, languageCode);
            if (mapped.IsGlobal)
            {
                definition.GlobalOptionSet = mapped.Name;
            }
            else
            {
                definition.OptionSet = mapped;
            }
        }

        return definition;
    }

    public static OptionSetDefinition MapOptionSet(JsonElement optionSet, int languageCode)
    {
        var definition = new OptionSetDefinition
        {
            Name = String(optionSet, "Name") ?? string.Empty,
            DisplayName = Label(optionSet, "DisplayName", languageCode),
            Description = Label(optionSet, "Description", languageCode),
            IsGlobal = Bool(optionSet, "IsGlobal") ?? false,
            OptionSetType = String(optionSet, "OptionSetType"),
        };

        foreach (var option in Array(optionSet, "Options"))
        {
            var value = Int(option, "Value");
            if (!value.HasValue)
            {
                continue;
            }

            definition.Options.Add(new OptionDefinition
            {
                Value = value.Value,
                Label = Label(option, "Label", languageCode),
                Description = Label(option, "Description", languageCode),
                Color = NonEmpty(String(option, "Color")),
                ExternalValue = NonEmpty(String(option, "ExternalValue")),
                InvariantName = NonEmpty(String(option, "InvariantName")),
                State = Int(option, "State"),
                DefaultStatus = Int(option, "DefaultStatus"),
            });
        }

        return definition;
    }

    /// <summary>The column type as used in the YAML files: AttributeTypeName without its "Type" suffix.</summary>
    internal static string AttributeType(JsonElement attribute)
    {
        var typeName = ManagedValue(attribute, "AttributeTypeName");
        if (!string.IsNullOrEmpty(typeName))
        {
            return typeName!.EndsWith("Type", StringComparison.Ordinal)
                ? typeName.Substring(0, typeName.Length - 4)
                : typeName;
        }

        return String(attribute, "AttributeType") ?? AttributeTypes.String;
    }

    /// <summary>
    /// The text of a label in the wanted language, falling back to the caller's language and then to any.
    /// Empty labels are treated as missing.
    /// </summary>
    internal static string? Label(JsonElement owner, string property, int languageCode)
    {
        if (!owner.TryGetProperty(property, out var label) || label.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? any = null;
        foreach (var localized in Array(label, "LocalizedLabels"))
        {
            var text = String(localized, "Label");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (Int(localized, "LanguageCode") == languageCode)
            {
                return text;
            }

            any ??= text;
        }

        if (label.TryGetProperty("UserLocalizedLabel", out var user) && user.ValueKind == JsonValueKind.Object)
        {
            var text = String(user, "Label");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return any;
    }

    private static string? Format(JsonElement attribute)
    {
        var format = String(attribute, "Format") ?? ManagedValue(attribute, "FormatName");
        return string.IsNullOrEmpty(format) || format == "None" ? null : format;
    }

    /// <summary>The API sends empty strings for values that are not set; they are not worth a line in the file.</summary>
    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>The <c>Value</c> of a wrapper object such as RequiredLevel or AttributeTypeName.</summary>
    private static string? ManagedValue(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var wrapper))
        {
            return null;
        }

        return wrapper.ValueKind switch
        {
            JsonValueKind.Object => String(wrapper, "Value"),
            JsonValueKind.String => wrapper.GetString(),
            _ => null,
        };
    }

    internal static string? String(JsonElement owner, string property) =>
        owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static int? Int(JsonElement owner, string property) =>
        owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    internal static bool? Bool(JsonElement owner, string property)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string? Number(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (value.TryGetInt64(out var integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        // G29 drops the trailing zeros the API sends, so 100.0 and 100 are written the same way.
        return value.TryGetDecimal(out var number)
            ? number.ToString("G29", CultureInfo.InvariantCulture)
            : value.GetDouble().ToString("R", CultureInfo.InvariantCulture);
    }

    internal static IEnumerable<JsonElement> Array(JsonElement owner, string property)
    {
        if (owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                yield return item;
            }
        }
    }
}
