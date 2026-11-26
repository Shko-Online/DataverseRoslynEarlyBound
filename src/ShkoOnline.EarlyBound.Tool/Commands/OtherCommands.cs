using System.Text;
using ShkoOnline.EarlyBound.CodeGeneration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Model;
using ShkoOnline.EarlyBound.Tool.Authentication;
using ShkoOnline.EarlyBound.Tool.Dataverse;
using ShkoOnline.EarlyBound.Tool.Output;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Tool.Commands;

/// <summary><c>earlybound init</c>: writes a commented earlybound.yaml.</summary>
internal static class InitCommand
{
    public static int Run(string? directory, string? environment, IReadOnlyList<string> entities, IReadOnlyList<string> solutions, string? @namespace, bool force, ConsoleUi ui)
    {
        ui.Banner();
        var target = Path.GetFullPath(string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory);
        var path = Path.Combine(target, ProjectConfiguration.FileName);
        if (File.Exists(path) && !force)
        {
            ui.Error($"{path} already exists. Edit it, or pass --force to replace it.");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(environment))
        {
            // Whatever form the URL was given in, the file gets the environment's root URL.
            var text = environment.Contains("://", StringComparison.Ordinal) ? environment.Trim() : "https://" + environment.Trim();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
            {
                ui.Error($"'{environment}' is not a valid environment URL. It looks like https://yourorg.crm.dynamics.com.");
                return 1;
            }

            environment = DataverseApi.Normalize(parsed).ToString().TrimEnd('/');
        }

        Directory.CreateDirectory(target);
        File.WriteAllText(path, ProjectConfiguration.Template(environment, entities, solutions, @namespace), new UTF8Encoding(false));
        ui.Done($"Created {path}");
        ui.Line();
        ui.Line("Next:");
        if (string.IsNullOrWhiteSpace(environment))
        {
            ui.Note("Set 'environment' in the file to your Dataverse URL.");
        }

        ui.Note("List the tables you need under 'pull.entities' ('earlybound tables' shows what exists).");
        ui.Note("Download the metadata:");
        ui.Snippet("dotnet earlybound pull");
        ui.Note("Add the generator to the project, if it is not there yet:");
        ui.Snippet("dotnet add package ShkoOnline.EarlyBound");
        return 0;
    }
}

