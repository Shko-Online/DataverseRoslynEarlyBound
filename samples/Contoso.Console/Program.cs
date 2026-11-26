using Contoso.Model;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

// Usage: Contoso.Console "AuthType=OAuth;Url=https://yourorg.crm.dynamics.com;..."
if (args.Length == 0)
{
    Console.Error.WriteLine("Pass a Dataverse connection string as the first argument.");
    return 1;
}

using var client = new ServiceClient(args[0]);
using var context = new DataverseContext(client);

// LINQ over the generated service context, typed end to end.
var accounts =
    from account in context.AccountSet
    where account.StateCode == Account_StateCode.Active
    orderby account.Name
    select new { account.Name, account.AccountCategoryCode, account.PrimaryContactId };

foreach (var account in accounts.Take(10))
{
    // GetMetadata() reads the label that was captured from Dataverse when the metadata was pulled.
    var category = account.AccountCategoryCode?.GetMetadata().Name ?? "(none)";
    Console.WriteLine($"{account.Name}: {category}, primary contact {account.PrimaryContactId?.Name ?? "(none)"}");
}

// Creating a row with an object initializer, including a multi-select choice.
var created = new Account
{
    Name = "Contoso",
    AccountCategoryCode = Account_AccountCategoryCode.PreferredCustomer,
    Sho_Tags = new[] { Sho_Account_Sho_Tags.Vip, Sho_Account_Sho_Tags.KeyAccount },
    CreditLimit = new Money(25000m),
};
created.Id = client.Create(created);
Console.WriteLine($"Created {Account.EntityLogicalName} {created.Id}");
return 0;
