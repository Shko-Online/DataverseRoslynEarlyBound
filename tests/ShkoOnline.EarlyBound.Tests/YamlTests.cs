using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Tests.Support;
using ShkoOnline.EarlyBound.Yaml;

namespace ShkoOnline.EarlyBound.Tests;

public class YamlReaderTests
{
    [Fact]
    public void Reads_tables_columns_choices_and_relationships()
    {
        var (path, text) = Fixtures.MetadataFiles().Single(f => f.Path.EndsWith("account.yaml"));
        var file = MetadataYamlReader.Read(text, path);

        Assert.Empty(file.Diagnostics);
        var account = Assert.Single(file.Entities);
        Assert.Equal("account", account.LogicalName);
        Assert.Equal("Account", account.SchemaName);
        Assert.Equal("accountid", account.PrimaryIdAttribute);
        Assert.Equal(1, account.ObjectTypeCode);

        var category = account.FindAttribute("accountcategorycode")!;
        Assert.Equal(AttributeTypes.Picklist, category.Type);
        Assert.Equal("account_accountcategorycode", category.OptionSet!.Name);
        Assert.Equal(new[] { 1, 2 }, category.OptionSet.Options.Select(o => o.Value));
        Assert.Equal("Preferred Customer", category.OptionSet.Options[0].Label);

        Assert.Equal("sho_budgetstatus", account.FindAttribute("sho_budgetstatus")!.GlobalOptionSet);
        Assert.Equal(new[] { "systemuser", "team" }, account.FindAttribute("ownerid")!.Targets);
        Assert.False(account.FindAttribute("createdon")!.IsValidForUpdate);
        Assert.Equal("SystemRequired", account.FindAttribute("accountid")!.RequiredLevel);
        Assert.Equal(0, account.FindAttribute("statuscode")!.OptionSet!.Options[0].State);
        Assert.Equal("Active", account.FindAttribute("statecode")!.OptionSet!.Options[0].InvariantName);

        Assert.Contains(account.ManyToOne, r => r.SchemaName == "account_primary_contact" && r.Entity == "contact" && r.Attribute == "primarycontactid");
        Assert.Contains(account.ManyToMany, r => r.IntersectEntity == "sho_account_contact");
    }

    [Fact]
    public void One_file_can_hold_tables_choices_and_settings_together()
    {
        var file = MetadataYamlReader.Read("""
            environment: https://contoso.crm4.dynamics.com
            pull:
              entities: [account, "sho_*"]
              relationships: none
            generator:
              namespace: Contoso.Model
              camelCaseCustomWords: [sho]
              labelTextReplacement: { "$": "", "&": And }
            entities:
              - logicalName: account
            optionSets:
              - name: sho_colour
                options:
                  - { value: 1, label: Red }
            """);

        Assert.Empty(file.Diagnostics);
        Assert.Equal("https://contoso.crm4.dynamics.com", file.Configuration!.Environment);
        Assert.Equal(new[] { "account", "sho_*" }, file.Configuration.Pull.Entities);
        Assert.Equal("none", file.Configuration.Pull.Relationships);
        Assert.True(file.HasGeneratorSettings);
        Assert.Equal("Contoso.Model", file.Configuration.Generator.Namespace);
        Assert.Equal(new[] { "sho" }, file.Configuration.Generator.CamelCaseCustomWords);
        Assert.Equal(string.Empty, file.Configuration.Generator.LabelTextReplacement.Single(p => p.Key == "$").Value);
        Assert.Equal("And", file.Configuration.Generator.LabelTextReplacement.Single(p => p.Key == "&").Value);
        Assert.Equal("account", Assert.Single(file.Entities).SchemaName);
        Assert.True(Assert.Single(file.OptionSets).IsGlobal);
    }

    [Fact]
    public void Invalid_yaml_is_reported_where_it_is()
    {
        var file = MetadataYamlReader.Read("entities:\n  - logicalName: account\n    attributes: [unclosed\n", "/repo/bad.yaml");

        var diagnostic = Assert.Single(file.Diagnostics);
        Assert.Equal(DiagnosticIds.InvalidYaml, diagnostic.Id);
        Assert.Equal(MetadataSeverity.Error, diagnostic.Severity);
        Assert.Equal("/repo/bad.yaml", diagnostic.Path);
        Assert.True(diagnostic.Position.Line >= 2);
    }

    [Fact]
    public void Missing_names_and_bad_values_are_errors_with_a_position()
    {
        var file = MetadataYamlReader.Read("""
            entities:
              - schemaName: NoLogicalName
              - logicalName: account
                objectTypeCode: one
                attributes:
                  - schemaName: Orphan
                  - logicalName: name
                    isValidForCreate: maybe
            """);

        Assert.Equal(4, file.Diagnostics.Count);
        Assert.All(file.Diagnostics, d => Assert.Equal(DiagnosticIds.InvalidValue, d.Id));
        Assert.Contains(file.Diagnostics, d => d.Message.Contains("objectTypeCode") && d.Position.Line == 3);
        Assert.Contains(file.Diagnostics, d => d.Message.Contains("isValidForCreate") && d.Position.Line == 7);

        // What can be read is still read.
        var account = Assert.Single(file.Entities);
        Assert.Equal("name", Assert.Single(account.Attributes).LogicalName);
    }

