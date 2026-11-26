using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Tags;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>Builds the entries shown in the completion list.</summary>
internal static class CompletionItems
{
    /// <summary>Text shown in the description pane.</summary>
    public const string DescriptionKey = "EarlyBound.Description";

    /// <summary>Text inserted when it differs from the text displayed.</summary>
    public const string InsertionKey = "EarlyBound.Insertion";

    /// <summary>Sorts our entries above the symbols of other providers when the list is shared with them.</summary>
    private const string TopOfList = "!";

    // FetchXML keywords contain hyphens and spaces; they filter the list instead of committing an entry.
    private static readonly CompletionItemRules KeywordRules = CompletionItemRules.Create(
        filterCharacterRules: ImmutableArray.Create(CharacterSetModificationRule.Create(CharacterSetModificationKind.Add, '-', ' ')),
        commitCharacterRules: ImmutableArray.Create(CharacterSetModificationRule.Create(CharacterSetModificationKind.Remove, '-', ' ')));

    private static readonly ImmutableArray<string> EntityTags = ImmutableArray.Create(WellKnownTags.Class, WellKnownTags.Public);
    private static readonly ImmutableArray<string> AttributeTags = ImmutableArray.Create(WellKnownTags.Field, WellKnownTags.Public);
    private static readonly ImmutableArray<string> RelationshipTags = ImmutableArray.Create(WellKnownTags.Reference);
    private static readonly ImmutableArray<string> OptionTags = ImmutableArray.Create(WellKnownTags.EnumMember, WellKnownTags.Public);
    private static readonly ImmutableArray<string> KeywordTags = ImmutableArray.Create(WellKnownTags.Keyword);

    public static IEnumerable<CompletionItem> Entities(MetadataSet metadata)
    {
        foreach (var entity in metadata.Entities)
        {
            var description = new StringBuilder();
            description.Append(entity.DisplayName ?? entity.LogicalName).Append(" (table)");
            AppendLine(description, entity.Description);
            AppendLine(description, "Schema name: " + entity.SchemaName);
            if (!string.IsNullOrEmpty(entity.PrimaryNameAttribute))
            {
                AppendLine(description, "Primary column: " + entity.PrimaryNameAttribute);
            }

            yield return Create(entity.LogicalName, entity.DisplayName, description.ToString(), EntityTags);
        }
    }

    /// <summary>The columns of one table.</summary>
    public static IEnumerable<CompletionItem> Attributes(EntityDefinition entity)
    {
        foreach (var attribute in entity.Attributes)
        {
            if (!IsColumn(attribute))
            {
                continue;
            }

            yield return Create(
                attribute.LogicalName,
                InlineDescription(attribute),
                Describe(attribute, entity.DisplayName ?? entity.LogicalName),
                AttributeTags);
        }
    }

    /// <summary>Every column of every table, for when the table could not be worked out.</summary>
    public static IEnumerable<CompletionItem> AllAttributes(MetadataSet metadata)
    {
        var byName = new SortedDictionary<string, List<(EntityDefinition Entity, AttributeDefinition Attribute)>>(StringComparer.Ordinal);
        foreach (var entity in metadata.Entities)
        {
            foreach (var attribute in entity.Attributes)
            {
                if (!IsColumn(attribute))
                {
                    continue;
                }

                if (!byName.TryGetValue(attribute.LogicalName, out var owners))
                {
                    byName[attribute.LogicalName] = owners = new List<(EntityDefinition, AttributeDefinition)>();
                }

                owners.Add((entity, attribute));
            }
        }

        foreach (var pair in byName)
        {
            var owners = pair.Value;
            var first = owners[0];
            var tables = string.Join(", ", owners.Take(4).Select(o => o.Entity.LogicalName));
            if (owners.Count > 4)
            {
                tables += ", +" + (owners.Count - 4).ToString(CultureInfo.InvariantCulture);
            }

            yield return Create(
                pair.Key,
                tables,
                Describe(first.Attribute, owners.Count == 1 ? first.Entity.LogicalName : tables),
                AttributeTags);
        }
    }

