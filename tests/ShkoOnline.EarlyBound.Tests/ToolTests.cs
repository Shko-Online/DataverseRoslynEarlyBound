using System.Text.Json;
using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Tool;
using ShkoOnline.EarlyBound.Tool.Authentication;
using ShkoOnline.EarlyBound.Tool.Dataverse;
using ShkoOnline.EarlyBound.Tool.Output;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>A Dataverse Web API made of canned answers, shaped like the real ones.</summary>
internal sealed class FakeDataverse : IDataverseApi
{
    private static string Label(string text, int lcid = 1033) =>
        $$"""{ "LocalizedLabels": [ { "Label": "{{text}}", "LanguageCode": {{lcid}} } ], "UserLocalizedLabel": { "Label": "{{text}}", "LanguageCode": {{lcid}} } }""";

    private const string EmptyLabel = """{ "LocalizedLabels": [], "UserLocalizedLabel": null }""";

    public List<string> Requests { get; } = new();

    private static readonly (string LogicalName, string SchemaName, string Display, string Id, bool Custom, bool Intersect)[] Index =
    {
        ("account", "Account", "Account", "70816501-edb9-4740-a16c-6a5efbc05d84", false, false),
        ("activityparty", "ActivityParty", "Activity Party", "b1b16c9a-9a43-4a13-8b8c-39e7b6a8e2b1", false, false),
        ("contact", "Contact", "Contact", "608861bc-50a4-4c5f-a02c-21fe1943e2cf", false, false),
        ("email", "Email", "Email", "e3f6a1d2-5d3f-4b1a-9c40-0a1a6b6c7d8e", false, false),
        ("sho_project", "sho_Project", "Project", "0f1e2d3c-4b5a-6978-8695-a4b3c2d1e0f9", true, false),
        ("sho_task", "sho_Task", "Task", "11111111-2222-3333-4444-555555555555", true, false),
        ("sho_account_contact", "sho_account_contact", "", "99999999-2222-3333-4444-555555555555", true, true),
    };

