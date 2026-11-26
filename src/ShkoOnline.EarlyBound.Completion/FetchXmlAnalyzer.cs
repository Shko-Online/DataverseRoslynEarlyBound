namespace ShkoOnline.EarlyBound.Completion;

internal enum FetchXmlRequestKind
{
    None,

    /// <summary>A table logical name.</summary>
    Entity,

    /// <summary>A column of <see cref="FetchXmlRequest.EntityName"/>.</summary>
    Attribute,

    /// <summary>A value of the choice column <see cref="FetchXmlRequest.AttributeName"/>.</summary>
    OptionValue,

    /// <summary>A condition operator.</summary>
    Operator,

    /// <summary>A link-type of a link-entity.</summary>
    LinkType,

    /// <summary>The type of a filter: and / or.</summary>
    FilterType,
}

internal readonly struct FetchXmlRequest
{
    public FetchXmlRequest(FetchXmlRequestKind kind, string? entityName = null, string? attributeName = null)
    {
        Kind = kind;
        EntityName = entityName;
        AttributeName = attributeName;
    }

    public FetchXmlRequestKind Kind { get; }

    public string? EntityName { get; }

    public string? AttributeName { get; }
}

/// <summary>
/// Reads the part of a FetchXML string that precedes the caret and says what belongs at the caret.
/// It is a forgiving scanner, not an XML parser: the text is by definition incomplete.
/// </summary>
internal static class FetchXmlAnalyzer
{
    private static readonly string[] Markers = { "<fetch", "<entity", "<link-entity", "<attribute", "<condition", "<filter", "<order" };

