using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>A place in code where the number of a choice option is expected.</summary>
internal readonly struct ChoiceRequest
{
    public ChoiceRequest(string? entityLogicalName, string attributeLogicalName, TextSpan replacementSpan)
    {
        EntityLogicalName = entityLogicalName;
        AttributeLogicalName = attributeLogicalName;
        ReplacementSpan = replacementSpan;
    }

    public string? EntityLogicalName { get; }

    public string AttributeLogicalName { get; }

    /// <summary>What is replaced when an option is picked: empty, or the number or word already typed.</summary>
    public TextSpan ReplacementSpan { get; }
}

/// <summary>
/// Recognises the spots where an option value is written as a number: <c>new OptionSetValue(…)</c>,
/// the values of a condition, and comparisons with <c>OptionSetValue.Value</c>.
/// </summary>
internal static class ChoiceContextAnalyzer
{
    public static bool TryAnalyze(SyntaxNode root, int position, SemanticModel model, CancellationToken cancellationToken, out ChoiceRequest request)
    {
        request = default;
        var token = root.FindToken(position);
        if (token.SpanStart >= position)
        {
            token = token.GetPreviousToken();
        }

        if (token.RawKind == 0)
        {
            return false;
        }

        // Either the caret follows a token that opens a value slot, or the value has been started.
        SyntaxNode? typed = null;
        var replacement = new TextSpan(position, 0);
        if (token.Span.End >= position
            && (token.IsKind(SyntaxKind.IdentifierToken) || token.IsKind(SyntaxKind.NumericLiteralToken))
            && token.Parent is IdentifierNameSyntax or LiteralExpressionSyntax)
        {
            typed = token.Parent;
            replacement = token.Span;
            if (typed.Parent is PrefixUnaryExpressionSyntax negative && negative.IsKind(SyntaxKind.UnaryMinusExpression))
            {
                typed = negative;
                replacement = negative.Span;
            }
        }

        var found = typed != null
            ? FromValueExpression(typed, model, cancellationToken, out var entity, out var attribute)
            : FromOpeningToken(token, position, model, cancellationToken, out entity, out attribute);

        if (!found || attribute == null)
        {
            return false;
        }

        request = new ChoiceRequest(entity, attribute, replacement);
        return true;
    }

    private static bool FromOpeningToken(SyntaxToken token, int position, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        switch (token.Kind())
        {
            case SyntaxKind.OpenParenToken when token.Parent is ArgumentListSyntax arguments:
                return FromArgumentSlot(arguments, 0, model, cancellationToken, out entity, out attribute);

            case SyntaxKind.CommaToken when token.Parent is ArgumentListSyntax arguments:
                var index = 0;
                foreach (var separator in arguments.Arguments.GetSeparators())
                {
                    if (separator.SpanStart < position)
                    {
                        index++;
                    }
                }

                return FromArgumentSlot(arguments, index, model, cancellationToken, out entity, out attribute);

            case SyntaxKind.EqualsEqualsToken or SyntaxKind.ExclamationEqualsToken when token.Parent is BinaryExpressionSyntax binary:
                return FromOptionSetValueAccess(binary.Left, model, cancellationToken, out entity, out attribute);

            case SyntaxKind.CaseKeyword when token.Parent?.Parent?.Parent is SwitchStatementSyntax switchStatement:
                return FromOptionSetValueAccess(switchStatement.Expression, model, cancellationToken, out entity, out attribute);

            case SyntaxKind.OpenBraceToken or SyntaxKind.CommaToken
                when token.Parent is InitializerExpressionSyntax initializer && initializer.IsKind(SyntaxKind.ArrayInitializerExpression):
                return FromValueExpression(initializer, model, cancellationToken, out entity, out attribute);

            default:
                return false;
        }
    }