    public Task<JsonDocument> GetAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(relativeUrl);
        }

        return Task.FromResult(JsonDocument.Parse(Answer(relativeUrl)));
    }

    private static string Answer(string url)
    {
        if (url.StartsWith("organizations?"))
        {
            return """{ "value": [ { "languagecode": 1033, "organizationid": "00000000-0000-0000-0000-000000000001" } ] }""";
        }

        if (url.StartsWith("EntityDefinitions?"))
        {
            return "{ \"value\": [" + string.Join(",", Index.Select(e =>
                $$"""{ "LogicalName": "{{e.LogicalName}}", "SchemaName": "{{e.SchemaName}}", "MetadataId": "{{e.Id}}", "IsCustomEntity": {{(e.Custom ? "true" : "false")}}, "IsIntersect": {{(e.Intersect ? "true" : "false")}}, "DisplayName": {{(e.Display.Length == 0 ? EmptyLabel : Label(e.Display))}} }""")) + "] }";
        }

        if (url.StartsWith("solutions?$select=solutionid"))
        {
            return url.Contains("uniquename eq 'ContosoCore'")
                ? """{ "value": [ { "solutionid": "aaaaaaaa-0000-0000-0000-000000000001" } ] }"""
                : """{ "value": [] }""";
        }

        if (url.StartsWith("solutions?"))
        {
            return """{ "value": [ { "uniquename": "ContosoCore", "friendlyname": "Contoso Core", "version": "1.2.0.0", "ismanaged": false } ] }""";
        }

        if (url.StartsWith("solutioncomponents?"))
        {
            // Two pages, to exercise paging: the project table, then the task table and a non-table component.
            return """
                { "value": [ { "objectid": "0f1e2d3c-4b5a-6978-8695-a4b3c2d1e0f9" } ],
                  "@odata.nextLink": "https://contoso.crm4.dynamics.com/api/data/v9.2/next-page-of-components" }
                """;
        }

        if (url.EndsWith("next-page-of-components"))
        {
            return """{ "value": [ { "objectid": "11111111-2222-3333-4444-555555555555" }, { "objectid": "deadbeef-0000-0000-0000-000000000000" } ] }""";
        }

        var logicalName = url.Substring("EntityDefinitions(LogicalName='".Length).Split('\'')[0];
        if (url.Contains("/Attributes/Microsoft.Dynamics.CRM."))
        {
            return OptionSets(logicalName, url);
        }

        return Entity(logicalName, url.Contains("ManyToOneRelationships"));
    }

    private static string Attribute(string logicalName, string schemaName, string typeName, string legacyType, string extra = "") =>
        $$"""
        { "@odata.type": "#Microsoft.Dynamics.CRM.{{typeName.Replace("Type", string.Empty)}}AttributeMetadata",
          "LogicalName": "{{logicalName}}", "SchemaName": "{{schemaName}}", "AttributeType": "{{legacyType}}",
          "AttributeTypeName": { "Value": "{{typeName}}" }, "DisplayName": {{Label(schemaName + " label")}},
          "Description": {{EmptyLabel}}, "RequiredLevel": { "Value": "None", "CanBeChanged": true },
          "IsValidForCreate": true, "IsValidForUpdate": true, "IsValidForRead": true, "AttributeOf": null, "DeprecatedVersion": null
          {{(extra.Length == 0 ? string.Empty : ", " + extra)}} }
        """;

    private static string Entity(string logicalName, bool withRelationships)
    {
        var entry = Index.Single(e => e.LogicalName == logicalName);
        var attributes = new List<string>
        {
            Attribute(logicalName + "id", entry.SchemaName + "Id", "UniqueidentifierType", "Uniqueidentifier"),
        };
        var relationships = string.Empty;

        switch (logicalName)
        {
            case "account":
                attributes.Add(Attribute("name", "Name", "StringType", "String", "\"MaxLength\": 160, \"Format\": \"Text\", \"FormatName\": { \"Value\": \"Text\" }"));
                attributes.Add(Attribute("revenue", "Revenue", "MoneyType", "Money", "\"MinValue\": -922337203685477.0, \"MaxValue\": 922337203685477.0, \"Precision\": 2"));
                attributes.Add(Attribute("createdon", "CreatedOn", "DateTimeType", "DateTime", "\"Format\": \"DateAndTime\", \"DateTimeBehavior\": { \"Value\": \"UserLocal\" }").Replace("\"IsValidForCreate\": true, \"IsValidForUpdate\": true", "\"IsValidForCreate\": false, \"IsValidForUpdate\": false"));
                attributes.Add(Attribute("primarycontactid", "PrimaryContactId", "LookupType", "Lookup", "\"Targets\": [ \"contact\" ]"));
                attributes.Add(Attribute("primarycontactidname", "PrimaryContactIdName", "StringType", "String").Replace("\"AttributeOf\": null", "\"AttributeOf\": \"primarycontactid\""));
                attributes.Add(Attribute("industrycode", "IndustryCode", "PicklistType", "Picklist"));
                attributes.Add(Attribute("sho_budgetstatus", "sho_BudgetStatus", "PicklistType", "Picklist"));
                attributes.Add(Attribute("sho_tags", "sho_Tags", "MultiSelectPicklistType", "Virtual"));
                attributes.Add(Attribute("statecode", "StateCode", "StateType", "State"));
                attributes.Add(Attribute("statuscode", "StatusCode", "StatusType", "Status"));
                attributes.Add(Attribute("sho_old", "sho_Old", "StringType", "String").Replace("\"DeprecatedVersion\": null", "\"DeprecatedVersion\": \"9.1.0.0\""));
                relationships = """
                    "ManyToOneRelationships": [
                      { "SchemaName": "account_primary_contact", "ReferencedEntity": "contact", "ReferencingAttribute": "primarycontactid" },
                      { "SchemaName": "lk_accountbase_createdby", "ReferencedEntity": "systemuser", "ReferencingAttribute": "createdby" } ],
                    "OneToManyRelationships": [
                      { "SchemaName": "sho_account_sho_project", "ReferencingEntity": "sho_project", "ReferencingAttribute": "sho_accountid" },
                      { "SchemaName": "Account_Tasks", "ReferencingEntity": "task", "ReferencingAttribute": "regardingobjectid" } ],
                    "ManyToManyRelationships": [
                      { "SchemaName": "sho_account_contact_stakeholders", "Entity1LogicalName": "account", "Entity2LogicalName": "contact", "IntersectEntityName": "sho_account_contact" } ]
                    """;
                break;
            case "email":
                attributes.Add(Attribute("to", "To", "PartyListType", "PartyList"));
                break;
            case "sho_project":
                attributes.Add(Attribute("sho_accountid", "sho_AccountId", "LookupType", "Lookup", "\"Targets\": [ \"account\" ]"));
                attributes.Add(Attribute("sho_budgetstatus", "sho_budgetstatus", "PicklistType", "Picklist"));
                relationships = """
                    "ManyToOneRelationships": [ { "SchemaName": "sho_account_sho_project", "ReferencedEntity": "account", "ReferencingAttribute": "sho_accountid" } ],
                    "OneToManyRelationships": [], "ManyToManyRelationships": []
                    """;
                break;
        }

        return $$"""
            { "LogicalName": "{{logicalName}}", "SchemaName": "{{entry.SchemaName}}", "DisplayName": {{Label(entry.Display)}},
              "DisplayCollectionName": {{Label(entry.Display + "s")}}, "Description": {{Label("About " + entry.Display)}},
              "EntitySetName": "{{logicalName}}s", "LogicalCollectionName": "{{logicalName}}s",
              "PrimaryIdAttribute": "{{logicalName}}id", "PrimaryNameAttribute": "name", "PrimaryImageAttribute": null,
              "ObjectTypeCode": 1, "OwnershipType": "UserOwned", "IsCustomEntity": {{(entry.Custom ? "true" : "false")}},
              "IsActivity": {{(logicalName == "email" ? "true" : "false")}}, "IsIntersect": false,
              "Attributes": [ {{string.Join(",", attributes)}} ]
              {{(withRelationships && relationships.Length > 0 ? ", " + relationships : string.Empty)}} }
            """;
    }

    private static string Option(int value, string label, string extra = "") =>
        $$"""{ "Value": {{value}}, "Label": {{Label(label)}}, "Description": {{EmptyLabel}}, "Color": null, "ExternalValue": "" {{extra}} }""";

    private const string BudgetStatus = """
        "OptionSet": { "Name": "sho_budgetstatus", "IsGlobal": true, "OptionSetType": "Picklist",
          "DisplayName": { "LocalizedLabels": [ { "Label": "Budget Status", "LanguageCode": 1033 }, { "Label": "Statusi i buxhetit", "LanguageCode": 1052 } ], "UserLocalizedLabel": null },
          "Description": { "LocalizedLabels": [], "UserLocalizedLabel": null },
          "Options": [
            { "Value": 100000000, "Label": { "LocalizedLabels": [ { "Label": "May Buy", "LanguageCode": 1033 }, { "Label": "Mund të blejë", "LanguageCode": 1052 } ], "UserLocalizedLabel": null }, "Description": { "LocalizedLabels": [], "UserLocalizedLabel": null }, "Color": "#0070F4", "ExternalValue": null },
            { "Value": 100000001, "Label": { "LocalizedLabels": [ { "Label": "Will Buy", "LanguageCode": 1033 } ], "UserLocalizedLabel": null }, "Description": { "LocalizedLabels": [], "UserLocalizedLabel": null }, "Color": null, "ExternalValue": null } ] }
        """;

    private static string OptionSets(string logicalName, string url)
    {
        if (url.Contains("PicklistAttributeMetadata") && !url.Contains("MultiSelect"))
        {
            var items = new List<string> { $$"""{ "LogicalName": "sho_budgetstatus", {{BudgetStatus}} }""" };
            if (logicalName == "account")
            {
                items.Add($$"""
                    { "LogicalName": "industrycode", "OptionSet": { "Name": "account_industrycode", "IsGlobal": false, "OptionSetType": "Picklist",
                      "DisplayName": {{Label("Industry")}}, "Description": {{EmptyLabel}},
                      "Options": [ {{Option(1, "Accounting")}}, {{Option(2, "Agriculture")}} ] } }
                    """);
            }

            return "{ \"value\": [" + string.Join(",", items) + "] }";
        }

        if (url.Contains("MultiSelectPicklistAttributeMetadata"))
        {
            return $$"""
                { "value": [ { "LogicalName": "sho_tags", "OptionSet": { "Name": "sho_account_sho_tags", "IsGlobal": false, "OptionSetType": "Picklist",
                  "DisplayName": {{Label("Tags")}}, "Description": {{EmptyLabel}}, "Options": [ {{Option(100000000, "VIP")}} ] } } ] }
                """;
        }

        if (url.Contains("StateAttributeMetadata"))
        {
            return $$"""
                { "value": [ { "LogicalName": "statecode", "OptionSet": { "Name": "account_statecode", "IsGlobal": false, "OptionSetType": "State",
                  "DisplayName": {{Label("Status")}}, "Description": {{EmptyLabel}},
                  "Options": [ {{Option(0, "Active", ", \"@odata.type\": \"#Microsoft.Dynamics.CRM.StateOptionMetadata\", \"DefaultStatus\": 1, \"InvariantName\": \"Active\"")}},
                               {{Option(1, "Inactive", ", \"DefaultStatus\": 2, \"InvariantName\": \"Inactive\"")}} ] } } ] }
                """;
        }

        return $$"""
            { "value": [ { "LogicalName": "statuscode", "OptionSet": { "Name": "account_statuscode", "IsGlobal": false, "OptionSetType": "Status",
              "DisplayName": {{Label("Status Reason")}}, "Description": {{EmptyLabel}},
              "Options": [ {{Option(1, "Active", ", \"State\": 0, \"TransitionData\": null")}}, {{Option(2, "Inactive", ", \"State\": 1")}} ] } } ] }
            """;
    }
}

