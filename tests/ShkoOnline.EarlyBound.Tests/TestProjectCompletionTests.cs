using ShkoOnline.EarlyBound.Tests.Support;

namespace ShkoOnline.EarlyBound.Tests;

/// <summary>
/// Autocomplete in a test project: the table classes come from the referenced plug-in assembly,
/// and what they lack lives in derived classes named <c>…TestEntity</c>. $$ marks the caret.
/// </summary>
public class TestProjectCompletionTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using Microsoft.Xrm.Sdk;
        using Contoso.Model;

        class Tests
        {
            void Test()
            {

        """;

    private static Task<CompletionResult> Complete(string statements, char? typed = null) =>
        CompletionHarness.CompleteAsync(Prelude + statements + "\n    }\n}\n", typed, reference: PluginAssembly.Default.Reference);

    [Theory]
    [InlineData("""var account = new $$""")]
    [InlineData("""var account = new $$;""")]
    [InlineData("""var account = new Acc$$();""")]
    [InlineData("""Account account = new $$""")]
    public async Task After_new_the_test_entities_that_do_not_exist_yet_are_offered(string statements)
    {
        var result = await Complete(statements);

        Assert.Equal(
            new[] { "AccountTestEntity", "ActivityPartyTestEntity", "ContactTestEntity", "EmailTestEntity", "Sho_ProjectTestEntity" },
            result.Names);
        Assert.Contains("Contoso.Plugins does not generate", await result.DescribeAsync("AccountTestEntity"));
        Assert.Contains("new AccountTestEntity", await result.CommitAsync("AccountTestEntity"));
    }

    [Fact]
    public async Task A_test_entity_in_use_is_left_to_the_compilers_own_completion()
    {
        var result = await Complete("""var contact = new ContactTestEntity(); var account = new $$;""");

        Assert.DoesNotContain("ContactTestEntity", result.Names);
        Assert.Contains("AccountTestEntity", result.Names);
    }

    [Theory]
    [InlineData("""var account = new AccountTestEntity(); var x = account.$$;""")]
    [InlineData("""var account = new AccountTestEntity(); var x = account.Cred$$;""")]
    [InlineData("""var account = new AccountTestEntity { $$ };""")]
    [InlineData("""var account = new AccountTestEntity { Name = "x", $$ };""")]
    public async Task Members_the_plug_in_class_lacks_are_offered_on_its_test_entity(string statements)
    {
        var result = await Complete(statements);

        Assert.Contains("CreditLimit", result.Names);
        Assert.Contains("AccountCategoryCode", result.Names);
        Assert.Contains("Account_Primary_Contact", result.Names);

        // What Account has in the plug-in assembly is inherited; the compiler's completion shows it.
        Assert.DoesNotContain("Name", result.Names);
        Assert.DoesNotContain("PrimaryContactId", result.Names);
        Assert.DoesNotContain("AccountId", result.Names);
    }

    [Fact]
    public async Task Constants_the_plug_in_class_lacks_are_offered_under_the_test_entitys_fields()
    {
        var result = await Complete("""var column = AccountTestEntity.Fields.$$;""");

        Assert.Contains("CreditLimit", result.Names);
        Assert.DoesNotContain("Name", result.Names);
    }

    [Theory]
    [InlineData("""Account account = null; var x = account.$$;""")] // the plug-in class cannot grow members
    [InlineData("""var account = new Account { $$ };""")]
    [InlineData("""var column = Account.Fields.$$;""")]
    [InlineData("""var list = new List<string>().$$;""")]
    public async Task Nothing_is_offered_on_the_plug_in_class_itself(string statements)
    {
        var result = await Complete(statements);
        Assert.Empty(result.Names);
    }

    [Fact]
    public async Task Names_in_strings_and_choice_values_work_in_a_test_project_too()
    {
        var names = await Complete("""var account = new AccountTestEntity(); var x = account["$$"];""");
        Assert.Contains("creditlimit", names.Names);
        Assert.DoesNotContain("emailaddress1", names.Names);

        // The enum does not exist in the plug-in assembly; writing its name generates it here.
        var values = await Complete("""var account = new AccountTestEntity(); account["accountcategorycode"] = new OptionSetValue($$);""");
        Assert.Contains("(int)Account_AccountCategoryCode.Standard", await values.CommitAsync("Standard"));
    }
}
