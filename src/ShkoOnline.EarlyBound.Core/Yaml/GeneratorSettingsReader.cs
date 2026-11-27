using ShkoOnline.EarlyBound.Configuration;
using YamlDotNet.RepresentationModel;

namespace ShkoOnline.EarlyBound.Yaml;

/// <summary>Reads the <c>generator:</c> section of <c>earlybound.yaml</c> into <see cref="GeneratorSettings"/>.</summary>
internal static class GeneratorSettingsReader
{
    private delegate void Setter(GeneratorSettings settings, YamlNode value, MetadataYamlReader.ReadContext context);

    private static readonly Dictionary<string, Setter> Setters = new(StringComparer.Ordinal)
    {
        ["namespace"] = (s, v, c) => s.Namespace = c.String(v),
        ["serviceContextName"] = (s, v, c) => s.ServiceContextName = c.String(v),
        ["generateTypesAsInternal"] = (s, v, c) => s.GenerateTypesAsInternal = c.Bool(v, "generateTypesAsInternal") ?? s.GenerateTypesAsInternal,
        ["generateOnlyUsedAttributes"] = (s, v, c) => s.GenerateOnlyUsedAttributes = c.Bool(v, "generateOnlyUsedAttributes") ?? s.GenerateOnlyUsedAttributes,
        ["alwaysGenerate"] = (s, v, c) => s.AlwaysGenerate = c.StringList(v, "alwaysGenerate"),

        ["camelCaseClassNames"] = (s, v, c) => s.CamelCaseClassNames = c.Bool(v, "camelCaseClassNames") ?? s.CamelCaseClassNames,
        ["camelCaseMemberNames"] = (s, v, c) => s.CamelCaseMemberNames = c.Bool(v, "camelCaseMemberNames") ?? s.CamelCaseMemberNames,
        ["camelCaseOptionSetNames"] = (s, v, c) => s.CamelCaseOptionSetNames = c.Bool(v, "camelCaseOptionSetNames") ?? s.CamelCaseOptionSetNames,
        ["camelCaseCustomWords"] = (s, v, c) => s.CamelCaseCustomWords = c.StringList(v, "camelCaseCustomWords"),
        ["tokenCapitalizationOverrides"] = (s, v, c) => s.TokenCapitalizationOverrides = c.StringList(v, "tokenCapitalizationOverrides"),
        ["useLogicalNames"] = (s, v, c) => s.UseLogicalNames = c.Bool(v, "useLogicalNames") ?? s.UseLogicalNames,
        ["localOptionSetFormat"] = (s, v, c) => s.LocalOptionSetFormat = c.String(v) ?? s.LocalOptionSetFormat,
        ["useCrmSvcUtilStateEnumNamingConvention"] = (s, v, c) => s.UseCrmSvcUtilStateEnumNamingConvention = c.Bool(v, "useCrmSvcUtilStateEnumNamingConvention") ?? s.UseCrmSvcUtilStateEnumNamingConvention,
        ["useDisplayNameForBpfName"] = (s, v, c) => s.UseDisplayNameForBpfName = c.Bool(v, "useDisplayNameForBpfName") ?? s.UseDisplayNameForBpfName,
        ["invalidCSharpNamePrefix"] = (s, v, c) => s.InvalidCSharpNamePrefix = c.String(v) ?? s.InvalidCSharpNamePrefix,
        ["adjustCasingForEnumOptions"] = (s, v, c) => s.AdjustCasingForEnumOptions = c.Bool(v, "adjustCasingForEnumOptions") ?? s.AdjustCasingForEnumOptions,
        ["labelTextReplacement"] = (s, v, c) => s.LabelTextReplacement = Pairs(v, "labelTextReplacement", c),
        ["optionNameOverrides"] = (s, v, c) => s.OptionNameOverrides = Pairs(v, "optionNameOverrides", c),
        ["entityClassNameOverrides"] = (s, v, c) => s.EntityClassNameOverrides = Map(v, "entityClassNameOverrides", c),
        ["entityAttributeSpecifiedNames"] = (s, v, c) => s.EntityAttributeSpecifiedNames = ListMap(v, "entityAttributeSpecifiedNames", c),
        ["optionSetNames"] = (s, v, c) => s.OptionSetNames = Map(v, "optionSetNames", c),

        ["generateAttributeNameConsts"] = (s, v, c) => s.GenerateAttributeNameConsts = c.Bool(v, "generateAttributeNameConsts") ?? s.GenerateAttributeNameConsts,
        ["attributeConstsClassName"] = (s, v, c) => s.AttributeConstsClassName = c.String(v) ?? s.AttributeConstsClassName,
        ["generateAnonymousTypeConstructor"] = (s, v, c) => s.GenerateAnonymousTypeConstructor = c.Bool(v, "generateAnonymousTypeConstructor") ?? s.GenerateAnonymousTypeConstructor,
        ["generateConstructorsSansLogicalName"] = (s, v, c) => s.GenerateConstructorsSansLogicalName = c.Bool(v, "generateConstructorsSansLogicalName") ?? s.GenerateConstructorsSansLogicalName,
        ["generateEntityRelationships"] = (s, v, c) => s.GenerateEntityRelationships = c.Bool(v, "generateEntityRelationships") ?? s.GenerateEntityRelationships,
        ["emitEntityETC"] = (s, v, c) => s.EmitEntityEtc = c.Bool(v, "emitEntityETC") ?? s.EmitEntityEtc,
        ["emitVirtualAttributes"] = (s, v, c) => s.EmitVirtualAttributes = c.Bool(v, "emitVirtualAttributes") ?? s.EmitVirtualAttributes,
        ["generateOptionSetMetadataAttribute"] = (s, v, c) => s.GenerateOptionSetMetadataAttribute = c.Bool(v, "generateOptionSetMetadataAttribute") ?? s.GenerateOptionSetMetadataAttribute,
        ["addOptionSetMetadataAttribute"] = (s, v, c) => s.AddOptionSetMetadataAttribute = c.Bool(v, "addOptionSetMetadataAttribute") ?? s.AddOptionSetMetadataAttribute,
        ["replaceEnumPropertiesWithOptionSet"] = (s, v, c) => s.ReplaceEnumPropertiesWithOptionSet = c.Bool(v, "replaceEnumPropertiesWithOptionSet") ?? s.ReplaceEnumPropertiesWithOptionSet,
        ["makeReadonlyFieldsEditable"] = (s, v, c) => s.MakeReadonlyFieldsEditable = c.Bool(v, "makeReadonlyFieldsEditable") ?? s.MakeReadonlyFieldsEditable,
        ["makeAllFieldsEditable"] = (s, v, c) => s.MakeAllFieldsEditable = c.Bool(v, "makeAllFieldsEditable") ?? s.MakeAllFieldsEditable,
        ["addDebuggerNonUserCode"] = (s, v, c) => s.AddDebuggerNonUserCode = c.Bool(v, "addDebuggerNonUserCode") ?? s.AddDebuggerNonUserCode,
        ["obsoleteDeprecated"] = (s, v, c) => s.ObsoleteDeprecated = c.Bool(v, "obsoleteDeprecated") ?? s.ObsoleteDeprecated,
        ["obsoleteTokens"] = (s, v, c) => s.ObsoleteTokens = c.StringList(v, "obsoleteTokens"),
        ["suppressGeneratedCodeAttribute"] = (s, v, c) => s.SuppressGeneratedCodeAttribute = c.Bool(v, "suppressGeneratedCodeAttribute") ?? s.SuppressGeneratedCodeAttribute,
        ["generateProxyTypesAssemblyAttribute"] = (s, v, c) => s.GenerateProxyTypesAssemblyAttribute = c.Bool(v, "generateProxyTypesAssemblyAttribute") ?? s.GenerateProxyTypesAssemblyAttribute,
    };