public class MetadataDownloaderTests
{
    private static async Task<(PullResult Result, FakeDataverse Api)> Pull(Action<PullSettings> configure)
    {
        var api = new FakeDataverse();
        var settings = new PullSettings();
        configure(settings);
        var result = await new MetadataDownloader(api).PullAsync(settings, null, CancellationToken.None);
        return (result, api);
    }

    [Fact]
    public async Task Maps_what_the_web_api_returns()
    {
        var (result, _) = await Pull(s => s.Entities.Add("account"));

        Assert.Equal(1033, result.LanguageCode);
        var account = Assert.Single(result.Entities);
        Assert.Equal("Account", account.SchemaName);
        Assert.Equal("Accounts", account.DisplayCollectionName);
        Assert.Equal("About Account", account.Description);
        Assert.Equal("accounts", account.EntitySetName);
        Assert.Equal("UserOwned", account.OwnershipType);

        var name = account.FindAttribute("name")!;
        Assert.Equal(AttributeTypes.String, name.Type);
        Assert.Equal(160, name.MaxLength);
        Assert.Equal("Text", name.Format);
        Assert.Null(name.Description);

        var revenue = account.FindAttribute("revenue")!;
        Assert.Equal(AttributeTypes.Money, revenue.Type);
        Assert.Equal(2, revenue.Precision);
        Assert.Equal("-922337203685477", revenue.MinValue);

        var createdOn = account.FindAttribute("createdon")!;
        Assert.False(createdOn.IsValidForCreate);
        Assert.Equal("UserLocal", createdOn.DateTimeBehavior);

        Assert.Equal(new[] { "contact" }, account.FindAttribute("primarycontactid")!.Targets);
        Assert.Equal("primarycontactid", account.FindAttribute("primarycontactidname")!.AttributeOf);
        Assert.True(account.FindAttribute("sho_old")!.IsDeprecated);

        // The newer column types are told apart by AttributeTypeName, not by the legacy "Virtual".
        var tags = account.FindAttribute("sho_tags")!;
        Assert.Equal(AttributeTypes.MultiSelectPicklist, tags.Type);
        Assert.Equal("sho_account_sho_tags", tags.OptionSet!.Name);

        var industry = account.FindAttribute("industrycode")!;
        Assert.Null(industry.GlobalOptionSet);
        Assert.Equal(new[] { "Accounting", "Agriculture" }, industry.OptionSet!.Options.Select(o => o.Label));
        Assert.Null(industry.OptionSet.Options[0].ExternalValue is { Length: 0 } ? null : industry.OptionSet.Options[0].Color);

        var state = account.FindAttribute("statecode")!.OptionSet!;
        Assert.Equal("State", state.OptionSetType);
        Assert.Equal("Active", state.Options[0].InvariantName);
        Assert.Equal(1, state.Options[0].DefaultStatus);
        Assert.Equal(1, account.FindAttribute("statuscode")!.OptionSet!.Options[1].State);

        // A global choice is referenced by the column and defined once.
        var budget = account.FindAttribute("sho_budgetstatus")!;
        Assert.Equal("sho_budgetstatus", budget.GlobalOptionSet);
        Assert.Null(budget.OptionSet);
        var global = Assert.Single(result.GlobalOptionSets);
        Assert.True(global.IsGlobal);
        Assert.Equal("Budget Status", global.DisplayName);
        Assert.Equal("#0070F4", global.Options[0].Color);
    }

