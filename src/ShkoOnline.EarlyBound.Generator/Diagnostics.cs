using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ShkoOnline.EarlyBound.Metadata;

namespace ShkoOnline.EarlyBound.Generator;

internal static class Diagnostics
{
    private const string Category = "EarlyBound";
    private const string HelpLink = "https://github.com/Shko-Online/DataverseRoslynEarlyBound/blob/main/docs/diagnostics.md";

    public static readonly DiagnosticDescriptor SdkNotReferenced = new(
        DiagnosticIds.SdkNotReferenced,
        "The Dataverse SDK is not referenced",
        "Early-bound types were not generated because the project does not reference Microsoft.Xrm.Sdk. Add the Microsoft.CrmSdk.CoreAssemblies or Microsoft.PowerPlatform.Dataverse.Client package.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "#soeb001");

    public static readonly DiagnosticDescriptor NoMetadata = new(
        DiagnosticIds.NoMetadata,
        "No Dataverse metadata was found",
        "ShkoOnline.EarlyBound found no metadata, so nothing was generated. Run 'dotnet earlybound pull' in the project folder to download it.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "#soeb002");

    public static readonly DiagnosticDescriptor InvalidYaml = Create(DiagnosticIds.InvalidYaml, "Invalid YAML", DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor InvalidValue = Create(DiagnosticIds.InvalidValue, "Invalid metadata value", DiagnosticSeverity.Error);

    public static readonly DiagnosticDescriptor DuplicateDefinition = Create(DiagnosticIds.DuplicateDefinition, "Duplicate definition", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor UnknownOptionSet = Create(DiagnosticIds.UnknownOptionSet, "Global choice is not defined", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor NameCollision = Create(DiagnosticIds.NameCollision, "Generated name collision", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor UnknownSetting = Create(DiagnosticIds.UnknownSetting, "Unknown setting", DiagnosticSeverity.Warning);

    public static readonly DiagnosticDescriptor InternalError = new(
        DiagnosticIds.InternalError,
        "Early Bound generator failure",
        "Shko Online's Early Bound failed and generated nothing: {0}. Please report this at https://github.com/Shko-Online/DataverseRoslynEarlyBound/issues.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink + "#soeb900");

    private static DiagnosticDescriptor Create(string id, string title, DiagnosticSeverity severity) =>
        new(id, title, "{0}", Category, severity, isEnabledByDefault: true, helpLinkUri: HelpLink + "#" + id.ToLowerInvariant());

    public static Diagnostic ToDiagnostic(MetadataDiagnostic diagnostic)
    {
        var descriptor = diagnostic.Id switch
        {
            DiagnosticIds.InvalidYaml => InvalidYaml,
            DiagnosticIds.InvalidValue => InvalidValue,
            DiagnosticIds.DuplicateDefinition => DuplicateDefinition,
            DiagnosticIds.UnknownOptionSet => UnknownOptionSet,
            DiagnosticIds.NameCollision => NameCollision,
            DiagnosticIds.UnknownSetting => UnknownSetting,
            _ => InvalidValue,
        };

        var location = Location.None;
        if (!string.IsNullOrEmpty(diagnostic.Path))
        {
            var position = new LinePosition(diagnostic.Position.Line, diagnostic.Position.Column);
            location = Location.Create(diagnostic.Path!, default, new LinePositionSpan(position, position));
        }

        // The severity found while reading can be lower than the rule's default (e.g. an informational rename).
        var severity = diagnostic.Severity switch
        {
            MetadataSeverity.Error => DiagnosticSeverity.Error,
            MetadataSeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info,
        };

        return Diagnostic.Create(
            descriptor,
            location,
            effectiveSeverity: severity,
            additionalLocations: null,
            properties: null,
            diagnostic.Message);
    }
}
