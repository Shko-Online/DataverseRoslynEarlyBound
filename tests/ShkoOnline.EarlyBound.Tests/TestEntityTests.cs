using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShkoOnline.EarlyBound.Generator;
using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>
/// A test project references the plug-in project, whose classes have only the members the
/// plug-ins use. It does not generate those classes again; what a test needs beyond them goes
/// into a derived class, <c>AccountTestEntity : Account</c>.
/// </summary>
public class TestEntityTests
{
    private const string Usings = "using System; using System.Linq; using Microsoft.Xrm.Sdk; using Contoso.Model;\n";

    private static IReadOnlyList<(string, string)> Files(string? extraSettings = null) =>
        Fixtures.MetadataFilesWith(("/repo/earlybound.yaml", Fixtures.Configuration + (extraSettings == null ? string.Empty : "\n" + extraSettings)));

    private static GeneratorRun TestProject(
        string code,
        PluginAssembly? plugin = null,
        LanguageVersion languageVersion = LanguageVersion.CSharp7_3,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        IEnumerable<(string Path, string Text)>? files = null) =>
        GeneratorHarness.Run(
            files ?? Files(),
            userSource: Usings + code,
            languageVersion: languageVersion,
            buildProperties: buildProperties,
            assemblyName: "Contoso.Plugins.Tests",
            moreReferences: new[] { (plugin ?? PluginAssembly.Default).Reference });

    [Fact]
    public void The_plug_in_assembly_has_only_what_the_plug_ins_use()
    {
        var account = PluginAssembly.Default.Run.Source("Entities.Account.g.cs");

        Assert.Contains("public string Name", account);
        Assert.Contains("PrimaryContactId", account);
        Assert.DoesNotContain("CreditLimit", account);
        Assert.DoesNotContain("AccountCategoryCode", account);
    }

    [Fact]
    public void Classes_that_a_reference_provides_are_not_generated_again()
    {
        var run = TestProject("""
            class Tests
            {
                string Uses_what_the_plug_in_classes_have(Account account, Contact contact)
                {
                    return Contoso.Plugins.Logic.Describe(account) + contact.FirstName + Account.Fields.Name + Account_StateCode.Active;
                }
            }
            """);

        Assert.Empty(run.CompilationErrors);
        Assert.Empty(run.GeneratorDiagnostics.Where(d => d.Id != "SOEB103"));

        // No table classes, no enums and no service context: the plug-in assembly has them.
        // And no proxy types attribute: this assembly is not where the proxy types are.
        Assert.Empty(run.HintNames);
    }

    [Fact]
    public void Naming_the_test_entity_generates_it_with_the_members_the_base_class_lacks()
    {
        var run = TestProject("""
            class Tests
            {
                object Arrange()
                {
                    return new AccountTestEntity
                    {
                        Name = "Contoso",
                        CreditLimit = new Money(100m),
                        AccountCategoryCode = Account_AccountCategoryCode.Standard,
                        Account_Primary_Contact = new Contact(),
                    };
                }
            }
            """);

        Assert.Empty(run.CompilationErrors);
        var testEntity = run.Source("TestEntities.AccountTestEntity.g.cs");

        // A partial class deriving from the plug-in assembly's class, for the same table.
        Assert.Contains("public partial class AccountTestEntity : Account", testEntity);
        Assert.Contains("[Microsoft.Xrm.Sdk.Client.EntityLogicalNameAttribute(\"account\")]", testEntity);
        Assert.Contains("Contoso.Plugins does not generate", testEntity);

        // What the test uses and Account lacks.
        Assert.Contains("public Microsoft.Xrm.Sdk.Money CreditLimit", testEntity);
        Assert.Contains("public virtual System.Nullable<Account_AccountCategoryCode> AccountCategoryCode", testEntity);
        Assert.Contains("public Contact Account_Primary_Contact", testEntity);

        // What Account has already is inherited, not declared again.
        Assert.DoesNotContain("public string Name", testEntity);
        Assert.DoesNotContain("AccountId", testEntity);
        Assert.DoesNotContain("public override System.Guid Id", testEntity);

        // Its constants extend those of the base class.
        Assert.Contains("public new partial class Fields : Account.Fields", testEntity);
        Assert.Contains("public const string CreditLimit = \"creditlimit\";", testEntity);
        Assert.DoesNotContain("public const string Name =", testEntity);

        // The enum the new property needs is generated here; the one the plug-in assembly has is not.
        Assert.Contains("OptionSets.Account_AccountCategoryCode.g.cs", run.HintNames);
        Assert.DoesNotContain("OptionSets.Account_StateCode.g.cs", run.HintNames);

        // Still no copy of Account itself, and no test entity for tables that were not named.
        Assert.DoesNotContain(run.HintNames, h => h.StartsWith("Entities."));
        Assert.Equal(new[] { "TestEntities.AccountTestEntity.g.cs" }, run.HintNames.Where(h => h.StartsWith("TestEntities.")));
    }