    [Fact]
    public async Task Keeps_only_relationships_between_pulled_tables_by_default()
    {
        var (both, _) = await Pull(s => s.Entities.AddRange(new[] { "account", "sho_project" }));
        var account = both.Entities.Single(e => e.LogicalName == "account");
        Assert.Equal("sho_account_sho_project", Assert.Single(account.OneToMany).SchemaName);
        Assert.Empty(account.ManyToOne);
        Assert.Empty(account.ManyToMany);
        Assert.Equal("account", Assert.Single(both.Entities.Single(e => e.LogicalName == "sho_project").ManyToOne).Entity);

        var (all, _) = await Pull(s => { s.Entities.Add("account"); s.Relationships = PullSettings.RelationshipsAll; });
        Assert.Equal(2, all.Entities[0].ManyToOne.Count);
        Assert.Equal("contact", Assert.Single(all.Entities[0].ManyToMany).Entity);

        var (none, api) = await Pull(s => { s.Entities.Add("account"); s.Relationships = PullSettings.RelationshipsNone; });
        Assert.Empty(none.Entities[0].ManyToOne);
        Assert.DoesNotContain(api.Requests, r => r.Contains("ManyToOneRelationships"));
    }

    [Fact]
    public async Task Selects_tables_by_pattern_and_solution_and_reports_what_matches_nothing()
    {
        var (result, _) = await Pull(s =>
        {
            s.Entities.AddRange(new[] { "ACCOUNT", "sho_*", "nosuchtable" });
            s.Solutions.AddRange(new[] { "ContosoCore", "Missing" });
            s.Exclude.Add("sho_account*");
        });

        Assert.Equal(new[] { "account", "sho_project", "sho_task" }, result.Entities.Select(e => e.LogicalName));
        Assert.Contains(result.Warnings, w => w.Contains("'nosuchtable'"));
        Assert.Contains(result.Warnings, w => w.Contains("'Missing'") && w.Contains("unique name"));
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task Tables_of_a_solution_are_read_across_pages()
    {
        var (result, api) = await Pull(s => s.Solutions.Add("ContosoCore"));

        Assert.Equal(new[] { "sho_project", "sho_task" }, result.Entities.Select(e => e.LogicalName));
        Assert.Contains(api.Requests, r => r.EndsWith("next-page-of-components"));
    }

    [Fact]
    public async Task Adds_activityparty_for_party_lists_unless_excluded()
    {
        var (added, _) = await Pull(s => s.Entities.Add("email"));
        Assert.Equal(new[] { "activityparty", "email" }, added.Entities.Select(e => e.LogicalName));
        Assert.Contains("activityparty", Assert.Single(added.Notes));

        var (excluded, _) = await Pull(s => { s.Entities.Add("email"); s.Exclude.Add("activityparty"); });
        Assert.Equal(new[] { "email" }, excluded.Entities.Select(e => e.LogicalName));
        Assert.Empty(excluded.Notes);
    }

    [Fact]
    public async Task Labels_come_in_the_requested_language_with_a_fallback()
    {
        var (albanian, api) = await Pull(s => { s.Entities.Add("sho_project"); s.LanguageCode = 1052; });

        var budget = Assert.Single(albanian.GlobalOptionSets);
        Assert.Equal("Statusi i buxhetit", budget.DisplayName);
        Assert.Equal("Mund të blejë", budget.Options[0].Label);
        Assert.Equal("Will Buy", budget.Options[1].Label); // not translated: falls back
        Assert.DoesNotContain(api.Requests, r => r.StartsWith("organizations"));
    }

    [Fact]
    public async Task Only_asks_for_the_choice_types_a_table_has()
    {
        var (_, api) = await Pull(s => s.Entities.Add("sho_project"));

        Assert.Single(api.Requests, r => r.Contains("PicklistAttributeMetadata"));
        Assert.DoesNotContain(api.Requests, r => r.Contains("StateAttributeMetadata") || r.Contains("MultiSelect"));
    }

    [Fact]
    public async Task Refuses_to_pull_nothing()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Pull(_ => { }));
        Assert.Contains("pull.entities", error.Message);
    }

    [Fact]
    public async Task Lists_tables_and_solutions()
    {
        var downloader = new MetadataDownloader(new FakeDataverse());

        var tables = await downloader.ListEntitiesAsync(1033, CancellationToken.None);
        Assert.Equal(7, tables.Count);
        Assert.Equal("Project", tables.Single(t => t.LogicalName == "sho_project").DisplayName);
        Assert.True(tables.Single(t => t.LogicalName == "sho_account_contact").IsIntersect);

        var solution = Assert.Single(await downloader.ListSolutionsAsync(CancellationToken.None));
        Assert.Equal(("ContosoCore", "Contoso Core", "1.2.0.0", false), (solution.UniqueName, solution.FriendlyName, solution.Version, solution.IsManaged));
    }

    [Theory]
    [InlineData("account", "account", true)]
    [InlineData("Account", "account", true)]
    [InlineData("sho_*", "sho_project", true)]
    [InlineData("*_project", "sho_project", true)]
    [InlineData("sho_*", "account", false)]
    [InlineData("acc", "account", false)]
    [InlineData("a.c*", "abc", false)] // a dot is a dot, not a wildcard
    public void Patterns(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, EntitySelector.Matcher(pattern)(name));
    }
}

