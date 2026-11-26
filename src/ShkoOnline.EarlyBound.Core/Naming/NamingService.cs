// The naming rules in this file follow NamingService from DLaB.Xrm.XrmToolBoxTools
// (the Early Bound Generator), Copyright (c) 2015 Daryl LaBar, MIT License, layered on the
// default names of Microsoft's model builder. See THIRD-PARTY-NOTICES.md.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Naming;

/// <summary>Derives C# names from Dataverse metadata, following the Early Bound Generator's conventions.</summary>
public sealed class NamingService
{
    /// <summary>Appended when a member would otherwise have the name of its class.</summary>
    public const string PropertyCollisionPostfix = "__Member";

    private const string ConflictSuffix = "1";

    /// <summary>Members of Microsoft.Xrm.Sdk.Entity a generated property may not hide.</summary>
    private static readonly HashSet<string> ReservedAttributeNames = new(StringComparer.Ordinal)
    {
        "Attributes", "EntityState", "ExtensionData", "FormattedValues", "HasLazyFileAttribute", "Id", "Item",
        "KeyAttributes", "LazyFileAttributeKey", "LazyFileAttributeValue", "LazyFileSizeAttributeKey",
        "LazyFileSizeAttributeValue", "LogicalName", "RelatedEntities", "RowVersion",
    };

    private static readonly Dictionary<string, string> CasingByGlobalOptionSet = new(StringComparer.Ordinal)
    {
        { "budgetstatus", "BudgetStatus" },
        { "componentstate", "ComponentState" },
        { "componenttype", "ComponentType" },
        { "connectionrole_category", "ConnectionRole_Category" },
        { "convertrule_channelactivity", "ConvertRule_ChannelActivity" },
        { "dependencytype", "DependencyType" },
        { "emailserverprofile_authenticationprotocol", "EmailServerProfile_AuthenticationProtocol" },
        { "field_security_permission_type", "Field_Security_Permission_Type" },
        { "goal_fiscalperiod", "Goal_FiscalPeriod" },
        { "goal_fiscalyear", "Goal_FiscalYear" },
        { "incident_caseorigincode", "Incident_CaseOriginCode" },
        { "initialcommunication", "InitialCommunication" },
        { "lead_salesstage", "Lead_SalesStage" },
        { "metric_goaltype", "Metric_GoalType" },
        { "need", "Need" },
        { "opportunity_salesstage", "Opportunity_SalesStage" },
        { "principalsyncattributemapping_syncdirection", "PrincipalSyncAttributeMapping_SyncDirection" },
        { "processstage_category", "Processstage_Category" },
        { "purchaseprocess", "PurchaseProcess" },
        { "purchasetimeframe", "PurchaseTimeFrame" },
        { "qooi_pricingerrorcode", "Qooi_PricingErrorCode" },
        { "qooiproduct_producttype", "QooiProduct_ProductType" },
        { "qooiproduct_propertiesconfigurationstatus", "QooiProduct_PropertiesConfigurationStatus" },
        { "recurrencerule_monthofyear", "RecurrenceRule_MonthOfYear" },
        { "servicestage", "ServiceStage" },
        { "sharepoint_validationstatus", "SharePoint_ValidationStatus" },
        { "sharepoint_validationstatusreason", "SharePoint_ValidationStatusReason" },
        { "sharepointdocumentlocation_locationtype", "SharePointDocumentLocation_LocationType" },
        { "socialactivity_postmessagetype", "SocialActivity_PostMessageType" },
        { "socialprofile_community", "SocialProfile_Community" },
        { "syncattributemapping_syncdirection", "SyncAttributeMapping_SyncDirection" },
        { "workflow_runas", "Workflow_RunAs" },
        { "workflow_stage", "Workflow_Stage" },
        { "workflowlog_objecttypecode", "WorkflowLog_ObjectTypeCode" },
    };

