using ShkoOnline.EarlyBound.Configuration;
using ShkoOnline.EarlyBound.Metadata;
using ShkoOnline.EarlyBound.Naming;

namespace ShkoOnline.EarlyBound.Tests;

public class CamelCaserTests
{
    // Expected values were produced by the Early Bound Generator's own CamelCaser over the same
    // word list; the port has to agree with it name for name.
    [Theory]
    [InlineData("new_duedate", "New_DueDate")]
    [InlineData("activitymimeattachment", "ActivityMimeAttachment")]
    [InlineData("systemuser", "SystemUser")]
    [InlineData("address1_stateorprovince", "Address1_StateOrProvince")]
    [InlineData("emailaddress1", "EmailAddress1")]
    [InlineData("transactioncurrencyid", "TransactionCurrencyId")]
    [InlineData("donotbulkemail", "DoNotBulkEmail")]
    [InlineData("owneridtype", "OwnerIdType")]
    [InlineData("utcconversiontimezonecode", "UtcConversionTimeZoneCode")]
    [InlineData("msdyn_workorderid", "Msdyn_WorkOrderId")]
    public void Cases_names_like_the_early_bound_generator(string input, string expected)
    {
        Assert.Equal(expected, new CamelCaser().Case(input));
    }

    [Fact]
    public void An_unknown_prefix_is_spelled_out_until_it_is_a_custom_word()
    {
        Assert.Equal("SHo_Project", new CamelCaser().Case("sho_project"));
        Assert.Equal("Sho_Project", new CamelCaser(customWords: new[] { "sho" }).Case("sho_project"));
    }

    [Fact]
    public void Token_overrides_are_written_as_given()
    {
        var caser = new CamelCaser(tokenCapitalizationOverrides: GeneratorSettings.DefaultTokenCapitalizationOverrides);
        Assert.Equal("ExchangeRate", caser.Case("exchangerate"));
        Assert.Equal("SlaId", caser.Case("slaid"));
    }

    [Fact]
    public void Input_casing_does_not_matter()
    {
        var caser = new CamelCaser();
        Assert.Equal(caser.Case("accountcategorycode"), caser.Case("AccountCategoryCode"));
    }
}

public class NamingServiceTests
{
    private static NamingService Naming(Action<GeneratorSettings>? configure = null)
    {
        var settings = new GeneratorSettings { CamelCaseCustomWords = { "sho" } };
        configure?.Invoke(settings);
        return new NamingService(settings);
    }

    private static OptionSetDefinition Options(params (int Value, string? Label, int? State)[] options)
    {
        var set = new OptionSetDefinition { Name = "test" };
        foreach (var (value, label, state) in options)
        {
            set.Options.Add(new OptionDefinition { Value = value, Label = label, State = state });
        }

        return set;
    }

    [Theory]
    [InlineData("Preferred Customer", "PreferredCustomer")]
    [InlineData("in progress", "InProgress")]
    [InlineData("WON", "Won")]
    [InlineData("Agriculture and Non-petrol Natural Resource Extraction", "AgricultureAndNonPetrolNaturalResourceExtraction")]
    [InlineData("1st Review", "_1stReview")]
    [InlineData("Customer's Choice", "CustomersChoice")]
    [InlineData("Çmimi i lartë", "CmimiILarte")]
    [InlineData("50% (half)", "_50Half")]
    [InlineData("LinkedIn", "LinkedIn")]
    public void Option_labels_become_member_names(string label, string expected)
    {
        var names = Naming().GetNamesForOptions(Options((1, label, null)), isStatus: false);
        Assert.Equal(expected, names[1]);
    }

    [Fact]
    public void Options_without_a_usable_label_are_named_after_their_value()
    {
        var names = Naming().GetNamesForOptions(Options((7, null, null), (8, "   ", null), (9, "???", null)), isStatus: false);
        Assert.Equal("UnknownLabel7", names[7]);
        Assert.Equal("UnknownLabel8", names[8]);
        Assert.Equal("UnknownLabel9", names[9]);
    }

    [Fact]
    public void Duplicate_labels_get_their_value_appended()
    {
        var names = Naming().GetNamesForOptions(Options((1, "Open", null), (2, "Open", null), (-3, "Open", null), (4, "Closed", null)), isStatus: false);
        Assert.Equal("Open_1", names[1]);
        Assert.Equal("Open_2", names[2]);
        Assert.Equal("Open_neg3", names[-3]);
        Assert.Equal("Closed", names[4]);
    }

    [Fact]
    public void Duplicate_status_labels_are_told_apart_by_state_first()
    {
        var names = Naming().GetNamesForOptions(
            Options((1, "On Hold", 0), (2, "On Hold", 1), (3, "Canceled", 1), (4, "Canceled", 1)),
            isStatus: true);
        Assert.Equal("OnHold_Active", names[1]);
        Assert.Equal("OnHold_Inactive", names[2]);
        Assert.Equal("Canceled_Inactive_3", names[3]);
        Assert.Equal("Canceled_Inactive_4", names[4]);
    }

    [Fact]
    public void State_options_use_their_invariant_name()
    {
        var set = new OptionSetDefinition { Name = "account_statecode" };
        set.Options.Add(new OptionDefinition { Value = 0, Label = "Aktiv", InvariantName = "Active" });
        Assert.Equal("Active", Naming().GetNamesForOptions(set, isStatus: false)[0]);
    }

    private static (EntityDefinition Entity, AttributeDefinition Attribute) Column(string entitySchema, string attributeSchema, string type)
    {
        var attribute = new AttributeDefinition { LogicalName = attributeSchema.ToLowerInvariant(), SchemaName = attributeSchema, Type = type };
        var entity = new EntityDefinition { LogicalName = entitySchema.ToLowerInvariant(), SchemaName = entitySchema, Attributes = { attribute } };
        return (entity, attribute);
    }