public class MetadataFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "earlybound-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static EntityDefinition Table(string logicalName) => new() { LogicalName = logicalName, SchemaName = logicalName };

    private static OptionSetDefinition Choice(string name) => new() { Name = name, IsGlobal = true, Options = { new OptionDefinition { Value = 1, Label = "One" } } };

    [Fact]
    public void Writes_one_file_per_table_and_choice_and_leaves_unchanged_files_alone()
    {
        var first = MetadataFileWriter.Write(_directory, PullSettings.LayoutPerEntity, new[] { Table("account"), Table("contact") }, new[] { Choice("sho_colour") });

        Assert.Equal((3, 0, 0), (first.Written, first.Unchanged, first.Removed));
        var account = Path.Combine(_directory, "entities", "account.yaml");
        Assert.True(File.Exists(account));
        Assert.True(File.Exists(Path.Combine(_directory, "optionsets", "sho_colour.yaml")));
        Assert.StartsWith("# " + MetadataFileWriter.Marker, File.ReadAllText(account));
        Assert.Equal("account", Assert.Single(MetadataYamlReader.Read(File.ReadAllText(account)).Entities).LogicalName);

        var stamp = File.GetLastWriteTimeUtc(account);
        var second = MetadataFileWriter.Write(_directory, PullSettings.LayoutPerEntity, new[] { Table("account"), Table("contact") }, new[] { Choice("sho_colour") });
        Assert.Equal((0, 3, 0), (second.Written, second.Unchanged, second.Removed));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(account));
    }

    [Fact]
    public void Removes_files_of_tables_no_longer_pulled_but_never_a_file_it_did_not_write()
    {
        MetadataFileWriter.Write(_directory, PullSettings.LayoutPerEntity, new[] { Table("account"), Table("contact") }, Array.Empty<OptionSetDefinition>());
        var mine = Path.Combine(_directory, "entities", "handwritten.yaml");
        File.WriteAllText(mine, "entities:\n  - logicalName: handwritten\n");
        var notes = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(notes, "keep me");

        var summary = MetadataFileWriter.Write(_directory, PullSettings.LayoutPerEntity, new[] { Table("account") }, Array.Empty<OptionSetDefinition>());

        Assert.Equal(1, summary.Removed);
        Assert.False(File.Exists(Path.Combine(_directory, "entities", "contact.yaml")));
        Assert.True(File.Exists(mine));
        Assert.True(File.Exists(notes));
    }

    [Fact]
    public void Switching_to_a_single_file_cleans_up_the_per_table_files()
    {
        MetadataFileWriter.Write(_directory, PullSettings.LayoutPerEntity, new[] { Table("account") }, new[] { Choice("sho_colour") });

        var summary = MetadataFileWriter.Write(_directory, PullSettings.LayoutSingleFile, new[] { Table("account") }, new[] { Choice("sho_colour") });

        Assert.Equal((1, 2), (summary.Written, summary.Removed));
        var file = MetadataYamlReader.Read(File.ReadAllText(Path.Combine(_directory, MetadataFileWriter.SingleFileName)));
        Assert.Single(file.Entities);
        Assert.Single(file.OptionSets);
    }
}

