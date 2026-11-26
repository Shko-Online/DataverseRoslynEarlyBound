# Shko Online's Early Bound

Early-bound Dataverse classes that are generated when you build, not checked in, plus
IntelliSense for the late-bound code you still write.

You keep the metadata of the tables you use as YAML files in the repository. This package turns
them into typed classes inside the compiler, in Visual Studio, VS Code, Rider and on the build
server.

```csharp
var account = service.Retrieve(Account.EntityLogicalName, id, new ColumnSet(Account.Fields.Name))
                     .ToEntity<Account>();

if (account.AccountCategoryCode == Account_AccountCategoryCode.PreferredCustomer)
{
    account.CreditLimit = new Money(50000m);
}
```

## Get started

```console
dotnet new tool-manifest
dotnet tool install ShkoOnline.EarlyBound.Tool
dotnet add package ShkoOnline.EarlyBound

dotnet earlybound init --environment https://yourorg.crm4.dynamics.com --entities account contact
dotnet earlybound pull
dotnet build
```

The project also needs the Dataverse SDK: `Microsoft.CrmSdk.CoreAssemblies` for plug-ins or
`Microsoft.PowerPlatform.Dataverse.Client` for modern .NET.

## What you get

- A class per table and an enum per choice, named and shaped like the output of the Early Bound
  Generator for XrmToolBox, so existing code keeps compiling.
- Constants for every name: `Account.EntityLogicalName`, `Account.Fields.Name`.
- Relationship properties and a LINQ service context.
- Code that compiles as C# 7.3, for .NET Framework plug-in projects.
- Autocomplete for table and column names in SDK calls (`new Entity("`, `entity["`,
  `new ColumnSet("`), inside FetchXML strings, and for choice values, straight from the same
  metadata. Nothing to install in the IDE.

## Requirements

.NET SDK 8 or later, or Visual Studio 2022 17.8 or later. `PackageReference`-style projects.

Documentation, configuration and source: <https://github.com/Shko-Online/DataverseRoslynEarlyBound>

Apache-2.0. Copyright 2026 Shko Online LLC.
