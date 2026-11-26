using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>What a string literal in SDK code should be completed with.</summary>
internal readonly struct NameRequest
{
    public NameRequest(NameRole role, string? entityLogicalName = null)
    {
        Role = role;
        EntityLogicalName = entityLogicalName;
    }

    public NameRole Role { get; }

    /// <summary>For column names: the table they belong to, or null when it could not be worked out.</summary>
    public string? EntityLogicalName { get; }
}

/// <summary>
/// Decides whether a string literal is a table, column or relationship name, by looking at the
/// SDK member it is passed to, assigned to or compared with.
/// </summary>
internal static class NameContextAnalyzer
{
    public static bool TryAnalyze(ExpressionSyntax stringExpression, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        SyntaxNode node = stringExpression;
        while (node.Parent is ParenthesizedExpressionSyntax)
        {
            node = node.Parent;
        }

        // "a" inside new[] { "a", "b" } or ["a", "b"] stands for the whole list.
        node = ListContaining(node) ?? node;

        switch (node.Parent)
        {
            case ArgumentSyntax argument:
                return FromArgument(argument, model, cancellationToken, out request);

            case AssignmentExpressionSyntax assignment when assignment.Right == node:
                return FromAssignment(assignment, model, cancellationToken, out request);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                return FromComparison(binary.Left == node ? binary.Right : binary.Left, model, cancellationToken, out request);

            case CaseSwitchLabelSyntax label when label.Parent?.Parent is SwitchStatementSyntax switchStatement:
                return FromComparison(switchStatement.Expression, model, cancellationToken, out request);

            case ConstantPatternSyntax pattern:
                return FromPattern(pattern, model, cancellationToken, out request);

            case InitializerExpressionSyntax initializer:
                return FromInitializerElement(initializer, (ExpressionSyntax)node, model, cancellationToken, out request);

            default:
                return false;
        }
    }

    private static SyntaxNode? ListContaining(SyntaxNode element)
    {
        if (element.Parent is InitializerExpressionSyntax initializer
            && initializer.IsKind(SyntaxKind.ArrayInitializerExpression)
            && initializer.Parent is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax)
        {
            return initializer.Parent;
        }

        if (element.Parent is ExpressionElementSyntax { Parent: CollectionExpressionSyntax collection })
        {
            return collection;
        }

        return null;
    }

    private static bool FromArgument(ArgumentSyntax argument, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        if (argument.Parent is not BaseArgumentListSyntax argumentList || argumentList.Parent is not SyntaxNode owner)
        {
            return false;
        }

        var index = argumentList.Arguments.IndexOf(argument);
        var name = argument.NameColon?.Name.Identifier.ValueText;
        var receiver = ReceiverOf(owner);

        // In an initializer the indexed object is the member being initialised (Attributes = { ["..."] = 1 }),
        // while the receiver used to find the table is the object being created.
        var receiverType = owner is ImplicitElementAccessSyntax || receiver == null
            ? ImplicitReceiverType(owner, model, cancellationToken)
            : model.GetTypeInfo(receiver, cancellationToken).Type;
        var receiverMember = receiver == null || owner is ImplicitElementAccessSyntax
            ? null
            : model.GetSymbolInfo(receiver, cancellationToken).Symbol;

        foreach (var candidate in CandidateMembers(owner, model, cancellationToken, argumentList.Arguments.Count))
        {
            var parameters = candidate switch
            {
                IMethodSymbol method => method.Parameters,
                IPropertySymbol property => property.Parameters,
                _ => default,
            };

            var parameter = FindParameter(parameters, index, name);
            if (parameter == null)
            {
                continue;
            }

            var role = SdkSymbols.RoleOfParameter(parameter, candidate, receiverType, receiverMember);
            if (role == NameRole.None)
            {
                continue;
            }

            request = role == NameRole.Attribute
                ? new NameRequest(role, EntityForAttributeArgument(parameter, candidate, argumentList, owner, receiver, model, cancellationToken))
                : new NameRequest(role);
            return true;
        }

        return false;
    }