public class ProjectConfigurationTests
{
    [Fact]
    public void The_init_template_is_valid_and_means_what_was_asked()
    {
        var yaml = ProjectConfiguration.Template("https://contoso.crm4.dynamics.com", new[] { "account", "sho_*" }, new[] { "Contoso Core" }, "Contoso.Model");
        var file = MetadataYamlReader.Read(yaml);

        Assert.Empty(file.Diagnostics);
        var configuration = file.Configuration!;
        Assert.Equal("https://contoso.crm4.dynamics.com", configuration.Environment);
        Assert.Equal(new[] { "account", "sho_*" }, configuration.Pull.Entities);
        Assert.Equal(new[] { "Contoso Core" }, configuration.Pull.Solutions);
        Assert.Equal("Metadata", configuration.Pull.Output);
        Assert.Equal(PullSettings.LayoutPerEntity, configuration.Pull.Layout);
        Assert.Equal(PullSettings.RelationshipsIncluded, configuration.Pull.Relationships);
        Assert.Null(configuration.Pull.LanguageCode);
        Assert.Equal("Contoso.Model", configuration.Generator.Namespace);
        Assert.Equal("DataverseContext", configuration.Generator.ServiceContextName);
        Assert.True(configuration.Generator.GenerateOnlyUsedAttributes);
    }