    public static IEnumerable<CompletionItem> Relationships(MetadataSet metadata)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in metadata.Entities)
        {
            foreach (var (relationship, kind) in entity.ManyToOne.Select(r => (r, "N:1"))
                .Concat(entity.OneToMany.Select(r => (r, "1:N")))
                .Concat(entity.ManyToMany.Select(r => (r, "N:N"))))
            {
                if (!seen.Add(relationship.SchemaName))
                {
                    continue;
                }

                var shape = $"{entity.LogicalName} {kind} {relationship.Entity}";
                yield return Create(relationship.SchemaName, shape, $"{relationship.SchemaName} (relationship)\n{shape}", RelationshipTags);
            }
        }
    }

    /// <summary>The options of a choice.</summary>
    /// <param name="insertion">Gives the text to insert for an option; the number itself when null.</param>
    public static IEnumerable<CompletionItem> Options(OptionSetDefinition optionSet, string attributeLogicalName, Func<OptionDefinition, string?>? insertion, bool shareList)
    {
        var index = 0;
        foreach (var option in optionSet.Options)
        {
            var value = option.Value.ToString(CultureInfo.InvariantCulture);
            var label = string.IsNullOrWhiteSpace(option.Label) ? value : option.Label!;
            var insert = insertion?.Invoke(option) ?? value;

            var description = new StringBuilder();
            description.Append(label).Append(" = ").Append(value);
            AppendLine(description, option.Description);
            AppendLine(description, $"Option of {attributeLogicalName}" + (optionSet.DisplayName == null ? string.Empty : $" ({optionSet.DisplayName})"));
            if (insert != value)
            {
                AppendLine(description, "Inserts " + insert);
            }

            var properties = ImmutableDictionary<string, string>.Empty
                .Add(DescriptionKey, description.ToString())
                .Add(InsertionKey, insert);

            // Typing either the label or the number finds the option; the list keeps Dataverse's order.
            yield return CompletionItem.Create(
                displayText: label,
                filterText: label + " " + value,
                sortText: (shareList ? TopOfList : string.Empty) + index.ToString("D5", CultureInfo.InvariantCulture),
                properties: properties,
                tags: OptionTags,
                rules: KeywordRules,
                displayTextSuffix: " = " + value,
                inlineDescription: attributeLogicalName,
                isComplexTextEdit: true);
            index++;
        }
    }

    public static IEnumerable<CompletionItem> Keywords(IEnumerable<string> keywords, string what)
    {
        var index = 0;
        foreach (var keyword in keywords)
        {
            yield return CompletionItem.Create(
                displayText: keyword,
                filterText: keyword,
                sortText: index.ToString("D5", CultureInfo.InvariantCulture),
                properties: ImmutableDictionary<string, string>.Empty.Add(DescriptionKey, $"{keyword} (FetchXML {what})"),
                tags: KeywordTags,
                rules: KeywordRules);
            index++;
        }
    }

    /// <summary>Columns that are real data; the platform's computed companions are left out.</summary>
    private static bool IsColumn(AttributeDefinition attribute) => string.IsNullOrEmpty(attribute.AttributeOf);

    private static CompletionItem Create(string name, string? inlineDescription, string description, ImmutableArray<string> tags) =>
        CompletionItem.Create(
            displayText: name,
            filterText: name,
            sortText: name,
            properties: ImmutableDictionary<string, string>.Empty.Add(DescriptionKey, description),
            tags: tags,
            rules: CompletionItemRules.Default,
            inlineDescription: inlineDescription);

    private static string InlineDescription(AttributeDefinition attribute) =>
        string.IsNullOrEmpty(attribute.DisplayName) ? attribute.Type : attribute.DisplayName + " · " + attribute.Type;

    private static string Describe(AttributeDefinition attribute, string owner)
    {
        var description = new StringBuilder();
        description.Append(attribute.DisplayName ?? attribute.LogicalName)
            .Append(" (").Append(attribute.Type).Append(" column of ").Append(owner).Append(')');
        AppendLine(description, attribute.Description);

        var facts = new List<string>();
        if (!string.IsNullOrEmpty(attribute.RequiredLevel) && attribute.RequiredLevel != "None")
        {
            facts.Add("Required: " + attribute.RequiredLevel);
        }

        if (attribute.MaxLength.HasValue)
        {
            facts.Add("Max length: " + attribute.MaxLength.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (attribute.Targets.Count > 0)
        {
            facts.Add("Targets: " + string.Join(", ", attribute.Targets));
        }

        if (!attribute.IsValidForCreate && !attribute.IsValidForUpdate)
        {
            facts.Add("Read-only");
        }

        if (facts.Count > 0)
        {
            AppendLine(description, string.Join(" · ", facts));
        }

        var options = attribute.OptionSet?.Options;
        if (options != null && options.Count > 0)
        {
            var shown = options.Take(6).Select(o => $"{o.Value.ToString(CultureInfo.InvariantCulture)} {o.Label}");
            AppendLine(description, "Options: " + string.Join(", ", shown) + (options.Count > 6 ? ", …" : string.Empty));
        }
        else if (attribute.GlobalOptionSet != null)
        {
            AppendLine(description, "Global choice: " + attribute.GlobalOptionSet);
        }

        return description.ToString();
    }

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            builder.Append('\n').Append(line!.Trim());
        }
    }
}