    [Fact]
    public void A_test_entity_that_is_only_named_is_an_empty_subclass_ready_to_grow()
    {
        var run = TestProject("class Tests { object Arrange() { return new ContactTestEntity(); } }");

        Assert.Empty(run.CompilationErrors);
        var testEntity = run.Source("TestEntities.ContactTestEntity.g.cs");
        Assert.Contains("public partial class ContactTestEntity : Contact", testEntity);
        Assert.Contains("public new partial class Fields : Contact.Fields", testEntity);
        Assert.DoesNotContain("AttributeLogicalNameAttribute", testEntity);
    }

    [Fact]
    public void Using_a_missing_member_without_naming_the_test_entity_generates_nothing()
    {
        // Account from the plug-in assembly cannot be given the member; the compiler says so.
        var run = TestProject("class Tests { object Read(Account account) { return account.CreditLimit; } }");

        Assert.Empty(run.HintNames);
        Assert.Contains(run.CompilationErrors, d => d.Id == "CS1061" && d.GetMessage().Contains("CreditLimit"));
    }

    [Fact]
    public void Test_entities_compile_as_csharp_7_3_and_as_the_latest()
    {
        const string code = """
            class Tests
            {
                object Arrange()
                {
                    var account = new AccountTestEntity(Guid.NewGuid()) { Sho_Tags = new[] { Sho_Account_Sho_Tags.Vip } };
                    account.CreditLimit = new Money(1m);
                    var project = new Sho_ProjectTestEntity { Sho_DueDate = DateTime.Today, Sho_BudgetStatus = Sho_BudgetStatus.MayBuy };
                    return AccountTestEntity.Fields.CreditLimit + AccountTestEntity.Fields.Name + project.Sho_DueDate + account.PrimaryContactIdName;
                }
            }
            """;

        Assert.Empty(TestProject(code, languageVersion: LanguageVersion.CSharp7_3).CompilationErrors);
        Assert.Empty(TestProject(code, languageVersion: LanguageVersion.Latest).CompilationErrors);
    }

    [Fact]
    public void A_table_the_reference_does_not_have_is_generated_as_usual()
    {
        // The plug-in project only has account and contact; the test project also has the project table.
        var onlyTwo = Fixtures.MetadataFiles()
            .Where(f => f.Path.EndsWith("account.yaml") || f.Path.EndsWith("contact.yaml") || f.Path.EndsWith("sho_budgetstatus.yaml"))
            .Append(("/repo/earlybound.yaml", Fixtures.Configuration + "\n  generateEntityRelationships: false"))
            .ToList();
        var plugin = PluginAssembly.Build(PluginAssembly.Code, onlyTwo);

        var run = TestProject(
            "class Tests { object Arrange() { return new Sho_Project { Sho_Name = \"x\" }.Sho_Name + new AccountTestEntity().CreditLimit; } }",
            plugin,
            files: Files("  generateEntityRelationships: false"));

        Assert.Empty(run.CompilationErrors);
        Assert.Contains("public partial class Sho_Project : Microsoft.Xrm.Sdk.Entity", run.Source("Entities.Sho_Project.g.cs"));
        Assert.DoesNotContain("Entities.Account.g.cs", run.HintNames);
        Assert.Contains("TestEntities.AccountTestEntity.g.cs", run.HintNames);

        // With a table class of its own, this assembly has proxy types too.
        Assert.Contains("ProxyTypesAssemblyAttribute", run.Source("EarlyBound.Helpers.g.cs"));
    }

