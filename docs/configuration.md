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

#### Publisher prefixes

A prefix such as `sho` is not an English word, so it is spelled out letter by letter:
`sho_duedate` becomes `SHo_DueDate`. List it under `camelCaseCustomWords` to get `Sho_DueDate`.
`earlybound pull` checks for this and prints the line to add.

## MSBuild properties

Set these in the project file.

| Property | Default | Meaning |
| --- | --- | --- |
| `EarlyBoundMetadataPath` | `Metadata` | Folder whose `*.yaml` and `*.yml` files are metadata. |
| `EarlyBoundNamespace` | none | Namespace of the generated types. Wins over `earlybound.yaml`. |
| `EnableDefaultEarlyBoundItems` | `true` | Set to `false` to stop `earlybound.yaml` and the metadata folder being picked up automatically. |

To use files from somewhere else, for example a folder shared by several projects:

```xml
<PropertyGroup>
  <EnableDefaultEarlyBoundItems>false</EnableDefaultEarlyBoundItems>
</PropertyGroup>
<ItemGroup>
  <EarlyBoundMetadata Include="..\Shared\earlybound.yaml" />
  <EarlyBoundMetadata Include="..\Shared\Metadata\**\*.yaml" />
</ItemGroup>
```

Only one file may have a `generator` section. Each project that uses the package gets its own
copy of the classes, so in a solution where one project references another, put the package in
the project the others reference.