    private static readonly string[] OptionSetPreferredEndings = { "StateCode", "Status", "State" };
    private static readonly Regex InvalidNameChars = new("[^a-zA-Z0-9_]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly GeneratorSettings _settings;
    private readonly CamelCaser _caser;
    private readonly Dictionary<string, string> _entityNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bpfLogicalNamesByClassName = new(StringComparer.Ordinal);

    public NamingService(GeneratorSettings settings)
    {
        _settings = settings;
        _caser = new CamelCaser(settings.CamelCaseCustomWords, settings.TokenCapitalizationOverrides);
    }

    /// <summary>The class name of a table.</summary>
    public string GetNameForEntity(EntityDefinition entity)
    {
        if (_entityNames.TryGetValue(entity.LogicalName, out var known))
        {
            return known;
        }

        string name;
        if (_settings.EntityClassNameOverrides.TryGetValue(entity.LogicalName, out var overridden)
            && !string.IsNullOrWhiteSpace(overridden))
        {
            // An explicit name is used exactly as written.
            name = overridden.Trim();
        }
        else
        {
            name = CreateValidName(entity.SchemaName);
            if (_settings.UseDisplayNameForBpfName)
            {
                name = GetBpfNameToDisplay(name, entity);
            }

            if (_settings.CamelCaseClassNames)
            {
                name = _caser.Case(name);
            }
        }

        name = EnsureIdentifier(name);
        _entityNames[entity.LogicalName] = name;
        return name;
    }

    /// <summary>The property name of a column.</summary>
    public string GetNameForAttribute(EntityDefinition entity, AttributeDefinition attribute) =>
        GetNameForAttribute(entity, attribute, _settings.CamelCaseMemberNames, _settings.UseLogicalNames);

    private string GetNameForAttribute(EntityDefinition entity, AttributeDefinition attribute, bool camelCase, bool useLogicalNames)
    {
        if (_settings.EntityAttributeSpecifiedNames.TryGetValue(entity.LogicalName, out var specifiedNames))
        {
            foreach (var specified in specifiedNames)
            {
                if (string.Equals(specified, attribute.LogicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return specified;
                }
            }
        }

        var entityName = GetNameForEntity(entity);
        string name;
        if (useLogicalNames)
        {
            name = attribute.LogicalName;
        }
        else
        {
            name = CreateValidName(attribute.SchemaName);
            if (ReservedAttributeNames.Contains(name) || name == entityName)
            {
                name += ConflictSuffix;
            }

            if (camelCase)
            {
                name = _caser.Case(name);
            }
        }

        if (entityName == name)
        {
            name += PropertyCollisionPostfix;
        }

        return EnsureIdentifier(name);
    }

    /// <summary>The property name of a relationship.</summary>
    /// <param name="rolePrefix"><c>Referencing</c> or <c>Referenced</c> for self-referencing relationships, otherwise null.</param>
    /// <param name="attributeNames">Property names already taken by the table's columns.</param>
    public string GetNameForRelationship(
        EntityDefinition entity,
        RelationshipDefinition relationship,
        string? rolePrefix,
        ICollection<string> attributeNames,
        EntityDefinition? bpfEntity)
    {
        var name = CreateValidName((rolePrefix ?? string.Empty) + relationship.SchemaName);
        if (ReservedAttributeNames.Contains(name)
            || name == GetNameForEntity(entity)
            || attributeNames.Contains(name))
        {
            name += ConflictSuffix;
        }

        if (_settings.UseDisplayNameForBpfName && bpfEntity != null)
        {
            name = GetBpfNameToDisplay(name, bpfEntity);
        }

        if (_settings.CamelCaseMemberNames)
        {
            name = _caser.Case(name);
        }

        return EnsureIdentifier(name);
    }

    /// <summary>The enum name of a choice.</summary>
    /// <param name="entity">The table the choice was found on.</param>
    /// <param name="attribute">The column the choice was found on.</param>
    /// <param name="entityClassNames">All class names, which an enum must not reuse.</param>
    public string GetNameForOptionSet(
        EntityDefinition entity,
        AttributeDefinition attribute,
        OptionSetDefinition optionSet,
        ICollection<string> entityClassNames)
    {
        var name = GetPossiblyConflictedNameForOptionSet(entity, attribute, optionSet);
        while (entityClassNames.Contains(name))
        {
            name += "_Enum";
        }

        if (_settings.UseCrmSvcUtilStateEnumNamingConvention
            && !optionSet.IsGlobal
            && attribute.Type == AttributeTypes.State
            && name.EndsWith("_statecode", StringComparison.OrdinalIgnoreCase))
        {
            name = GetNameForEntity(entity) + "State";
        }

        if (_settings.OptionSetNames.TryGetValue(name, out var overridden) && !string.IsNullOrWhiteSpace(overridden))
        {
            return EnsureIdentifier(overridden.Trim());
        }

        return EnsureIdentifier(name);
    }

    private string GetPossiblyConflictedNameForOptionSet(EntityDefinition entity, AttributeDefinition attribute, OptionSetDefinition optionSet)
    {
        var defaultName = CreateValidName(optionSet.Name.Length > 0
            ? optionSet.Name
            : entity.LogicalName + "_" + attribute.LogicalName);

        if (_settings.UseDisplayNameForBpfName && defaultName.EndsWith("_statecode", StringComparison.OrdinalIgnoreCase))
        {
            var end = "_" + defaultName.Split('_').Last();
            var prefix = defaultName.Substring(0, defaultName.Length - end.Length);
            defaultName = GetBpfNameToDisplay(prefix, entity) + end;
        }

        if (!optionSet.IsGlobal)
        {
            if (attribute.Type == AttributeTypes.Picklist)
            {
                // A column's own choice is named after the class and the column.
                return string.Format(
                    CultureInfo.InvariantCulture,
                    _settings.LocalOptionSetFormat,
                    GetNameForEntity(entity),
                    GetNameForAttribute(entity, attribute, _settings.CamelCaseClassNames, _settings.UseLogicalNames));
            }

            if (attribute.Type == AttributeTypes.Status && defaultName.EndsWith("statuscode", StringComparison.Ordinal))
            {
                defaultName = string.Format(
                    CultureInfo.InvariantCulture, _settings.LocalOptionSetFormat, GetNameForEntity(entity), "StatusCode");
            }
        }

        return UpdateCasingForGlobalOptionSets(defaultName, optionSet);
    }

    private string UpdateCasingForGlobalOptionSets(string name, OptionSetDefinition optionSet) =>
        CasingByGlobalOptionSet.TryGetValue(name, out var casing)
            ? casing
            : UpdateCasingForCustomGlobalOptionSets(name, optionSet);

    private string UpdateCasingForCustomGlobalOptionSets(string name, OptionSetDefinition optionSet)
    {
        var displayName = optionSet.DisplayName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            displayName = RemoveDiacritics(displayName).Replace(" ", string.Empty);
            var lower = displayName.ToLowerInvariant();
            if (lower.Length > 0)
            {
                if (name.EndsWith(lower, StringComparison.Ordinal))
                {
                    name = name.Replace(lower, displayName);
                }
                else if (name.Contains(lower)
                    && name.IndexOf(lower, StringComparison.Ordinal) == name.LastIndexOf(lower, StringComparison.Ordinal))
                {
                    // The display name appears once inside the name: capitalise it and the letter after it,
                    // so HelloWorld turns helloworldstatus into HelloWorldStatus.
                    var index = name.IndexOf(lower, StringComparison.Ordinal) + displayName.Length;
                    name = name.Replace(lower, displayName);
                    if (index < name.Length - 1)
                    {
                        name = name.Substring(0, index) + char.ToUpperInvariant(name[index]) + name.Substring(index + 1);
                    }
                }
            }
        }

        return _settings.CamelCaseClassNames
            ? _caser.Case(name, OptionSetPreferredEndings)
            : name;
    }

    /// <summary>The enum member name of every option of a choice, keyed by value.</summary>
    public Dictionary<int, string> GetNamesForOptions(OptionSetDefinition optionSet, bool isStatus)
    {
        var possibleNames = new List<string>(optionSet.Options.Count);
        var duplicates = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var option in optionSet.Options)
        {
            var name = GetPossiblyDuplicateNameForOption(option);
            possibleNames.Add(name);
            duplicates[name] = duplicates.ContainsKey(name);

            if (isStatus)
            {
                var withState = AppendState(option, name);
                duplicates[withState] = duplicates.ContainsKey(withState);
            }
        }

        var result = new Dictionary<int, string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < optionSet.Options.Count; i++)
        {
            var option = optionSet.Options[i];
            if (result.ContainsKey(option.Value))
            {
                continue;
            }

            var name = possibleNames[i];
            if (duplicates.TryGetValue(name, out var isDuplicate) && isDuplicate)
            {
                var resolved = false;
                if (isStatus)
                {
                    name = AppendState(option, name);
                    resolved = !duplicates[name];
                }

                if (!resolved)
                {
                    name += option.Value < 0
                        ? "_neg" + Math.Abs((long)option.Value).ToString(CultureInfo.InvariantCulture)
                        : "_" + option.Value.ToString(CultureInfo.InvariantCulture);
                }
            }

            // Whatever the labels are, the enum must compile.
            while (!used.Add(name))
            {
                name += "_" + option.Value.ToString(CultureInfo.InvariantCulture).Replace("-", "neg");
            }

            result[option.Value] = name;
        }