    [Fact]
    public void Generating_everything_gives_every_referenced_table_a_test_entity_with_all_it_lacks()
    {
        var everything = new Dictionary<string, string> { ["build_property.EarlyBoundGenerateOnlyUsedAttributes"] = "false" };
        var run = TestProject("class Tests { }", buildProperties: everything);

        Assert.Empty(run.CompilationErrors);
        var testEntity = run.Source("TestEntities.AccountTestEntity.g.cs");
        Assert.Contains("public string AccountNumber", testEntity);
        Assert.Contains("public Microsoft.Xrm.Sdk.Money CreditLimit", testEntity);
        Assert.DoesNotContain("public string Name", testEntity);
        Assert.Contains("TestEntities.ContactTestEntity.g.cs", run.HintNames);
        Assert.Contains("TestEntities.EmailTestEntity.g.cs", run.HintNames);
    }

    [Fact]
    public void When_the_plug_in_assembly_has_everything_there_is_nothing_to_add()
    {
        var full = PluginAssembly.Build(PluginAssembly.Code, extraSettings: "  generateOnlyUsedAttributes: false");
        var run = TestProject(
            "class Tests { object Arrange() { return new Account { CreditLimit = new Money(1m) }; } }",
            full,
            files: Files("  generateOnlyUsedAttributes: false"));

        Assert.Empty(run.CompilationErrors);
        Assert.Empty(run.HintNames);
    }

    [Fact]
    public void Every_generated_class_is_partial()
    {
        var everything = new Dictionary<string, string> { ["build_property.EarlyBoundGenerateOnlyUsedAttributes"] = "false" };
        var sources = PluginAssembly.Build(PluginAssembly.Code, extraSettings: "  generateOnlyUsedAttributes: false").Run.Result.GeneratedTrees
            .Concat(TestProject("class Tests { }", buildProperties: everything).Result.GeneratedTrees)
            .Select(tree => tree.ToString())
            .ToList();

        var declarations = sources
            .SelectMany(source => Regex.Matches(source, @"^\s*(?:public|internal)[^\r\n(]*\bclass\s+\w+", RegexOptions.Multiline).Select(m => m.Value.Trim()))
            .ToList();

        foreach (var expected in new[] { "Account", "AccountTestEntity", "Fields", "EntityOptionSetEnum", "OptionSetMetadataAttribute", "OptionSetExtension", "DataverseContext" })
        {
            Assert.Contains(declarations, declaration => declaration.EndsWith("class " + expected));
        }
        Assert.All(declarations, declaration => Assert.Contains(" partial class ", declaration));
    }

