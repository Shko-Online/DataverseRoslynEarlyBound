using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

public class GeneratorTests
{
    private const string ConfigPath = "/repo/earlybound.yaml";

    // These tests are about the shape of everything that can be generated, so they generate all
    // of it. What is generated when only used members are wanted is in OnlyUsedMembersTests.
    private static IReadOnlyList<(string, string)> Files(string? generatorSettings = null) =>
        Fixtures.MetadataFilesWith((ConfigPath, generatorSettings == null
            ? Fixtures.FullConfiguration
            : "generator:\n  camelCaseCustomWords: [sho]\n  generateOnlyUsedAttributes: false\n" + generatorSettings));

    [Theory]
    [InlineData(LanguageVersion.Latest)]
    [InlineData(LanguageVersion.CSharp7_3)] // what a .NET Framework plug-in project compiles with
    public void Generated_code_compiles_against_the_dataverse_sdk(LanguageVersion languageVersion)
    {
        var run = GeneratorHarness.Run(Files(), languageVersion: languageVersion);

        Assert.Empty(run.CompilationErrors);
        Assert.Empty(run.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true));
        Assert.Contains("Entities.Account.g.cs", run.HintNames);
        Assert.Contains("Entities.Sho_Project.g.cs", run.HintNames);
        Assert.Contains("OptionSets.Account_AccountCategoryCode.g.cs", run.HintNames);
        Assert.Contains("OptionSets.Sho_BudgetStatus.g.cs", run.HintNames);
        Assert.Contains("EarlyBound.ServiceContext.g.cs", run.HintNames);
        Assert.Contains("EarlyBound.Helpers.g.cs", run.HintNames);
    }

    [Fact]
    public void Follows_the_early_bound_generator_conventions()
    {
        var run = GeneratorHarness.Run(Files());
        var account = run.Source("Entities.Account.g.cs");

        Assert.Contains("namespace Contoso.Model", account);
        Assert.Contains("[Microsoft.Xrm.Sdk.Client.EntityLogicalNameAttribute(\"account\")]", account);
        Assert.Contains("public partial class Account : Microsoft.Xrm.Sdk.Entity", account);
        Assert.Contains("public partial class Fields", account);
        Assert.Contains("public const string AccountCategoryCode = \"accountcategorycode\";", account);
        Assert.Contains("public const string EntityLogicalName = \"account\";", account);
        Assert.Contains("public const string PrimaryNameAttribute = \"name\";", account);

        // Choices are typed as enums named Entity_Attribute; global ones keep their own name.
        Assert.Contains("public virtual System.Nullable<Account_AccountCategoryCode> AccountCategoryCode", account);
        Assert.Contains("public virtual System.Nullable<Account_StateCode> StateCode", account);
        Assert.Contains("public virtual System.Nullable<Sho_BudgetStatus> Sho_BudgetStatus", account);
        Assert.Contains("public virtual System.Collections.Generic.IEnumerable<Sho_Account_Sho_Tags> Sho_Tags", account);

        // Relationships, with the role prefix on self-references.
        Assert.Contains("public Contact Account_Primary_Contact", account);
        Assert.Contains("public Account ReferencingAccount_Parent_Account", account);
        Assert.Contains("public System.Collections.Generic.IEnumerable<Account> ReferencedAccount_Parent_Account", account);
        Assert.Contains("public System.Collections.Generic.IEnumerable<Sho_Project> Sho_Account_Sho_Project", account);

        // The primary key keeps Entity.Id in step, and there is the anonymous-type constructor.
        Assert.Contains("public override System.Guid Id", account);
        Assert.Contains("public Account(object anonymousType)", account);

        // Computed companions: the display text of a lookup, read-only.
        Assert.Contains("public string PrimaryContactIdName", account);
        Assert.DoesNotContain("PrimaryContactIdYomiName", account);
        Assert.DoesNotContain("EntityImageId", account);

        // Read-only columns have no setter; deprecated ones are marked.
        Assert.Matches(@"public System\.Nullable<System\.DateTime> CreatedOn\s*\{\s*\[[^\]]+\]\s*get\s*\{[^}]*\}\s*\}", account);
        Assert.Matches(@"\[System\.ObsoleteAttribute\(\)\]\s*public string Sho_LegacyCode", account);

        var statusEnum = run.Source("OptionSets.Sho_Project_StatusCode.g.cs");
        Assert.Contains("[OptionSetMetadataAttribute(\"On Hold\", 1)]", statusEnum);
        Assert.Contains("OnHold_Active = 100000000,", statusEnum);
        Assert.Contains("OnHold_Inactive = 100000001,", statusEnum);

        var context = run.Source("EarlyBound.ServiceContext.g.cs");
        Assert.Contains("public partial class DataverseContext : Microsoft.Xrm.Sdk.Client.OrganizationServiceContext", context);
        Assert.Contains("public System.Linq.IQueryable<Account> AccountSet", context);

        Assert.Contains("[assembly: Microsoft.Xrm.Sdk.Client.ProxyTypesAssemblyAttribute()]", run.Source("EarlyBound.Helpers.g.cs"));
    }

    [Fact]
    public void Party_lists_are_typed_when_activityparty_is_there()
    {
        var run = GeneratorHarness.Run(Files());
        Assert.Contains("public System.Collections.Generic.IEnumerable<ActivityParty> To", run.Source("Entities.Email.g.cs"));

        var withoutParty = Files().Select(f => f.Item1.EndsWith("email.yaml")
            ? (f.Item1, f.Item2.Substring(f.Item2.IndexOf("  - logicalName: email", StringComparison.Ordinal)).Insert(0, "entities:\n"))
            : f).ToList();
        var untyped = GeneratorHarness.Run(withoutParty);
        Assert.Empty(untyped.CompilationErrors);
        Assert.Contains("public Microsoft.Xrm.Sdk.EntityCollection To", untyped.Source("Entities.Email.g.cs"));
    }

    [Fact]
    public void Reports_problems_on_the_yaml_line_they_come_from()
    {
        var run = GeneratorHarness.Run(Files());

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal(DiagnosticIds.UnknownOptionSet, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.EndsWith("sho_project.yaml", diagnostic.Location.GetLineSpan().Path);
        Assert.Contains("sho_notpulled", diagnostic.GetMessage());

        // The column is still generated, as the untyped SDK value.
        Assert.Contains("public Microsoft.Xrm.Sdk.OptionSetValue Sho_UnknownGlobal", run.Source("Entities.Sho_Project.g.cs"));
    }

    [Fact]
    public void Invalid_yaml_is_an_error_and_the_other_files_still_generate()
    {
        var run = GeneratorHarness.Run(Files().Append(("/repo/Metadata/broken.yaml", "entities:\n  - logicalName: [oops\n")));

        var error = Assert.Single(run.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticIds.InvalidYaml, error.Id);
        Assert.Equal("/repo/Metadata/broken.yaml", error.Location.GetLineSpan().Path);
        Assert.Contains("Entities.Account.g.cs", run.HintNames);
    }

    [Fact]
    public void Says_so_when_there_is_no_metadata()
    {
        var run = GeneratorHarness.Run(Array.Empty<(string, string)>());

        Assert.Equal(DiagnosticIds.NoMetadata, Assert.Single(run.GeneratorDiagnostics).Id);
        Assert.Empty(run.Result.GeneratedTrees);
    }

    [Fact]
    public void Says_so_when_the_sdk_is_not_referenced_instead_of_failing_the_build_with_noise()
    {
        var run = GeneratorHarness.Run(Files(), includeSdk: false);

        Assert.Equal(DiagnosticIds.SdkNotReferenced, Assert.Single(run.GeneratorDiagnostics, d => d.Id != DiagnosticIds.UnknownOptionSet).Id);
        Assert.Empty(run.Result.GeneratedTrees);
        Assert.Empty(run.CompilationErrors);
    }

    [Fact]
    public void Namespace_comes_from_the_msbuild_property_then_the_settings_then_the_project()
    {
        var properties = new Dictionary<string, string> { ["build_property.RootNamespace"] = "Root.Ns" };

        var fromSettings = GeneratorHarness.Run(Files(), buildProperties: properties);
        Assert.Contains("namespace Contoso.Model", fromSettings.Source("Entities.Account.g.cs"));

        var fromProject = GeneratorHarness.Run(Files(generatorSettings: string.Empty), buildProperties: properties);
        Assert.Contains("namespace Root.Ns", fromProject.Source("Entities.Account.g.cs"));

        properties["build_property.EarlyBoundNamespace"] = "Explicit.Ns";
        var fromProperty = GeneratorHarness.Run(Files(), buildProperties: properties);
        Assert.Contains("namespace Explicit.Ns", fromProperty.Source("Entities.Account.g.cs"));

        var fallback = GeneratorHarness.Run(Files(generatorSettings: string.Empty));
        Assert.Contains("namespace DataverseModel", fallback.Source("Entities.Account.g.cs"));
    }

    [Fact]
    public void Settings_change_the_shape_of_the_code()
    {
        var run = GeneratorHarness.Run(Files("""
              namespace: Contoso.Model
              serviceContextName: ""
              generateTypesAsInternal: true
              replaceEnumPropertiesWithOptionSet: true
              generateEntityRelationships: false
              generateAttributeNameConsts: false
              generateAnonymousTypeConstructor: false
              emitVirtualAttributes: false
              emitEntityETC: true
              addDebuggerNonUserCode: false
              suppressGeneratedCodeAttribute: false
              entityClassNameOverrides: { account: Customer }
            """));

        Assert.Empty(run.CompilationErrors);
        Assert.DoesNotContain("EarlyBound.ServiceContext.g.cs", run.HintNames);
        var customer = run.Source("Entities.Customer.g.cs");
        Assert.Contains("internal partial class Customer : Microsoft.Xrm.Sdk.Entity", customer);
        Assert.Contains("public Microsoft.Xrm.Sdk.OptionSetValue AccountCategoryCode", customer);
        Assert.Contains("public const int EntityTypeCode = 1;", customer);
        Assert.Contains("[System.CodeDom.Compiler.GeneratedCodeAttribute(\"ShkoOnline.EarlyBound\"", customer);
        Assert.DoesNotContain("RelationshipSchemaNameAttribute", customer);
        Assert.DoesNotContain("class Fields", customer);
        Assert.DoesNotContain("anonymousType", customer);
        Assert.DoesNotContain("PrimaryContactIdName", customer);
        Assert.DoesNotContain("DebuggerNonUserCode", customer);
    }

    [Fact]
    public void Does_not_repeat_the_proxy_types_attribute_the_project_already_has()
    {
        var run = GeneratorHarness.Run(Files(), userSource: "[assembly: Microsoft.Xrm.Sdk.Client.ProxyTypesAssemblyAttribute()]");

        Assert.Empty(run.CompilationErrors);
        Assert.DoesNotContain("[assembly:", run.Source("EarlyBound.Helpers.g.cs"));
    }

    [Fact]
    public void Only_takes_the_files_it_was_given()
    {
        // Without the item metadata, only the well-known file names count.
        var untagged = GeneratorHarness.Run(Files(), tagFiles: false);
        Assert.Empty(untagged.Result.GeneratedTrees);

        var byName = GeneratorHarness.Run(new[] { ("/repo/tables.earlybound.yaml", "entities:\n  - logicalName: account\n    schemaName: Account\n") }, tagFiles: false);
        Assert.Contains("Entities.Account.g.cs", byName.HintNames);

        var notYaml = GeneratorHarness.Run(new[] { ("/repo/notes.txt", "entities:\n  - logicalName: account\n") });
        Assert.Empty(notYaml.Result.GeneratedTrees);
    }
}
