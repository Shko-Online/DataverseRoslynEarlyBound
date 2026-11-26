using System.Globalization;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ShkoOnline.EarlyBound.Yaml;

/// <summary>
/// Reads Early Bound YAML: metadata files (<c>entities:</c>, <c>optionSets:</c>) and the
/// configuration file (<c>environment:</c>, <c>pull:</c>, <c>generator:</c>). A file may contain
/// any combination of these keys, so one file or many both work.
/// </summary>
/// <remarks>Never throws for bad input; problems are returned as diagnostics.</remarks>
public static class MetadataYamlReader
{
    public static MetadataFile Read(string text, string? path = null)
    {
        var file = new MetadataFile(path);
        var stream = new YamlStream();
        try
        {
            using var reader = new StringReader(text);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            file.Diagnostics.Add(new MetadataDiagnostic(
                DiagnosticIds.InvalidYaml,
                MetadataSeverity.Error,
                "The file is not valid YAML: " + FirstLine(ex.Message),
                path,
                ToPosition(ex.Start)));
            return file;
        }

        var context = new ReadContext(file);
        foreach (var document in stream.Documents)
        {
            if (document.RootNode is YamlMappingNode root)
            {
                ReadRoot(root, context);
            }
            else if (!IsNull(document.RootNode))
            {
                context.Error(document.RootNode, "Expected a mapping with 'entities', 'optionSets', 'pull' or 'generator' at the top of the file.");
            }
        }

        return file;
    }

    private static void ReadRoot(YamlMappingNode root, ReadContext context)
    {
        foreach (var pair in root.Children)
        {
            var key = context.Key(pair.Key);
            switch (key)
            {
                case "entities":
                    foreach (var node in context.Sequence(pair.Value, "entities"))
                    {
                        var entity = ReadEntity(node, context);
                        if (entity != null)
                        {
                            context.File.Entities.Add(entity);
                        }
                    }

                    break;
                case "optionSets":
                    foreach (var node in context.Sequence(pair.Value, "optionSets"))
                    {
                        var optionSet = ReadOptionSet(node, context, isGlobal: true);
                        if (optionSet != null)
                        {
                            context.File.OptionSets.Add(optionSet);
                        }
                    }

                    break;
                case "environment":
                    Configuration(context).Environment = context.String(pair.Value);
                    break;
                case "pull":
                    ReadPull(pair.Value, Configuration(context).Pull, context);
                    break;
                case "generator":
                    context.File.HasGeneratorSettings = true;
                    GeneratorSettingsReader.Read(pair.Value, Configuration(context).Generator, context);
                    break;
                default:
                    // Unknown top-level keys are left alone so newer files load in older versions.
                    break;
            }
        }
    }

    private static EarlyBoundConfiguration Configuration(ReadContext context) =>
        context.File.Configuration ??= new EarlyBoundConfiguration();

    private static void ReadPull(YamlNode node, PullSettings pull, ReadContext context)
    {
        if (node is not YamlMappingNode map)
        {
            if (!IsNull(node))
            {
                context.Error(node, "'pull' must be a mapping.");
            }

            return;
        }

        foreach (var pair in map.Children)
        {
            switch (context.Key(pair.Key))
            {
                case "entities":
                    pull.Entities = context.StringList(pair.Value, "pull.entities");
                    break;
                case "solutions":
                    pull.Solutions = context.StringList(pair.Value, "pull.solutions");
                    break;
                case "exclude":
                    pull.Exclude = context.StringList(pair.Value, "pull.exclude");
                    break;
                case "languageCode":
                    pull.LanguageCode = context.Int(pair.Value, "pull.languageCode");
                    break;
                case "relationships":
                    pull.Relationships = context.OneOf(pair.Value, "pull.relationships", pull.Relationships,
                        PullSettings.RelationshipsIncluded, PullSettings.RelationshipsAll, PullSettings.RelationshipsNone);
                    break;
                case "output":
                    pull.Output = context.String(pair.Value) ?? pull.Output;
                    break;
                case "layout":
                    pull.Layout = context.OneOf(pair.Value, "pull.layout", pull.Layout,
                        PullSettings.LayoutPerEntity, PullSettings.LayoutSingleFile);
                    break;
                default:
                    context.UnknownSetting(pair.Key, "pull");
                    break;
            }
        }
    }