/// <summary><c>earlybound tables</c> and <c>earlybound solutions</c>: show what an environment has.</summary>
internal static class ListCommands
{
    public static async Task<int> TablesAsync(string? config, string? environmentOption, string? filter, bool customOnly, AuthenticationOptions authentication, ConsoleUi ui, CancellationToken cancellationToken)
    {
        ui.Banner();
        var project = ProjectConfiguration.Load(config);
        if (!CommandSupport.TryGetEnvironment(environmentOption, project, ui, out var environment))
        {
            return 1;
        }

        using var api = await CommandSupport.ConnectAsync(environment, authentication, ui, cancellationToken).ConfigureAwait(false);
        var downloader = new MetadataDownloader(api);
        var tables = await ui.StatusAsync("Reading tables…", async _ =>
        {
            var languageCode = project.Configuration.Pull.LanguageCode ?? await downloader.GetBaseLanguageAsync(cancellationToken).ConfigureAwait(false);
            return await downloader.ListEntitiesAsync(languageCode, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);

        var matcher = string.IsNullOrWhiteSpace(filter) ? null : EntitySelector.Matcher(filter!.Contains('*') ? filter : "*" + filter + "*");
        var shown = tables
            .Where(t => !t.IsIntersect && (!customOnly || t.IsCustom) && (matcher == null || matcher(t.LogicalName) || (t.DisplayName != null && matcher(t.DisplayName))))
            .ToList();

        ui.Table(new[] { "Logical name", "Display name", "Custom" }, shown.Select(t => (IReadOnlyList<string>)new[]
        {
            t.LogicalName, t.DisplayName ?? string.Empty, t.IsCustom ? "yes" : string.Empty,
        }));
        ui.Detail($"{shown.Count} of {tables.Count} tables.");
        return 0;
    }

    public static async Task<int> SolutionsAsync(string? config, string? environmentOption, AuthenticationOptions authentication, ConsoleUi ui, CancellationToken cancellationToken)
    {
        ui.Banner();
        var project = ProjectConfiguration.Load(config);
        if (!CommandSupport.TryGetEnvironment(environmentOption, project, ui, out var environment))
        {
            return 1;
        }

        using var api = await CommandSupport.ConnectAsync(environment, authentication, ui, cancellationToken).ConfigureAwait(false);
        var solutions = await ui.StatusAsync("Reading solutions…", _ => new MetadataDownloader(api).ListSolutionsAsync(cancellationToken)).ConfigureAwait(false);
        ui.Table(new[] { "Unique name", "Display name", "Version", "Managed" }, solutions.Select(s => (IReadOnlyList<string>)new[]
        {
            s.UniqueName, s.FriendlyName ?? string.Empty, s.Version ?? string.Empty, s.IsManaged ? "yes" : string.Empty,
        }));
        ui.Detail($"{solutions.Count} solutions.");
        return 0;
    }
}

/// <summary>
/// <c>earlybound generate</c>: writes the C# the source generator would produce to a folder.
/// For toolchains without source generators, and for looking at the output.
/// </summary>
internal static class GenerateCommand
{
    public static int Run(string? config, string output, string? @namespace, ConsoleUi ui)
    {
        ui.Banner();
        var project = ProjectConfiguration.Load(config);
        var files = new List<MetadataFile>();
        if (project.Exists)
        {
            files.Add(MetadataYamlReader.Read(File.ReadAllText(project.FilePath, Encoding.UTF8), project.FilePath));
        }

        if (Directory.Exists(project.OutputDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(project.OutputDirectory, "*.*", SearchOption.AllDirectories)
                .Where(MetadataFileCache.HasYamlExtension)
                .OrderBy(p => p, StringComparer.Ordinal))
            {
                files.Add(MetadataYamlReader.Read(File.ReadAllText(path, Encoding.UTF8), path));
            }
        }

        var set = MetadataSet.Create(files);
        if (set.IsEmpty)
        {
            ui.Error($"No metadata was found in {project.OutputDirectory}. Run 'earlybound pull' first.");
            return 1;
        }

        var model = EarlyBoundModelBuilder.Build(set, rootNamespace: null, namespaceOverride: @namespace);
        var failed = false;
        foreach (var diagnostic in set.Diagnostics.Concat(model.Diagnostics))
        {
            switch (diagnostic.Severity)
            {
                case MetadataSeverity.Error:
                    ui.Error(diagnostic.ToString());
                    failed = true;
                    break;
                case MetadataSeverity.Warning:
                    ui.Warn(diagnostic.ToString());
                    break;
            }
        }

        if (failed)
        {
            return 1;
        }

        var directory = Path.GetFullPath(output);
        Directory.CreateDirectory(directory);
        var sources = CSharpEmitter.Emit(model, new EmitOptions { ToolVersion = ToolInfo.Version });
        foreach (var source in sources)
        {
            File.WriteAllText(Path.Combine(directory, source.HintName), source.Text, new UTF8Encoding(false));
        }

        ui.Done($"Wrote {sources.Count} files for {model.Entities.Count} tables and {model.OptionSets.Count} choices to {directory}");
        ui.Detail($"Namespace {model.Namespace}");
        return 0;
    }
}

/// <summary><c>earlybound logout</c>: forgets remembered sign-ins.</summary>
internal static class LogoutCommand
{
    public static int Run(ConsoleUi ui)
    {
        var result = CredentialFactory.SignOut();
        if (result.SignIns == 0 && !result.TokensRemoved)
        {
            ui.Done("No remembered sign-in.");
        }
        else
        {
            ui.Done($"Signed out{(result.SignIns == 0 ? string.Empty : $" of {result.SignIns} environment{(result.SignIns == 1 ? string.Empty : "s")}")}"
                + (result.TokensRemoved ? " and removed the cached tokens." : ".")
                + " The next command signs in again.");
        }

        if (result.TokensMayRemain)
        {
            ui.Note("Tokens kept in this system's keyring could not be removed from here. They can no longer be used by this tool and expire on their own.");
        }

        return 0;
    }
}