        return result;
    }

    private string GetPossiblyDuplicateNameForOption(OptionDefinition option)
    {
        var label = option.Label ?? string.Empty;
        foreach (var replacement in _settings.LabelTextReplacement)
        {
            if (replacement.Key.Length > 0)
            {
                label = label.Replace(replacement.Key, replacement.Value);
            }
        }

        string defaultName;
        if (_settings.AdjustCasingForEnumOptions)
        {
            var source = !string.IsNullOrEmpty(option.InvariantName) ? option.InvariantName! : label;
            defaultName = GetNameFromLabel(source);
            if (defaultName.Length == 0)
            {
                defaultName = "UnknownLabel" + option.Value.ToString(CultureInfo.InvariantCulture);
            }

            defaultName = RemoveDiacritics(defaultName);
        }
        else
        {
            var source = !string.IsNullOrEmpty(option.InvariantName) ? option.InvariantName! : label;
            defaultName = CreateValidName(source);
            if (defaultName.Length == 0)
            {
                defaultName = "UnknownLabel" + option.Value.ToString(CultureInfo.InvariantCulture);
            }

            defaultName = RemoveDiacritics(defaultName);
        }

        var name = GetValidCSharpName(defaultName);
        if (defaultName.Length == 0)
        {
            name += option.Value.ToString(CultureInfo.InvariantCulture);
        }

        return _settings.CamelCaseOptionSetNames
            ? _caser.Case(name)
            : name;
    }

    /// <summary>Turns a label such as "Preferred Customer" into "PreferredCustomer".</summary>
    internal string GetNameFromLabel(string label)
    {
        label = label.Replace("'s ", "s ").Replace("'S ", "S ");
        if (label.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
        {
            label = label.Substring(0, label.Length - 2) + "s";
        }

        var words = RemoveInvalidIdentifierChars(label).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder(label.Length);
        foreach (var word in words)
        {
            if (!string.IsNullOrWhiteSpace(word))
            {
                builder.Append(ToTitleCase(word));
            }
        }

        var combinedName = builder.ToString();
        foreach (var nameOverride in _settings.OptionNameOverrides)
        {
            if (nameOverride.Key.Length > 0
                && combinedName.IndexOf(nameOverride.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                combinedName = Regex.Replace(
                    combinedName,
                    Regex.Escape(nameOverride.Key),
                    nameOverride.Value.Replace("$", "$$"),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        return combinedName;
    }

    /// <summary>
    /// Lower-cases a word and capitalises its first letter, the way TextInfo.ToTitleCase does, but
    /// independent of culture and runtime so names are identical in Visual Studio and on the command line.
    /// </summary>
    private static string ToTitleCase(string word)
    {
        var chars = word.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                break;
            }
        }

        return new string(chars);
    }

    private static string RemoveInvalidIdentifierChars(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsLetterOrDigit(c)
                || category == UnicodeCategory.ConnectorPunctuation
                || category == UnicodeCategory.NonSpacingMark
                || category == UnicodeCategory.SpacingCombiningMark
                || category == UnicodeCategory.Format)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }

    private static string AppendState(OptionDefinition option, string name) =>
        name + "_" + (option.State.GetValueOrDefault() == 0 ? "Active" : "Inactive");

    private string GetValidCSharpName(string name)
    {
        name = InvalidNameChars.Replace(name, string.Empty);
        if (name.Length > 0 && !char.IsLetter(name, 0) && name[0] != '_')
        {
            name = _settings.InvalidCSharpNamePrefix + name;
        }
        else if (name.Length == 0)
        {
            name = _settings.InvalidCSharpNamePrefix;
        }

        return name;
    }

    private string GetBpfNameToDisplay(string name, EntityDefinition entity)
    {
        if (!BpfInfo.TryParse(name, out var bpf))
        {
            return name;
        }

        name = bpf.Prefix + GetValidCSharpName(entity.DisplayName ?? string.Empty) + bpf.Postfix;
        if (_bpfLogicalNamesByClassName.TryGetValue(name, out var logicalName) && logicalName != entity.LogicalName)
        {
            name += bpf.Id;
        }
        else
        {
            _bpfLogicalNamesByClassName[name] = entity.LogicalName;
        }

        return name;
    }

    /// <summary>True when a schema name is that of a business process flow table, e.g. <c>new_bpf_0123...</c>.</summary>
    public static bool IsBpfName(string name) => BpfInfo.TryParse(name, out _);

    /// <summary>The default name Microsoft's model builder gives an identifier.</summary>
    internal static string CreateValidName(string name)
    {
        var input = name.Replace("$", "CurrencySymbol_").Replace("(", "_");
        return InvalidNameChars.Replace(input, string.Empty);
    }

    /// <summary>Last line of defence: whatever the rules produced must be a usable C# identifier.</summary>
    internal static string EnsureIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length + 1);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
        }

        if (builder.Length == 0)
        {
            return "_";
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        var result = builder.ToString();
        return CSharpKeywords.IsKeyword(result) ? "@" + result : result;
    }

    internal static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private readonly struct BpfInfo
    {
        private BpfInfo(string prefix, string id, string postfix)
        {
            Prefix = prefix;
            Id = id.ToUpperInvariant();
            Postfix = postfix;
        }

        public string Id { get; }

        public string Prefix { get; }

        public string Postfix { get; }

        public static bool TryParse(string name, out BpfInfo info)
        {
            info = default;
            var parts = name.Split('_');
            var bpfIndex = Array.FindLastIndex(parts, p => string.Equals(p, "bpf", StringComparison.OrdinalIgnoreCase));
            if (bpfIndex < 0 || bpfIndex + 1 >= parts.Length)
            {
                return false;
            }

            var id = parts[bpfIndex + 1];
            if (!Guid.TryParse(id, out _))
            {
                return false;
            }

            var prefix = string.Join("_", parts.Take(bpfIndex + 1)) + "_";
            var postfix = bpfIndex + 2 < parts.Length
                ? "_" + string.Join("_", parts.Skip(bpfIndex + 2))
                : string.Empty;
            info = new BpfInfo(prefix, id, postfix);
            return true;
        }
    }
}
