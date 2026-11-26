using Microsoft.CodeAnalysis;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>What a string in code names.</summary>
internal enum NameRole
{
    None,

    /// <summary>A table logical name.</summary>
    Entity,

    /// <summary>A column logical name.</summary>
    Attribute,

    /// <summary>A relationship schema name.</summary>
    Relationship,
}

/// <summary>Knowledge about the Dataverse SDK's API surface, expressed over Roslyn symbols.</summary>
internal static class SdkSymbols
{
    private static readonly HashSet<string> EntityParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "entityName", "entityLogicalName", "logicalName", "linkFromEntityName", "linkToEntityName",
    };

    private static readonly HashSet<string> AttributeParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "attributeName", "attributeLogicalName", "attributeKey", "column", "columns",
        "linkFromAttributeName", "linkToAttributeName",
    };

    /// <summary>Parameter names recognised on a project's own helper methods that work with SDK types.</summary>
    private static readonly HashSet<string> HelperEntityParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "entityName", "entityLogicalName", "tableName", "tableLogicalName",
    };

    private static readonly HashSet<string> HelperAttributeParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "attributeName", "attributeLogicalName", "attributeNames", "columnName", "columnNames", "fieldName",
    };

    private static readonly HashSet<string> EntityProperties = new(StringComparer.Ordinal)
    {
        "EntityName", "LogicalName", "LinkFromEntityName", "LinkToEntityName", "PrimaryEntityName", "SecondaryEntityName",
    };

    private static readonly HashSet<string> AttributeProperties = new(StringComparer.Ordinal)
    {
        "AttributeName", "LinkFromAttributeName", "LinkToAttributeName",
    };

    /// <summary>Collections keyed by column logical name.</summary>
    private static readonly HashSet<string> AttributeKeyedCollections = new(StringComparer.Ordinal)
    {
        "AttributeCollection", "FormattedValueCollection", "KeyAttributeCollection",
    };

    public static bool IsSdkNamespace(INamespaceSymbol? @namespace)
    {
        if (@namespace == null || @namespace.IsGlobalNamespace)
        {
            return false;
        }

        var name = @namespace.ToDisplayString();
        return name.StartsWith("Microsoft.Xrm.Sdk", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.PowerPlatform.Dataverse", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Crm.Sdk", StringComparison.Ordinal);
    }

    public static bool IsSdkType(ITypeSymbol? type) =>
        type != null && IsSdkNamespace(type.OriginalDefinition.ContainingNamespace);

    /// <summary>True when <paramref name="type"/> is, or derives from, the SDK type with the given name.</summary>
    public static bool IsOrDerivesFrom(ITypeSymbol? type, string sdkTypeName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.Name == sdkTypeName && IsSdkType(current))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsEntity(ITypeSymbol? type) => IsOrDerivesFrom(type, "Entity");

    /// <summary>The logical name an early-bound class is bound to through <c>[EntityLogicalName]</c>.</summary>
    public static string? GetEntityLogicalName(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "EntityLogicalNameAttribute"
                    && attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is string logicalName
                    && logicalName.Length > 0)
                {
                    return logicalName;
                }
            }
        }

        return null;
    }

    /// <summary>What the string argument bound to <paramref name="parameter"/> names.</summary>
    /// <param name="member">The method, constructor or indexer the parameter belongs to.</param>
    /// <param name="receiverType">Type of the expression the member is accessed on, when there is one.</param>
    /// <param name="receiverMember">The property the receiver expression itself refers to, e.g. <c>Columns</c>.</param>
    public static NameRole RoleOfParameter(IParameterSymbol parameter, ISymbol member, ITypeSymbol? receiverType, ISymbol? receiverMember)
    {
        if (!IsNameLike(parameter.Type))
        {
            return NameRole.None;
        }

        // Collections of column names. Their Add and Contains can be declared on the framework's
        // Collection<T>, so it is the collection being called that counts, not where the method lives.
        if (parameter.Name == "key" && IsAttributeKeyedCollection(receiverType))
        {
            // Attributes["..."], FormattedValues.Contains("...") and friends.
            return NameRole.Attribute;
        }

        if ((parameter.Name == "item" || parameter.Name == "items") && IsAttributeNameList(receiverMember))
        {
            // ColumnSet.Columns.Add("..."), QueryByAttribute.Attributes.AddRange("...").
            return NameRole.Attribute;
        }

        var containingType = member.ContainingType;
        if (IsSdkType(containingType))
        {
            if (containingType.Name == "Relationship" && parameter.Name == "schemaName")
            {
                return NameRole.Relationship;
            }

            if (parameter.Name == "key" || parameter.Name == "item" || parameter.Name == "items")
            {
                // Some other SDK collection: InputParameters, SharedVariables, ...
                return NameRole.None;
            }

            if (EntityParameters.Contains(parameter.Name))
            {
                return NameRole.Entity;
            }

            if (AttributeParameters.Contains(parameter.Name))
            {
                return NameRole.Attribute;
            }

            return NameRole.None;
        }

        // A project's own helper: only when it clearly works with SDK types.
        if (member is IMethodSymbol method && TouchesSdk(method))
        {
            if (HelperEntityParameters.Contains(parameter.Name))
            {
                return NameRole.Entity;
            }

            if (HelperAttributeParameters.Contains(parameter.Name))
            {
                return NameRole.Attribute;
            }
        }

        return NameRole.None;
    }

    /// <summary>What a string assigned to, or compared with, <paramref name="property"/> names.</summary>
    public static NameRole RoleOfProperty(IPropertySymbol property)
    {
        if (property.Type.SpecialType != SpecialType.System_String || !IsSdkType(property.ContainingType))
        {
            return NameRole.None;
        }

        if (property.ContainingType.Name == "Relationship" && property.Name == "SchemaName")
        {
            return NameRole.Relationship;
        }

        if (EntityProperties.Contains(property.Name))
        {
            return NameRole.Entity;
        }

        return AttributeProperties.Contains(property.Name) ? NameRole.Attribute : NameRole.None;
    }

    public static bool IsAttributeKeyedCollection(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (AttributeKeyedCollections.Contains(current.Name) && IsSdkType(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True for <c>ColumnSet.Columns</c> and <c>QueryByAttribute.Attributes</c>: lists of column names.</summary>
    public static bool IsAttributeNameList(ISymbol? member) =>
        member is IPropertySymbol property
        && IsSdkType(property.ContainingType)
        && ((property.Name == "Columns" && property.ContainingType.Name == "ColumnSet")
            || (property.Name == "Attributes" && property.ContainingType.Name == "QueryByAttribute"));

    private static bool IsNameLike(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String || type.TypeKind == TypeKind.TypeParameter)
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType.SpecialType == SpecialType.System_String || array.ElementType.TypeKind == TypeKind.TypeParameter;
        }

        // IEnumerable<string>, List<string>, ...
        return type is INamedTypeSymbol named
            && named.TypeArguments.Length == 1
            && named.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    private static bool TouchesSdk(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        if (IsSdkType(definition.ContainingType) || IsSdkType(definition.ContainingType?.BaseType))
        {
            return true;
        }

        foreach (var parameter in definition.Parameters)
        {
            if (IsSdkType(parameter.Type))
            {
                return true;
            }
        }

        return IsSdkType(definition.ReturnType)
            || (method.ReceiverType != null && IsSdkType(method.ReceiverType));
    }
}
