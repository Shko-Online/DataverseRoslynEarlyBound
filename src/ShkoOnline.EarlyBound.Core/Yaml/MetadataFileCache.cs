using System.Runtime.CompilerServices;

namespace ShkoOnline.EarlyBound.Yaml;

/// <summary>
/// Remembers parsed files per text instance. Hosts keep handing over the same text object
/// for as long as a file is unchanged, so a file is parsed once however many times the
/// generator and the autocomplete provider ask for it.
/// </summary>
public static class MetadataFileCache
{
    private static readonly ConditionalWeakTable<object, MetadataFile> Cache = new();

    /// <param name="textIdentity">The host's object for this version of the file, e.g. a SourceText.</param>
    /// <param name="getText">Produces the file content; only called when it was not parsed before.</param>
    public static MetadataFile GetOrRead(object textIdentity, Func<string> getText, string? path)
    {
        if (Cache.TryGetValue(textIdentity, out var cached) && string.Equals(cached.Path, path, StringComparison.Ordinal))
        {
            return cached;
        }

        var file = MetadataYamlReader.Read(getText(), path);
        try
        {
            Cache.Remove(textIdentity);
            Cache.Add(textIdentity, file);
        }
        catch (ArgumentException)
        {
            // Another thread stored the same text first; both results are equivalent.
        }

        return file;
    }

    /// <summary>True for file names the product owns even without MSBuild item metadata.</summary>
    public static bool IsWellKnownFileName(string path)
    {
        var separator = path.LastIndexOfAny(new[] { '/', '\\' });
        var name = separator < 0 ? path : path.Substring(separator + 1);
        return name.Equals("earlybound.yaml", StringComparison.OrdinalIgnoreCase)
            || name.Equals("earlybound.yml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".earlybound.yaml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".earlybound.yml", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasYamlExtension(string path) =>
        path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
}
