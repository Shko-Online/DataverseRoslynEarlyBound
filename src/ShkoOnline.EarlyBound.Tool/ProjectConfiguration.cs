using System.Text;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Tool;

/// <summary>The <c>earlybound.yaml</c> of a project, or its defaults when there is none.</summary>
internal sealed class ProjectConfiguration
{
    public const string FileName = "earlybound.yaml";
    private const string AlternativeFileName = "earlybound.yml";

    private ProjectConfiguration(string path, bool exists, EarlyBoundConfiguration configuration, IReadOnlyList<MetadataDiagnostic> diagnostics)
    {
        FilePath = path;
        Exists = exists;
        Configuration = configuration;
        Diagnostics = diagnostics;
    }

    public string FilePath { get; }

    public string Directory => Path.GetDirectoryName(FilePath)!;

    public bool Exists { get; }

    public EarlyBoundConfiguration Configuration { get; }

    public IReadOnlyList<MetadataDiagnostic> Diagnostics { get; }

    /// <summary>The folder metadata files live in.</summary>
    public string OutputDirectory => Path.GetFullPath(Path.Combine(Directory, Configuration.Pull.Output));

    /// <param name="pathOrDirectory">A configuration file, or the folder to look for one in; the current folder when null.</param>
    public static ProjectConfiguration Load(string? pathOrDirectory)
    {
        var path = Resolve(pathOrDirectory);
        if (!File.Exists(path))
        {
            return new ProjectConfiguration(path, false, new EarlyBoundConfiguration(), Array.Empty<MetadataDiagnostic>());
        }

        var file = MetadataYamlReader.Read(File.ReadAllText(path, Encoding.UTF8), path);
        return new ProjectConfiguration(path, true, file.Configuration ?? new EarlyBoundConfiguration(), file.Diagnostics);
    }

    private static string Resolve(string? pathOrDirectory)
    {
        var target = string.IsNullOrWhiteSpace(pathOrDirectory)
            ? System.IO.Directory.GetCurrentDirectory()
            : Path.GetFullPath(pathOrDirectory);

        if (System.IO.Directory.Exists(target))
        {
            var alternative = Path.Combine(target, AlternativeFileName);
            var preferred = Path.Combine(target, FileName);
            return !File.Exists(preferred) && File.Exists(alternative) ? alternative : preferred;
        }

        return target;
    }

    /// <summary>The commented starter file written by <c>earlybound init</c>.</summary>
    public static string Template(string? environment, IReadOnlyList<string> entities, IReadOnlyList<string> solutions, string? @namespace)
    {
        var builder = new StringBuilder();
        void Line(string text = "") => builder.Append(text).Append('\n');

        Line("# Shko Online's Early Bound");
        Line("# https://github.com/Shko-Online/DataverseRoslynEarlyBound");
        Line("#");
        Line("# 'earlybound pull' reads the \"pull\" section and downloads metadata into the output folder.");
        Line("# The ShkoOnline.EarlyBound package reads the \"generator\" section whenever the project builds.");
        Line();
        Line("# The Dataverse environment metadata is pulled from.");
        Line("environment: " + (string.IsNullOrWhiteSpace(environment) ? "https://yourorg.crm.dynamics.com" : environment));
        Line();
        Line("pull:");
        Line("  # Tables to pull, by logical name. * matches any run of characters, for example \"sho_*\".");
        if (entities.Count == 0 && solutions.Count == 0)
        {
            Line("  entities:");
            Line("    - account");
            Line("    - contact");
        }
        else if (entities.Count == 0)
        {
            Line("  entities: []");
        }
        else
        {
            Line("  entities:");
            foreach (var entity in entities)
            {
                Line("    - " + Quote(entity));
            }
        }

        Line();
        Line("  # Also pull every table of these solutions (unique names, not display names).");
        if (solutions.Count == 0)
        {
            Line("  solutions: []");
        }
        else
        {
            Line("  solutions:");
            foreach (var solution in solutions)
            {
                Line("    - " + Quote(solution));
            }
        }

        Line();
        Line("  # Tables to leave out, even when a pattern or a solution above includes them.");
        Line("  exclude: []");
        Line();
        Line("  # Folder for the metadata files, relative to this file.");
        Line("  output: Metadata");
        Line();
        Line("  # perEntity: one file per table and per global choice. singleFile: everything in one file.");
        Line("  layout: perEntity");
        Line();
        Line("  # included: relationships between pulled tables. all: every relationship. none: no relationships.");
        Line("  relationships: included");
        Line();
        Line("  # Language of labels and descriptions, as an LCID. Defaults to the environment's base language.");
        Line("  # languageCode: 1033");
        Line();
        Line("generator:");
        Line("  # Namespace of the generated classes. Defaults to the project's root namespace.");
        Line(string.IsNullOrWhiteSpace(@namespace) ? "  # namespace: Contoso.Model" : "  namespace: " + @namespace);
        Line();
        Line("  # Name of the generated OrganizationServiceContext. Leave empty to generate none.");
        Line("  serviceContextName: DataverseContext");
        Line();
        Line("  # Publisher prefixes and other words to recognise when PascalCasing names,");
        Line("  # so that sho_duedate becomes Sho_DueDate. 'earlybound pull' tells you which ones are missing.");
        Line("  camelCaseCustomWords: []");
        Line();
        Line("  # Every setting: https://github.com/Shko-Online/DataverseRoslynEarlyBound/blob/main/docs/configuration.md");
        return builder.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny(new[] { '*', ':', '#', '{', '[', ',', '&', '!', '|', '>', '\'', '"', '%', '@', '`', ' ' }) >= 0
            ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value;
}