    private static EntityDefinition? ReadEntity(YamlNode node, ReadContext context)
    {
        if (node is not YamlMappingNode map)
        {
            context.Error(node, "Each item of 'entities' must be a mapping.");
            return null;
        }

        var entity = new EntityDefinition
        {
            SourcePath = context.File.Path,
            Position = ToPosition(map.Start),
        };

        foreach (var pair in map.Children)
        {
            switch (context.Key(pair.Key))
            {
                case "logicalName":
                    entity.LogicalName = context.String(pair.Value) ?? string.Empty;
                    entity.Position = ToPosition(pair.Value.Start);
                    break;
                case "schemaName":
                    entity.SchemaName = context.String(pair.Value) ?? string.Empty;
                    break;
                case "displayName":
                    entity.DisplayName = context.String(pair.Value);
                    break;
                case "displayCollectionName":
                    entity.DisplayCollectionName = context.String(pair.Value);
                    break;
                case "description":
                    entity.Description = context.String(pair.Value);
                    break;
                case "entitySetName":
                    entity.EntitySetName = context.String(pair.Value);
                    break;
                case "logicalCollectionName":
                    entity.LogicalCollectionName = context.String(pair.Value);
                    break;
                case "primaryIdAttribute":
                    entity.PrimaryIdAttribute = context.String(pair.Value);
                    break;
                case "primaryNameAttribute":
                    entity.PrimaryNameAttribute = context.String(pair.Value);
                    break;
                case "primaryImageAttribute":
                    entity.PrimaryImageAttribute = context.String(pair.Value);
                    break;
                case "objectTypeCode":
                    entity.ObjectTypeCode = context.Int(pair.Value, "objectTypeCode");
                    break;
                case "ownershipType":
                    entity.OwnershipType = context.String(pair.Value);
                    break;
                case "isCustomEntity":
                    entity.IsCustomEntity = context.Bool(pair.Value, "isCustomEntity") ?? false;
                    break;
                case "isActivity":
                    entity.IsActivity = context.Bool(pair.Value, "isActivity") ?? false;
                    break;
                case "isIntersect":
                    entity.IsIntersect = context.Bool(pair.Value, "isIntersect") ?? false;
                    break;
                case "attributes":
                    foreach (var item in context.Sequence(pair.Value, "attributes"))
                    {
                        var attribute = ReadAttribute(item, context);
                        if (attribute != null)
                        {
                            entity.Attributes.Add(attribute);
                        }
                    }

                    break;
                case "manyToOne":
                    ReadRelationships(pair.Value, "manyToOne", entity.ManyToOne, context);
                    break;
                case "oneToMany":
                    ReadRelationships(pair.Value, "oneToMany", entity.OneToMany, context);
                    break;
                case "manyToMany":
                    ReadRelationships(pair.Value, "manyToMany", entity.ManyToMany, context);
                    break;
            }
        }

        if (entity.LogicalName.Length == 0)
        {
            context.Error(map, "An entity is missing its 'logicalName'.");
            return null;
        }

        if (entity.SchemaName.Length == 0)
        {
            entity.SchemaName = entity.LogicalName;
        }

        return entity;
    }

