# Configuration

Everything is configured in `earlybound.yaml`, next to the project file. `earlybound init`
creates it with comments.

```yaml
environment: https://yourorg.crm4.dynamics.com

pull:
  # read by the earlybound tool

generator:
  # read by the source generator when the project builds
```

Unknown keys in `pull` and `generator` are reported as warnings (`SOEB105`), so a typo does not
silently do nothing.

## `environment`

The URL of the Dataverse environment metadata is pulled from. Any URL of the environment works;
only the host is used. `--environment` on the command line overrides it.

## `pull`

| Setting | Default | Meaning |
| --- | --- | --- |
| `entities` | none | Logical names of the tables to pull. `*` matches any run of characters: `sho_*`. Matching ignores case. |
| `solutions` | none | Unique names of solutions. Every table in them is pulled as well. |
| `exclude` | none | Tables to leave out, with the same pattern rules. Applied last. |
| `output` | `Metadata` | Folder for the metadata files, relative to `earlybound.yaml`. If you change it, set the `EarlyBoundMetadataPath` MSBuild property to the same folder. |
| `layout` | `perEntity` | `perEntity` writes `entities/<table>.yaml` and `optionsets/<choice>.yaml`. `singleFile` writes `metadata.yaml`. |
| `relationships` | `included` | `included` keeps relationships between pulled tables. `all` keeps every relationship. `none` keeps none. Only relationships whose both ends are pulled produce properties, so `all` mainly helps autocomplete. |
| `languageCode` | the environment's base language | LCID of the labels and descriptions to keep, for example `1033`. A label missing in that language falls back to any language it has. |

When a pulled table has party-list columns (`to`, `from`, …), `activityparty` is pulled too, so
those columns can be typed. Put it under `exclude` to prevent that.

## `generator`

The settings have the names and defaults of the Early Bound Generator's settings where both
tools have the feature, so a `builderSettings.json` can be carried over key by key.

### Output

| Setting | Default | Meaning |
| --- | --- | --- |
| `namespace` | the project's root namespace | Namespace of the generated types. The `EarlyBoundNamespace` MSBuild property overrides it. |
| `serviceContextName` | `DataverseContext` | Name of the generated `OrganizationServiceContext`. Empty generates none. |
| `generateTypesAsInternal` | `false` | Make the generated types `internal`. |
| `generateOnlyUsedAttributes` | `true` | Generate members only for the columns and relationships the project's code uses. See [Only used members](#only-used-members). The `EarlyBoundGenerateOnlyUsedAttributes` MSBuild property overrides it. |
| `alwaysGenerate` | none | Members to generate whether they are used or not: `table.column` or `table.relationship`, by logical and schema name, with `*` as a wildcard. A table name alone means all of its members. |
| `generateProxyTypesAssemblyAttribute` | `true` | Emit `[assembly: ProxyTypesAssembly]`. Skipped automatically when the project declares it. |
| `suppressGeneratedCodeAttribute` | `true` | Set to `false` to put `[GeneratedCode]` on the types. |
| `addDebuggerNonUserCode` | `true` | Put `[DebuggerNonUserCode]` on accessors, so the debugger steps over them. |

### Classes

| Setting | Default | Meaning |
| --- | --- | --- |
| `generateAttributeNameConsts` | `true` | Generate the nested `Fields` class with a constant per column. |
| `attributeConstsClassName` | `Fields` | Name of that class. |
| `generateConstructorsSansLogicalName` | `true` | Generate constructors taking an id, a key name and value, or a key collection. |
| `generateAnonymousTypeConstructor` | `true` | Generate the constructor that takes an anonymous type, for LINQ projections. |
| `generateEntityRelationships` | `true` | Generate relationship properties. |
| `emitEntityETC` | `false` | Generate `EntityTypeCode`. Off by default because the code of a custom table differs between environments. |
| `emitVirtualAttributes` | `true` | Generate the read-only `…Name` properties that return the display text of a lookup or choice. |
| `makeReadonlyFieldsEditable` | `false` | Give `createdby`, `createdon`, `modifiedby`, `modifiedon`, `owningbusinessunit`, `owningteam` and `owninguser` setters. |
| `makeAllFieldsEditable` | `false` | Give every property a setter. |
| `obsoleteDeprecated` | `true` | Mark deprecated columns `[Obsolete]`. |
| `obsoleteTokens` | `["*(Deprecated)*"]` | Display-name patterns that mark a column as deprecated. |

