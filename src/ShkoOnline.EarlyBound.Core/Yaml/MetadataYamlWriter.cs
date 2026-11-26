using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ShkoOnline.EarlyBound.Metadata;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace ShkoOnline.EarlyBound.Yaml;

/// <summary>
/// Writes metadata files. Output is deterministic and ordered so that pulling the same
/// environment twice produces identical files and reviews show only real changes.
/// </summary>
public static class MetadataYamlWriter
{
    private static readonly Regex AmbiguousPlainScalar = new(
        @"^(?:true|false|yes|no|on|off|y|n|null|~|[-+]?\.(?:inf|nan)|[-+]?(?:0x[0-9a-f]+|0o[0-7]+|\d[\d_]*(?:\.\d*)?(?:e[-+]?\d+)?|\.\d+)|\d{4}-\d{2}-\d{2}.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Serialises tables and global choices into one YAML document.</summary>
    /// <param name="header">Lines written as comments at the top of the file.</param>
    public static string Write(
        IEnumerable<EntityDefinition> entities,
        IEnumerable<OptionSetDefinition> optionSets,
        IEnumerable<string>? header = null)
    {
        var builder = new StringBuilder();
        using (var writer = new StringWriter(builder, CultureInfo.InvariantCulture) { NewLine = "\n" })
        {
            if (header != null)
            {
                foreach (var line in header)
                {
                    writer.Write(line.Length == 0 ? "#" : "# " + line);
                    writer.Write('\n');
                }
            }

            // Two-space indent, list items indented under their key, no line wrapping, LF line ends.
            var settings = new EmitterSettings(2, int.MaxValue, false, 1024, false, true, "\n");
            var y = new Emit(new Emitter(writer, settings));
            y.Begin();
            y.BeginMap();

            var orderedEntities = entities.OrderBy(e => e.LogicalName, StringComparer.Ordinal).ToList();
            if (orderedEntities.Count > 0)
            {
                y.Key("entities");
                y.BeginSequence();
                foreach (var entity in orderedEntities)
                {
                    WriteEntity(y, entity);
                }

                y.EndSequence();
            }

            var orderedOptionSets = optionSets.OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
            if (orderedOptionSets.Count > 0)
            {
                y.Key("optionSets");
                y.BeginSequence();
                foreach (var optionSet in orderedOptionSets)
                {
                    WriteOptionSet(y, optionSet, includeName: true);
                }

                y.EndSequence();
            }

            y.EndMap();
            y.End();
        }

        // The emitter uses the platform newline; files are always written with LF.
        return builder.ToString().Replace("\r\n", "\n");
    }

    private static void WriteEntity(Emit y, EntityDefinition entity)
    {
        y.BeginMap();
        y.Pair("logicalName", entity.LogicalName);
        y.Pair("schemaName", entity.SchemaName);
        y.Pair("displayName", entity.DisplayName);
        y.Pair("displayCollectionName", entity.DisplayCollectionName);
        y.Pair("description", entity.Description);
        y.Pair("entitySetName", entity.EntitySetName);
        y.Pair("logicalCollectionName", entity.LogicalCollectionName);
        y.Pair("primaryIdAttribute", entity.PrimaryIdAttribute);
        y.Pair("primaryNameAttribute", entity.PrimaryNameAttribute);
        y.Pair("primaryImageAttribute", entity.PrimaryImageAttribute);
        y.Pair("objectTypeCode", entity.ObjectTypeCode);
        y.Pair("ownershipType", entity.OwnershipType);
        y.PairIfTrue("isCustomEntity", entity.IsCustomEntity);
        y.PairIfTrue("isActivity", entity.IsActivity);
        y.PairIfTrue("isIntersect", entity.IsIntersect);

        if (entity.Attributes.Count > 0)
        {
            y.Key("attributes");
            y.BeginSequence();
            foreach (var attribute in entity.Attributes.OrderBy(a => a.LogicalName, StringComparer.Ordinal))
            {
                WriteAttribute(y, attribute);
            }

            y.EndSequence();
        }

        WriteRelationships(y, "manyToOne", entity.ManyToOne);
        WriteRelationships(y, "oneToMany", entity.OneToMany);
        WriteRelationships(y, "manyToMany", entity.ManyToMany);
        y.EndMap();
    }

    private static void WriteAttribute(Emit y, AttributeDefinition attribute)
    {
        y.BeginMap();
        y.Pair("logicalName", attribute.LogicalName);
        y.Pair("schemaName", attribute.SchemaName);
        y.Pair("type", attribute.Type);
        y.Pair("displayName", attribute.DisplayName);
        y.Pair("description", attribute.Description);
        if (!string.IsNullOrEmpty(attribute.RequiredLevel) && attribute.RequiredLevel != "None")
        {
            y.Pair("requiredLevel", attribute.RequiredLevel);
        }

        y.PairIfFalse("isValidForCreate", attribute.IsValidForCreate);
        y.PairIfFalse("isValidForUpdate", attribute.IsValidForUpdate);
        y.PairIfFalse("isValidForRead", attribute.IsValidForRead);
        y.Pair("attributeOf", attribute.AttributeOf);
        y.PairIfTrue("isDeprecated", attribute.IsDeprecated);
        y.Pair("maxLength", attribute.MaxLength);
        y.Pair("format", attribute.Format);
        y.Pair("minValue", attribute.MinValue);
        y.Pair("maxValue", attribute.MaxValue);
        y.Pair("precision", attribute.Precision);
        y.Pair("dateTimeBehavior", attribute.DateTimeBehavior);

        if (attribute.Targets.Count > 0)
        {
            y.Key("targets");
            y.BeginSequence(flow: true);
            foreach (var target in attribute.Targets)
            {
                y.Value(target);
            }

            y.EndSequence();
        }

        y.Pair("globalOptionSet", attribute.GlobalOptionSet);
        if (attribute.OptionSet != null)
        {
            y.Key("optionSet");
            WriteOptionSet(y, attribute.OptionSet, includeName: true);
        }

        y.EndMap();
    }

    private static void WriteOptionSet(Emit y, OptionSetDefinition optionSet, bool includeName)
    {
        y.BeginMap();
        if (includeName)
        {
            y.Pair("name", optionSet.Name);
        }

        y.Pair("displayName", optionSet.DisplayName);
        y.Pair("description", optionSet.Description);
        y.Pair("type", optionSet.OptionSetType);
        y.Key("options");
        y.BeginSequence();
        foreach (var option in optionSet.Options)
        {
            y.BeginMap(flow: true);
            y.Pair("value", option.Value);
            y.Pair("label", option.Label);
            y.Pair("invariantName", option.InvariantName);
            y.Pair("state", option.State);
            y.Pair("defaultStatus", option.DefaultStatus);
            y.Pair("color", option.Color);
            y.Pair("externalValue", option.ExternalValue);
            y.Pair("description", option.Description);
            y.EndMap();
        }

        y.EndSequence();
        y.EndMap();
    }

    private static void WriteRelationships(Emit y, string name, List<RelationshipDefinition> relationships)
    {
        if (relationships.Count == 0)
        {
            return;
        }

        y.Key(name);
        y.BeginSequence();
        foreach (var relationship in relationships
            .OrderBy(r => r.SchemaName, StringComparer.Ordinal)
            .ThenBy(r => r.Entity, StringComparer.Ordinal))
        {
            y.BeginMap(flow: true);
            y.Pair("schemaName", relationship.SchemaName);
            y.Pair("entity", relationship.Entity);
            y.Pair("attribute", relationship.Attribute);
            y.Pair("intersectEntity", relationship.IntersectEntity);
            y.EndMap();
        }

        y.EndSequence();
    }

    /// <summary>A thin layer over the YAML emitter that picks safe scalar styles.</summary>
    private sealed class Emit
    {
        private readonly IEmitter _emitter;

        public Emit(IEmitter emitter)
        {
            _emitter = emitter;
        }

        public void Begin()
        {
            _emitter.Emit(new StreamStart());
            _emitter.Emit(new DocumentStart(null, null, true));
        }

        public void End()
        {
            _emitter.Emit(new DocumentEnd(true));
            _emitter.Emit(new StreamEnd());
        }

        public void BeginMap(bool flow = false) =>
            _emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, true, flow ? MappingStyle.Flow : MappingStyle.Block));

