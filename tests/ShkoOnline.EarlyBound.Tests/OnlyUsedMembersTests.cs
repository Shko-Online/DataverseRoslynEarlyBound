using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShkoOnline.EarlyBound.Generator;
using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>
/// The default: members are generated for the columns and relationships the project's code uses,
/// and for nothing else.
/// </summary>
public class OnlyUsedMembersTests
{
    private const string Usings = "using System; using System.Linq; using Microsoft.Xrm.Sdk; using Contoso.Model;\n";

    private static IReadOnlyList<(string, string)> Files(string? extraSettings = null) =>
        Fixtures.MetadataFilesWith(("/repo/earlybound.yaml", Fixtures.Configuration + (extraSettings == null ? string.Empty : "\n" + extraSettings)));

    private static GeneratorRun Run(string code, string? extraSettings = null, IReadOnlyDictionary<string, string>? buildProperties = null) =>
        GeneratorHarness.Run(Files(extraSettings), userSource: Usings + code, buildProperties: buildProperties);

    [Fact]
    public void Generates_the_members_the_code_uses_and_no_others()
    {
        var run = Run("""
            class Use
            {
                string M(Account account) =>
                    account.AccountCategoryCode == Account_AccountCategoryCode.Standard ? account.Name : null;
            }
            """);

        Assert.Empty(run.CompilationErrors);
        var account = run.Source("Entities.Account.g.cs");

        // Used: the properties, their constants, and the enum one of them is typed as.
        Assert.Contains("public string Name", account);
        Assert.Contains("public virtual System.Nullable<Account_AccountCategoryCode> AccountCategoryCode", account);
        Assert.Contains("public const string Name = \"name\";", account);
        Assert.Contains("OptionSets.Account_AccountCategoryCode.g.cs", run.HintNames);

        // Always there: the class, its names and its key.
        Assert.Contains("public const string EntityLogicalName = \"account\";", account);
        Assert.Contains("public System.Nullable<System.Guid> AccountId", account);
        Assert.Contains("public override System.Guid Id", account);
        Assert.Contains("public const string Id = \"accountid\";", account);

        // Not used: everything else on the table, the other choices, and the other tables' members.
        Assert.DoesNotContain("AccountNumber", account);
        Assert.DoesNotContain("CreditLimit", account);
        Assert.DoesNotContain("StateCode", account);
        Assert.DoesNotContain("RelationshipSchemaNameAttribute", account);
        Assert.Equal(new[] { "OptionSets.Account_AccountCategoryCode.g.cs" }, run.HintNames.Where(h => h.StartsWith("OptionSets.")));
        Assert.DoesNotContain("FirstName", run.Source("Entities.Contact.g.cs"));

        // Every table still gets its class.
        Assert.Equal(5, run.HintNames.Count(h => h.StartsWith("Entities.")));
    }

    [Fact]
    public void A_project_that_uses_nothing_gets_bare_classes()
    {
        var run = Run("class Use { }");

        Assert.Empty(run.CompilationErrors);
        Assert.DoesNotContain(run.HintNames, h => h.StartsWith("OptionSets."));

        var account = run.Source("Entities.Account.g.cs");
        Assert.Contains("public partial class Account : Microsoft.Xrm.Sdk.Entity", account);
        // The only column with members is the key: its property, and Id on top of it.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(account, @"AttributeLogicalNameAttribute\(""accountid""\)").Count);
        Assert.Empty(System.Text.RegularExpressions.Regex.Matches(account, @"AttributeLogicalNameAttribute\(""(?!accountid"")"));

        // No enum is generated, so neither are the helpers that only enums need.
        var helpers = run.Source("EarlyBound.Helpers.g.cs");
        Assert.Contains("[assembly: Microsoft.Xrm.Sdk.Client.ProxyTypesAssemblyAttribute()]", helpers);
        Assert.DoesNotContain("namespace", helpers);
    }

    [Theory]
    [InlineData("class Use { object M(Account a) => a.CreditLimit; }")]
    [InlineData("class Use { object M(Account a) => a?.CreditLimit; }")]
    [InlineData("class Use { object M() => new Account { CreditLimit = new Money(1m) }; }")]
    [InlineData("class Use { object M() => new Microsoft.Xrm.Sdk.Query.ColumnSet(Account.Fields.CreditLimit); }")]
    [InlineData("class Use { string M() => nameof(Account.CreditLimit); }")]
    [InlineData("class Use { bool M(object o) => o is Account { CreditLimit: null }; }")]
    [InlineData("class Use { object M(Account[] all) => from a in all where a.CreditLimit != null select a; }")]
    [InlineData("class Use { object M(Account[] all) => all.Select(a => new { a.CreditLimit }); }")]
    [InlineData("class Use { object M(dynamic a) => a.CreditLimit; }")]
    [InlineData("namespace Contoso.Model { partial class Account { public bool HasLimit => CreditLimit != null; } }")]
    [InlineData("class Use {\n    /// <summary>Sets <see cref=\"Account.CreditLimit\"/>.</summary>\n    void M() { }\n}")]
    public void Any_mention_of_the_name_counts_as_use(string code)
    {
        var run = Run(code);

        Assert.Empty(run.CompilationErrors);
        Assert.Contains("public Microsoft.Xrm.Sdk.Money CreditLimit", run.Source("Entities.Account.g.cs"));
    }

