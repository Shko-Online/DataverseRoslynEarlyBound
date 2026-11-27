using System;
using Contoso.Model;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Contoso.Plugins.Tests
{
    /// <summary>
    /// The classes used here are the plug-in project's own. A test often needs more of a table
    /// than the plug-ins touch, to set a row up or to check it afterwards. Those members are not in
    /// Contoso.Plugins, and are not put there for the sake of the tests: writing
    /// <c>AccountTestEntity</c> generates, in this project, a class derived from <c>Account</c>
    /// that has them.
    /// </summary>
    public class ProjectDefaultsTests
    {
        private static readonly DateTime Today = new DateTime(2026, 1, 1);

        [Fact]
        public void A_new_project_is_due_in_thirty_days()
        {
            // Sho_Name and Sho_EstimatedHours are columns no plug-in uses.
            var project = new Sho_ProjectTestEntity { Sho_Name = "Migration", Sho_EstimatedHours = 120m };

            // A test entity is the plug-in's class, with more on it. It goes wherever that class goes.
            ProjectDueDatePlugin.ApplyDefaults(project, null, Today);

            Assert.Equal(new DateTime(2026, 1, 31), project.Sho_DueDate);
            Assert.Equal("Migration", project.Sho_Name);
            Assert.Equal(120m, project.Sho_EstimatedHours);
        }

        [Fact]
        public void A_project_for_a_customer_without_a_budget_is_put_on_hold()
        {
            var project = new Sho_ProjectTestEntity { Sho_Name = "Migration" };
            var customer = new AccountTestEntity
            {
                Name = "Contoso",                                          // Account has this one
                Sho_BudgetStatus = Sho_BudgetStatus.NoCommittedBudget,     // and this one
                CreditLimit = new Money(0m),                               // the test entity adds these
                AccountCategoryCode = Account_AccountCategoryCode.Standard,
            };

            ProjectDueDatePlugin.ApplyDefaults(project, customer, Today);

            Assert.Equal(Sho_Project_StatusCode.OnHold_Active, project.StatusCode);
        }

        [Fact]
        public void A_test_entity_is_stored_like_any_row_of_its_table()
        {
            var customer = new AccountTestEntity { CreditLimit = new Money(500m) };

            Entity row = customer;
            Assert.Equal(Account.EntityLogicalName, row.LogicalName);
            Assert.Equal(500m, ((Money)row[AccountTestEntity.Fields.CreditLimit]).Value);
            Assert.Equal("name", AccountTestEntity.Fields.Name);
        }
    }
}
