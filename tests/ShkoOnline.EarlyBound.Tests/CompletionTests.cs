using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>
/// The autocomplete provider, asked through Roslyn's completion service with the provider and the
/// generator loaded from analyzer references. $$ marks the caret.
/// </summary>
public class CompletionTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.Xrm.Sdk;
        using Microsoft.Xrm.Sdk.Query;
        using Contoso.Model;

        class C
        {
            IOrganizationService service;
            IPluginExecutionContext context;
            Guid id;

            void M()
            {

        """;

    private static Task<CompletionResult> Complete(string statements, char? typed = null, bool withGenerator = true) =>
        CompletionHarness.CompleteAsync(Prelude + statements + "\n    }\n}\n", typed, withGenerator: withGenerator);

    private static readonly string[] Tables = { "account", "activityparty", "contact", "email", "sho_project" };

    [Theory]
    [InlineData("""var e = new Entity("$$");""")]
    [InlineData("""var e = new Entity("$$", id);""")]
    [InlineData("""var e = new Entity(entityName: "$$");""")]
    [InlineData("""var r = new EntityReference("$$", id);""")]
    [InlineData("""EntityReference r = new("$$");""")]
    [InlineData("""var q = new QueryExpression("$$");""")]
    [InlineData("""var q = new QueryByAttribute("$$");""")]
    [InlineData("""var e = service.Retrieve("$$", id, new ColumnSet(true));""")]
    [InlineData("""service.Delete("$$", id);""")]
    [InlineData("""var q = new QueryExpression { EntityName = "$$" };""")]
    [InlineData("""var e = new Entity(); e.LogicalName = "$$";""")]
    [InlineData("""var l = new LinkEntity("account", "$$", "primarycontactid", "contactid", JoinOperator.Inner);""")]
    [InlineData("""var q = new QueryExpression("account"); q.AddLink("$$", "primarycontactid", "contactid");""")]
    [InlineData("""var e = new Entity("account"); if (e.LogicalName == "$$") { }""")]
    [InlineData("""if ("$$" != context.PrimaryEntityName) { }""")]
    [InlineData("""switch (context.PrimaryEntityName) { case "$$": break; }""")]
    [InlineData("""var isAccount = context.PrimaryEntityName is "$$";""")]
    [InlineData("""var e = new Entity("$$""")] // still being typed
    public async Task Table_names_where_a_table_is_named(string statements)
    {
        var result = await Complete(statements);
        Assert.Equal(Tables, result.Names);
    }

    [Theory]
    // The row itself.
    [InlineData("""var e = new Entity("contact"); var x = e["$$"];""", "contact")]
    [InlineData("""var e = new Entity("contact"); var x = e.GetAttributeValue<string>("$$");""", "contact")]
    [InlineData("""var e = new Entity("contact"); if (e.Contains("$$")) { }""", "contact")]
    [InlineData("""var e = new Entity("contact"); e.TryGetAttributeValue<string>("$$", out var v);""", "contact")]
    [InlineData("""var e = new Entity("contact"); var x = e.Attributes["$$"];""", "contact")]
    [InlineData("""var e = new Entity("contact"); if (e.Attributes.ContainsKey("$$")) { }""", "contact")]
    [InlineData("""var e = new Entity("contact"); var x = e.FormattedValues["$$"];""", "contact")]
    [InlineData("""var e = new Entity("contact"); var x = e?["$$"];""", "contact")]
    [InlineData("""var e = new Entity(Contact.EntityLogicalName); var x = e["$$"];""", "contact")]
    [InlineData("""Entity e; e = new Entity("contact"); var x = e["$$"];""", "contact")]
    [InlineData("""var e = new Entity { LogicalName = "contact" }; var x = e["$$"];""", "contact")]
    // Initializers.
    [InlineData("""var e = new Entity("contact") { ["$$"] = 1 };""", "contact")]
    [InlineData("""var e = new Entity("contact") { Attributes = { ["$$"] = 1 } };""", "contact")]
    [InlineData("""var e = new Entity("contact") { Attributes = { { "$$", 1 } } };""", "contact")]
    // Rows that come from somewhere.
    [InlineData("""var e = service.Retrieve("contact", id, new ColumnSet(true)); var x = e["$$"];""", "contact")]
    [InlineData("""Contact typed = null; var x = typed["$$"];""", "contact")]
    [InlineData("""Entity e = null; var x = e.ToEntity<Contact>()["$$"];""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); foreach (var row in service.RetrieveMultiple(q).Entities) { var x = row["$$"]; }""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); var x = service.RetrieveMultiple(q).Entities.Select(row => row.GetAttributeValue<string>("$$"));""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); var x = service.RetrieveMultiple(q).Entities.First()["$$"];""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); var rows = service.RetrieveMultiple(q); var x = rows.Entities[0]["$$"];""", "contact")]
    // Queries.
    [InlineData("""var e = service.Retrieve("contact", id, new ColumnSet("$$"));""", "contact")]
    [InlineData("""var e = service.Retrieve("contact", id, new ColumnSet("firstname", "$$"));""", "contact")]
    [InlineData("""var e = service.Retrieve("contact", id, new ColumnSet(new[] { "firstname", "$$" }));""", "contact")]
    [InlineData("""var q = new QueryExpression("contact") { ColumnSet = new ColumnSet("$$") };""", "contact")]
    [InlineData("""var q = new QueryExpression { EntityName = "contact", ColumnSet = new ColumnSet("$$") };""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.ColumnSet = new ColumnSet("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.ColumnSet.AddColumns("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.ColumnSet.AddColumn("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.ColumnSet.Columns.Add("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.Criteria.AddCondition("$$", ConditionOperator.Equal, 1);""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.Criteria.AddCondition(new ConditionExpression("$$", ConditionOperator.Null));""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.Criteria.Conditions.Add(new ConditionExpression("$$", ConditionOperator.Null));""", "contact")]
    [InlineData("""var q = new QueryExpression("contact") { Criteria = { Conditions = { new ConditionExpression("$$", ConditionOperator.Null) } } };""", "contact")]
    [InlineData("""var q = new QueryExpression("contact") { Criteria = { Conditions = { new ConditionExpression { AttributeName = "$$" } } } };""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.AddOrder("$$", OrderType.Ascending);""", "contact")]
    [InlineData("""var q = new QueryExpression("contact") { Orders = { new OrderExpression("$$", OrderType.Ascending) } };""", "contact")]
    [InlineData("""var q = new QueryByAttribute("contact"); q.AddAttributeValue("$$", 1);""", "contact")]
    [InlineData("""var q = new QueryByAttribute("contact"); q.Attributes.Add("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.Criteria.AddCondition("$$""", "contact")] // still being typed
    // Links: "from" belongs to the table linked from, "to" and the link's own columns to the table linked to.
    [InlineData("""var l = new LinkEntity("contact", "account", "$$", "accountid", JoinOperator.Inner);""", "contact")]
    [InlineData("""var l = new LinkEntity("account", "contact", "primarycontactid", "$$", JoinOperator.Inner);""", "contact")]
    [InlineData("""var q = new QueryExpression("contact"); q.AddLink("account", "$$", "accountid");""", "contact")]
    [InlineData("""var q = new QueryExpression("account"); q.AddLink("contact", "primarycontactid", "$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("account"); var l = q.AddLink("contact", "primarycontactid", "contactid"); l.Columns.AddColumns("$$");""", "contact")]
    [InlineData("""var q = new QueryExpression("account"); var l = q.AddLink("contact", "primarycontactid", "contactid"); l.LinkCriteria.AddCondition("$$", ConditionOperator.Null);""", "contact")]
    [InlineData("""var q = new QueryExpression("account") { LinkEntities = { new LinkEntity("account", "contact", "primarycontactid", "contactid", JoinOperator.Inner) { Columns = new ColumnSet("$$") } } };""", "contact")]
    [InlineData("""var l = new LinkEntity { LinkToEntityName = "contact", LinkToAttributeName = "$$" };""", "contact")]
    // A table named explicitly next to the column.
    [InlineData("""var c = new ConditionExpression("contact", "$$", ConditionOperator.Null);""", "contact")]
    public async Task Column_names_of_the_table_the_code_is_about(string statements, string table)
    {
        var result = await Complete(statements);

        Assert.Equal("contact", table);
        Assert.Contains("emailaddress1", result.Names);
        Assert.Contains("parentcustomerid", result.Names);
        Assert.DoesNotContain("accountnumber", result.Names);
        Assert.Equal(result.Names.OrderBy(n => n, StringComparer.Ordinal), result.Names);
    }

    [Theory]
    [InlineData("""Entity e = null; var x = e["$$"];""")]
    [InlineData("""var c = new ColumnSet("$$");""")]
    [InlineData("""var f = new FilterExpression(); f.AddCondition("$$", ConditionOperator.Null);""")]
    [InlineData("""var e = (Entity)context.InputParameters["Target"]; var x = e["$$"];""")]
    public async Task Every_column_when_the_table_cannot_be_worked_out(string statements)
    {
        var result = await Complete(statements);

        Assert.Contains("emailaddress1", result.Names);
        Assert.Contains("accountnumber", result.Names);
        Assert.Contains("sho_duedate", result.Names);
        Assert.Single(result.Names, n => n == "statecode");
        Assert.Contains("account, contact, sho_project", result.Item("statecode").InlineDescription);
    }

    [Theory]
    [InlineData("""var s = "$$";""")]
    [InlineData("""Console.WriteLine("$$");""")]
    [InlineData("""var x = context.InputParameters["$$"];""")]
    [InlineData("""var d = new Dictionary<string, object>(); var x = d["$$"];""")]
    [InlineData("""var x = context.MessageName == "$$";""")]
    [InlineData("""var e = new Entity("account", "$$", 1);""")] // an alternate key name, not a column
    [InlineData("""var o = new OrderExpression("name", OrderType.Ascending, "$$");""")] // an alias
    [InlineData("""// new Entity("$$")""")]
    public async Task Nothing_in_strings_that_are_not_names(string statements)
    {
        var result = await Complete(statements);
        Assert.Empty(result.Names);
    }

    [Fact]
    public async Task Computed_companion_columns_are_not_offered()
    {
        var result = await Complete("""var e = new Entity("account"); var x = e["$$"];""");

        Assert.Contains("primarycontactid", result.Names);
        Assert.DoesNotContain("primarycontactidname", result.Names);
        Assert.DoesNotContain("accountcategorycodename", result.Names);
    }

    [Fact]
    public async Task Relationship_schema_names()
    {
        var result = await Complete("""var r = new Relationship("$$");""");

        Assert.Contains("account_primary_contact", result.Names);
        Assert.Contains("sho_account_contact_stakeholders", result.Names);
        Assert.Equal(result.Names.Distinct(), result.Names);
    }

    [Fact]
    public async Task Picking_an_entry_replaces_the_whole_name()
    {
        var result = await Complete("""var e = new Entity("contact"); var x = e["first$$name"];""");

        var committed = await result.CommitAsync("emailaddress1");
        Assert.Contains("""e["emailaddress1"];""", committed);
    }

    [Fact]
    public async Task Entries_describe_what_they_are()
    {
        var result = await Complete("""var e = new Entity("account"); var x = e["$$"];""");

        Assert.Equal("Category · Picklist", result.Item("accountcategorycode").InlineDescription);
        var description = await result.DescribeAsync("accountcategorycode");
        Assert.Contains("Category (Picklist column of Account)", description);
        Assert.Contains("Select a category to indicate whether the customer account is standard or preferred.", description);
        Assert.Contains("Options: 1 Preferred Customer, 2 Standard", description);

        var lookup = await result.DescribeAsync("primarycontactid");
        Assert.Contains("Targets: contact", lookup);

        var tables = await Complete("""var e = new Entity("$$");""");
        Assert.Equal("Account", tables.Item("account").InlineDescription);
        Assert.Contains("Primary column: name", await tables.DescribeAsync("account"));
    }

    [Theory]
    [InlineData("""var e = new Entity("$$");""", '"')] // the opening quote
    [InlineData("""var e = new Entity("a$$");""", 'a')] // the first letter of the name
    public async Task Typing_in_a_name_opens_the_list(string statements, char typed)
    {
        var result = await Complete(statements, typed);
        Assert.Contains("account", result.Names);
    }

    // ---- Choice values -------------------------------------------------------------------------

    [Theory]
    [InlineData("""var e = new Entity("account"); e["statuscode"] = new OptionSetValue($$);""")]
    [InlineData("""var e = new Entity("account"); e.Attributes["statuscode"] = new OptionSetValue($$);""")]
    [InlineData("""var e = new Entity("account") { ["statuscode"] = new OptionSetValue($$) };""")]
    [InlineData("""var e = new Entity("account") { Attributes = { { "statuscode", new OptionSetValue($$) } } };""")]
    [InlineData("""var e = new Entity("account"); e.Attributes.Add("statuscode", new OptionSetValue($$));""")]
    [InlineData("""var q = new QueryExpression("account"); q.Criteria.AddCondition("statuscode", ConditionOperator.Equal, $$);""")]
    [InlineData("""var q = new QueryExpression("account"); q.Criteria.AddCondition("statuscode", ConditionOperator.In, 1, $$);""")]
    [InlineData("""var q = new QueryExpression("account") { Criteria = { Conditions = { new ConditionExpression("statuscode", ConditionOperator.Equal, $$) } } };""")]
    [InlineData("""var c = new ConditionExpression("account", "statuscode", ConditionOperator.Equal, $$);""")]
    [InlineData("""var q = new QueryByAttribute("account"); q.AddAttributeValue("statuscode", $$);""")]
    [InlineData("""var e = new Entity("account"); if (e.GetAttributeValue<OptionSetValue>("statuscode").Value == $$) { }""")]
    [InlineData("""var e = new Entity("account"); if (e.GetAttributeValue<OptionSetValue>("statuscode")?.Value != $$) { }""")]
    [InlineData("""var e = new Entity("account"); if (((OptionSetValue)e["statuscode"]).Value == $$) { }""")]
    [InlineData("""var e = new Entity("account"); var status = e.GetAttributeValue<OptionSetValue>("statuscode"); if (status.Value == $$) { }""")]
    [InlineData("""var e = new Entity("account"); switch (e.GetAttributeValue<OptionSetValue>("statuscode").Value) { case $$ }""")]
    public async Task Option_values_where_a_number_is_expected(string statements)
    {
        var result = await Complete(statements);

        Assert.Equal(new[] { "Active", "Inactive" }, result.Names);
        Assert.Equal(" = 2", result.Item("Inactive").DisplayTextSuffix);

        // The generated enum is used instead of a magic number.
        var committed = await result.CommitAsync("Inactive");
        Assert.Contains("(int)Account_StatusCode.Inactive", committed);
    }

    [Fact]
    public async Task Option_values_fall_back_to_the_number_without_a_generated_enum()
    {
        var statements = """var e = new Entity("account"); e["statuscode"] = new OptionSetValue($$);""";
        var result = await CompletionHarness.CompleteAsync(Prelude.Replace("using Contoso.Model;\n", string.Empty) + statements + "\n    }\n}\n", withGenerator: false);

        Assert.Contains("new OptionSetValue(2);", await result.CommitAsync("Inactive"));
    }

    [Fact]
    public async Task The_enum_is_qualified_when_its_namespace_is_not_imported()
    {
        var statements = """var e = new Entity("account"); e["statuscode"] = new OptionSetValue($$);""";
        var result = await CompletionHarness.CompleteAsync(Prelude.Replace("using Contoso.Model;\n", string.Empty) + statements + "\n    }\n}\n");

        Assert.Contains("new OptionSetValue((int)Contoso.Model.Account_StatusCode.Inactive);", await result.CommitAsync("Inactive"));
    }

    [Fact]
    public async Task A_started_value_is_replaced()
    {
        var result = await Complete("""var e = new Entity("account"); e["statuscode"] = new OptionSetValue(2$$);""");

        Assert.Contains("new OptionSetValue((int)Account_StatusCode.Inactive);", await result.CommitAsync("Inactive"));
    }

    [Fact]
    public async Task Global_choices_and_unknown_tables_work_too()
    {
        var global = await Complete("""var e = new Entity("sho_project"); e["sho_budgetstatus"] = new OptionSetValue($$);""");
        Assert.Equal(new[] { "No Committed Budget", "May Buy", "Can Buy", "Will Buy" }, global.Names);
        Assert.Contains("(int)Sho_BudgetStatus.MayBuy", await global.CommitAsync("May Buy"));

        // The table is unknown, but only one table has this column.
        var unique = await Complete("""Entity e = null; e["gendercode"] = new OptionSetValue($$);""");
        Assert.Equal(new[] { "Male", "Female" }, unique.Names);

        // Every table has its own statuscode, so nothing can be said.
        var ambiguous = await Complete("""Entity e = null; e["statuscode"] = new OptionSetValue($$);""");
        Assert.Empty(ambiguous.Names);
    }

    [Theory]
    [InlineData("""var e = new Entity("account"); e["name"] = new OptionSetValue($$);""")] // not a choice column
    [InlineData("""var x = new OptionSetValue($$);""")] // no column in sight
    [InlineData("""var q = new QueryExpression("account"); q.Criteria.AddCondition("statuscode", $$""")] // that is the operator
    [InlineData("""var n = Math.Max(1, $$);""")]
    public async Task No_option_values_elsewhere(string statements)
    {
        var result = await Complete(statements);
        Assert.Empty(result.Names);
    }

    // ---- Members that are not generated yet ---------------------------------------------------------
    //
    // Only the members the code uses are generated, so the compiler's own completion can only show
    // those. The provider offers the rest; picking one is what gets it generated.

    [Theory]
    [InlineData("""Account account = null; var x = account.$$;""")]
    [InlineData("""Account account = null; var x = account.$$""")] // nothing after the dot yet
    [InlineData("""Account account = null; var x = account.Cred$$;""")]
    [InlineData("""Account account = null; var x = account?.$$;""")]
    [InlineData("""var x = new Account().$$;""")]
    [InlineData("""var x = new Entity("account").ToEntity<Account>().$$;""")]
    [InlineData("""var x = new List<Account>().Select(row => row.$$);""")]
    public async Task Members_not_generated_yet_are_offered_on_a_row(string statements)
    {
        var result = await Complete(statements);

        Assert.Contains("CreditLimit", result.Names);
        Assert.Contains("AccountCategoryCode", result.Names);
        Assert.Contains("PrimaryContactIdName", result.Names);
        Assert.Contains("Account_Primary_Contact", result.Names); // relationships too

        // The key is always generated, so the compiler's own completion already has it.
        Assert.DoesNotContain("AccountId", result.Names);
        Assert.DoesNotContain("FirstName", result.Names); // a column of another table
    }

    [Fact]
    public async Task A_member_in_use_is_left_to_the_compilers_own_completion()
    {
        var result = await Complete("""Account account = null; var n = account.Name; var x = account.$$;""");

        Assert.DoesNotContain("Name", result.Names);
        Assert.Contains("CreditLimit", result.Names);
    }

    [Theory]
    [InlineData("""var a = new Account { $$ };""")]
    [InlineData("""var a = new Account { Name = "x", $$ };""")]
    [InlineData("""var a = new Account { Cred$$ };""")]
    [InlineData("""var a = new Account { Cred$$ = null };""")]
    [InlineData("""Account a = new() { $$ };""")]
    public async Task Members_that_can_be_set_are_offered_in_an_object_initializer(string statements)
    {
        var result = await Complete(statements);

        Assert.Contains("CreditLimit", result.Names);
        Assert.Contains("Account_Primary_Contact", result.Names);
        Assert.DoesNotContain("CreatedOn", result.Names); // read-only
        Assert.DoesNotContain("PrimaryContactIdName", result.Names); // computed
    }

    [Fact]
    public async Task What_an_initializer_already_sets_is_not_offered_again()
    {
        var result = await Complete("""var a = new Account { Name = "x", $$ };""");

        Assert.DoesNotContain("Name", result.Names);
        Assert.Contains("AccountNumber", result.Names);
    }

    [Theory]
    [InlineData("""var a = new Account { $$ };""", ' ')]
    [InlineData("""var a = new Account { C$$ };""", 'C')]
    [InlineData("""var a = new Account { Name = "x", C$$ };""", 'C')]
    [InlineData("""Account account = null; var x = account.$$;""", '.')]
    public async Task Typing_where_a_member_goes_brings_them_up(string statements, char typed)
    {
        var result = await Complete(statements, typed);
        Assert.Contains("CreditLimit", result.Names);
    }

    [Fact]
    public async Task Column_name_constants_are_offered_under_fields()
    {
        var result = await Complete("""var column = Account.Fields.$$;""");

        Assert.Contains("CreditLimit", result.Names);
        Assert.Contains("AccountCategoryCode", result.Names);
        Assert.DoesNotContain("Account_Primary_Contact", result.Names); // a relationship is not a column
        Assert.DoesNotContain("AccountId", result.Names); // generated already
        Assert.Contains(Microsoft.CodeAnalysis.Tags.WellKnownTags.Constant, result.Item("CreditLimit").Tags);
    }

    [Theory]
    [InlineData("""Entity e = null; var x = e.$$;""")] // late bound: no generated class
    [InlineData("""var x = "text".$$;""")]
    [InlineData("""var x = context.$$;""")]
    [InlineData("""var x = Account.$$;""")] // the class itself, not a row
    [InlineData("""Account account = null; var x = account.Id.$$;""")]
    [InlineData("""var d = new Dictionary<string, object> { $$ };""")]
    [InlineData("""var q = new QueryExpression { $$ };""")]
    public async Task Members_are_not_offered_where_there_is_no_generated_class(string statements)
    {
        var result = await Complete(statements);
        Assert.Empty(result.Names);
    }

    [Fact]
    public async Task Members_are_offered_on_a_class_derived_from_a_generated_one()
    {
        var result = await CompletionHarness.CompleteAsync(Prelude + """
                    var x = new KeyAccount().$$;
                }
            }

            class KeyAccount : Account { }
            """);

        Assert.Contains("CreditLimit", result.Names);
    }

    [Fact]
    public async Task Picking_a_member_writes_its_name_and_says_what_will_happen()
    {
        var result = await Complete("""Account account = null; var x = account.Cred$$;""");

        Assert.Contains("var x = account.CreditLimit;", await result.CommitAsync("CreditLimit"));
        Assert.Equal("Credit Limit · Money", result.Item("CreditLimit").InlineDescription);
        var description = await result.DescribeAsync("CreditLimit");
        Assert.Contains("Credit Limit (Money column of Account)", description);
        Assert.Contains("Not generated yet", description);
        Assert.Contains("N:1 Contact", result.Item("Account_Primary_Contact").InlineDescription);
    }

    [Fact]
    public async Task Nothing_is_offered_when_every_member_is_generated_anyway()
    {
        var everything = Fixtures.MetadataFilesWith(("/consumer/earlybound.yaml", Fixtures.FullConfiguration));
        var result = await CompletionHarness.CompleteAsync(
            Prelude + """Account account = null; var x = account.$$;""" + "\n    }\n}\n", files: everything);

        Assert.Empty(result.Names);
    }

    // ---- FetchXML ------------------------------------------------------------------------------

    private static Task<CompletionResult> Fetch(string fetchXml, string open = "@\"", string close = "\"") =>
        Complete("var fetch = " + open + fetchXml + close + ";");

    [Theory]
    [InlineData("<fetch><entity name='$$'")]
    [InlineData("<fetch><entity name=\"\"$$\"\"></entity></fetch>")]
    [InlineData("<fetch top='5'>\n  <entity name='account'>\n    <link-entity name='$$' from='contactid' to='primarycontactid' />\n  </entity>\n</fetch>")]
    public async Task FetchXml_table_names(string fetchXml)
    {
        var result = await Fetch(fetchXml);
        Assert.Equal(Tables, result.Names);
    }

    [Theory]
    [InlineData("<fetch><entity name='contact'><attribute name='$$' /></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><attribute name='firstname' /><order attribute='$$' /></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><filter><condition attribute='$$' operator='eq' value='1' /></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><filter type='and'><filter type='or'><condition attribute='$$'")]
    // Inside a link, columns are those of the linked table; "from" too, "to" is the outer table's.
    [InlineData("<fetch><entity name='account'><link-entity name='contact' from='contactid' to='primarycontactid'><attribute name='$$' /></link-entity></entity></fetch>")]
    [InlineData("<fetch><entity name='account'><link-entity name='contact' from='$$' to='primarycontactid' /></entity></fetch>")]
    [InlineData("<fetch><entity name='account'><link-entity from='$$' name='contact' to='primarycontactid' /></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><link-entity name='account' from='accountid' to='$$' /></entity></fetch>")]
    // After a link is closed, the outer table applies again.
    [InlineData("<fetch><entity name='contact'><link-entity name='account' from='accountid' to='parentcustomerid'><attribute name='name' /></link-entity><attribute name='$$' /></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><link-entity name='account' from='accountid' to='parentcustomerid' /><attribute name='$$' /></entity></fetch>")]
    // A condition on a link, by alias or by table name.
    [InlineData("<fetch><entity name='account'><link-entity name='contact' from='contactid' to='primarycontactid' alias='c' /><filter><condition entityname='c' attribute='$$' /></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='account'><filter><condition entityname='contact' attribute='$$' /></filter></entity></fetch>")]
    public async Task FetchXml_column_names_of_the_enclosing_table(string fetchXml)
    {
        var result = await Fetch(fetchXml);

        Assert.Contains("emailaddress1", result.Names);
        Assert.DoesNotContain("accountnumber", result.Names);
    }

    [Theory]
    [InlineData("@\"", "\"")] // verbatim
    [InlineData("\"", "\"")] // regular, with apostrophes for the attributes
    [InlineData("\"\"\"\n", "\n\"\"\"")] // raw
    [InlineData("$@\"", "\"")] // interpolated
    public async Task FetchXml_in_every_kind_of_string(string open, string close)
    {
        var result = await Fetch("<fetch><entity name='contact'><attribute name='$$' /></entity></fetch>", open, close);
        Assert.Contains("emailaddress1", result.Names);
    }

    [Fact]
    public async Task FetchXml_built_from_pieces()
    {
        var concatenated = await Complete("""var top = 5; var fetch = "<fetch top='" + top + "'>" + "<entity name='contact'>" + "<attribute name='$$' />";""");
        Assert.Contains("emailaddress1", concatenated.Names);

        var interpolated = await Complete("""var top = 5; var fetch = $"<fetch top='{top}'><entity name='contact'><filter><condition attribute='statecode' operator='eq' value='{0}' /><condition attribute='$$' />";""");
        Assert.Contains("emailaddress1", interpolated.Names);
    }

    [Fact]
    public async Task FetchXml_operators_link_types_and_filter_types()
    {
        var operators = await Fetch("<fetch><entity name='contact'><filter><condition attribute='birthdate' operator='$$' /></filter></entity></fetch>");
        Assert.Equal("eq", operators.Names.First());
        Assert.Contains("olderthan-x-days", operators.Names);
        Assert.Contains("not-null", operators.Names);

        var typed = await Fetch("<fetch><entity name='contact'><filter><condition attribute='birthdate' operator='not-n$$' /></filter></entity></fetch>");
        Assert.Contains("operator='not-null'", await typed.CommitAsync("not-null"));

        var linkTypes = await Fetch("<fetch><entity name='contact'><link-entity name='account' link-type='$$'");
        Assert.Contains("outer", linkTypes.Names);

        var filterTypes = await Fetch("<fetch><entity name='contact'><filter type='$$'");
        Assert.Equal(new[] { "and", "or" }, filterTypes.Names);
    }

    [Theory]
    [InlineData("<fetch><entity name='account'><filter><condition attribute='statuscode' operator='eq' value='$$' /></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='account'><filter><condition operator='eq' value='$$' attribute='statuscode' /></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='account'><filter><condition attribute='statuscode' operator='in'><value>1</value><value>$$</value></condition></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><link-entity name='account' from='accountid' to='parentcustomerid' alias='a' /><filter><condition entityname='a' attribute='statuscode' operator='eq' value='$$' /></filter></entity></fetch>")]
    public async Task FetchXml_option_values_are_numbers(string fetchXml)
    {
        var result = await Fetch(fetchXml);

        Assert.Equal(new[] { "Active", "Inactive" }, result.Names);
        var committed = await result.CommitAsync("Inactive");
        Assert.Contains(fetchXml.Replace("$$", "2").Replace("'", "'"), committed);
    }

    [Theory]
    [InlineData("<fetch><entity name='contact'><attribute alias='$$' /></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'><filter><condition attribute='firstname' operator='eq' value='$$' /></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='contact'>$$</entity></fetch>")]
    [InlineData("<fetch><!-- <entity name='$$' -->")]
    [InlineData("<html><body class='$$'></body></html>")]
    public async Task FetchXml_nothing_where_no_name_belongs(string fetchXml)
    {
        var result = await Fetch(fetchXml);
        Assert.Empty(result.Names);
    }
}