    [Fact]
    public void Typing_in_a_test_does_not_regenerate_while_the_reference_and_the_used_names_stay_the_same()
    {
        var run = TestProject("class Tests { object Arrange() { return new AccountTestEntity { CreditLimit = null }; } }");

        var tree = run.InputCompilation.SyntaxTrees.Single();
        var edited = run.InputCompilation.ReplaceSyntaxTree(
            tree,
            CSharpSyntaxTree.ParseText(
                Usings + "class Tests { object Arrange() { var account = new AccountTestEntity { CreditLimit = null }; return account; } }",
                new CSharpParseOptions(LanguageVersion.CSharp7_3),
                path: tree.FilePath));
        var result = run.Driver.RunGenerators(edited).GetRunResult().Results.Single();

        Assert.All(
            result.TrackedSteps[EarlyBoundGenerator.ReferencedTypesStep].SelectMany(s => s.Outputs),
            output => Assert.NotEqual(IncrementalStepRunReason.Modified, output.Reason));
        Assert.All(
            result.TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs),
            output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));
    }

    // ---- At run time -----------------------------------------------------------------------------

    private const string Scenario = """
        public static class Scenario
        {
            public static string Run()
            {
                var log = new System.Text.StringBuilder();
                Action<string, bool> check = (name, ok) => log.Append(ok ? string.Empty : "FAILED: " + name + "\n");

                var contact = new ContactTestEntity { FirstName = "Ada", LastName = "Lovelace", EmailAddress1 = "ada@example.com" };
                var account = new AccountTestEntity
                {
                    Name = "Contoso",                                               // a member of Account
                    CreditLimit = new Money(250m),                                  // members only the test entity has
                    AccountCategoryCode = Account_AccountCategoryCode.PreferredCustomer,
                    Sho_Tags = new[] { Sho_Account_Sho_Tags.Vip, Sho_Account_Sho_Tags.AtRisk },
                    PrimaryContactId = new EntityReference("contact", Guid.NewGuid()),
                    Account_Primary_Contact = contact,
                };

                // It is an Account, for the same table, and goes wherever the plug-in code takes one.
                Account asAccount = account;
                check("logical name", account.LogicalName == "account");
                check("plug-in code accepts it", Contoso.Plugins.Logic.Describe(account) == "Contoso||contact");
                check("plug-in code reads base members", Contoso.Plugins.Logic.FullName(contact) == "Ada Lovelace");

                // The added members store what the same members on a generated class would.
                check("money", ((Money)asAccount["creditlimit"]).Value == 250m && account.CreditLimit.Value == 250m);
                check("enum stored", ((OptionSetValue)asAccount["accountcategorycode"]).Value == 1);
                check("enum read", account.AccountCategoryCode == Account_AccountCategoryCode.PreferredCustomer);
                check("multi-select", ((OptionSetValueCollection)asAccount["sho_tags"]).Count == 2 && account.Sho_Tags.Last() == Sho_Account_Sho_Tags.AtRisk);
                check("email", (string)contact["emailaddress1"] == "ada@example.com");
                check("relationship", account.Account_Primary_Contact == contact && asAccount.RelatedEntities.Count == 1);
                check("formatted companion", account.PrimaryContactIdName == null);

                // Constants: those of the base class, and the added ones.
                check("fields", AccountTestEntity.Fields.Name == "name" && AccountTestEntity.Fields.CreditLimit == "creditlimit" && AccountTestEntity.Fields.Id == "accountid");

                // Constructors behave like the base class's.
                var id = Guid.NewGuid();
                var byId = new AccountTestEntity(id);
                check("id constructor", byId.Id == id && byId.LogicalName == "account" && byId.Attributes.Count == new Account(id).Attributes.Count);
                check("key constructor", new AccountTestEntity("accountnumber", "A-1").KeyAttributes["accountnumber"].Equals("A-1"));
                var projected = new AccountTestEntity(new { Name = "Fabrikam", CreditLimit = new Money(5m) });
                check("anonymous constructor", projected.Name == "Fabrikam" && projected.CreditLimit.Value == 5m);

                // A late-bound row converts to it, as it does to Account.
                var late = new Entity("account");
                late["creditlimit"] = new Money(7m);
                late["name"] = "Late";
                var converted = late.ToEntity<AccountTestEntity>();
                check("ToEntity", converted.CreditLimit.Value == 7m && converted.Name == "Late" && late.ToEntity<Account>().Name == "Late");

                check("label of an enum generated here", Account_AccountCategoryCode.Standard.GetMetadata().Name == "Standard");
                check("label of an enum from the plug-in assembly", Account_StateCode.Inactive.GetMetadata().Name == "Inactive");
                return log.ToString();
            }
        }
        """;

    [Fact]
    public void A_test_entity_works_wherever_its_base_class_does()
    {
        var plugin = PluginAssembly.Default;
        var run = TestProject(Scenario);
        Assert.Empty(run.CompilationErrors);
        var tests = run.EmitImage();

        var context = new AssemblyLoadContext("plugin-and-tests", isCollectible: true);
        try
        {
            context.LoadFromStream(new MemoryStream(plugin.Image));
            var assembly = context.LoadFromStream(new MemoryStream(tests));
            var failures = (string)assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            Assert.Equal(string.Empty, failures);

            // The derived class is in the test assembly, on top of the class in the plug-in assembly.
            var testEntity = assembly.GetType("Contoso.Model.AccountTestEntity")!;
            Assert.Equal("Contoso.Plugins", testEntity.BaseType!.Assembly.GetName().Name);
            Assert.Equal("Contoso.Model.Account", testEntity.BaseType.FullName);
        }
        finally
        {
            context.Unload();
        }
    }
}