        public void EndMap() => _emitter.Emit(new MappingEnd());

        public void BeginSequence(bool flow = false) =>
            _emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, flow ? SequenceStyle.Flow : SequenceStyle.Block));

        public void EndSequence() => _emitter.Emit(new SequenceEnd());

        public void Key(string name) =>
            _emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, name, ScalarStyle.Plain, true, false));

        public void Value(string value)
        {
            // Text that a YAML reader could take for a number, boolean, null or date is quoted,
            // so the files mean the same thing to every tool that opens them.
            var quote = value.Length == 0 || AmbiguousPlainScalar.IsMatch(value) || value != value.Trim();
            _emitter.Emit(quote
                ? new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.DoubleQuoted, false, true)
                : new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.Any, true, true));
        }

        public void Pair(string name, string? value)
        {
            if (value == null)
            {
                return;
            }

            Key(name);
            Value(value);
        }

        public void Pair(string name, int? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            Key(name);
            Literal(value.Value.ToString(CultureInfo.InvariantCulture));
        }

        public void PairIfTrue(string name, bool value)
        {
            if (value)
            {
                Key(name);
                Literal("true");
            }
        }

        public void PairIfFalse(string name, bool value)
        {
            if (!value)
            {
                Key(name);
                Literal("false");
            }
        }

        private void Literal(string text) =>
            _emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, text, ScalarStyle.Plain, true, false));
    }
}
