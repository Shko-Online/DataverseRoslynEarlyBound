using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShkoOnline.EarlyBound.Completion;

/// <summary>A caret position inside a C# string literal.</summary>
internal sealed class StringLiteralContext
{
    private StringLiteralContext(ExpressionSyntax expression, TextSpan content, string textBeforeCaret, string textAfterCaret)
    {
        Expression = expression;
        Content = content;
        TextBeforeCaret = textBeforeCaret;
        TextAfterCaret = textAfterCaret;
    }

    /// <summary>The literal or interpolated string expression the caret is in.</summary>
    public ExpressionSyntax Expression { get; }

    /// <summary>Source span of the part of the literal the caret is in, without quotes.</summary>
    public TextSpan Content { get; }

    /// <summary>
    /// The text the string has before the caret, with C# escapes resolved. For concatenations and
    /// interpolated strings this includes the earlier pieces, with values that are not known
    /// replaced by a placeholder.
    /// </summary>
    public string TextBeforeCaret { get; }

    /// <summary>The rest of the piece the caret is in, with C# escapes resolved.</summary>
    public string TextAfterCaret { get; }

    public static StringLiteralContext? TryCreate(SyntaxNode root, SourceText text, int position)
    {
        if (position < 0 || position > root.FullSpan.End)
        {
            return null;
        }

        var token = root.FindToken(position);
        if (!TryGetContent(token, position, out var content, out var kind))
        {
            // The caret can sit right after the last character of a token: "abc|
            var previous = token.GetPreviousToken();
            if (previous.RawKind == 0 || !TryGetContent(previous, position, out content, out kind))
            {
                return null;
            }

            token = previous;
        }

        var expression = token.Parent switch
        {
            LiteralExpressionSyntax literal => (ExpressionSyntax)literal,
            InterpolatedStringTextSyntax { Parent: InterpolatedStringExpressionSyntax interpolated } => interpolated,
            InterpolatedStringExpressionSyntax interpolated => interpolated,
            _ => null,
        };
        if (expression == null)
        {
            return null;
        }

        var before = new StringBuilder();
        AppendEarlierPieces(expression, before);
        if (expression is InterpolatedStringExpressionSyntax interpolatedString)
        {
            AppendInterpolatedPrefix(interpolatedString, position, before);
        }

        before.Append(Unescape(text.ToString(TextSpan.FromBounds(content.Start, position)), kind));
        var after = Unescape(text.ToString(TextSpan.FromBounds(position, content.End)), kind);
        return new StringLiteralContext(expression, content, before.ToString(), after);
    }

    private enum LiteralKind
    {
        Regular,
        Verbatim,
        Raw,
    }

    /// <summary>The span between the quotes of a string token, when the caret is inside it.</summary>
    private static bool TryGetContent(SyntaxToken token, int position, out TextSpan content, out LiteralKind kind)
    {
        content = default;
        kind = LiteralKind.Regular;
        var text = token.Text;
        var start = token.SpanStart;
        var end = token.Span.End;

        switch (token.Kind())
        {
            case SyntaxKind.StringLiteralToken:
            {
                var isVerbatim = text.StartsWith("@", StringComparison.Ordinal);
                kind = isVerbatim ? LiteralKind.Verbatim : LiteralKind.Regular;
                var prefix = isVerbatim ? 2 : 1;
                if (text.Length < prefix)
                {
                    return false;
                }

                var terminated = text.Length > prefix && text[text.Length - 1] == '"' && !EndsWithEscapedQuote(text, prefix, isVerbatim);
                content = TextSpan.FromBounds(start + prefix, terminated ? end - 1 : end);
                break;
            }

            case SyntaxKind.SingleLineRawStringLiteralToken:
            case SyntaxKind.MultiLineRawStringLiteralToken:
            {
                kind = LiteralKind.Raw;
                var quotes = 0;
                while (quotes < text.Length && text[quotes] == '"')
                {
                    quotes++;
                }

                var closing = 0;
                while (closing < text.Length - quotes && text[text.Length - 1 - closing] == '"')
                {
                    closing++;
                }

                content = TextSpan.FromBounds(start + quotes, end - Math.Min(closing, quotes));
                break;
            }

            case SyntaxKind.InterpolatedStringTextToken:
            {
                kind = token.Parent?.Parent is InterpolatedStringExpressionSyntax interpolated
                    ? InterpolatedKind(interpolated)
                    : LiteralKind.Regular;
                content = token.Span;
                break;
            }

            case SyntaxKind.InterpolatedStringEndToken:
            case SyntaxKind.InterpolatedRawStringEndToken:
            {
                // $"|" has no text token; the caret sits right before the closing quote.
                if (position != start || token.Parent is not InterpolatedStringExpressionSyntax interpolated)
                {
                    return false;
                }

                kind = InterpolatedKind(interpolated);
                content = new TextSpan(position, 0);
                return true;
            }

            default:
                return false;
        }

        return position >= content.Start && position <= content.End;
    }

