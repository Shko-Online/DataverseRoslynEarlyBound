using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>
/// Works out which table an expression is about: <c>new Entity("account")</c>, a query on it,
/// a row retrieved from it, an early-bound class, or a variable that holds any of these.
/// </summary>
/// <remarks>Best effort by design: it follows the common shapes of SDK code and gives up otherwise.</remarks>
internal static class EntityInference
{
    private const int MaxDepth = 12;

    /// <summary>Members that lead from a table-bound object to another object about the same table.</summary>
    private static readonly HashSet<string> PassThroughMembers = new(StringComparer.Ordinal)
    {
        "Entities", "Attributes", "FormattedValues", "KeyAttributes", "ColumnSet", "Columns", "Criteria",
        "LinkCriteria", "Orders", "Conditions", "Filters", "Result",
    };

    /// <summary>Methods whose result is about the same table as their receiver.</summary>
    private static readonly HashSet<string> PassThroughMethods = new(StringComparer.Ordinal)
    {
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "ElementAt",
        "ElementAtOrDefault", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Take",
        "Skip", "ToList", "ToArray", "AsEnumerable", "AsQueryable", "Cast", "OfType", "Distinct", "Reverse",
        "DefaultIfEmpty", "Concat", "Union", "GetAwaiter", "GetResult", "ConfigureAwait", "AddFilter",
    };

    /// <summary>Methods that hand each element of their receiver to a lambda.</summary>
    private static readonly HashSet<string> ElementLambdaMethods = new(StringComparer.Ordinal)
    {
        "Select", "SelectMany", "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last",
        "LastOrDefault", "Any", "All", "Count", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending",
        "GroupBy", "ToDictionary", "ToLookup", "ForEach", "Sum", "Max", "Min", "Average", "TakeWhile",
        "SkipWhile", "Find", "FindAll", "Exists", "RemoveAll",
    };

    public static string? Infer(ExpressionSyntax? expression, SemanticModel model, CancellationToken cancellationToken, int depth = 0)
    {
        if (expression == null || depth > MaxDepth)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        expression = Unwrap(expression);
        if (expression.SyntaxTree != model.SyntaxTree)
        {
            return null;
        }

        var type = model.GetTypeInfo(expression, cancellationToken).Type;
        var typed = FromType(type);
        if (typed != null)
        {
            return typed;
        }

        switch (expression)
        {
            case BaseObjectCreationExpressionSyntax creation:
                return FromCreation(creation, type, model, cancellationToken, depth);

            case InvocationExpressionSyntax invocation:
                return FromInvocation(invocation, model, cancellationToken, depth);

            case MemberAccessExpressionSyntax member:
                if (PassThroughMembers.Contains(member.Name.Identifier.ValueText))
                {
                    return Infer(member.Expression, model, cancellationToken, depth + 1);
                }

                return FromSymbol(model.GetSymbolInfo(member, cancellationToken).Symbol, member, model, cancellationToken, depth);

            case ConditionalAccessExpressionSyntax conditional:
                return FromConditionalAccess(conditional, model, cancellationToken, depth);

            case ElementAccessExpressionSyntax element:
                // rows[0] is about the same table as rows; entity["column"] is a value, not a row.
                var receiverType = model.GetTypeInfo(element.Expression, cancellationToken).Type;
                return SdkSymbols.IsEntity(receiverType) || SdkSymbols.IsAttributeKeyedCollection(receiverType)
                    ? null
                    : Infer(element.Expression, model, cancellationToken, depth + 1);

            case IdentifierNameSyntax identifier:
                return FromSymbol(model.GetSymbolInfo(identifier, cancellationToken).Symbol, identifier, model, cancellationToken, depth);

            case ConditionalExpressionSyntax ternary:
                return Infer(ternary.WhenTrue, model, cancellationToken, depth + 1)
                    ?? Infer(ternary.WhenFalse, model, cancellationToken, depth + 1);

            case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                return Infer(coalesce.Left, model, cancellationToken, depth + 1)
                    ?? Infer(coalesce.Right, model, cancellationToken, depth + 1);

            case AssignmentExpressionSyntax assignment:
                return Infer(assignment.Right, model, cancellationToken, depth + 1);

            default:
                return null;
        }
    }

