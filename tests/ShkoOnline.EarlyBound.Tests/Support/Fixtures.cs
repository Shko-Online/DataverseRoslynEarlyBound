namespace ShkoOnline.EarlyBound.Tests.Support;

/// <summary>The YAML fixture files copied next to the test assembly.</summary>
internal static class Fixtures
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Every metadata fixture as (path, text), in path order.</summary>
    public static IReadOnlyList<(string Path, string Text)> MetadataFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "Metadata"), "*.yaml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => (p, File.ReadAllText(p)))
            .ToList();

    public static IReadOnlyList<(string Path, string Text)> MetadataFilesWith(params (string Path, string Text)[] extra) =>
        MetadataFiles().Concat(extra).ToList();

    /// <summary>A configuration file that makes the publisher prefix a known word.</summary>
    public const string Configuration = """
        generator:
          namespace: Contoso.Model
          camelCaseCustomWords: [sho]
        """;
}