### Choices

| Setting | Default | Meaning |
| --- | --- | --- |
| `replaceEnumPropertiesWithOptionSet` | `false` | Type choice properties as `OptionSetValue` instead of the enum. |
| `generateOptionSetMetadataAttribute` | `true` | Generate `OptionSetMetadataAttribute` and the `GetMetadata()` extension. |
| `addOptionSetMetadataAttribute` | `true` | Put that attribute on every enum member. |
| `localOptionSetFormat` | `{0}_{1}` | Name of the enum of a column's own choice. `{0}` is the class name, `{1}` the property name. |
| `useCrmSvcUtilStateEnumNamingConvention` | `false` | Name state enums `AccountState` instead of `Account_StateCode`. |
| `optionSetNames` | none | Enum names of your choice, by generated name: `{ account_industrycode: Industry }`. |
| `adjustCasingForEnumOptions` | `true` | Title-case each word of a label: `IN PROGRESS` becomes `InProgress`. |
| `optionNameOverrides` | the Early Bound Generator's list | Fixes applied to member names: `{ linkedin: LinkedIn }`. Replaces the default list. |
| `labelTextReplacement` | `{ "$": "", "(": "" }` | Text replaced in a label before its member name is derived. |
| `invalidCSharpNamePrefix` | `_` | Put before a member name that would start with a digit: `_1stReview`. |

### Names

| Setting | Default | Meaning |
| --- | --- | --- |
| `camelCaseClassNames` | `true` | PascalCase class names by finding words in them. |
| `camelCaseMemberNames` | `true` | The same for properties and relationships. |
| `camelCaseOptionSetNames` | `false` | The same for enum members. |
| `camelCaseCustomWords` | none | Words to recognise in addition to the built-in English list. Add your publisher prefix. |
| `tokenCapitalizationOverrides` | the Early Bound Generator's list | Tokens always written exactly as listed, such as `ExchangeRate`. Replaces the default list. |
| `useLogicalNames` | `false` | Name properties after logical names instead of schema names. |
| `entityClassNameOverrides` | none | Class name per table: `{ account: Customer }`. Used exactly as written. |
| `entityAttributeSpecifiedNames` | none | Property names per table, matched to columns ignoring case: `{ account: [AccountID] }`. |
| `useDisplayNameForBpfName` | `true` | Name business process flow tables after their display name instead of their GUID. |

### Only used members

With `generateOnlyUsedAttributes` on, which is the default, a class gets members only for what
the project's code uses. A member is generated when its C# name appears as an identifier
anywhere in the project's source: `account.CreditLimit`, `account?.CreditLimit`,
`new Account { CreditLimit = … }`, `Account.Fields.CreditLimit`, `nameof(Account.CreditLimit)`,
a property pattern, a bare `CreditLimit` inside a partial class, a `cref` in a documentation
comment. Comments, strings and code disabled with `#if` do not count.

It applies to column properties, relationship properties and the constants in `Fields`. Always
generated: the class with its constructors, `EntityLogicalName` and the other name constants,
and the primary key with `Id`. A choice's enum is generated when a generated property is typed
as it, or when its name is written, as in `(int)Account_StatusCode.Active`.

The decision is made on names because the generator runs before the members exist, so there is
nothing yet for `account.CreditLimit` to resolve to. Two consequences:

- A name used on one table is generated on every table that has a member of that name. Using
  `contact.StateCode` also generates `StateCode` on `account`. It never works the other way:
  nothing the compiler can see in the project is left out.
- Only this project's own source is seen. Code in other projects, code written by other source
  generators (Razor components, for instance) and access through reflection are not.