    /// <summary>The keys accepted in the <c>generator:</c> section.</summary>
    public static IEnumerable<string> KnownKeys => Setters.Keys;

    public static void Read(YamlNode node, GeneratorSettings settings, MetadataYamlReader.ReadContext context)
    {
        if (node is not YamlMappingNode map)
        {
            if (!MetadataYamlReader.IsNull(node))
            {
                context.Error(node, "'generator' must be a mapping.");
            }

            return;
        }

        foreach (var pair in map.Children)
        {
            var key = context.Key(pair.Key);
            if (Setters.TryGetValue(key, out var setter))
            {
                setter(settings, pair.Value, context);
            }
            else
            {
                context.UnknownSetting(pair.Key, "generator");
            }
        }
    }

    private static Dictionary<string, string> Map(YamlNode node, string name, MetadataYamlReader.ReadContext context)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Pairs(node, name, context))
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static List<KeyValuePair<string, string>> Pairs(YamlNode node, string name, MetadataYamlReader.ReadContext context)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (node is not YamlMappingNode map)
        {
            if (!MetadataYamlReader.IsNull(node))
            {
                context.Error(node, $"'{name}' must be a mapping of name to value.");
            }

            return result;
        }

        foreach (var pair in map.Children)
        {
            var key = context.Key(pair.Key);
            if (key.Length == 0)
            {
                continue;
            }

            // An explicit empty value ("$": "") is meaningful here, so do not treat it as null.
            var value = pair.Value is YamlScalarNode scalar ? scalar.Value ?? string.Empty : null;
            if (value == null)
            {
                context.Error(pair.Value, $"The value of '{key}' in '{name}' must be text.");
                continue;
            }

            result.Add(new KeyValuePair<string, string>(key, value));
        }

        return result;
    }

    private static Dictionary<string, List<string>> ListMap(YamlNode node, string name, MetadataYamlReader.ReadContext context)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (node is not YamlMappingNode map)
        {
            if (!MetadataYamlReader.IsNull(node))
            {
                context.Error(node, $"'{name}' must be a mapping of table name to a list of names.");
            }

            return result;
        }

        foreach (var pair in map.Children)
        {
            var key = context.Key(pair.Key);
            if (key.Length > 0)
            {
                result[key] = context.StringList(pair.Value, name + "." + key);
            }
        }

        return result;
    }
}
