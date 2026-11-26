using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Naming;
using ShkoOnline.EarlyBound.Tool.Authentication;
using ShkoOnline.EarlyBound.Tool.Dataverse;
using ShkoOnline.EarlyBound.Tool.Output;

namespace ShkoOnline.EarlyBound.Tool.Commands;

/// <summary>Command-line values that override <c>earlybound.yaml</c> for one run.</summary>
internal sealed class PullOptions
{
    public string? Config { get; set; }

    public string? Environment { get; set; }

    public IReadOnlyList<string> Entities { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Solutions { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Exclude { get; set; } = Array.Empty<string>();

    public string? Output { get; set; }

    public string? Layout { get; set; }

    public string? Relationships { get; set; }

    public int? LanguageCode { get; set; }

    public bool DryRun { get; set; }

    public AuthenticationOptions Authentication { get; set; } = new();
}

/// <summary><c>earlybound pull</c>: downloads metadata and writes the YAML files.</summary>
internal static class PullCommand
{
    public static async Task<int> RunAsync(PullOptions options, ConsoleUi ui, CancellationToken cancellationToken)
    {
        ui.Banner();
        var project = ProjectConfiguration.Load(options.Config);
        if (!CommandSupport.ReportConfiguration(project, ui))
        {
            return 1;
        }

        var settings = Merge(project.Configuration.Pull, options);
        if (!CommandSupport.TryGetEnvironment(options.Environment, project, ui, out var environment))
        {
            return 1;
        }

        if (settings.Entities.Count == 0 && settings.Solutions.Count == 0)
        {
            ui.Error("Nothing to pull: no tables or solutions are listed.");
            ui.Line(project.Exists
                ? $"Add table names under 'pull.entities' in {project.FilePath}, or pass --entities."
                : "Run 'earlybound init' to create earlybound.yaml, or pass --entities account contact.");
            return 1;
        }

        var outputDirectory = Path.GetFullPath(Path.Combine(project.Directory, settings.Output));
        ui.Detail($"Environment  {environment}");
        ui.Detail($"Output       {outputDirectory}");

        using var api = await CommandSupport.ConnectAsync(environment, options.Authentication, ui, cancellationToken).ConfigureAwait(false);
        var downloader = new MetadataDownloader(api);
        var result = await ui.StatusAsync(
            "Downloading metadata…",
            update => downloader.PullAsync(settings, (done, total, table) => update($"Downloading metadata… {done}/{total}  {table}"), cancellationToken))
            .ConfigureAwait(false);

        foreach (var warning in result.Warnings)
        {
            ui.Warn(warning);
        }

        foreach (var note in result.Notes)
        {
            ui.Note(note);
        }

        if (result.Entities.Count == 0)
        {
            ui.Error("No table was pulled, so no file was written.");
            return 1;
        }

        var columns = result.Entities.Sum(e => e.Attributes.Count);
        var what = $"{Count(result.Entities.Count, "table")} ({Count(columns, "column")}) and {Count(result.GlobalOptionSets.Count, "global choice")}";
        if (options.DryRun)
        {
            ui.Done($"Would write {what}. Nothing was written (--dry-run).");
            ui.Table(new[] { "Table", "Columns", "Display name" }, result.Entities.Select(e => (IReadOnlyList<string>)new[]
            {
                e.LogicalName, e.Attributes.Count.ToString(), e.DisplayName ?? string.Empty,
            }));
            return 0;
        }

        var summary = MetadataFileWriter.Write(outputDirectory, settings.Layout, result.Entities, result.GlobalOptionSets);
        ui.Done($"Pulled {what}, labels in language {result.LanguageCode}.");
        ui.Detail($"{Count(summary.Written, "file")} written, {summary.Unchanged} unchanged, {summary.Removed} removed.");

        SuggestCustomWords(result.Entities, project.Configuration.Generator, ui);
        if (!project.Exists)
        {
            ui.Line();
            ui.Note("There is no earlybound.yaml yet. Run 'earlybound init' to keep this selection for the next pull.");
        }

        return 0;
    }

    private static string Count(int number, string noun) => number == 1 ? $"1 {noun}" : $"{number} {noun}s";

    private static PullSettings Merge(PullSettings configured, PullOptions options) => new()
    {
        Entities = options.Entities.Count > 0 ? options.Entities.ToList() : configured.Entities,
        Solutions = options.Solutions.Count > 0 ? options.Solutions.ToList() : configured.Solutions,
        Exclude = options.Exclude.Count > 0 ? options.Exclude.ToList() : configured.Exclude,
        Output = string.IsNullOrWhiteSpace(options.Output) ? configured.Output : options.Output!,
        Layout = string.IsNullOrWhiteSpace(options.Layout) ? configured.Layout : options.Layout!,
        Relationships = string.IsNullOrWhiteSpace(options.Relationships) ? configured.Relationships : options.Relationships!,
        LanguageCode = options.LanguageCode ?? configured.LanguageCode,
    };

    /// <summary>
    /// Publisher prefixes are not English words, so the PascalCaser spells them letter by letter
    /// (sho_ becomes SHo_) until they are listed as custom words. Says so, with the fix.
    /// </summary>
    private static void SuggestCustomWords(IReadOnlyList<EntityDefinition> entities, GeneratorSettings generator, ConsoleUi ui)
    {
        if (!generator.CamelCaseClassNames && !generator.CamelCaseMemberNames)
        {
            return;
        }

        var prefixes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            AddPrefix(entity.SchemaName, prefixes);
            foreach (var attribute in entity.Attributes)
            {
                AddPrefix(attribute.SchemaName, prefixes);
            }
        }

        var caser = new CamelCaser(generator.CamelCaseCustomWords, generator.TokenCapitalizationOverrides);
        var missing = prefixes
            .Where(prefix => caser.Case(prefix) != char.ToUpperInvariant(prefix[0]) + prefix.Substring(1))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var example = missing[0];
        ui.Line();
        ui.Note($"Names starting with {string.Join(", ", missing.Select(p => p + "_"))} will be cased as '{caser.Case(example)}_…' because "
            + (missing.Count == 1 ? "that prefix is" : "those prefixes are") + " not a known word. To get '"
            + char.ToUpperInvariant(example[0]) + example.Substring(1) + "_…', add this to the generator section of earlybound.yaml:");
        ui.Snippet("camelCaseCustomWords: [" + string.Join(", ", generator.CamelCaseCustomWords.Concat(missing)) + "]");
    }

    private static void AddPrefix(string schemaName, ISet<string> prefixes)
    {
        var underscore = schemaName.IndexOf('_');
        if (underscore is > 1 and < 9)
        {
            var prefix = schemaName.Substring(0, underscore).ToLowerInvariant();
            if (prefix.All(char.IsLetterOrDigit) && char.IsLetter(prefix[0]))
            {
                prefixes.Add(prefix);
            }
        }
    }
}
