using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>Compiles the generated code into an assembly, loads it and uses it.</summary>
public class GeneratedCodeBehaviourTests
{
    private const string Scenario = """
        using System;
        using System.Linq;
        using Contoso.Model;
        using Microsoft.Xrm.Sdk;

        public static class Scenario
        {
            public static string Run()
            {
                var log = new System.Text.StringBuilder();
                void Check(string name, bool ok) => log.Append(ok ? string.Empty : "FAILED: " + name + "\n");

                var account = new Account();
                Check("logical name", account.LogicalName == "account" && Account.EntityLogicalName == "account");

                // Plain values go through the attribute collection.
                account.Name = "Contoso";
                account.NumberOfEmployees = 42;
                account.CreditLimit = new Money(10m);
                Check("string", (string)account["name"] == "Contoso" && account.Name == "Contoso");
                Check("int", (int)account["numberofemployees"] == 42);
                Check("missing value is null", account.AccountNumber == null && account.DoNotEmail == null);

                // The primary key and Entity.Id stay in step, both ways.
                var id = Guid.NewGuid();
                account.AccountId = id;
                Check("id from key", account.Id == id);
                var other = Guid.NewGuid();
                account.Id = other;
                Check("key from id", account.AccountId == other && (Guid)account["accountid"] == other);
                account.AccountId = null;
                Check("cleared key", account.Id == Guid.Empty);

                // Choices: an enum on the outside, an OptionSetValue on the inside.
                account.AccountCategoryCode = Account_AccountCategoryCode.Standard;
                Check("enum stored as OptionSetValue", ((OptionSetValue)account["accountcategorycode"]).Value == 2);
                account["accountcategorycode"] = new OptionSetValue(1);
                Check("enum read", account.AccountCategoryCode == Account_AccountCategoryCode.PreferredCustomer);
                account.AccountCategoryCode = null;
                Check("enum cleared", account["accountcategorycode"] == null && account.AccountCategoryCode == null);
                Check("enum absent", new Account().StateCode == null);

                // Multi-select choices.
                account.Sho_Tags = new[] { Sho_Account_Sho_Tags.Vip, Sho_Account_Sho_Tags.AtRisk };
                var stored = (OptionSetValueCollection)account["sho_tags"];
                Check("multi stored", stored.Count == 2 && stored[1].Value == 100000002);
                Check("multi read", account.Sho_Tags.SequenceEqual(new[] { Sho_Account_Sho_Tags.Vip, Sho_Account_Sho_Tags.AtRisk }));
                Check("multi absent is empty", !new Account().Sho_Tags.Any());

                // Lookups and their display text.
                account.PrimaryContactId = new EntityReference("contact", Guid.NewGuid());
                account.FormattedValues["primarycontactid"] = "Maria";
                Check("lookup", account.PrimaryContactId.LogicalName == Contact.EntityLogicalName);
                Check("formatted companion", account.PrimaryContactIdName == "Maria" && new Account().PrimaryContactIdName == null);

                // Late-bound rows convert to the typed class.
                var late = new Entity("contact") { ["firstname"] = "Ada", ["gendercode"] = new OptionSetValue(2) };
                var contact = late.ToEntity<Contact>();
                Check("ToEntity", contact.FirstName == "Ada" && contact.GenderCode == Contact_GenderCode.Female);

                // Party lists are sequences of typed rows.
                var email = new Email { To = new[] { new ActivityParty { PartyId = account.PrimaryContactId } } };
                Check("party list stored", ((EntityCollection)email["to"]).Entities.Count == 1);
                Check("party list read", email.To.Single().PartyId.LogicalName == "contact" && new Email().To == null);

                // The anonymous-type constructor used in LINQ projections.
                var projected = new Account(new { Name = "Fabrikam", AccountCategoryCode = Account_AccountCategoryCode.Standard, Id = id });
                Check("anonymous ctor", projected.Name == "Fabrikam" && projected.AccountCategoryCode == Account_AccountCategoryCode.Standard && projected.Id == id && projected.AccountId == id);

                // Key constructors.
                Check("id ctor", new Account(id).Id == id);
                Check("key ctor", new Account("accountnumber", "A-1").KeyAttributes["accountnumber"].Equals("A-1"));

                // Labels captured from the metadata.
                var metadata = Sho_BudgetStatus.WillBuy.GetMetadata();
                Check("option metadata", metadata.Name == "Will Buy" && metadata.DisplayIndex == 3 && metadata.Color == "#0070F4");
                Check("option description", Sho_BudgetStatus.CanBuy.GetMetadata().Description == "Budget is approved.");

                // Relationships read from RelatedEntities.
                var parent = new Account { Name = "Parent" };
                account.ReferencingAccount_Parent_Account = parent;
                Check("related entity", account.ReferencingAccount_Parent_Account.Name == "Parent");

                Check("fields", Account.Fields.Sho_BudgetStatus == "sho_budgetstatus" && Account.Fields.Id == "accountid");
                Check("entity attribute", typeof(Account).GetCustomAttributes(typeof(Microsoft.Xrm.Sdk.Client.EntityLogicalNameAttribute), false).Length == 1);
                Check("proxy types assembly", typeof(Account).Assembly.GetCustomAttributes(typeof(Microsoft.Xrm.Sdk.Client.ProxyTypesAssemblyAttribute), false).Length == 1);
                return log.ToString();
            }
        }
        """;

    [Fact]
    public void Generated_classes_behave_like_early_bound_classes_should()
    {
        var run = GeneratorHarness.Run(
            Fixtures.MetadataFilesWith(("/repo/earlybound.yaml", Fixtures.Configuration)),
            userSource: Scenario);
        Assert.Empty(run.CompilationErrors);

        using var stream = new MemoryStream();
        var emit = run.Compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        var context = new AssemblyLoadContext("generated", isCollectible: true);
        try
        {
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var failures = (string)assembly.GetType("Scenario")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
            Assert.Equal(string.Empty, failures);
        }
        finally
        {
            context.Unload();
        }
    }
}