    /// <summary>True when a string looks like FetchXML at all.</summary>
    public static bool LooksLikeFetchXml(string before, string after)
    {
        foreach (var marker in Markers)
        {
            if (before.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0
                || after.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static FetchXmlRequest Analyze(string before, string after)
    {
        var scan = Scan(before);
        switch (scan.Position)
        {
            case CaretPosition.AttributeValue:
                return ForAttributeValue(scan, after);

            case CaretPosition.Text when scan.Stack.Count > 0 && scan.Stack[scan.Stack.Count - 1].Name == "value":
                // <condition attribute="statuscode" operator="in"><value>|</value></condition>
                for (var i = scan.Stack.Count - 2; i >= 0; i--)
                {
                    if (scan.Stack[i].Name == "condition")
                    {
                        return ForConditionValue(scan, scan.Stack[i], i);
                    }
                }

                return default;

            default:
                return default;
        }
    }

    private static FetchXmlRequest ForAttributeValue(ScanResult scan, string after)
    {
        var element = scan.Current!;
        var attribute = scan.AttributeName!;

        // Attributes of the same tag that come after the caret matter too: <attribute alias="x" name="|"
        string? Sibling(string name) =>
            element.Attributes.TryGetValue(name, out var value) ? value : FindAttributeInRestOfTag(after, name);

        switch (element.Name)
        {
            case "entity" when attribute == "name":
                return new FetchXmlRequest(FetchXmlRequestKind.Entity);

            case "link-entity":
                switch (attribute)
                {
                    case "name":
                        return new FetchXmlRequest(FetchXmlRequestKind.Entity);
                    case "from":
                        // "from" is a column of the linked table itself.
                        return new FetchXmlRequest(FetchXmlRequestKind.Attribute, Sibling("name"));
                    case "to":
                        return new FetchXmlRequest(FetchXmlRequestKind.Attribute, EnclosingEntity(scan, scan.Stack.Count));
                    case "link-type":
                        return new FetchXmlRequest(FetchXmlRequestKind.LinkType);
                    default:
                        return default;
                }

            case "attribute" when attribute == "name":
            case "order" when attribute == "attribute":
                return new FetchXmlRequest(FetchXmlRequestKind.Attribute, EnclosingEntity(scan, scan.Stack.Count));

            case "condition":
                switch (attribute)
                {
                    case "attribute":
                        return new FetchXmlRequest(FetchXmlRequestKind.Attribute, ConditionEntity(scan, Sibling("entityname"), scan.Stack.Count));
                    case "entityname":
                        return new FetchXmlRequest(FetchXmlRequestKind.Entity);
                    case "operator":
                        return new FetchXmlRequest(FetchXmlRequestKind.Operator);
                    case "value":
                        return new FetchXmlRequest(
                            FetchXmlRequestKind.OptionValue,
                            ConditionEntity(scan, Sibling("entityname"), scan.Stack.Count),
                            Sibling("attribute"));
                    default:
                        return default;
                }

            case "filter" when attribute == "type":
                return new FetchXmlRequest(FetchXmlRequestKind.FilterType);

            default:
                return default;
        }
    }

    private static FetchXmlRequest ForConditionValue(ScanResult scan, Element condition, int conditionDepth)
    {
        condition.Attributes.TryGetValue("attribute", out var attribute);
        condition.Attributes.TryGetValue("entityname", out var entityName);
        return new FetchXmlRequest(FetchXmlRequestKind.OptionValue, ConditionEntity(scan, entityName, conditionDepth), attribute);
    }

    /// <summary>The table a condition is about: the one named by <c>entityname</c> (a table or a link alias), else the enclosing one.</summary>
    private static string? ConditionEntity(ScanResult scan, string? entityName, int depth)
    {
        if (!string.IsNullOrEmpty(entityName))
        {
            return scan.Aliases.TryGetValue(entityName!, out var aliased) ? aliased : entityName;
        }

        return EnclosingEntity(scan, depth);
    }

    /// <summary>The nearest entity or link-entity among the first <paramref name="depth"/> open elements.</summary>
    private static string? EnclosingEntity(ScanResult scan, int depth)
    {
        for (var i = Math.Min(depth, scan.Stack.Count) - 1; i >= 0; i--)
        {
            var element = scan.Stack[i];
            if ((element.Name == "entity" || element.Name == "link-entity")
                && element.Attributes.TryGetValue("name", out var name)
                && name.Length > 0)
            {
                return name;
            }
        }

        return null;
    }

    private static string? FindAttributeInRestOfTag(string after, string name)
    {
        var end = after.IndexOf('>');
        var rest = end < 0 ? after : after.Substring(0, end);

        // The first quote closes the value the caret is in; attributes follow it.
        var i = 0;
        while (i < rest.Length)
        {
            var index = rest.IndexOf(name, i, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var before = index == 0 ? ' ' : rest[index - 1];
            var j = index + name.Length;
            while (j < rest.Length && char.IsWhiteSpace(rest[j]))
            {
                j++;
            }

            if (char.IsWhiteSpace(before) && j < rest.Length && rest[j] == '=')
            {
                j++;
                while (j < rest.Length && char.IsWhiteSpace(rest[j]))
                {
                    j++;
                }

                if (j < rest.Length && (rest[j] == '"' || rest[j] == '\''))
                {
                    var close = rest.IndexOf(rest[j], j + 1);
                    if (close > j)
                    {
                        return rest.Substring(j + 1, close - j - 1);
                    }
                }

                return null;
            }

            i = index + name.Length;
        }

        return null;
    }

    private enum CaretPosition
    {
        /// <summary>Between tags.</summary>
        Text,

        /// <summary>Inside an attribute value.</summary>
        AttributeValue,

        /// <summary>Somewhere in a tag where nothing is completed.</summary>
        Other,
    }

    private sealed class Element
    {
        public Element(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ScanResult
    {
        /// <summary>Elements that are open at the caret, outermost first.</summary>
        public List<Element> Stack { get; } = new();

        /// <summary>link-entity alias → table logical name.</summary>
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

        public CaretPosition Position { get; set; } = CaretPosition.Text;

        /// <summary>The element whose tag the caret is in.</summary>
        public Element? Current { get; set; }

        public string? AttributeName { get; set; }
    }

    private static ScanResult Scan(string text)
    {
        var result = new ScanResult();
        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            if (text[i] != '<')
            {
                i++;
                continue;
            }

            if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Position = CaretPosition.Other;
                    return result;
                }

                i = end + 3;
                continue;
            }

            if (i + 1 < n && (text[i + 1] == '?' || text[i + 1] == '!'))
            {
                var end = text.IndexOf('>', i);
                if (end < 0)
                {
                    result.Position = CaretPosition.Other;
                    return result;
                }

                i = end + 1;
                continue;
            }

            if (i + 1 < n && text[i + 1] == '/')
            {
                var end = text.IndexOf('>', i);
                if (end < 0)
                {
                    result.Position = CaretPosition.Other;
                    return result;
                }

                var closing = text.Substring(i + 2, end - i - 2).Trim().ToLowerInvariant();
                for (var s = result.Stack.Count - 1; s >= 0; s--)
                {
                    if (result.Stack[s].Name == closing)
                    {
                        result.Stack.RemoveRange(s, result.Stack.Count - s);
                        break;
                    }
                }

                i = end + 1;
                continue;
            }

            // An opening tag.
            i++;
            var nameStart = i;
            while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
            {
                i++;
            }

            var element = new Element(text.Substring(nameStart, i - nameStart).ToLowerInvariant());
            if (i >= n)
            {
                result.Position = CaretPosition.Other;
                return result;
            }

            var closed = false;
            while (!closed)
            {
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                if (i >= n)
                {
                    result.Position = CaretPosition.Other;
                    result.Current = element;
                    return result;
                }

                if (text[i] == '>')
                {
                    i++;
                    Register(result, element);
                    result.Stack.Add(element);
                    closed = true;
                    continue;
                }

                if (text[i] == '/')
                {
                    // Self-closing: it is never the parent of anything, but its alias still counts.
                    i++;
                    if (i < n && text[i] == '>')
                    {
                        i++;
                    }

                    Register(result, element);
                    closed = true;
                    continue;
                }

                var attributeStart = i;
                while (i < n && text[i] != '=' && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '/')
                {
                    i++;
                }

                var attributeName = text.Substring(attributeStart, i - attributeStart).ToLowerInvariant();
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                if (i >= n || text[i] != '=')
                {
                    if (i >= n)
                    {
                        result.Position = CaretPosition.Other;
                        result.Current = element;
                        return result;
                    }

                    continue;
                }

                i++;
                while (i < n && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                if (i >= n || (text[i] != '"' && text[i] != '\''))
                {
                    if (i >= n)
                    {
                        result.Position = CaretPosition.Other;
                        result.Current = element;
                        return result;
                    }

                    continue;
                }

                var quote = text[i];
                i++;
                var valueStart = i;
                var valueEnd = text.IndexOf(quote, i);
                if (valueEnd < 0)
                {
                    // The text ends inside this value: this is where the caret is.
                    result.Position = CaretPosition.AttributeValue;
                    result.Current = element;
                    result.AttributeName = attributeName;
                    return result;
                }

                element.Attributes[attributeName] = text.Substring(valueStart, valueEnd - valueStart);
                i = valueEnd + 1;
            }
        }

        result.Position = CaretPosition.Text;
        return result;
    }

    private static void Register(ScanResult result, Element element)
    {
        if (element.Name == "link-entity"
            && element.Attributes.TryGetValue("alias", out var alias)
            && element.Attributes.TryGetValue("name", out var name)
            && alias.Length > 0)
        {
            result.Aliases[alias] = name;
        }
    }

    public static readonly string[] Operators =
    {
        "eq", "ne", "neq", "gt", "ge", "lt", "le", "like", "not-like", "in", "not-in", "between", "not-between",
        "null", "not-null", "begins-with", "not-begin-with", "ends-with", "not-end-with", "contain-values",
        "not-contain-values", "yesterday", "today", "tomorrow", "last-seven-days", "next-seven-days", "last-week",
        "this-week", "next-week", "last-month", "this-month", "next-month", "on", "on-or-before", "on-or-after",
        "last-year", "this-year", "next-year", "last-x-hours", "next-x-hours", "last-x-days", "next-x-days",
        "last-x-weeks", "next-x-weeks", "last-x-months", "next-x-months", "last-x-years", "next-x-years",
        "olderthan-x-minutes", "olderthan-x-hours", "olderthan-x-days", "olderthan-x-weeks", "olderthan-x-months",
        "olderthan-x-years", "eq-userid", "ne-userid", "eq-userteams", "eq-useroruserteams",
        "eq-useroruserhierarchy", "eq-useroruserhierarchyandteams", "eq-businessid", "ne-businessid",
        "eq-userlanguage", "this-fiscal-year", "this-fiscal-period", "next-fiscal-year", "next-fiscal-period",
        "last-fiscal-year", "last-fiscal-period", "last-x-fiscal-years", "last-x-fiscal-periods",
        "next-x-fiscal-years", "next-x-fiscal-periods", "in-fiscal-year", "in-fiscal-period",
        "in-fiscal-period-and-year", "in-or-before-fiscal-period-and-year", "in-or-after-fiscal-period-and-year",
        "under", "eq-or-under", "not-under", "above", "eq-or-above",
    };

    public static readonly string[] LinkTypes =
    {
        "inner", "outer", "any", "not any", "all", "not all", "exists", "in", "matchfirstrowusingcrossapply",
    };

    public static readonly string[] FilterTypes = { "and", "or" };
}