    private static LiteralKind InterpolatedKind(InterpolatedStringExpressionSyntax interpolated)
    {
        var start = interpolated.StringStartToken.Text;
        if (start.Contains("\"\"\""))
        {
            return LiteralKind.Raw;
        }

        return start.Contains("@") ? LiteralKind.Verbatim : LiteralKind.Regular;
    }

    private static bool EndsWithEscapedQuote(string text, int prefix, bool isVerbatim)
    {
        // Is the final quote an escaped one, i.e. is the literal still open?
        if (isVerbatim)
        {
            var quotes = 0;
            for (var i = text.Length - 1; i >= prefix && text[i] == '"'; i--)
            {
                quotes++;
            }

            return quotes % 2 == 0;
        }

        var backslashes = 0;
        for (var i = text.Length - 2; i >= prefix && text[i] == '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    /// <summary>For <c>"a" + b + "c|"</c>: everything to the left of the piece the caret is in.</summary>
    private static void AppendEarlierPieces(ExpressionSyntax expression, StringBuilder builder)
    {
        SyntaxNode current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax)
        {
            current = current.Parent;
        }

        var pieces = new Stack<ExpressionSyntax>();
        while (current.Parent is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            if (binary.Right == current)
            {
                pieces.Push(binary.Left);
            }

            current = binary;
            while (current.Parent is ParenthesizedExpressionSyntax)
            {
                current = current.Parent;
            }
        }

        foreach (var piece in pieces)
        {
            AppendValue(piece, builder);
        }
    }

    private static void AppendValue(ExpressionSyntax expression, StringBuilder builder)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                AppendValue(parenthesized.Expression, builder);
                break;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression):
                AppendValue(binary.Left, builder);
                AppendValue(binary.Right, builder);
                break;
            case LiteralExpressionSyntax literal when literal.Token.Value is string value:
                builder.Append(value);
                break;
            case InterpolatedStringExpressionSyntax interpolated:
                foreach (var content in interpolated.Contents)
                {
                    builder.Append(content is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : Placeholder);
                }

                break;
            default:
                builder.Append(Placeholder);
                break;
        }
    }

    private static void AppendInterpolatedPrefix(InterpolatedStringExpressionSyntax interpolated, int position, StringBuilder builder)
    {
        foreach (var content in interpolated.Contents)
        {
            if (content.Span.End > position || (content is InterpolatedStringTextSyntax && content.Span.End >= position))
            {
                break;
            }

            builder.Append(content is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : Placeholder);
        }
    }

    /// <summary>Stands in for a value that is only known at run time.</summary>
    internal const string Placeholder = "0";

    private static string Unescape(string source, LiteralKind kind)
    {
        switch (kind)
        {
            case LiteralKind.Verbatim:
                return source.Replace("\"\"", "\"");
            case LiteralKind.Raw:
                return source;
            default:
                if (source.IndexOf('\\') < 0)
                {
                    return source;
                }

                var builder = new StringBuilder(source.Length);
                for (var i = 0; i < source.Length; i++)
                {
                    var c = source[i];
                    if (c != '\\' || i + 1 >= source.Length)
                    {
                        builder.Append(c);
                        continue;
                    }

                    i++;
                    switch (source[i])
                    {
                        case 'n':
                        case 'r':
                        case 't':
                            builder.Append(' ');
                            break;
                        default:
                            builder.Append(source[i]);
                            break;
                    }
                }

                return builder.ToString();
        }
    }
}