    /// <summary>The value has been started (<c>1</c>, <c>Act</c>): find the slot it sits in.</summary>
    private static bool FromValueExpression(SyntaxNode value, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        while (value.Parent is ParenthesizedExpressionSyntax)
        {
            value = value.Parent;
        }

        // A value inside new object[] { ... } stands for the array.
        if (value.Parent is InitializerExpressionSyntax arrayInitializer && arrayInitializer.IsKind(SyntaxKind.ArrayInitializerExpression))
        {
            value = arrayInitializer;
        }

        if (value is InitializerExpressionSyntax && value.Parent is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax)
        {
            value = value.Parent;
        }

        switch (value.Parent)
        {
            case ArgumentSyntax argument when argument.Parent is ArgumentListSyntax arguments:
                return FromArgumentSlot(arguments, arguments.Arguments.IndexOf(argument), model, cancellationToken, out entity, out attribute);

            case BinaryExpressionSyntax binary
                when binary.Right == value && (binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)):
                return FromOptionSetValueAccess(binary.Left, model, cancellationToken, out entity, out attribute);

            case CaseSwitchLabelSyntax label when label.Parent?.Parent is SwitchStatementSyntax switchStatement:
                return FromOptionSetValueAccess(switchStatement.Expression, model, cancellationToken, out entity, out attribute);

            default:
                return false;
        }
    }

    private static bool FromArgumentSlot(ArgumentListSyntax arguments, int index, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        switch (arguments.Parent)
        {
            case BaseObjectCreationExpressionSyntax creation:
                var typeName = TypeName(creation, model, cancellationToken);
                if (typeName == "OptionSetValue")
                {
                    return index == 0 && FromOptionSetValueTarget(creation, model, cancellationToken, out entity, out attribute);
                }

                return typeName == "ConditionExpression"
                    && FromConditionArguments(arguments, index, creation, model, cancellationToken, out entity, out attribute);

            case InvocationExpressionSyntax invocation:
                switch (EntityInference.MethodName(invocation))
                {
                    case "AddCondition":
                        return FromConditionArguments(arguments, index, invocation, model, cancellationToken, out entity, out attribute);

                    case "AddAttributeValue" when index == 1:
                        attribute = EntityInference.ConstantArgument(arguments, 0, "attributeName", model, cancellationToken);
                        entity = EntityInference.Infer(EntityInference.Receiver(invocation), model, cancellationToken);
                        return attribute != null;

                    default:
                        return false;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// <c>ConditionExpression([entity,] attribute, operator, values…)</c> and <c>AddCondition</c> with the same shape:
    /// values come after the operator, and the names before it are string constants.
    /// </summary>
    private static bool FromConditionArguments(ArgumentListSyntax arguments, int index, ExpressionSyntax owner, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        var names = new List<string>(2);
        foreach (var argument in arguments.Arguments.Take(2))
        {
            var name = EntityInference.ConstantString(argument.Expression, model, cancellationToken);
            if (name == null)
            {
                break;
            }

            names.Add(name);
        }

        // Needs at least the column name, and the caret has to be past the operator.
        if (names.Count == 0 || index <= names.Count)
        {
            return false;
        }

        if (names.Count == 2)
        {
            entity = names[0];
            attribute = names[1];
            return true;
        }

        attribute = names[0];
        entity = owner is InvocationExpressionSyntax invocation
            ? EntityInference.Infer(EntityInference.Receiver(invocation), model, cancellationToken)
              ?? (invocation.Parent == null ? null : EntityInference.InferFromQueryContext(invocation.Parent, model, cancellationToken))
            : owner.Parent == null ? null : EntityInference.InferFromQueryContext(owner.Parent, model, cancellationToken);
        return true;
    }

    /// <summary>For <c>new OptionSetValue(|)</c>: the column the new value is stored in.</summary>
    private static bool FromOptionSetValueTarget(ExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        SyntaxNode value = creation;
        while (value.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
        {
            value = value.Parent;
        }

        switch (value.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Right == value:
                switch (assignment.Left)
                {
                    case ElementAccessExpressionSyntax element:
                        // entity["statuscode"] = new OptionSetValue(|)
                        attribute = EntityInference.ConstantArgument(element.ArgumentList, 0, null, model, cancellationToken);
                        entity = EntityInference.Infer(element.Expression, model, cancellationToken);
                        return attribute != null;

                    case ImplicitElementAccessSyntax implicitAccess:
                        // new Entity("account") { ["statuscode"] = new OptionSetValue(|) }
                        attribute = EntityInference.ConstantArgument(implicitAccess.ArgumentList, 0, null, model, cancellationToken);
                        entity = EnclosingCreationEntity(implicitAccess, model, cancellationToken);
                        return attribute != null;

                    default:
                        return false;
                }

            case ArgumentSyntax argument when argument.Parent is ArgumentListSyntax arguments && arguments.Parent is InvocationExpressionSyntax invocation:
                // entity.Attributes.Add("statuscode", new OptionSetValue(|)), entity.SetAttributeValue("statuscode", ...)
                if (arguments.Arguments.IndexOf(argument) != 1)
                {
                    return false;
                }

                attribute = EntityInference.ConstantArgument(arguments, 0, null, model, cancellationToken);
                entity = EntityInference.Infer(EntityInference.Receiver(invocation), model, cancellationToken);
                return attribute != null;

            case InitializerExpressionSyntax complex
                when complex.IsKind(SyntaxKind.ComplexElementInitializerExpression) && complex.Expressions.Count == 2 && complex.Expressions[1] == value:
                // Attributes = { { "statuscode", new OptionSetValue(|) } }
                attribute = EntityInference.ConstantString(complex.Expressions[0], model, cancellationToken);
                entity = EnclosingCreationEntity(complex, model, cancellationToken);
                return attribute != null;

            default:
                return false;
        }
    }

    private static string? EnclosingCreationEntity(SyntaxNode node, SemanticModel model, CancellationToken cancellationToken)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            if (current is BaseObjectCreationExpressionSyntax creation)
            {
                var entity = EntityInference.Infer(creation, model, cancellationToken);
                if (entity != null)
                {
                    return entity;
                }
            }

            if (current is StatementSyntax or MemberDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// For <c>x.Value == |</c>: <c>x</c> is <c>entity.GetAttributeValue&lt;OptionSetValue&gt;("statuscode")</c>,
    /// <c>(OptionSetValue)entity["statuscode"]</c>, or a variable holding one of these.
    /// </summary>
    private static bool FromOptionSetValueAccess(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken, out string? entity, out string? attribute)
    {
        entity = null;
        attribute = null;
        expression = EntityInference.Unwrap(expression);

        ExpressionSyntax? optionSetValue = expression switch
        {
            MemberAccessExpressionSyntax member when member.Name.Identifier.ValueText == "Value" => member.Expression,
            ConditionalAccessExpressionSyntax { WhenNotNull: MemberBindingExpressionSyntax binding } conditional
                when binding.Name.Identifier.ValueText == "Value" => conditional.Expression,
            _ => null,
        };

        for (var hops = 0; optionSetValue != null && hops < 4; hops++)
        {
            optionSetValue = EntityInference.Unwrap(optionSetValue);
            switch (optionSetValue)
            {
                case InvocationExpressionSyntax invocation when EntityInference.MethodName(invocation) == "GetAttributeValue":
                    attribute = EntityInference.ConstantArgument(invocation.ArgumentList, 0, "attributeLogicalName", model, cancellationToken);
                    entity = EntityInference.Infer(EntityInference.Receiver(invocation), model, cancellationToken);
                    return attribute != null;

                case ElementAccessExpressionSyntax element:
                    attribute = EntityInference.ConstantArgument(element.ArgumentList, 0, null, model, cancellationToken);
                    entity = EntityInference.Infer(element.Expression, model, cancellationToken);
                    return attribute != null;

                case IdentifierNameSyntax identifier
                    when model.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local
                        && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator:
                    // var status = entity.GetAttributeValue<OptionSetValue>("statuscode"); status.Value == |
                    optionSetValue = declarator.Initializer?.Value;
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    private static string? TypeName(BaseObjectCreationExpressionSyntax creation, SemanticModel model, CancellationToken cancellationToken)
    {
        var type = model.GetTypeInfo(creation, cancellationToken).Type;
        if (type != null && type.TypeKind != TypeKind.Error)
        {
            return SdkSymbols.IsSdkType(type) ? type.Name : null;
        }

        // While the call is incomplete the type may not bind; fall back to what is written.
        return (creation as ObjectCreationExpressionSyntax)?.Type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            _ => null,
        };
    }
}
