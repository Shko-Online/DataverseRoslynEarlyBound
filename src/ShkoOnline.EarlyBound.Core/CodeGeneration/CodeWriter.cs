using System.Text;

namespace ShkoOnline.EarlyBound.CodeGeneration;

/// <summary>A tab-indented text builder for C# source.</summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _builder = new(16 * 1024);
    private int _indent;

    public CodeWriter Line(string text)
    {
        _builder.Append('\t', _indent).Append(text).Append('\n');
        return this;
    }

    public CodeWriter Line()
    {
        _builder.Append('\n');
        return this;
    }

    public CodeWriter Open()
    {
        Line("{");
        _indent++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _indent--;
        Line("}" + suffix);
        return this;
    }

    /// <summary>Writes an XML documentation summary. Does nothing when there is no text.</summary>
    /// <param name="raw">True when <paramref name="text"/> already is XML and must not be escaped.</param>
    public CodeWriter Summary(string? text, bool raw = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return this;
        }

        Line("/// <summary>");

        // Every character C# treats as a line break has to start a new "///" line.
        var normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace('\u2028', '\n').Replace('\u2029', '\n').Replace('\u0085', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            Line("/// " + (raw ? line.Trim() : EscapeXml(line.Trim())));
        }

        Line("/// </summary>");
        return this;
    }

    public override string ToString() => _builder.ToString();

    public static string EscapeXml(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>A C# string literal for <paramref name="value"/>, quotes included.</summary>
    public static string Literal(string? value)
    {
        if (value == null)
        {
            return "null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\0':
                    builder.Append("\\0");
                    break;
                default:
                    // Line and paragraph separators end a line in C# source, so they are escaped too.
                    if (char.IsControl(c) || c == '\u2028' || c == '\u2029' || c == '\u0085')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
