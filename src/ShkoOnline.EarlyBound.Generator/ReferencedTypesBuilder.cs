using Microsoft.CodeAnalysis;
using ShkoOnline.EarlyBound.Model;

namespace ShkoOnline.EarlyBound.Generator;

/// <summary>
/// Finds the generated types a project already gets from its references: the table classes of a
/// plug-in project, as seen from its test project.
/// </summary>
internal static class ReferencedTypesBuilder
{
    public static ReferencedTypes From(Compilation compilation, EarlyBoundModel? model, CancellationToken cancellationToken)
    {
        if (model == null)
        {
            return ReferencedTypes.None;
        }

        var @namespace = FindNamespace(compilation.GlobalNamespace, model.Namespace);
        if (@namespace == null || !HasPartOutside(@namespace, compilation.Assembly))
        {
            // The usual case: nothing referenced declares the model's namespace.
            return ReferencedTypes.None;
        }

        var fieldsClassName = model.Settings.AttributeConstsClassName;
        var entities = new Dictionary<string, ReferencedEntity>(StringComparer.Ordinal);
        foreach (var entity in model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var type = FindType(@namespace, entity.ClassName, compilation);
            if (type == null
                || type.IsSealed
                || type.IsStatic
                || LogicalNameOf(type) != entity.Definition.LogicalName)
            {
                continue;
            }

            var described = Describe(type, entity.Definition.LogicalName, fieldsClassName, compilation);
            if (described != null)
            {
                entities[entity.Definition.LogicalName] = described;
            }
        }

        var typeNames = new HashSet<string>(StringComparer.Ordinal);
        void AddIfProvided(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name) && FindType(@namespace, name!.Trim(), compilation) != null)
            {
                typeNames.Add(name.Trim().TrimStart('@'));
            }
        }

        foreach (var optionSet in model.OptionSets)
        {
            AddIfProvided(optionSet.EnumName);
        }

        AddIfProvided(EarlyBoundModelBuilder.OptionSetMetadataAttributeName);
        AddIfProvided(EarlyBoundModelBuilder.OptionSetExtensionName);
        AddIfProvided(model.Settings.ServiceContextName);

        return entities.Count == 0 && typeNames.Count == 0
            ? ReferencedTypes.None
            : new ReferencedTypes(entities, typeNames);
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol global, string name)
    {
        var current = global;
        foreach (var part in name.Split('.'))
        {
            INamespaceSymbol? next = null;
            foreach (var member in current.GetNamespaceMembers())
            {
                if (member.Name == part)
                {
                    next = member;
                    break;
                }
            }

            if (next == null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static bool HasPartOutside(INamespaceSymbol @namespace, IAssemblySymbol assembly)
    {
        foreach (var part in @namespace.ConstituentNamespaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(part.ContainingAssembly, assembly))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A type of the given name that comes from a reference and can be used from this project.</summary>
    private static INamedTypeSymbol? FindType(INamespaceSymbol @namespace, string name, Compilation compilation)
    {
        foreach (var type in @namespace.GetTypeMembers(name.TrimStart('@'), 0))
        {
            if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
                && compilation.IsSymbolAccessibleWithin(type, compilation.Assembly))
            {
                return type;
            }
        }

        return null;
    }

    private static ReferencedEntity? Describe(INamedTypeSymbol type, string logicalName, string fieldsClassName, Compilation compilation)
    {
        bool hasDefault = false, hasId = false, hasKey = false, hasKeyCollection = false, hasAnonymous = false;
        foreach (var constructor in type.InstanceConstructors)
        {
            // A derived class can call public and protected constructors.
            if (constructor.DeclaredAccessibility != Accessibility.Public
                && constructor.DeclaredAccessibility != Accessibility.Protected
                && constructor.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            {
                continue;
            }

            var parameters = constructor.Parameters;
            if (parameters.Length == 0)
            {
                hasDefault = true;
            }
            else if (parameters.Length == 1 && parameters[0].Type.ToDisplayString() == "System.Guid")
            {
                hasId = true;
            }
            else if (parameters.Length == 1 && parameters[0].Type.SpecialType == SpecialType.System_Object)
            {
                hasAnonymous = true;
            }
            else if (parameters.Length == 1 && parameters[0].Type.Name == "KeyAttributeCollection")
            {
                hasKeyCollection = true;
            }
            else if (parameters.Length == 2
                && parameters[0].Type.SpecialType == SpecialType.System_String
                && parameters[1].Type.SpecialType == SpecialType.System_Object)
            {
                hasKey = true;
            }
        }

        if (!hasDefault)
        {
            // Nothing a derived class could be built on.
            return null;
        }

        INamedTypeSymbol? fields = null;
        foreach (var nested in type.GetTypeMembers(fieldsClassName, 0))
        {
            if (!nested.IsSealed && !nested.IsStatic && compilation.IsSymbolAccessibleWithin(nested, compilation.Assembly))
            {
                fields = nested;
                break;
            }
        }

        return new ReferencedEntity(
            logicalName,
            type.ContainingAssembly?.Name ?? string.Empty,
            type.DeclaredAccessibility == Accessibility.Public,
            new HashSet<string>(type.MemberNames, StringComparer.Ordinal),
            fields != null,
            fields == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(fields.MemberNames, StringComparer.Ordinal),
            hasId,
            hasKey,
            hasKeyCollection,
            hasAnonymous);
    }

    private static string? LogicalNameOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "EntityLogicalNameAttribute"
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string logicalName)
            {
                return logicalName;
            }
        }

        return null;
    }
}
