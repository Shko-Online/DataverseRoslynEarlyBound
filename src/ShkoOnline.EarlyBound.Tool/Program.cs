using System.CommandLine;
using Azure.Identity;
using ShkoOnline.EarlyBound.Tool.Authentication;
using ShkoOnline.EarlyBound.Tool.Commands;
using ShkoOnline.EarlyBound.Tool.Dataverse;
using ShkoOnline.EarlyBound.Tool.Output;

namespace ShkoOnline.EarlyBound.Tool;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var ui = new ConsoleUi();

        // Options shared by every command that talks to Dataverse.
        var config = new Option<string?>("--config", "-c")
        {
            Description = "Path to earlybound.yaml, or the folder it is in. Defaults to the current folder.",
            HelpName = "path",
        };
        var environment = new Option<string?>("--environment", "-e")
        {
            Description = "URL of the Dataverse environment, e.g. https://yourorg.crm.dynamics.com. Overrides earlybound.yaml.",
            HelpName = "url",
        };
        var auth = new Option<string>("--auth")
        {
            Description = "How to sign in: browser (default), devicecode, azurecli, environment (service principal from AZURE_* variables) or default (non-interactive chain).",
            HelpName = "method",
            DefaultValueFactory = _ => "browser",
        };
        var tenant = new Option<string?>("--tenant")
        {
            Description = "Directory (tenant) ID or domain to sign in to. Needed for guest accounts.",
            HelpName = "tenant",
        };
        var clientId = new Option<string?>("--client-id")
        {
            Description = "App registration to use for interactive sign-in instead of the built-in one.",
            HelpName = "id",
        };

        AuthenticationOptions Authentication(ParseResult parse)
        {
            if (!CredentialFactory.TryParseMethod(parse.GetValue(auth), out var method))
            {
                throw new ArgumentException($"'{parse.GetValue(auth)}' is not a sign-in method. Use browser, devicecode, azurecli, environment or default.");
            }

            return new AuthenticationOptions { Method = method, TenantId = parse.GetValue(tenant), ClientId = parse.GetValue(clientId) };
        }

        var root = new RootCommand("Shko Online's Early Bound: downloads Dataverse metadata into YAML files for the ShkoOnline.EarlyBound source generator.");

        // ---- init ----------------------------------------------------------------------------
        var initDirectory = new Option<string?>("--directory", "-d") { Description = "Folder to create earlybound.yaml in. Defaults to the current folder.", HelpName = "path" };
        var initEntities = new Option<string[]>("--entities") { Description = "Tables to list in the file.", AllowMultipleArgumentsPerToken = true, HelpName = "names" };
        var initSolutions = new Option<string[]>("--solutions") { Description = "Solutions to list in the file.", AllowMultipleArgumentsPerToken = true, HelpName = "names" };
        var initNamespace = new Option<string?>("--namespace") { Description = "Namespace of the generated classes.", HelpName = "namespace" };
        var initForce = new Option<bool>("--force") { Description = "Replace an existing earlybound.yaml." };
        var init = new Command("init", "Create earlybound.yaml, the file that says what to pull and how to generate.")
        {
            initDirectory, environment, initEntities, initSolutions, initNamespace, initForce,
        };
        init.SetAction(parse => InitCommand.Run(
            parse.GetValue(initDirectory),
            parse.GetValue(environment),
            parse.GetValue(initEntities) ?? Array.Empty<string>(),
            parse.GetValue(initSolutions) ?? Array.Empty<string>(),
            parse.GetValue(initNamespace),
            parse.GetValue(initForce),
            ui));
        root.Add(init);

        // ---- pull ----------------------------------------------------------------------------
        var entities = new Option<string[]>("--entities") { Description = "Tables to pull instead of those in earlybound.yaml. * is a wildcard.", AllowMultipleArgumentsPerToken = true, HelpName = "names" };
        var solutions = new Option<string[]>("--solutions") { Description = "Solutions whose tables to pull instead of those in earlybound.yaml.", AllowMultipleArgumentsPerToken = true, HelpName = "names" };
        var exclude = new Option<string[]>("--exclude") { Description = "Tables to leave out.", AllowMultipleArgumentsPerToken = true, HelpName = "names" };
        var output = new Option<string?>("--output", "-o") { Description = "Folder for the metadata files, relative to earlybound.yaml.", HelpName = "path" };
        var layout = new Option<string?>("--layout") { Description = "perEntity (one file per table) or singleFile.", HelpName = "layout" };
        layout.AcceptOnlyFromAmong("perEntity", "singleFile");
        var relationships = new Option<string?>("--relationships") { Description = "included (between pulled tables), all or none.", HelpName = "mode" };
        relationships.AcceptOnlyFromAmong("included", "all", "none");
        var language = new Option<int?>("--language") { Description = "Language of labels as an LCID, e.g. 1033. Defaults to the environment's base language.", HelpName = "lcid" };
        var dryRun = new Option<bool>("--dry-run") { Description = "Show what would be pulled without writing files." };
        var pull = new Command("pull", "Download table metadata from Dataverse into YAML files.")
        {
            config, environment, entities, solutions, exclude, output, layout, relationships, language, dryRun, auth, tenant, clientId,
        };
        pull.SetAction((parse, cancellationToken) => RunAsync(ui, () => PullCommand.RunAsync(
            new PullOptions
            {
                Config = parse.GetValue(config),
                Environment = parse.GetValue(environment),
                Entities = parse.GetValue(entities) ?? Array.Empty<string>(),
                Solutions = parse.GetValue(solutions) ?? Array.Empty<string>(),
                Exclude = parse.GetValue(exclude) ?? Array.Empty<string>(),
                Output = parse.GetValue(output),
                Layout = parse.GetValue(layout),
                Relationships = parse.GetValue(relationships),
                LanguageCode = parse.GetValue(language),
                DryRun = parse.GetValue(dryRun),
                Authentication = Authentication(parse),
            },
            ui,
            cancellationToken)));
        root.Add(pull);

        // ---- tables / solutions --------------------------------------------------------------
        var filter = new Option<string?>("--filter", "-f") { Description = "Show tables whose logical or display name contains this text. * is a wildcard.", HelpName = "text" };
        var custom = new Option<bool>("--custom") { Description = "Show custom tables only." };
        var tables = new Command("tables", "List the tables of an environment.") { config, environment, filter, custom, auth, tenant, clientId };
        tables.SetAction((parse, cancellationToken) => RunAsync(ui, () => ListCommands.TablesAsync(
            parse.GetValue(config), parse.GetValue(environment), parse.GetValue(filter), parse.GetValue(custom), Authentication(parse), ui, cancellationToken)));
        root.Add(tables);

        var solutionList = new Command("solutions", "List the solutions of an environment.") { config, environment, auth, tenant, clientId };
        solutionList.SetAction((parse, cancellationToken) => RunAsync(ui, () => ListCommands.SolutionsAsync(
            parse.GetValue(config), parse.GetValue(environment), Authentication(parse), ui, cancellationToken)));
        root.Add(solutionList);

        // ---- generate ------------------------------------------------------------------------
        var generateOutput = new Option<string>("--output", "-o") { Description = "Folder to write the C# files to.", Required = true, HelpName = "path" };
        var generateNamespace = new Option<string?>("--namespace") { Description = "Namespace of the generated classes. Overrides earlybound.yaml.", HelpName = "namespace" };
        var generate = new Command("generate", "Write the generated C# to a folder, for projects that cannot use the source generator.")
        {
            config, generateOutput, generateNamespace,
        };
        generate.SetAction(parse => Guard(ui, () => GenerateCommand.Run(parse.GetValue(config), parse.GetRequiredValue(generateOutput), parse.GetValue(generateNamespace), ui)));
        root.Add(generate);

        // ---- logout --------------------------------------------------------------------------
        var logout = new Command("logout", "Forget remembered sign-ins.");
        logout.SetAction(_ => Guard(ui, () => LogoutCommand.Run(ui)));
        root.Add(logout);

        return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
    }

    private static int Guard(ConsoleUi ui, Func<int> command)
    {
        try
        {
            return command();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ui.Error(ex.Message);
            return 1;
        }
    }

    /// <summary>Runs a command and turns the failures a user can act on into a message and exit code 1.</summary>
    private static async Task<int> RunAsync(ConsoleUi ui, Func<Task<int>> command)
    {
        try
        {
            return await command().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ui.Warn("Cancelled.");
            return 130;
        }
        catch (DataverseException ex)
        {
            ui.Error(ex.Message);
            return 1;
        }
        catch (CredentialUnavailableException ex)
        {
            ui.Error("No credentials are available for the chosen sign-in method: " + FirstLine(ex.Message));
            return 1;
        }
        catch (AuthenticationFailedException ex)
        {
            ui.Error("Sign-in failed: " + FirstLine(ex.Message));
            ui.Line("If your account is a guest in the environment's tenant, pass --tenant <tenant id or domain>.");
            return 1;
        }
        catch (HttpRequestException ex)
        {
            ui.Error("The environment could not be reached: " + ex.Message);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ui.Error(ex.Message);
            return 1;
        }
    }

    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(new[] { '\r', '\n' });
        return index < 0 ? message : message.Substring(0, index);
    }
}