A test project is taken care of: see [Test projects](#test-projects). For other projects that
use the classes directly, generate everything, or keep what is needed:

```yaml
generator:
  # Everything, as before:
  generateOnlyUsedAttributes: false

  # Or: only what is used, plus these.
  alwaysGenerate:
    - contact                           # every member of a table
    - account.creditlimit               # one column, by logical name
    - "*.statecode"                     # a column on every table that has it
    - account.account_primary_contact   # a relationship, by schema name
```

To trim only what is deployed, set the MSBuild property per configuration:

```xml
<PropertyGroup Condition="'$(Configuration)' != 'Release'">
  <EarlyBoundGenerateOnlyUsedAttributes>false</EarlyBoundGenerateOnlyUsedAttributes>
</PropertyGroup>
```

In the editor, the members that are not generated yet are offered by
[autocomplete](autocomplete.md#members-that-are-not-generated-yet), and using one generates it
as you type. If your IDE is set to run source generators only on save or build (Visual Studio's
"Balanced" setting), a member used for the first time shows an error until then.

`earlybound generate` writes files without looking at any code, so it always writes everything.

### Test projects

A test project references the plug-in project and so already has its classes, with the members
the plug-ins use. Tests usually need more. Members cannot be added to a class from outside its
assembly, and they are not added to the plug-in assembly for the tests' sake; instead the test
project generates classes that derive from the plug-in project's.

Add the package to the test project and let it read the plug-in project's files:

```xml
<PropertyGroup>
  <EarlyBoundMetadataRoot>..\Contoso.Plugins</EarlyBoundMetadataRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="..\Contoso.Plugins\Contoso.Plugins.csproj" />
  <PackageReference Include="ShkoOnline.EarlyBound" Version="…" PrivateAssets="all" />
</ItemGroup>
```

Then:

- A table class that a referenced assembly provides is not generated again. Neither are the
  enums, the service context and the helper types it provides.
- For such a class `X`, writing `XTestEntity` generates `partial class XTestEntity : X` in the
  test project. It gets a property for every column and relationship the test project uses
  that `X` does not have, and a nested `Fields : X.Fields` with the constants `X.Fields` lacks.
- An enum that only a test entity's property needs is generated in the test project.
- A table the referenced assembly has no class for is generated in the test project as usual.

```csharp
var customer = new AccountTestEntity { Name = "Contoso", CreditLimit = new Money(0m) };
Account account = customer;                              // it is an Account
var column = AccountTestEntity.Fields.CreditLimit;       // and has its constants, and more
var copy = row.ToEntity<AccountTestEntity>();
```

`XTestEntity` has the same table, the same constructors and the same behaviour as `X`. The
members a test uses directly on `X` have to exist in `X`: `account.CreditLimit` on a plain
`Account` is an error when no plug-in uses `CreditLimit`. Declare the variable as the test
entity, or convert with `ToEntity<AccountTestEntity>()`.

With `generateOnlyUsedAttributes: false` in the test project, every referenced class that lacks
members gets its test entity, holding all of them, without having to be named.

The same happens in any project that has the package and references an assembly with generated
classes, test project or not.

#### Publisher prefixes

A prefix such as `sho` is not an English word, so it is spelled out letter by letter:
`sho_duedate` becomes `SHo_DueDate`. List it under `camelCaseCustomWords` to get `Sho_DueDate`.
`earlybound pull` checks for this and prints the line to add.

## MSBuild properties

Set these in the project file.

| Property | Default | Meaning |
| --- | --- | --- |
| `EarlyBoundMetadataRoot` | the project folder | Folder that holds `earlybound.yaml` and the metadata folder. Point a test project at its plug-in project with `..\Contoso.Plugins`. |
| `EarlyBoundMetadataPath` | `Metadata` | Folder whose `*.yaml` and `*.yml` files are metadata, relative to the root. |
| `EarlyBoundNamespace` | none | Namespace of the generated types. Wins over `earlybound.yaml`. |
| `EarlyBoundGenerateOnlyUsedAttributes` | none | `true` or `false`: whether only used members are generated. Wins over `generateOnlyUsedAttributes` in `earlybound.yaml`. |
| `EnableDefaultEarlyBoundItems` | `true` | Set to `false` to stop `earlybound.yaml` and the metadata folder being picked up automatically. |

To use another project's files, set `EarlyBoundMetadataRoot` to its folder. To pick files one
by one, for example from a folder shared by several projects:

```xml
<PropertyGroup>
  <EnableDefaultEarlyBoundItems>false</EnableDefaultEarlyBoundItems>
</PropertyGroup>
<ItemGroup>
  <EarlyBoundMetadata Include="..\Shared\earlybound.yaml" />
  <EarlyBoundMetadata Include="..\Shared\Metadata\**\*.yaml" />
</ItemGroup>
```

Only one file may have a `generator` section.

A project generates the classes that its references do not already provide. Two projects that
share metadata but do not reference each other each get their own copy. Where one references
the other, the classes live in the referenced one; the referencing project needs the package
only to get [test entities](#test-projects), and otherwise the referenced project has to
generate what it uses, with `alwaysGenerate` or `generateOnlyUsedAttributes: false`.
