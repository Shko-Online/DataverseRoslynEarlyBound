using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Tool.Authentication;
using ShkoOnline.EarlyBound.Tool.Dataverse;
using ShkoOnline.EarlyBound.Tool.Output;

namespace ShkoOnline.EarlyBound.Tool.Commands;

/// <summary>What the commands have in common.</summary>
internal static class CommandSupport
{
    /// <summary>Prints problems found in earlybound.yaml. Returns false when they are fatal.</summary>
    public static bool ReportConfiguration(ProjectConfiguration project, ConsoleUi ui)
    {
        var ok = true;
        foreach (var diagnostic in project.Diagnostics)
        {
            if (diagnostic.Severity == MetadataSeverity.Error)
            {
                ui.Error(diagnostic.ToString());
                ok = false;
            }
            else
            {
                ui.Warn(diagnostic.ToString());
            }
        }

        return ok;
    }

    /// <summary>The environment URL from the command line, else from earlybound.yaml.</summary>
    public static bool TryGetEnvironment(string? option, ProjectConfiguration project, ConsoleUi ui, out Uri environment)
    {
        environment = null!;
        var text = string.IsNullOrWhiteSpace(option) ? project.Configuration.Environment : option;
        if (string.IsNullOrWhiteSpace(text))
        {
            ui.Error("No Dataverse environment is set.");
            ui.Line(project.Exists
                ? $"Set 'environment' in {project.FilePath}, or pass --environment https://yourorg.crm.dynamics.com."
                : "Pass --environment https://yourorg.crm.dynamics.com, or run 'earlybound init' first.");
            return false;
        }

        text = text!.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps)
        {
            ui.Error($"'{text}' is not a valid environment URL. It looks like https://yourorg.crm.dynamics.com.");
            return false;
        }

        environment = DataverseApi.Normalize(parsed);
        return true;
    }

    public static async Task<DataverseApi> ConnectAsync(Uri environment, AuthenticationOptions authentication, ConsoleUi ui, CancellationToken cancellationToken)
    {
        var credential = await CredentialFactory.CreateAsync(environment, authentication, ui.Note, cancellationToken).ConfigureAwait(false);
        return new DataverseApi(environment, credential);
    }
}