    /// <summary>The table a query, link or retrieve call scopes the given node to, found by walking outwards.</summary>
    public static string? InferFromQueryContext(SyntaxNode start, SemanticModel model, CancellationToken cancellationToken)
    {
        SyntaxNode? previous = null;
        for (var current = start; current != null; previous = current, current = current.Parent)
        {
            switch (current)
            {
                case BaseObjectCreationExpressionSyntax creation:
                    var fromCreation = Infer(creation, model, cancellationToken, 1);
                    if (fromCreation != null)
                    {
                        return fromCreation;
                    }

                    break;

                case InvocationExpressionSyntax invocation:
                    var name = MethodName(invocation);
                    if (name == "Retrieve" || name == "RetrieveAsync")
                    {
                        var entity = ConstantArgument(invocation.ArgumentList, 0, "entityName", model, cancellationToken);
                        if (entity != null)
                        {
                            return entity;
                        }
                    }

                    var receiver = Receiver(invocation);
                    if (receiver != null)
                    {
                        var fromReceiver = Infer(receiver, model, cancellationToken, 1);
                        if (fromReceiver != null)
                        {
                            return fromReceiver;
                        }
                    }

                    break;

                case AssignmentExpressionSyntax assignment
                    when assignment.Right == previous && assignment.Parent is not InitializerExpressionSyntax:
                    // query.ColumnSet = new ColumnSet("...")
                    if (assignment.Left is MemberAccessExpressionSyntax left)
                    {
                        var fromTarget = Infer(left.Expression, model, cancellationToken, 1);
                        if (fromTarget != null)
                        {
                            return fromTarget;
                        }
                    }

                    break;

                case StatementSyntax:
                case AnonymousFunctionExpressionSyntax:
                case MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }

    /// <summary>The expression a call or element access is made on, also across <c>?.</c>.</summary>
    public static ExpressionSyntax? Receiver(ExpressionSyntax callOrAccess)
    {
        var target = callOrAccess switch
        {
            InvocationExpressionSyntax invocation => invocation.Expression,
            _ => callOrAccess,
        };

        switch (target)
        {
            case MemberAccessExpressionSyntax member:
                return member.Expression;
            case ElementAccessExpressionSyntax element:
                return element.Expression;
            case MemberBindingExpressionSyntax:
            case ElementBindingExpressionSyntax:
                return ConditionalReceiver(target);
            default:
                return null;
        }
    }

    public static string? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.ValueText,
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => null,
    };

    /// <summary>The value of a string argument given by position or by name, when it is a compile-time constant.</summary>
    public static string? ConstantArgument(BaseArgumentListSyntax? arguments, int index, string? name, SemanticModel model, CancellationToken cancellationToken)
    {
        if (arguments == null)
        {
            return null;
        }

        ArgumentSyntax? match = null;
        if (name != null)
        {
            match = arguments.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == name);
        }

        if (match == null && index >= 0 && index < arguments.Arguments.Count && arguments.Arguments[index].NameColon == null)
        {
            match = arguments.Arguments[index];
        }

        return match == null ? null : ConstantString(match.Expression, model, cancellationToken);
    }

    public static string? ConstantString(ExpressionSyntax? expression, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression == null || expression.SyntaxTree != model.SyntaxTree)
        {
            return null;
        }

