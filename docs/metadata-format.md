# Metadata format

Metadata files are YAML. `earlybound pull` writes them; you normally do not edit them, because
the next pull overwrites them. Customising the generated code is done in `earlybound.yaml`.

Knowing the format is still useful: for reading diffs, for writing a file by hand for a table
you cannot pull, and for producing the files with another tool.

## Files

A file has any of these top-level keys:

```yaml
entities:     # a list of tables
optionSets:   # a list of global choices
```

How tables are spread over files does not matter. One file per table, one file for everything,
or anything in between gives the same result. A table or global choice defined twice is
reported (`SOEB102`) and the first definition, in path order, is used.

`earlybound.yaml` is read the same way, so a small project can keep its configuration and its
metadata in that one file.

Which files are read is decided by the project: `earlybound.yaml` and everything under
`Metadata/` by default. See [Configuration](configuration.md#msbuild-properties).

## A table

```yaml
entities:
  - logicalName: account                 # required
    schemaName: Account                  # defaults to the logical name
    displayName: Account
    displayCollectionName: Accounts
    description: Business that represents a customer or potential customer.
    entitySetName: accounts
    logicalCollectionName: accounts
    primaryIdAttribute: accountid
    primaryNameAttribute: name
    primaryImageAttribute: entityimage
    objectTypeCode: 1
    ownershipType: UserOwned
    isCustomEntity: false                # these three default to false and are omitted then
    isActivity: false
    isIntersect: false
    attributes: [...]
    manyToOne: [...]
    oneToMany: [...]
    manyToMany: [...]
```

## A column

```yaml
    attributes:
      - logicalName: name                # required
        schemaName: Name                 # defaults to the logical name
        type: String                     # see the table below; defaults to String
        displayName: Account Name
        description: Type the company or business name.
        requiredLevel: ApplicationRequired   # None (omitted), Recommended, ApplicationRequired, SystemRequired
        isValidForCreate: false          # these three default to true and are omitted then
        isValidForUpdate: false
        isValidForRead: false
        attributeOf: primarycontactid    # set on columns the platform computes from another one
        isDeprecated: true
        maxLength: 160
        format: Text
        minValue: "0"
        maxValue: "1000000000"
        precision: 2
        dateTimeBehavior: UserLocal      # UserLocal, DateOnly or TimeZoneIndependent
        targets: [account, contact]      # lookups: the tables it can point to
```

| `type` | Generated property type |
| --- | --- |
| `String`, `Memo`, `EntityName` | `string` |
| `Integer` | `int?` |
| `BigInt` | `long?` |
| `Decimal` | `decimal?` |
| `Double` | `double?` |
| `Boolean` | `bool?` |
| `DateTime` | `DateTime?` |
| `Money` | `Money` |
| `Uniqueidentifier` | `Guid?` |
| `Lookup`, `Customer`, `Owner` | `EntityReference` |
| `Picklist`, `State`, `Status` | the choice's enum, nullable |
| `MultiSelectPicklist` | `IEnumerable<` the choice's enum `>` |
| `PartyList` | `IEnumerable<ActivityParty>` when `activityparty` is among the tables, else `EntityCollection` |
| `CalendarRules` | `IEnumerable<CalendarRule>` when `calendarrule` is among the tables, else `EntityCollection` |
| `Image` | `byte[]` |
| `File` | `Guid` |
| `ManagedProperty` | `BooleanManagedProperty` |
| anything else | `object` |

The names are Dataverse's `AttributeTypeName` values without the `Type` suffix.

A column gets a property when it is valid for create, update or read. A column with `attributeOf`
gets one only when it is an image, ends in `_url` or `_timestamp`, or is the `…name` companion of
a lookup or choice; that companion returns the display text from `FormattedValues` and is
read-only. A column that is valid for neither create nor update gets no setter.

## Choices

A column either has its own choice:

```yaml
      - logicalName: statuscode
        type: Status
        optionSet:
          name: account_statuscode
          displayName: Status Reason
          type: Status                   # Picklist, State or Status
          options:
            - {value: 1, label: Active, state: 0}
            - {value: 2, label: Inactive, state: 1}
```

or refers to a global one, which is defined once at the top level of any file:

```yaml
      - logicalName: sho_budgetstatus
        type: Picklist
        globalOptionSet: sho_budgetstatus
```

```yaml
optionSets:
  - name: sho_budgetstatus
    displayName: Budget Status
    options:
      - {value: 100000000, label: No Committed Budget}
      - {value: 100000001, label: May Buy, color: "#0070F4", description: Budget is likely.}
```

A column that names a global choice no file defines is reported (`SOEB103`) and typed as
`OptionSetValue`.

| Option key | Meaning |
| --- | --- |
| `value` | The number. Required. |
| `label` | The label, in the language that was pulled. The enum member is named after it. |
| `description`, `color`, `externalValue` | Carried into `OptionSetMetadataAttribute`. |
| `invariantName` | State options: the language-independent name, used for the member name. |
| `state` | Status options: the state value the status belongs to. Tells duplicate labels apart: `OnHold_Active`, `OnHold_Inactive`. |
| `defaultStatus` | State options: the status a row gets when it enters the state. |

The order of the options is their display order in Dataverse and becomes `DisplayIndex`.

## Relationships

```yaml
    manyToOne:      # this table has the lookup column
      - {schemaName: account_primary_contact, entity: contact, attribute: primarycontactid}
    oneToMany:      # the other table has a lookup to this one
      - {schemaName: contact_customer_accounts, entity: contact, attribute: parentcustomerid}
    manyToMany:
      - {schemaName: accountleads_association, entity: lead, intersectEntity: accountleads}
```

`entity` is the table on the other side, `attribute` the lookup column. A relationship produces
a property only when the table on the other side is among the tables too. A relationship from a
table to itself appears under both `manyToOne` and `oneToMany` and produces `Referencing…` and
`Referenced…` properties.

## Writing values

Any text is allowed in labels and descriptions. Text that YAML would read as something else
(`true`, `123`, `null`, a date, an empty string, text with leading spaces) is written in quotes
by the tool and has to be quoted by hand too.

The tool writes files with LF line endings, UTF-8 without a byte-order mark, tables and columns
in alphabetical order, and leaves out everything that has its default value.