    [Fact]
    public void A_columns_own_choice_is_named_after_class_and_property()
    {
        var (entity, attribute) = Column("Account", "AccountCategoryCode", AttributeTypes.Picklist);
        var optionSet = new OptionSetDefinition { Name = "account_accountcategorycode" };
        Assert.Equal("Account_AccountCategoryCode", Naming().GetNameForOptionSet(entity, attribute, optionSet, Array.Empty<string>()));
    }

    [Fact]
    public void State_and_status_enums_follow_the_entity_name()
    {
        var naming = Naming();
        var (entity, state) = Column("sho_project", "statecode", AttributeTypes.State);
        Assert.Equal("Sho_Project_StateCode",
            naming.GetNameForOptionSet(entity, state, new OptionSetDefinition { Name = "sho_project_statecode", DisplayName = "Status" }, Array.Empty<string>()));

        var status = new AttributeDefinition { LogicalName = "statuscode", SchemaName = "statuscode", Type = AttributeTypes.Status };
        Assert.Equal("Sho_Project_StatusCode",
            naming.GetNameForOptionSet(entity, status, new OptionSetDefinition { Name = "sho_project_statuscode", DisplayName = "Status Reason" }, Array.Empty<string>()));
    }

    [Fact]
    public void The_crmsvcutil_state_convention_can_be_switched_on()
    {
        var (entity, state) = Column("Account", "StateCode", AttributeTypes.State);
        var naming = Naming(s => s.UseCrmSvcUtilStateEnumNamingConvention = true);
        Assert.Equal("AccountState", naming.GetNameForOptionSet(entity, state, new OptionSetDefinition { Name = "account_statecode" }, Array.Empty<string>()));
    }

    [Fact]
    public void Global_choices_keep_their_name_with_fixed_casing()
    {
        var (entity, attribute) = Column("Lead", "BudgetStatus", AttributeTypes.Picklist);
        var naming = Naming();
        Assert.Equal("BudgetStatus",
            naming.GetNameForOptionSet(entity, attribute, new OptionSetDefinition { Name = "budgetstatus", IsGlobal = true }, Array.Empty<string>()));
        Assert.Equal("Sho_BudgetStatus",
            naming.GetNameForOptionSet(entity, attribute, new OptionSetDefinition { Name = "sho_budgetstatus", DisplayName = "Budget Status", IsGlobal = true }, Array.Empty<string>()));
    }

    [Fact]
    public void An_enum_never_takes_the_name_of_a_class()
    {
        var (entity, attribute) = Column("Lead", "Need", AttributeTypes.Picklist);
        var name = Naming().GetNameForOptionSet(entity, attribute, new OptionSetDefinition { Name = "need", IsGlobal = true }, new[] { "Need" });
        Assert.Equal("Need_Enum", name);
    }

    [Fact]
    public void A_column_named_like_its_table_gets_a_suffix()
    {
        var (entity, attribute) = Column("sho_project", "sho_project", AttributeTypes.String);
        var naming = Naming();
        Assert.Equal("Sho_Project", naming.GetNameForEntity(entity));
        Assert.Equal("Sho_Project__Member", naming.GetNameForAttribute(entity, attribute));
    }

    [Fact]
    public void Members_of_the_entity_base_class_are_not_hidden()
    {
        var (entity, attribute) = Column("sho_thing", "Attributes", AttributeTypes.String);
        Assert.Equal("Attributes1", Naming().GetNameForAttribute(entity, attribute));
    }

    [Fact]
    public void Explicit_names_win()
    {
        var (entity, attribute) = Column("Account", "AccountNumber", AttributeTypes.String);
        var naming = Naming(s =>
        {
            s.EntityClassNameOverrides["account"] = "Customer";
            s.EntityAttributeSpecifiedNames["account"] = new List<string> { "ACCOUNTNUMBER" };
        });
        Assert.Equal("Customer", naming.GetNameForEntity(entity));
        Assert.Equal("ACCOUNTNUMBER", naming.GetNameForAttribute(entity, attribute));
    }

    [Fact]
    public void Logical_names_can_be_used_as_they_are()
    {
        var (entity, attribute) = Column("Account", "AccountNumber", AttributeTypes.String);
        Assert.Equal("accountnumber", Naming(s => s.UseLogicalNames = true).GetNameForAttribute(entity, attribute));
    }

    [Fact]
    public void Business_process_flow_tables_are_named_after_their_display_name()
    {
        var entity = new EntityDefinition
        {
            LogicalName = "sho_bpf_0123456789abcdef0123456789abcdef",
            SchemaName = "sho_bpf_0123456789abcdef0123456789abcdef",
            DisplayName = "Project Approval",
        };
        Assert.Equal("Sho_Bpf_ProjectApproval", Naming().GetNameForEntity(entity));
    }

    [Fact]
    public void Reflexive_relationships_carry_their_role()
    {
        var entity = new EntityDefinition { LogicalName = "account", SchemaName = "Account" };
        var relationship = new RelationshipDefinition { SchemaName = "account_parent_account", Entity = "account" };
        var naming = Naming();
        Assert.Equal("ReferencingAccount_Parent_Account", naming.GetNameForRelationship(entity, relationship, "Referencing", Array.Empty<string>(), null));
        Assert.Equal("ReferencedAccount_Parent_Account", naming.GetNameForRelationship(entity, relationship, "Referenced", Array.Empty<string>(), null));
    }
}
