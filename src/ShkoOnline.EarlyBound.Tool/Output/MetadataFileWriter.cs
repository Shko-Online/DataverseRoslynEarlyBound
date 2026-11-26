using System.Text;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Tool.Output;

internal sealed record WriteSummary(int Written, int Unchanged, int Removed, string Directory);

/// <summary>Writes pulled metadata to disk in the configured layout and keeps the folder in step with it.</summary>
internal static class MetadataFileWriter
{
    public const string SingleFileName = "metadata.yaml";

    /// <summary>First header line; also what marks a file as ours when cleaning up.</summary>
    public const string Marker = "Shko Online's Early Bound - Dataverse metadata";

    private static readonly string[] Header =
    {
        Marker,
        "Written by 'earlybound pull'. Do not edit: the next pull overwrites this file.",
        "To change what is generated from it, edit earlybound.yaml.",
        string.Empty,
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static WriteSummary Write(string outputDirectory, string layout, IReadOnlyList<EntityDefinition> entities, IReadOnlyList<OptionSetDefinition> globalOptionSets)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.Equals(layout, PullSettings.LayoutSingleFile, StringComparison.OrdinalIgnoreCase))
        {
            files[Path.Combine(outputDirectory, SingleFileName)] = MetadataYamlWriter.Write(entities, globalOptionSets, Header);
        }
        else
        {
            foreach (var entity in entities)
            {
                files[Path.Combine(outputDirectory, "entities", FileName(entity.LogicalName))] =
                    MetadataYamlWriter.Write(new[] { entity }, Array.Empty<OptionSetDefinition>(), Header);
            }

            foreach (var optionSet in globalOptionSets)
            {
                files[Path.Combine(outputDirectory, "optionsets", FileName(optionSet.Name))] =
                    MetadataYamlWriter.Write(Array.Empty<EntityDefinition>(), new[] { optionSet }, Header);
            }
        }

        var written = 0;
        var unchanged = 0;
        foreach (var file in files)
        {
            // Untouched files keep their timestamp, so nothing rebuilds or reloads without reason.
            if (File.Exists(file.Key) && File.ReadAllText(file.Key, Encoding.UTF8) == file.Value)
            {
                unchanged++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);
            File.WriteAllText(file.Key, file.Value, Utf8NoBom);
            written++;
        }

        var removed = RemoveStale(outputDirectory, files.Keys);
        return new WriteSummary(written, unchanged, removed, outputDirectory);
    }

    /// <summary>
    /// Deletes metadata files of tables that are no longer pulled. Only files this tool wrote are
    /// touched, recognised by their header; anything else in the folder is left alone.
    /// </summary>
    private static int RemoveStale(string outputDirectory, IEnumerable<string> current)
    {
        if (!Directory.Exists(outputDirectory))
        {
            return 0;
        }

        var keep = new HashSet<string>(current.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.y*ml", SearchOption.AllDirectories))
        {
            if (keep.Contains(Path.GetFullPath(path)) || !IsOurs(path))
            {
                continue;
            }

            File.Delete(path);
            removed++;
        }

        return removed;
    }

    private static bool IsOurs(string path)
    {
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            var firstLine = reader.ReadLine();
            return firstLine != null && firstLine.Contains(Marker, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string FileName(string logicalName)
    {
        var builder = new StringBuilder(logicalName.Length + 5);
        foreach (var c in logicalName)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        }

        return builder.Append(".yaml").ToString();
    }
}