        var constant = model.GetConstantValue(expression, cancellationToken);
        return constant.HasValue && constant.Value is string text && text.Length > 0 ? text : null;
    }

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case AwaitExpressionSyntax awaited:
                    expression = awaited.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                    expression = binary.Left;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static string? FromType(ITypeSymbol? type)
    {
        var direct = SdkSymbols.GetEntityLogicalName(type);
        if (direct != null)
        {
            return direct;
        }

        // IQueryable<Account>, List<Contact>, Account[] ...
        if (type is IArrayTypeSymbol array)
        {
            return SdkSymbols.GetEntityLogicalName(array.ElementType);
        }

        if (type is INamedTypeSymbol named && named.TypeArguments.Length == 1)
        {
            return SdkSymbols.GetEntityLogicalName(named.TypeArguments[0]);
        }

        return null;
    }

    private static string? FromCreation(BaseObjectCreationExpressionSyntax creation, ITypeSymbol? type, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        var typeName = type?.Name ?? (creation as ObjectCreationExpressionSyntax)?.Type.ToString();
        switch (typeName)
        {
            case "LinkEntity":
                // A link scopes its columns and criteria to the table it links to.
                return ConstantArgument(creation.ArgumentList, 1, "linkToEntityName", model, cancellationToken)
                    ?? InitializerString(creation, model, cancellationToken, "LinkToEntityName");

            case "ColumnSet":
            case "FilterExpression":
            case "ConditionExpression":
            case "OrderExpression":
            case "OptionSetValue":
                return null;

            default:
                var fromArgument = FirstConstantNameArgument(creation, model, cancellationToken);
                if (fromArgument != null)
                {
                    return fromArgument;
                }

                var fromInitializer = InitializerString(creation, model, cancellationToken, "LogicalName", "EntityName");
                if (fromInitializer != null)
                {
                    return fromInitializer;
                }

                // new RetrieveRequest { Target = new EntityReference("account", id), ... }
                var target = InitializerValue(creation, "Target");
                return target == null ? null : Infer(target, model, cancellationToken, depth + 1);
        }
    }

    /// <summary>The first argument of a constructor that names a table: <c>new Entity("account")</c>.</summary>
    private static string? FirstConstantNameArgument(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count == 0)
        {
            return null;
        }

        var type = model.GetTypeInfo(creation, cancellationToken).Type;
        if (!SdkSymbols.IsSdkType(type) && !SdkSymbols.IsEntity(type))
        {
            return null;
        }

        foreach (var name in new[] { "entityName", "logicalName", "entityLogicalName" })
        {
            var named = ConstantArgument(creation.ArgumentList, -1, name, model, cancellationToken);
            if (named != null)
            {
                return named;
            }
        }

        var first = creation.ArgumentList.Arguments[0];
        if (first.NameColon != null)
        {
            return null;
        }

        // Only when that first parameter is the table name; new Relationship("...") is not about a table.
        var constructor = model.GetSymbolInfo(creation, cancellationToken).Symbol as IMethodSymbol
            ?? model.GetSymbolInfo(creation, cancellationToken).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (constructor != null
            && constructor.Parameters.Length > 0
            && SdkSymbols.RoleOfParameter(constructor.Parameters[0], constructor, null, null) != NameRole.Entity)
        {
            return null;
        }

        return ConstantString(first.Expression, model, cancellationToken);
    }

    private static string? InitializerString(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = InitializerValue(creation, propertyName);
            var text = ConstantString(value, model, cancellationToken);
            if (text != null)
            {
                return text;
            }
        }

        return null;
    }

    private static ExpressionSyntax? InitializerValue(BaseObjectCreationExpressionSyntax creation, string propertyName)
    {
        if (creation.Initializer == null)
        {
            return null;
        }

        foreach (var expression in creation.Initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax assignment
                && assignment.Left is IdentifierNameSyntax left
                && left.Identifier.ValueText == propertyName)
            {
                return assignment.Right;
            }
        }

        return null;
    }

    private static string? FromInvocation(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        var name = MethodName(invocation);
        if (name == null)
        {
            return null;
        }

        var receiver = Receiver(invocation);
        switch (name)
        {
            case "Retrieve":
            case "RetrieveAsync":
                return ConstantArgument(invocation.ArgumentList, 0, "entityName", model, cancellationToken);

            case "RetrieveMultiple":
            case "RetrieveMultipleAsync":
                return invocation.ArgumentList.Arguments.Count > 0
                    ? Infer(invocation.ArgumentList.Arguments[0].Expression, model, cancellationToken, depth + 1)
                    : null;

            case "CreateQuery":
                return ConstantArgument(invocation.ArgumentList, 0, "entityLogicalName", model, cancellationToken);

            case "AddLink":
                // Returns the new link, which is about the table linked to.
                return ConstantArgument(invocation.ArgumentList, 0, "linkToEntityName", model, cancellationToken);

            case "ToEntity":
                return Infer(receiver, model, cancellationToken, depth + 1);

            default:
                return PassThroughMethods.Contains(name)
                    ? Infer(receiver, model, cancellationToken, depth + 1)
                    : null;
        }
    }

    private static string? FromConditionalAccess(ConditionalAccessExpressionSyntax conditional, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        switch (conditional.WhenNotNull)
        {
            case MemberBindingExpressionSyntax binding when PassThroughMembers.Contains(binding.Name.Identifier.ValueText):
                return Infer(conditional.Expression, model, cancellationToken, depth + 1);
            case InvocationExpressionSyntax invocation:
                return FromInvocation(invocation, model, cancellationToken, depth);
            case ElementBindingExpressionSyntax:
                var receiverType = model.GetTypeInfo(conditional.Expression, cancellationToken).Type;
                return SdkSymbols.IsEntity(receiverType) ? null : Infer(conditional.Expression, model, cancellationToken, depth + 1);
            default:
                return null;
        }
    }

    private static ExpressionSyntax? ConditionalReceiver(SyntaxNode binding)
    {
        for (var current = binding.Parent; current != null; current = current.Parent)
        {
            if (current is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull.Span.Contains(binding.Span))
            {
                return conditional.Expression;
            }

            if (current is StatementSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static string? FromSymbol(ISymbol? symbol, ExpressionSyntax usage, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        switch (symbol)
        {
            case ILocalSymbol local:
                return FromLocal(local, usage, model, cancellationToken, depth);

            case IParameterSymbol parameter:
                return FromLambdaParameter(parameter, model, cancellationToken, depth);

            case IFieldSymbol or IPropertySymbol:
                var declaration = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
                if (declaration == null || declaration.SyntaxTree != model.SyntaxTree)
                {
                    return null;
                }

                var initializer = declaration switch
                {
                    VariableDeclaratorSyntax field => field.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value ?? property.ExpressionBody?.Expression,
                    _ => null,
                };
                return Infer(initializer, model, cancellationToken, depth + 1);

            default:
                return null;
        }
    }

    private static string? FromLocal(ILocalSymbol local, ExpressionSyntax usage, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        var declaration = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        switch (declaration)
        {
            case VariableDeclaratorSyntax declarator:
                var fromInitializer = Infer(declarator.Initializer?.Value, model, cancellationToken, depth + 1);
                return fromInitializer ?? FromAssignments(local, declarator, usage, model, cancellationToken, depth);

            case ForEachStatementSyntax forEach:
                return Infer(forEach.Expression, model, cancellationToken, depth + 1);

            default:
                return null;
        }
    }

    /// <summary>
    /// For a local without a telling initializer: the last <c>x = ...</c>, <c>x.LogicalName = "..."</c> or
    /// <c>x.EntityName = "..."</c> before the usage.
    /// </summary>
    private static string? FromAssignments(ILocalSymbol local, SyntaxNode declaration, ExpressionSyntax usage, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        var scope = declaration.FirstAncestorOrSelf<BlockSyntax>() ?? declaration.FirstAncestorOrSelf<MemberDeclarationSyntax>() as SyntaxNode;
        if (scope == null)
        {
            return null;
        }

        string? result = null;
        foreach (var assignment in scope.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.SpanStart >= usage.SpanStart)
            {
                break;
            }

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                continue;
            }

            if (assignment.Left is IdentifierNameSyntax direct && direct.Identifier.ValueText == local.Name)
            {
                result = Infer(assignment.Right, model, cancellationToken, depth + 1) ?? result;
            }
            else if (assignment.Left is MemberAccessExpressionSyntax member
                && member.Expression is IdentifierNameSyntax owner
                && owner.Identifier.ValueText == local.Name
                && member.Name.Identifier.ValueText is "LogicalName" or "EntityName" or "LinkToEntityName")
            {
                result = ConstantString(assignment.Right, model, cancellationToken) ?? result;
            }
        }

        return result;
    }

    /// <summary>For <c>rows.Select(r => r["..."])</c>: the lambda parameter is a row of whatever <c>rows</c> is about.</summary>
    private static string? FromLambdaParameter(IParameterSymbol parameter, SemanticModel model, CancellationToken cancellationToken, int depth)
    {
        var syntax = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var lambda = syntax?.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda?.Parent is not ArgumentSyntax argument
            || argument.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        var name = MethodName(invocation);
        return name != null && ElementLambdaMethods.Contains(name)
            ? Infer(Receiver(invocation), model, cancellationToken, depth + 1)
            : null;
    }
}
