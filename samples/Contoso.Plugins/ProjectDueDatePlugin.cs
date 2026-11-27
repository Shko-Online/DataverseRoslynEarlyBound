using System;
using System.Linq;
using Contoso.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Contoso.Plugins
{
    /// <summary>
    /// Runs when a project is created: gives it a due date, and puts it on hold when the
    /// customer has no committed budget.
    /// </summary>
    /// <remarks>
    /// Every type from <c>Contoso.Model</c> is generated at build time from the YAML files in
    /// the Metadata folder. Nothing generated is checked in.
    /// </remarks>
    public sealed class ProjectDueDatePlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            // Early bound: the Target as a typed class.
            var target = (Entity)context.InputParameters["Target"];
            var project = target.ToEntity<Sho_Project>();

            // Constants instead of strings.
            Account customer = null;
            if (project.Sho_AccountId != null)
            {
                customer = service
                    .Retrieve(Account.EntityLogicalName, project.Sho_AccountId.Id, new ColumnSet(Account.Fields.Name, Account.Fields.Sho_BudgetStatus))
                    .ToEntity<Account>();
            }

            ApplyDefaults(project, customer, DateTime.UtcNow.Date);

            // Hand the result to the pipeline as plain attributes.
            target[Sho_Project.Fields.Sho_DueDate] = project.Sho_DueDate;
            if (project.StatusCode != null)
            {
                target[Sho_Project.Fields.StatusCode] = new OptionSetValue((int)project.StatusCode.Value);
            }
        }

        /// <summary>
        /// The rule itself, apart from the pipeline so that it can be tested with plain objects:
        /// see Contoso.Plugins.Tests.
        /// </summary>
        public static void ApplyDefaults(Sho_Project project, Account customer, DateTime today)
        {
            if (project.Sho_DueDate == null)
            {
                project.Sho_DueDate = today.AddDays(30);
            }

            // An enum instead of a magic number.
            if (customer != null && customer.Sho_BudgetStatus == Sho_BudgetStatus.NoCommittedBudget)
            {
                project.StatusCode = Sho_Project_StatusCode.OnHold_Active;
            }
        }

        /// <summary>The same kind of work written late bound, which is where autocomplete helps.</summary>
        public static int CountOpenProjects(IOrganizationService service, Guid accountId)
        {
            // Inside each string, IntelliSense offers the tables and columns from the metadata;
            // after the operator, the options of "statecode".
            var query = new QueryExpression("sho_project")
            {
                ColumnSet = new ColumnSet("sho_name", "sho_duedate"),
            };
            query.Criteria.AddCondition("sho_accountid", ConditionOperator.Equal, accountId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, (int)Sho_Project_StateCode.Active);

            return service.RetrieveMultiple(query).Entities.Count(row => row.Contains("sho_duedate"));
        }

        /// <summary>FetchXML gets the same help: names, operators and option values.</summary>
        public const string OverdueProjects = @"
            <fetch>
              <entity name='sho_project'>
                <attribute name='sho_name' />
                <attribute name='sho_duedate' />
                <filter type='and'>
                  <condition attribute='statecode' operator='eq' value='0' />
                  <condition attribute='sho_duedate' operator='olderthan-x-days' value='1' />
                </filter>
                <link-entity name='account' from='accountid' to='sho_accountid' alias='customer'>
                  <attribute name='name' />
                </link-entity>
              </entity>
            </fetch>";
    }
}