    [Fact]
    public void A_name_that_only_appears_in_text_is_not_a_use()
    {
        var run = Run("""
            class Use
            {
                // CreditLimit is mentioned in a comment,
                string M() => "and CreditLimit in a string";
            #if NEVER
                object N(Account a) => a.CreditLimit;
            #endif
            }
            """);

        Assert.DoesNotContain("CreditLimit", run.Source("Entities.Account.g.cs"));
    }

    [Fact]
    public void An_enum_can_be_used_without_its_property()
    {
        var run = Run("class Use { int M() => (int)Account_StatusCode.Inactive; }");

        Assert.Empty(run.CompilationErrors);
        Assert.Contains("OptionSets.Account_StatusCode.g.cs", run.HintNames);
        Assert.DoesNotContain("StatusCode", run.Source("Entities.Account.g.cs"));

        // The enum's members carry OptionSetMetadata, so that comes along; the property helper does not.
        var helpers = run.Source("EarlyBound.Helpers.g.cs");
        Assert.Contains("class OptionSetMetadataAttribute", helpers);
        Assert.DoesNotContain("class EntityOptionSetEnum", helpers);
    }

    [Fact]
    public void A_global_choice_is_generated_once_for_every_table_that_uses_it()
    {
        var run = Run("class Use { object M(Account a, Sho_Project p) => a.Sho_BudgetStatus ?? p.Sho_BudgetStatus; }");

        Assert.Empty(run.CompilationErrors);
        Assert.Single(run.HintNames, h => h == "OptionSets.Sho_BudgetStatus.g.cs");
        Assert.Contains("class EntityOptionSetEnum", run.Source("EarlyBound.Helpers.g.cs"));
    }

    [Fact]
    public void Relationships_follow_the_same_rule()
    {
        var run = Run("class Use { object M(Account a) => a.Account_Primary_Contact; }");

        Assert.Empty(run.CompilationErrors);
        var account = run.Source("Entities.Account.g.cs");
        Assert.Contains("public Contact Account_Primary_Contact", account);
        Assert.DoesNotContain("ReferencingAccount_Parent_Account", account);
        Assert.DoesNotContain("Contact_Customer_Accounts", account);
    }

    [Fact]
    public void A_name_shared_by_tables_is_generated_on_each_of_them()
    {
        // Which table "StateCode" belongs to cannot be known before the property exists.
        var run = Run("class Use { object M(Contact c) => c.StateCode; }");

        Assert.Empty(run.CompilationErrors);
        Assert.Contains("StateCode", run.Source("Entities.Contact.g.cs"));
        Assert.Contains("StateCode", run.Source("Entities.Account.g.cs"));
        Assert.DoesNotContain("StatusCode", run.Source("Entities.Account.g.cs"));
    }

    [Fact]
    public void Members_can_be_kept_whether_they_are_used_or_not()
    {
        var run = Run("class Use { }", """
              alwaysGenerate:
                - contact               # a whole table
                - account.creditlimit   # one column
                - "*.statecode"         # a column of every table that has it
                - account.account_primary_contact   # a relationship
            """);

        Assert.Empty(run.CompilationErrors);
        var contact = run.Source("Entities.Contact.g.cs");
        Assert.Contains("public string FirstName", contact);
        Assert.Contains("public virtual System.Nullable<Contact_GenderCode> GenderCode", contact);
        Assert.Contains("Contact_Customer_Accounts", contact);

        var account = run.Source("Entities.Account.g.cs");
        Assert.Contains("public Microsoft.Xrm.Sdk.Money CreditLimit", account);
        Assert.Contains("StateCode", account);
        Assert.Contains("public Contact Account_Primary_Contact", account);
        Assert.DoesNotContain("AccountNumber", account);
        Assert.Contains("StateCode", run.Source("Entities.Sho_Project.g.cs"));
    }

