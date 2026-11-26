using Spectre.Console;

namespace ShkoOnline.EarlyBound.Tool.Output;

/// <summary>
/// Console output in Shko Online's design language: crimson for identity, grey for what is
/// secondary, and plain words for everything else. Colour and animation switch off by themselves
/// when output is redirected or NO_COLOR is set.
/// </summary>
internal sealed class ConsoleUi
{
    /// <summary>Shko Online crimson: the logo colour.</summary>
    public const string Crimson = "#E40046";

    /// <summary>Secondary text.</summary>
    public const string Muted = "#7F7F7F";

    private const string Success = "#319795";
    private const string Warning = "#B7791F";
    private const string Danger = "#B80038";

    private readonly IAnsiConsole _out;
    private readonly IAnsiConsole _error;

    public ConsoleUi(IAnsiConsole? output = null, IAnsiConsole? error = null)
    {
        _out = output ?? AnsiConsole.Console;
        _error = error ?? AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

        // A file or a pipe has no width: without this, paths and messages would be wrapped at 80 columns.
        if (Console.IsOutputRedirected)
        {
            _out.Profile.Width = UnlimitedWidth;
        }

        if (Console.IsErrorRedirected)
        {
            _error.Profile.Width = UnlimitedWidth;
        }
    }

    private const int UnlimitedWidth = 10_000;

    public bool IsInteractive => _out.Profile.Capabilities.Interactive && !Console.IsOutputRedirected;

    public void Banner()
    {
        _out.MarkupLine($"[bold {Crimson}]Shko Online's Early Bound[/] [{Muted}]{Markup.Escape(ToolInfo.Version)}[/]");
    }

    public void Line(string text = "") => _out.WriteLine(text);

    /// <summary>A line of secondary information.</summary>
    public void Detail(string text) => _out.MarkupLine($"[{Muted}]{Markup.Escape(text)}[/]");

    public void Done(string text) => _out.MarkupLine($"[{Success}]✓[/] {Markup.Escape(text)}");

    public void Note(string text) => _out.MarkupLine($"[{Muted}]•[/] {Markup.Escape(text)}");

    public void Warn(string text) => _error.MarkupLine($"[{Warning}]![/] {Markup.Escape(text)}");

    public void Error(string text) => _error.MarkupLine($"[{Danger}]✗[/] {Markup.Escape(text)}");

    /// <summary>A code sample or command the user can copy; printed without decoration.</summary>
    public void Snippet(string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            _out.MarkupLine($"    [{Crimson}]{Markup.Escape(line)}[/]");
        }
    }

    public void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var table = new Table().Border(TableBorder.Minimal).BorderColor(Color.Grey50);
        foreach (var header in headers)
        {
            table.AddColumn(new TableColumn($"[bold]{Markup.Escape(header)}[/]"));
        }

        foreach (var row in rows)
        {
            table.AddRow(row.Select(cell => Markup.Escape(cell)).ToArray());
        }

        _out.Write(table);
    }

    /// <summary>Runs <paramref name="work"/> behind a spinner, or just a line of text when not interactive.</summary>
    public async Task<T> StatusAsync<T>(string text, Func<Action<string>, Task<T>> work)
    {
        if (!IsInteractive)
        {
            Detail(text);
            return await work(_ => { }).ConfigureAwait(false);
        }

        return await _out.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(Crimson))
            .StartAsync(Markup.Escape(text), context => work(update => context.Status(Markup.Escape(update))))
            .ConfigureAwait(false);
    }
}
