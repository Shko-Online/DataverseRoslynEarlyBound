using Microsoft.CodeAnalysis;

namespace ShkoOnline.EarlyBound.Tests.Support;

/// <summary>
/// A plug-in assembly as a test project sees it: compiled, with the generated classes holding only
/// the members the plug-in code uses.
/// </summary>
internal sealed class PluginAssembly
{
    /// <summary>
    /// Plug-in code that uses, on Account: Name, StateCode, PrimaryContactId and the Name constant;
    /// on Contact: FirstName and LastName.
    /// </summary>
    public const string Code = """
        using Contoso.Model;

        namespace Contoso.Plugins
        {
            public static class Logic
            {
                public static string Describe(Account account)
                {
                    return account.Name + "|" + account.StateCode + "|" + (account.PrimaryContactId == null ? "none" : account.PrimaryContactId.LogicalName);
                }

                public static string FullName(Contact contact)
                {
                    return contact.FirstName + " " + contact.LastName;
                }

                public static string NameColumn
                {
                    get { return Account.Fields.Name; }
                }
            }
        }
        """;

    private static readonly Lazy<PluginAssembly> LazyDefault = new(() => Build(Code));

    private PluginAssembly(GeneratorRun run, byte[] image)
    {
        Run = run;
        Image = image;
        Reference = MetadataReference.CreateFromImage(image);
    }

    /// <summary>Built from the fixture metadata and <see cref="Code"/>.</summary>
    public static PluginAssembly Default => LazyDefault.Value;

    public GeneratorRun Run { get; }

    public byte[] Image { get; }

    public MetadataReference Reference { get; }

    public static PluginAssembly Build(string code, IEnumerable<(string Path, string Text)>? files = null, string? extraSettings = null)
    {
        var run = GeneratorHarness.Run(
            files ?? Fixtures.MetadataFilesWith(("/repo/earlybound.yaml", Fixtures.Configuration + (extraSettings == null ? string.Empty : "\n" + extraSettings))),
            userSource: code,
            assemblyName: "Contoso.Plugins");
        Assert.Empty(run.CompilationErrors);
        return new PluginAssembly(run, run.EmitImage());
    }
}