    [Fact]
    public void The_bare_template_has_working_placeholders()
    {
        var file = MetadataYamlReader.Read(ProjectConfiguration.Template(null, Array.Empty<string>(), Array.Empty<string>(), null));

        Assert.Empty(file.Diagnostics);
        Assert.Equal(new[] { "account", "contact" }, file.Configuration!.Pull.Entities);
        Assert.Null(file.Configuration.Generator.Namespace);
    }

    [Theory]
    [InlineData(null, "Browser")]
    [InlineData("browser", "Browser")]
    [InlineData("DeviceCode", "DeviceCode")]
    [InlineData("device-code", "DeviceCode")]
    [InlineData("azurecli", "AzureCli")]
    [InlineData("environment", "Environment")]
    [InlineData("default", "Default")]
    public void Sign_in_methods(string? text, string expected)
    {
        Assert.True(CredentialFactory.TryParseMethod(text, out var method));
        Assert.Equal(expected, method.ToString());
        Assert.False(CredentialFactory.TryParseMethod("password", out _));
    }

    [Theory]
    [InlineData("https://contoso.crm4.dynamics.com", "https://contoso.crm4.dynamics.com/")]
    [InlineData("https://contoso.crm4.dynamics.com/main.aspx?appid=1", "https://contoso.crm4.dynamics.com/")]
    [InlineData("https://contoso.api.crm.dynamics.com/api/data/v9.2/", "https://contoso.api.crm.dynamics.com/")]
    public void Any_url_of_an_environment_is_reduced_to_its_root(string pasted, string expected)
    {
        Assert.Equal(expected, DataverseApi.Normalize(new Uri(pasted)).ToString());
        Assert.Equal(expected.TrimEnd('/') + "/.default", DataverseApi.Scope(new Uri(pasted)));
    }
}
