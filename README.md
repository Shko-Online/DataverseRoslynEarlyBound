<p align="center">
  <img src="assets/icon.svg" width="96" height="96" alt="Shko Online's Early Bound">
</p>

# Shko Online's Early Bound

Early-bound Dataverse classes that are **generated when you build**, not checked in.

You download the metadata of the tables you use into YAML files and commit those. A Roslyn
source generator turns them into typed classes inside the compiler, in Visual Studio, VS Code and
on the build server alike. The same metadata drives IntelliSense for the late-bound code you
still write: table and column names in strings, FetchXML, and choice values.

```csharp
var account = service.Retrieve(Account.EntityLogicalName, id, new ColumnSet(Account.Fields.Name))
                     .ToEntity<Account>();

if (account.AccountCategoryCode == Account_AccountCategoryCode.PreferredCustomer)
{
    account.CreditLimit = new Money(50000m);
}
```

| Package | What it is |
| --- | --- |
| `ShkoOnline.EarlyBound` | The source generator and the autocomplete provider. Add it to every project that needs the classes. |
| `ShkoOnline.EarlyBound.Tool` | The `earlybound` command that downloads metadata from an environment. |

## Get started

In the folder of the project that needs early-bound classes:

```console
dotnet new tool-manifest                              # once per repository, if there is none yet
dotnet tool install ShkoOnline.EarlyBound.Tool
dotnet add package ShkoOnline.EarlyBound

dotnet earlybound init --environment https://yourorg.crm4.dynamics.com --entities account contact "sho_*"
dotnet earlybound pull
dotnet build
```

`init` writes `earlybound.yaml`, which says what to pull and how to generate. `pull` opens a
browser to sign in and writes one YAML file per table into `Metadata/`. From then on the classes
exist: there is no generated C# to review, merge or forget to regenerate. When the tables change
in Dataverse, run `pull` again and commit the YAML diff.

The project needs a reference to the Dataverse SDK, as any early-bound code does:
`Microsoft.CrmSdk.CoreAssemblies` for plug-ins, `Microsoft.PowerPlatform.Dataverse.Client` for
modern .NET.

## What is generated