    /// <summary>The table whose columns the argument names.</summary>
    private static string? EntityForAttributeArgument(
        IParameterSymbol parameter,
        ISymbol member,
        BaseArgumentListSyntax arguments,
        SyntaxNode owner,
        ExpressionSyntax? receiver,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var parameters = member is IMethodSymbol method ? method.Parameters : ((IPropertySymbol)member).Parameters;

        // 1. A table named by a sibling argument: LinkEntity(from, to, fromAttribute, toAttribute), ConditionExpression(entity, attribute, ...).
        var siblingName = parameter.Name switch
        {
            "linkFromAttributeName" => "linkFromEntityName",
            "linkToAttributeName" => "linkToEntityName",
            _ => "entityName",
        };

        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, siblingName, StringComparison.OrdinalIgnoreCase)
                || (siblingName == "entityName" && string.Equals(parameters[i].Name, "entityLogicalName", StringComparison.OrdinalIgnoreCase)))
            {
                var sibling = EntityInference.ConstantArgument(arguments, i, parameters[i].Name, model, cancellationToken);
                if (sibling != null)
                {
                    return sibling;
                }
            }
        }

        // 2. The object the member is called on: entity["..."], query.AddOrder("..."), entity.Attributes.Contains("...").
        if (receiver != null)
        {
            var fromReceiver = EntityInference.Infer(receiver, model, cancellationToken);
            if (fromReceiver != null)
            {
                return fromReceiver;
            }
        }

        // 3. A project's own extension method called as a static method: Helper.Get(entity, "...").
        if (member is IMethodSymbol { IsExtensionMethod: true } && receiver == null && arguments.Arguments.Count > 0)
        {
            var fromFirst = EntityInference.Infer(arguments.Arguments[0].Expression, model, cancellationToken);
            if (fromFirst != null)
            {
                return fromFirst;
            }
        }

        // 4. The query, link or retrieve call the expression is nested in.
        var start = owner is ImplicitElementAccessSyntax ? owner.Parent : owner;
        return start == null ? null : EntityInference.InferFromQueryContext(start, model, cancellationToken);
    }

    private static bool FromAssignment(AssignmentExpressionSyntax assignment, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        if (model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not IPropertySymbol property)
        {
            return false;
        }

        var role = SdkSymbols.RoleOfProperty(property);
        if (role == NameRole.None)
        {
            return false;
        }

        if (role != NameRole.Attribute)
        {
            request = new NameRequest(role);
            return true;
        }

        string? entity = null;
        if (assignment.Parent is InitializerExpressionSyntax initializer)
        {
            // new LinkEntity { LinkToEntityName = "contact", LinkToAttributeName = "..." }
            var sibling = property.Name switch
            {
                "LinkFromAttributeName" => "LinkFromEntityName",
                "LinkToAttributeName" => "LinkToEntityName",
                _ => "EntityName",
            };

            foreach (var expression in initializer.Expressions)
            {
                if (expression is AssignmentExpressionSyntax other
                    && other.Left is IdentifierNameSyntax left
                    && left.Identifier.ValueText == sibling)
                {
                    entity = EntityInference.ConstantString(other.Right, model, cancellationToken);
                }
            }

            if (entity == null && property.Name == "AttributeName" && initializer.Parent?.Parent != null)
            {
                entity = EntityInference.InferFromQueryContext(initializer.Parent.Parent, model, cancellationToken);
            }
        }
        else if (assignment.Left is MemberAccessExpressionSyntax member && property.Name == "AttributeName")
        {
            entity = EntityInference.InferFromQueryContext(member, model, cancellationToken);
        }

        request = new NameRequest(role, entity);
        return true;
    }

    private static bool FromComparison(ExpressionSyntax other, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        other = EntityInference.Unwrap(other);
        if (model.GetSymbolInfo(other, cancellationToken).Symbol is not IPropertySymbol property)
        {
            return false;
        }

        var role = SdkSymbols.RoleOfProperty(property);
        if (role == NameRole.None)
        {
            return false;
        }

        request = new NameRequest(role);
        return true;
    }

    private static bool FromPattern(ConstantPatternSyntax pattern, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        for (SyntaxNode? current = pattern.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case IsPatternExpressionSyntax isPattern:
                    return FromComparison(isPattern.Expression, model, cancellationToken, out request);
                case SwitchExpressionSyntax switchExpression:
                    return FromComparison(switchExpression.GoverningExpression, model, cancellationToken, out request);
                case SwitchStatementSyntax switchStatement:
                    return FromComparison(switchStatement.Expression, model, cancellationToken, out request);
                case StatementSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Collection initializers, which call an Add method behind the scenes:
    /// <c>Columns = { "name" }</c> and <c>Attributes = { { "name", value } }</c>.
    /// </summary>
    private static bool FromInitializerElement(InitializerExpressionSyntax initializer, ExpressionSyntax element, SemanticModel model, CancellationToken cancellationToken, out NameRequest request)
    {
        request = default;
        ExpressionSyntax addElement;
        InitializerExpressionSyntax collection;
        if (initializer.IsKind(SyntaxKind.ComplexElementInitializerExpression))
        {
            // { "key", value }: only the key names a column.
            if (initializer.Expressions.Count == 0 || initializer.Expressions[0] != element
                || initializer.Parent is not InitializerExpressionSyntax outer)
            {
                return false;
            }

            addElement = initializer;
            collection = outer;
        }
        else if (initializer.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            addElement = element;
            collection = initializer;
        }
        else
        {
            return false;
        }

        // The collection being filled: the property on the left of "Columns = { ... }", or the created object.
        ITypeSymbol? collectionType = null;
        ISymbol? collectionMember = null;
        SyntaxNode? contextStart = collection.Parent;
        if (collection.Parent is AssignmentExpressionSyntax assignment)
        {
            collectionMember = model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
            collectionType = (collectionMember as IPropertySymbol)?.Type ?? (collectionMember as IFieldSymbol)?.Type;
            contextStart = assignment.Parent?.Parent;
        }
        else if (collection.Parent is BaseObjectCreationExpressionSyntax creation)
        {
            collectionType = model.GetTypeInfo(creation, cancellationToken).Type;
        }

        var add = model.GetCollectionInitializerSymbolInfo(addElement, cancellationToken).Symbol as IMethodSymbol;
        var isAttributeName = add != null && add.Parameters.Length > 0
            ? SdkSymbols.RoleOfParameter(add.Parameters[0], add, collectionType, collectionMember) == NameRole.Attribute
            : SdkSymbols.IsAttributeKeyedCollection(collectionType) || SdkSymbols.IsAttributeNameList(collectionMember);
        if (!isAttributeName)
        {
            return false;
        }

        var entity = contextStart == null ? null : EntityInference.InferFromQueryContext(contextStart, model, cancellationToken);
        request = new NameRequest(NameRole.Attribute, entity);
        return true;
    }

    /// <summary>The members a call may bind to, best match first. Works while the call is still being typed.</summary>
    internal static IEnumerable<ISymbol> CandidateMembers(SyntaxNode owner, SemanticModel model, CancellationToken cancellationToken, int argumentCount)
    {
        var info = model.GetSymbolInfo(owner, cancellationToken);
        if (info.Symbol != null)
        {
            yield return info.Symbol;
        }

        IEnumerable<ISymbol> candidates = info.CandidateSymbols;
        if (info.CandidateSymbols.IsEmpty)
        {
            candidates = owner switch
            {
                InvocationExpressionSyntax invocation => model.GetMemberGroup(invocation.Expression, cancellationToken),
                BaseObjectCreationExpressionSyntax creation =>
                    (model.GetTypeInfo(creation, cancellationToken).Type as INamedTypeSymbol)?.InstanceConstructors
                        ?? Enumerable.Empty<ISymbol>(),
                ElementAccessExpressionSyntax element => Indexers(model.GetTypeInfo(element.Expression, cancellationToken).Type),
                ElementBindingExpressionSyntax binding => Indexers(ImplicitReceiverType(binding, model, cancellationToken)),
                ImplicitElementAccessSyntax implicitAccess => Indexers(ImplicitReceiverType(implicitAccess, model, cancellationToken)),
                _ => Enumerable.Empty<ISymbol>(),
            };
        }

        // While arguments are still missing, the smallest overload that can take what is typed so far is the best guess.
        foreach (var candidate in candidates
            .Where(c => !SymbolEqualityComparer.Default.Equals(c, info.Symbol))
            .OrderBy(c => ParameterCount(c) >= argumentCount ? 0 : 1)
            .ThenBy(ParameterCount))
        {
            yield return candidate;
        }
    }

    private static int ParameterCount(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.Parameters.Length,
        IPropertySymbol property => property.Parameters.Length,
        _ => int.MaxValue,
    };

    private static IEnumerable<ISymbol> Indexers(ITypeSymbol? type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsIndexer: true })
                {
                    yield return member;
                }
            }
        }
    }

    private static IParameterSymbol? FindParameter(System.Collections.Immutable.ImmutableArray<IParameterSymbol> parameters, int index, string? name)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return null;
        }

        if (name != null)
        {
            return parameters.FirstOrDefault(p => p.Name == name);
        }

        if (index < parameters.Length)
        {
            return parameters[index];
        }

        var last = parameters[parameters.Length - 1];
        return last.IsParams ? last : null;
    }

    /// <summary>The expression a call, creation or element access is made on.</summary>
    private static ExpressionSyntax? ReceiverOf(SyntaxNode owner) => owner switch
    {
        InvocationExpressionSyntax invocation => EntityInference.Receiver(invocation),
        ElementAccessExpressionSyntax element => element.Expression,
        ElementBindingExpressionSyntax binding => EntityInference.Receiver(binding),
        ImplicitElementAccessSyntax implicitAccess => InitializedObject(implicitAccess),
        _ => null,
    };

    /// <summary>For <c>["key"] = value</c> in an initializer: the expression creating the object being initialised.</summary>
    private static ExpressionSyntax? InitializedObject(ImplicitElementAccessSyntax implicitAccess)
    {
        // ImplicitElementAccess → Assignment → Initializer → creation, or → Assignment (nested: Attributes = { ... }) → ...
        var initializer = implicitAccess.Parent?.Parent as InitializerExpressionSyntax;
        while (initializer != null)
        {
            switch (initializer.Parent)
            {
                case BaseObjectCreationExpressionSyntax creation:
                    return creation;
                case AssignmentExpressionSyntax nested when nested.Parent is InitializerExpressionSyntax outer:
                    initializer = outer;
                    continue;
                default:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The type an implicit or conditional element access indexes into.</summary>
    private static ITypeSymbol? ImplicitReceiverType(SyntaxNode owner, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (owner)
        {
            case ImplicitElementAccessSyntax implicitAccess when implicitAccess.Parent?.Parent is InitializerExpressionSyntax initializer:
                return initializer.Parent switch
                {
                    BaseObjectCreationExpressionSyntax creation => model.GetTypeInfo(creation, cancellationToken).Type,
                    AssignmentExpressionSyntax nested => model.GetSymbolInfo(nested.Left, cancellationToken).Symbol switch
                    {
                        IPropertySymbol property => property.Type,
                        IFieldSymbol field => field.Type,
                        _ => null,
                    },
                    _ => null,
                };

            case ElementBindingExpressionSyntax binding:
                var receiver = EntityInference.Receiver(binding);
                return receiver == null ? null : model.GetTypeInfo(receiver, cancellationToken).Type;

            default:
                return null;
        }
    }
}