    [Fact]
    public void A_misspelled_setting_is_pointed_out()
    {
        var file = MetadataYamlReader.Read("generator:\n  namspace: Typo\n", "/repo/earlybound.yaml");

        var diagnostic = Assert.Single(file.Diagnostics);
        Assert.Equal(DiagnosticIds.UnknownSetting, diagnostic.Id);
        Assert.Equal(MetadataSeverity.Warning, diagnostic.Severity);
        Assert.Contains("namspace", diagnostic.Message);
        Assert.Equal(1, diagnostic.Position.Line);
    }

    [Fact]
    public void Empty_and_unrelated_files_are_fine()
    {
        Assert.True(MetadataYamlReader.Read(string.Empty).IsEmpty);
        Assert.True(MetadataYamlReader.Read("# just a comment\n").IsEmpty);
        var other = MetadataYamlReader.Read("somethingElse:\n  key: value\n");
        Assert.True(other.IsEmpty);
        Assert.Empty(other.Diagnostics);
    }
}

public class YamlWriterTests
{
    [Fact]
    public void What_is_written_reads_back_the_same()
    {
        var files = Fixtures.MetadataFiles().Select(f => MetadataYamlReader.Read(f.Text, f.Path)).ToList();
        var entities = files.SelectMany(f => f.Entities).ToList();
        var optionSets = files.SelectMany(f => f.OptionSets).ToList();

        var first = MetadataYamlWriter.Write(entities, optionSets, new[] { "header" });
        var reread = MetadataYamlReader.Read(first);
        var second = MetadataYamlWriter.Write(reread.Entities, reread.OptionSets, new[] { "header" });

        Assert.Empty(reread.Diagnostics);
        Assert.Equal(entities.Count, reread.Entities.Count);
        Assert.Equal(first, second);
        Assert.StartsWith("# header\n", first);
        Assert.DoesNotContain("\r", first);
    }

    [Fact]
    public void Output_does_not_depend_on_input_order()
    {
        var files = Fixtures.MetadataFiles().Select(f => MetadataYamlReader.Read(f.Text, f.Path)).ToList();
        var entities = files.SelectMany(f => f.Entities).ToList();
        var reversed = entities.AsEnumerable().Reverse().ToList();
        foreach (var entity in reversed)
        {
            entity.Attributes.Reverse();
        }

        var a = MetadataYamlWriter.Write(reversed, Array.Empty<OptionSetDefinition>());
        foreach (var entity in entities)
        {
            entity.Attributes.Reverse();
        }

        Assert.Equal(MetadataYamlWriter.Write(entities, Array.Empty<OptionSetDefinition>()), a);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("No")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("1.5")]
    [InlineData("2026-01-31")]
    [InlineData("")]
    [InlineData(" padded ")]
    [InlineData("colon: inside")]
    [InlineData("# not a comment")]
    [InlineData("line one\nline two")]
    [InlineData("quote \" and ' and \\ backslash")]
    [InlineData("{ braces } [ brackets ], comma")]
    [InlineData("Çmimi i lartë — 50%")]
    public void Any_label_survives_a_round_trip_as_text(string label)
    {
        var optionSet = new OptionSetDefinition { Name = "sho_test", IsGlobal = true, DisplayName = label };
        optionSet.Options.Add(new OptionDefinition { Value = 1, Label = label, Description = label });

        var yaml = MetadataYamlWriter.Write(Array.Empty<EntityDefinition>(), new[] { optionSet });
        var read = Assert.Single(MetadataYamlReader.Read(yaml).OptionSets);

        Assert.Equal(label, read.DisplayName);
        Assert.Equal(label, read.Options[0].Label);
        Assert.Equal(label, read.Options[0].Description);
    }

    [Fact]
    public void Defaults_are_left_out_to_keep_files_small()
    {
        var entity = new EntityDefinition { LogicalName = "account", SchemaName = "Account" };
        entity.Attributes.Add(new AttributeDefinition { LogicalName = "name", SchemaName = "Name", RequiredLevel = "None" });
        entity.Attributes.Add(new AttributeDefinition { LogicalName = "createdon", SchemaName = "CreatedOn", Type = AttributeTypes.DateTime, IsValidForCreate = false, IsValidForUpdate = false });

        var yaml = MetadataYamlWriter.Write(new[] { entity }, Array.Empty<OptionSetDefinition>());

        Assert.DoesNotContain("requiredLevel", yaml);
        Assert.DoesNotContain("isValidForRead", yaml);
        Assert.DoesNotContain("isCustomEntity", yaml);
        Assert.Contains("isValidForCreate: false", yaml);
    }
}

public class MetadataSetTests
{
    [Fact]
    public void A_table_defined_twice_keeps_the_first_definition_and_says_so()
    {
        var a = MetadataYamlReader.Read("entities:\n  - logicalName: account\n    schemaName: First\n", "/repo/a.yaml");
        var b = MetadataYamlReader.Read("entities:\n  - logicalName: account\n    schemaName: Second\n", "/repo/b.yaml");

        var set = MetadataSet.Create(new[] { b, a });

        Assert.Equal("First", set.FindEntity("account")!.SchemaName);
        var diagnostic = Assert.Single(set.Diagnostics);
        Assert.Equal(DiagnosticIds.DuplicateDefinition, diagnostic.Id);
        Assert.Equal("/repo/b.yaml", diagnostic.Path);
    }
}
