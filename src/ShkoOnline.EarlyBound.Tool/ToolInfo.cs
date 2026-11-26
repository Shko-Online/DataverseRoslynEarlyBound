using System.Reflection;

namespace ShkoOnline.EarlyBound.Tool;

internal static class ToolInfo
{
    /// <summary>The package version, without build metadata.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(ToolInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational.Substring(0, plus);
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