    private static AttributeDefinition? ReadAttribute(YamlNode node, ReadContext context)
    {
        if (node is not YamlMappingNode map)
        {
            context.Error(node, "Each item of 'attributes' must be a mapping.");
            return null;
        }

        var attribute = new AttributeDefinition { Position = ToPosition(map.Start) };
        foreach (var pair in map.Children)
        {
            switch (context.Key(pair.Key))
            {
                case "logicalName":
                    attribute.LogicalName = context.String(pair.Value) ?? string.Empty;
                    attribute.Position = ToPosition(pair.Value.Start);
                    break;
                case "schemaName":
                    attribute.SchemaName = context.String(pair.Value) ?? string.Empty;
                    break;
                case "type":
                    attribute.Type = context.String(pair.Value) ?? AttributeTypes.String;
                    break;
                case "displayName":
                    attribute.DisplayName = context.String(pair.Value);
                    break;
                case "description":
                    attribute.Description = context.String(pair.Value);
                    break;
                case "requiredLevel":
                    attribute.RequiredLevel = context.String(pair.Value);
                    break;
                case "isValidForCreate":
                    attribute.IsValidForCreate = context.Bool(pair.Value, "isValidForCreate") ?? true;
                    break;
                case "isValidForUpdate":
                    attribute.IsValidForUpdate = context.Bool(pair.Value, "isValidForUpdate") ?? true;
                    break;
                case "isValidForRead":
                    attribute.IsValidForRead = context.Bool(pair.Value, "isValidForRead") ?? true;
                    break;
                case "attributeOf":
                    attribute.AttributeOf = context.String(pair.Value);
                    break;
                case "isDeprecated":
                    attribute.IsDeprecated = context.Bool(pair.Value, "isDeprecated") ?? false;
                    break;
                case "maxLength":
                    attribute.MaxLength = context.Int(pair.Value, "maxLength");
                    break;
                case "format":
                    attribute.Format = context.String(pair.Value);
                    break;
                case "minValue":
                    attribute.MinValue = context.String(pair.Value);
                    break;
                case "maxValue":
                    attribute.MaxValue = context.String(pair.Value);
                    break;
                case "precision":
                    attribute.Precision = context.Int(pair.Value, "precision");
                    break;
                case "dateTimeBehavior":
                    attribute.DateTimeBehavior = context.String(pair.Value);
                    break;
                case "targets":
                    attribute.Targets = context.StringList(pair.Value, "targets");
                    break;
                case "globalOptionSet":
                    attribute.GlobalOptionSet = context.String(pair.Value);
                    break;
                case "optionSet":
                    attribute.OptionSet = ReadOptionSet(pair.Value, context, isGlobal: false);
                    break;
            }
        }

        if (attribute.LogicalName.Length == 0)
        {
            context.Error(map, "An attribute is missing its 'logicalName'.");
            return null;
        }

        if (attribute.SchemaName.Length == 0)
        {
            attribute.SchemaName = attribute.LogicalName;
        }

        return attribute;
    }

    private static OptionSetDefinition? ReadOptionSet(YamlNode node, ReadContext context, bool isGlobal)
    {
        if (node is not YamlMappingNode map)
        {
            if (!IsNull(node))
            {
                context.Error(node, isGlobal ? "Each item of 'optionSets' must be a mapping." : "'optionSet' must be a mapping.");
            }

            return null;
        }

        var optionSet = new OptionSetDefinition
        {
            IsGlobal = isGlobal,
            SourcePath = context.File.Path,
            Position = ToPosition(map.Start),
        };

        foreach (var pair in map.Children)
        {
            switch (context.Key(pair.Key))
            {
                case "name":
                    optionSet.Name = context.String(pair.Value) ?? string.Empty;
                    optionSet.Position = ToPosition(pair.Value.Start);
                    break;
                case "displayName":
                    optionSet.DisplayName = context.String(pair.Value);
                    break;
                case "description":
                    optionSet.Description = context.String(pair.Value);
                    break;
                case "type":
                    optionSet.OptionSetType = context.String(pair.Value);
                    break;
                case "options":
                    foreach (var item in context.Sequence(pair.Value, "options"))
                    {
                        var option = ReadOption(item, context);
                        if (option != null)
                        {
                            optionSet.Options.Add(option);
                        }
                    }

                    break;
            }
        }

        if (isGlobal && optionSet.Name.Length == 0)
        {
            context.Error(map, "An option set is missing its 'name'.");
            return null;
        }

        return optionSet;
    }

    private static OptionDefinition? ReadOption(YamlNode node, ReadContext context)
    {
        if (node is not YamlMappingNode map)
        {
            context.Error(node, "Each item of 'options' must be a mapping with 'value' and 'label'.");
            return null;
        }

        var option = new OptionDefinition();
        var hasValue = false;
        foreach (var pair in map.Children)
        {
            switch (context.Key(pair.Key))
            {
                case "value":
                    var value = context.Int(pair.Value, "value");
                    if (value.HasValue)
                    {
                        option.Value = value.Value;
                        hasValue = true;
                    }

                    break;
                case "label":
                    option.Label = context.String(pair.Value);
                    break;
                case "description":
                    option.Description = context.String(pair.Value);
                    break;
                case "color":
                    option.Color = context.String(pair.Value);
                    break;
                case "externalValue":
                    option.ExternalValue = context.String(pair.Value);
                    break;
                case "invariantName":
                    option.InvariantName = context.String(pair.Value);
                    break;
                case "state":
                    option.State = context.Int(pair.Value, "state");
                    break;
                case "defaultStatus":
                    option.DefaultStatus = context.Int(pair.Value, "defaultStatus");
                    break;
            }
        }

        if (!hasValue)
        {
            context.Error(map, "An option is missing its numeric 'value'.");
            return null;
        }

        return option;
    }