    [Fact]
    public void The_msbuild_property_wins_over_the_setting()
    {
        var off = new Dictionary<string, string> { ["build_property.EarlyBoundGenerateOnlyUsedAttributes"] = "false" };
        var everything = Run("class Use { }", buildProperties: off);
        Assert.Contains("public string AccountNumber", everything.Source("Entities.Account.g.cs"));
        Assert.Contains("OptionSets.Account_IndustryCode.g.cs", everything.HintNames);

        var on = new Dictionary<string, string> { ["build_property.EarlyBoundGenerateOnlyUsedAttributes"] = "true" };
        var trimmed = Run("class Use { }", "  generateOnlyUsedAttributes: false", on);
        Assert.DoesNotContain("AccountNumber", trimmed.Source("Entities.Account.g.cs"));
    }

    [Fact]
    public void Used_members_compile_as_csharp_7_3()
    {
        var run = GeneratorHarness.Run(
            Files(),
            userSource: Usings + "class Use { object M(Account a) { return a.Sho_Tags ?? (object)a.StateCode ?? a.Account_Primary_Contact; } }",
            languageVersion: LanguageVersion.CSharp7_3);

        Assert.Empty(run.CompilationErrors);
    }

    // ---- What typing costs ---------------------------------------------------------------------

    private static GeneratorRunResult Edit(GeneratorRun run, string path, string newText, out GeneratorDriver driver)
    {
        var tree = run.InputCompilation.SyntaxTrees.Single(t => t.FilePath == path);
        var edited = run.InputCompilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(newText, path: path));
        driver = run.Driver.RunGenerators(edited);
        return driver.GetRunResult().Results.Single();
    }

    private static IEnumerable<IncrementalStepRunReason> Reasons(GeneratorRunResult result, string step) =>
        result.TrackedSteps[step].SelectMany(s => s.Outputs).Select(o => o.Reason);

    private static IEnumerable<IncrementalStepRunReason> OutputReasons(GeneratorRunResult result) =>
        result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).Select(o => o.Reason);

    [Fact]
    public void Typing_code_that_uses_nothing_new_regenerates_nothing()
    {
        var run = GeneratorHarness.Run(
            Files(),
            userSource: Usings + "class A { string M(Account a) => a.Name; }",
            moreSources: new[] { ("Other.cs", Usings + "class B { object M(Contact c) => c.FirstName; }") });

        // An edit that adds identifiers, none of which names a member that is not in use already.
        var result = Edit(run, "Consumer.cs", Usings + "class A { string M(Account a) { var text = a.Name; return text.Trim(); } }", out _);

        // The model is not rebuilt, the file that was not touched is not scanned again,
        // the set of used names comes out the same, and so no code is generated.
        Assert.All(Reasons(result, EarlyBoundGenerator.ModelStep), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        Assert.Equal(
            new[] { IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Cached },
            Reasons(result, EarlyBoundGenerator.UsedNamesPerFileStep).OrderBy(r => r));
        Assert.All(Reasons(result, EarlyBoundGenerator.UsedNamesStep), reason => Assert.NotEqual(IncrementalStepRunReason.Modified, reason));
        Assert.NotEmpty(result.TrackedOutputSteps);
        Assert.All(OutputReasons(result), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    [Fact]
    public void Using_a_member_for_the_first_time_generates_it_and_dropping_the_last_use_removes_it()
    {
        var run = GeneratorHarness.Run(Files(), userSource: Usings + "class A { string M(Account a) => a.Name; }");
        Assert.DoesNotContain("CreditLimit", run.Source("Entities.Account.g.cs"));

        var added = Edit(run, "Consumer.cs", Usings + "class A { object M(Account a) => a.Name + a.CreditLimit; }", out var driver);
        Assert.Contains(IncrementalStepRunReason.Modified, OutputReasons(added));
        var account = added.GeneratedSources.Single(s => s.HintName == "Entities.Account.g.cs").SourceText.ToString();
        Assert.Contains("public Microsoft.Xrm.Sdk.Money CreditLimit", account);
        Assert.Contains("public string Name", account);

        var compilation = run.InputCompilation.ReplaceSyntaxTree(
            run.InputCompilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(Usings + "class A { string M(Account a) => null; }", path: "Consumer.cs"));
        var removed = driver.RunGenerators(compilation).GetRunResult().Results.Single();
        account = removed.GeneratedSources.Single(s => s.HintName == "Entities.Account.g.cs").SourceText.ToString();
        Assert.DoesNotContain("CreditLimit", account);
        Assert.DoesNotContain("public string Name", account);
    }

    [Fact]
    public void Generating_everything_does_not_look_at_the_code_at_all()
    {
        var run = GeneratorHarness.Run(
            Fixtures.MetadataFilesWith(("/repo/earlybound.yaml", Fixtures.FullConfiguration)),
            userSource: Usings + "class A { string M(Account a) => a.Name; }");

        var result = Edit(run, "Consumer.cs", Usings + "class A { object M(Account a) => a.CreditLimit; }", out _);

        Assert.All(OutputReasons(result), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }
}