For every table, a class deriving from `Microsoft.Xrm.Sdk.Entity`; for every choice, an enum.
The shape and the names follow the [Early Bound Generator](https://github.com/daryllabar/DLaB.Xrm.XrmToolBoxTools)
for XrmToolBox, so a project that used it can switch without touching its code.

- **Typed columns.** `string`, `int?`, `DateTime?`, `Money`, `EntityReference`, and enums for
  choices: `Account_AccountCategoryCode? AccountCategoryCode`, and an `IEnumerable` of the enum
  for multi-select. Read-only columns have no setter. Deprecated ones are marked `[Obsolete]`.
- **Names as constants.** `Account.EntityLogicalName`, `Account.Fields.Name`,
  `Account.PrimaryIdAttribute`, `Account.EntitySetName`.
- **Relationships** between the tables you pulled: `account.Account_Primary_Contact`,
  `account.Contact_Customer_Accounts`.
- **A LINQ context**, `DataverseContext`, with `AccountSet`, `ContactSet`, …
- **Labels at run time.** `Account_StatusCode.Active.GetMetadata().Name` returns the label that
  was pulled, with its colour and display order.
- **PascalCase for lower-case schema names.** `new_duedate` becomes `New_DueDate`, using the
  Early Bound Generator's word list.

Every generated class is `partial`, so you can add to it in a file of your own. The generated
code compiles as C# 7.3, so it works in .NET Framework plug-in projects.

## Only what you use

A table like `account` has a few hundred columns, and a plug-in touches a dozen of them. So
members are generated only for the columns and relationships your project's code mentions. For
a project that uses a handful of members of `account` and `contact`, that came to 834 generated
lines instead of 11,378, and a 15 KB assembly instead of 124 KB.

Use is decided by name: a member is generated when its name appears in the project's C# code,
as `account.CreditLimit`, `new Account { CreditLimit = … }`, `Account.Fields.CreditLimit`,
`nameof(Account.CreditLimit)` or in any other form. The class itself, its name constants and its
primary key are always generated. An enum is generated when a generated property is typed as
it, or when you write its name.

IntelliSense still shows the whole table. After `account.`, in an object initializer and under
`Account.Fields.`, the autocomplete provider lists the members that are not generated yet next
to the ones that are. Pick one and it is generated, in the editor, without a build.

### In a test project

Tests need more of a table than the plug-ins touch: to set a row up, or to check it afterwards.
Those members are not added to the plug-in assembly for the tests' sake. Give the test project
the package and point it at the plug-in project's metadata:

```xml
<PropertyGroup>
  <EarlyBoundMetadataRoot>..\Contoso.Plugins</EarlyBoundMetadataRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\Contoso.Plugins\Contoso.Plugins.csproj" />
  <PackageReference Include="ShkoOnline.EarlyBound" Version="…" PrivateAssets="all" />
</ItemGroup>
```

The test project does not generate `Account` again; it uses the plug-in project's. Write
`AccountTestEntity` and a class deriving from `Account` is generated in the test project, with a
member for everything your tests use that `Account` lacks:

```csharp
var customer = new AccountTestEntity
{
    Name = "Contoso",                 // Account has this, because a plug-in uses it
    CreditLimit = new Money(0m),      // AccountTestEntity adds this, because this test uses it
};

ProjectDueDatePlugin.ApplyDefaults(project, customer, today);   // it is an Account
```

An `AccountTestEntity` is an `Account`, so it goes wherever plug-in code expects one. It works
in any C# version. [samples/Contoso.Plugins.Tests](samples/Contoso.Plugins.Tests) shows it.

### When to generate everything

The generator only sees the project it runs in. Turn this off, or name what to keep, when:

- another project uses the generated classes directly, such as an application that references
  a shared model library;
- members are reached through reflection, or from code that another source generator writes,
  such as Razor components.

```yaml
generator:
  generateOnlyUsedAttributes: false     # generate everything
  alwaysGenerate: [contact, "*.statecode", account.creditlimit]   # or keep these regardless
```

See [docs/configuration.md](docs/configuration.md#only-used-members).

## Autocomplete in late-bound code

Wherever the SDK takes a name as a string, IntelliSense offers the names from your metadata, with
display names and descriptions.

| You type | You are offered |
| --- | --- |
| `new Entity("`, `service.Retrieve("`, `new QueryExpression("`, `entity.LogicalName == "` | Tables |
| `entity["`, `entity.GetAttributeValue<string>("`, `new ColumnSet("`, `AddCondition("`, `AddOrder("` | Columns of the table that code is about |
| `new Relationship("` | Relationships |
| `account.`, `new Account { `, `Account.Fields.` | The members of the class that are not generated yet, because nothing uses them |
| `new ` in a test project | `AccountTestEntity` and the other classes that add members to the plug-in project's |
| `<entity name='`, `<attribute name='`, `<condition attribute='`, `<link-entity from='` in a FetchXML string | Tables and columns, by where you are in the query |
| `operator='` in FetchXML | The FetchXML operators |
| `new OptionSetValue(`, `AddCondition("statuscode", ConditionOperator.Equal, `, `.Value == `, `value='` in FetchXML | The options of that choice column |

The table is worked out from the code around the caret: a `new Entity("contact")` assigned to
the variable, the `QueryExpression` a `ColumnSet` belongs to, a typed `Contact`, the query behind
a `foreach` over `RetrieveMultiple(...)`. When it cannot be worked out, every column is offered,
with the tables it exists on.

An option is inserted as `(int)Account_StatusCode.Inactive` where the generated enum is
available, and as the number otherwise.

The provider ships inside the NuGet package and is loaded by the IDE from the project, so there
is no extension to install. More in [docs/autocomplete.md](docs/autocomplete.md).

## Metadata files

```
MyProject/
├─ earlybound.yaml            what to pull, how to generate
└─ Metadata/
   ├─ entities/
   │  ├─ account.yaml
   │  └─ contact.yaml
   └─ optionsets/
      └─ sho_budgetstatus.yaml      global choices
```

```yaml
entities:
  - logicalName: account
    schemaName: Account
    displayName: Account
    primaryIdAttribute: accountid
    primaryNameAttribute: name
    attributes:
      - logicalName: accountcategorycode
        schemaName: AccountCategoryCode
        type: Picklist
        displayName: Category
        optionSet:
          name: account_accountcategorycode
          options:
            - {value: 1, label: Preferred Customer}
            - {value: 2, label: Standard}
```

One file per table keeps diffs readable and merges quiet; `layout: singleFile` puts everything
in one file instead. Files are written in a stable order, so pulling an unchanged environment
changes nothing. Several projects can share one folder. The format is described in
[docs/metadata-format.md](docs/metadata-format.md).

## Configuration

`earlybound.yaml` has a `pull` section for the tool and a `generator` section for the build:

```yaml
environment: https://yourorg.crm4.dynamics.com

pull:
  entities: [account, contact, "sho_*"]   # * is a wildcard
  solutions: [ContosoCore]                # every table of these solutions too

generator:
  namespace: Contoso.Model
  serviceContextName: DataverseContext
  camelCaseCustomWords: [sho]             # your publisher prefix: Sho_DueDate instead of SHo_DueDate
```

The `generator` settings carry the names and defaults of the Early Bound Generator's settings
(`camelCaseClassNames`, `localOptionSetFormat`, `entityClassNameOverrides`, …). All of them are
listed in [docs/configuration.md](docs/configuration.md).

## Coming from another generator

**Early Bound Generator.** Class, property, enum and option names are derived by the same rules
and the same word list, and its default settings are the defaults here. Copy your non-default
settings into the `generator` section. Not generated: request and response classes for messages
and custom APIs, and `INotifyPropertyChanged`. Unlike that tool, members nothing uses are left
out by default; see [Only what you use](#only-what-you-use).

**`pac modelbuilder` and CrmSvcUtil.** Their names differ where the Early Bound Generator's do:
enums are `Account_AccountCategoryCode` rather than `account_accountcategorycode`, and
lower-case schema names are PascalCased. Set `camelCaseClassNames` and `camelCaseMemberNames` to
`false` to keep schema names as they are.

Whatever you come from, the compiler tells you what differs: delete the old generated files,
build, and fix what no longer resolves.

## Requirements

- .NET SDK 8 or later, or Visual Studio 2022 17.8 or later, to build. The target framework of
  your project does not matter: .NET Framework 4.6.2 plug-ins and modern .NET both work.
- `PackageReference`-style NuGet references. `packages.config` is not supported.
- The `earlybound` tool needs the .NET 8 runtime or later.

Verified in this repository: builds with the .NET 10 SDK for `net462` (C# 7.3) and `net10.0`,
and generation and autocomplete in the language server of the VS Code C# extension. Visual Studio
and Rider (2025.2 or later) load source generators and completion providers from NuGet packages
through the same Roslyn mechanism.

One thing to know: IDEs load completion providers from packages in the background, so the very
first completion after opening a solution can come up without these entries. Invoke it again.

## Documentation

- [Configuration](docs/configuration.md): every setting of `earlybound.yaml` and the MSBuild properties.
- [The `earlybound` command](docs/cli.md): commands, sign-in methods, use in pipelines.
- [Metadata format](docs/metadata-format.md): the YAML files.
- [Autocomplete](docs/autocomplete.md): what is completed where.
- [Diagnostics](docs/diagnostics.md): the `SOEB` warnings and errors.

## Building from source

```console
dotnet build
dotnet test --project tests/ShkoOnline.EarlyBound.Tests
dotnet pack -c Release             # packages land in artifacts/package/release
dotnet build samples/Samples.slnx  # the samples consume those packages
```

| Folder | Contents |
| --- | --- |
| `src/ShkoOnline.EarlyBound.Core` | Metadata model, YAML reader and writer, naming, C# emitter. Shared by everything else. |
| `src/ShkoOnline.EarlyBound.Generator` | The incremental source generator. |
| `src/ShkoOnline.EarlyBound.Completion` | The completion provider. |
| `src/ShkoOnline.EarlyBound` | The NuGet package that carries the three above, and its MSBuild files. |
| `src/ShkoOnline.EarlyBound.Tool` | The `earlybound` command. |
| `samples` | A plug-in project, its test project and a console app that use the packages. |

## Licence

[Apache License 2.0](LICENSE). Copyright 2026 [Shko Online LLC](https://shko.online/).

The naming rules and the word list come from the Early Bound Generator by Daryl LaBar (MIT);
see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