    private static void ReadRelationships(YamlNode node, string name, List<RelationshipDefinition> target, ReadContext context)
    {
        foreach (var item in context.Sequence(node, name))
        {
            if (item is not YamlMappingNode map)
            {
                context.Error(item, $"Each item of '{name}' must be a mapping.");
                continue;
            }

            var relationship = new RelationshipDefinition();
            foreach (var pair in map.Children)
            {
                switch (context.Key(pair.Key))
                {
                    case "schemaName":
                        relationship.SchemaName = context.String(pair.Value) ?? string.Empty;
                        break;
                    case "entity":
                        relationship.Entity = context.String(pair.Value) ?? string.Empty;
                        break;
                    case "attribute":
                        relationship.Attribute = context.String(pair.Value);
                        break;
                    case "intersectEntity":
                        relationship.IntersectEntity = context.String(pair.Value);
                        break;
                }
            }

            if (relationship.SchemaName.Length == 0 || relationship.Entity.Length == 0)
            {
                context.Error(map, $"A '{name}' relationship needs 'schemaName' and 'entity'.");
                continue;
            }

            target.Add(relationship);
        }
    }

    internal static bool IsNull(YamlNode? node)
    {
        if (node == null)
        {
            return true;
        }

        return node is YamlScalarNode scalar
            && scalar.Style == ScalarStyle.Plain
            && (string.IsNullOrEmpty(scalar.Value) || scalar.Value == "~" || scalar.Value == "null");
    }

    internal static SourcePosition ToPosition(in Mark mark) =>
        new(Math.Max(0, (int)mark.Line - 1), Math.Max(0, (int)mark.Column - 1));

    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(new[] { '\r', '\n' });
        return index < 0 ? message : message.Substring(0, index);
    }

    /// <summary>Typed access to YAML nodes that records a diagnostic instead of throwing.</summary>
    internal sealed class ReadContext
    {
        public ReadContext(MetadataFile file)
        {
            File = file;
        }

        public MetadataFile File { get; }

        public string Key(YamlNode node) => (node as YamlScalarNode)?.Value ?? string.Empty;

        public string? String(YamlNode node)
        {
            if (IsNull(node))
            {
                return null;
            }

            if (node is YamlScalarNode scalar)
            {
                return scalar.Value;
            }

            Error(node, "Expected a single value.");
            return null;
        }

        public int? Int(YamlNode node, string name)
        {
            var text = String(node);
            if (text == null)
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            Error(node, $"'{name}' must be a whole number, but was '{text}'.");
            return null;
        }

        public bool? Bool(YamlNode node, string name)
        {
            var text = String(node);
            if (text == null)
            {
                return null;
            }

            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Error(node, $"'{name}' must be true or false, but was '{text}'.");
            return null;
        }

        public string OneOf(YamlNode node, string name, string fallback, params string[] allowed)
        {
            var text = String(node);
            if (text == null)
            {
                return fallback;
            }

            foreach (var candidate in allowed)
            {
                if (string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            Error(node, $"'{name}' must be one of: {string.Join(", ", allowed)}. It was '{text}'.");
            return fallback;
        }

        public IEnumerable<YamlNode> Sequence(YamlNode node, string name)
        {
            if (node is YamlSequenceNode sequence)
            {
                return sequence.Children;
            }

            if (!IsNull(node))
            {
                Error(node, $"'{name}' must be a list.");
            }

            return Array.Empty<YamlNode>();
        }

        public List<string> StringList(YamlNode node, string name)
        {
            var result = new List<string>();
            if (node is YamlScalarNode && !IsNull(node))
            {
                // A single value is accepted where a list is expected.
                result.Add(((YamlScalarNode)node).Value!);
                return result;
            }

            foreach (var item in Sequence(node, name))
            {
                var value = String(item);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value!.Trim());
                }
            }

            return result;
        }

        public void Error(YamlNode node, string message) =>
            File.Diagnostics.Add(new MetadataDiagnostic(
                DiagnosticIds.InvalidValue, MetadataSeverity.Error, message, File.Path, ToPosition(node.Start)));

        public void UnknownSetting(YamlNode key, string section) =>
            File.Diagnostics.Add(new MetadataDiagnostic(
                DiagnosticIds.UnknownSetting,
                MetadataSeverity.Warning,
                $"'{Key(key)}' is not a known setting of '{section}' and is ignored.",
                File.Path,
                ToPosition(key.Start)));
    }
}
