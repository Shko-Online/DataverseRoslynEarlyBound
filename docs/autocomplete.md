# Autocomplete

The completion provider offers names from your metadata where late-bound SDK code needs them.
It is part of the `ShkoOnline.EarlyBound` package: Visual Studio, the VS Code C# extension and
Rider load it from the project's analyzers, so there is nothing to install in the IDE.

Completion opens when you type the opening quote of a name, and can always be invoked by hand
(Ctrl+Space). Every entry shows the display name and type beside it, and the description, the
targets of a lookup or the options of a choice in the details pane.

## Table names

Where a string names a table:

```csharp
new Entity("…")                          new EntityReference("…", id)
new QueryExpression("…")                 new QueryByAttribute("…")
service.Retrieve("…", id, columns)       service.Delete("…", id)
new LinkEntity("…", "…", from, to, op)   query.AddLink("…", from, to)
query.EntityName = "…"                   entity.LogicalName == "…"
context.PrimaryEntityName == "…"         case "…":  after switch (entity.LogicalName)
```

## Column names

Where a string names a column:

```csharp
entity["…"]                              entity.GetAttributeValue<T>("…")
entity.Contains("…")                     entity.TryGetAttributeValue("…", out value)
entity.Attributes["…"]                   entity.FormattedValues["…"]
new Entity("account") { ["…"] = value }
new ColumnSet("…", "…")                  columns.AddColumns("…")
new ConditionExpression("…", op, value)  filter.AddCondition("…", op, value)
query.AddOrder("…", OrderType.Ascending) new OrderExpression("…", order)
new LinkEntity(from, to, "…", "…", op)   query.AddLink(to, "…", "…")
```

### Which table's columns

The provider looks at the code around the caret to find the table:

| Code | Table |
| --- | --- |
| `var e = new Entity("contact"); e["…"]` | from the constructor argument, also when it is a constant such as `Contact.EntityLogicalName` |
| `Contact c; c["…"]` and `e.ToEntity<Contact>()["…"]` | from the early-bound type |
| `service.Retrieve("contact", id, new ColumnSet("…"))` | from the call the column set is an argument of |
| `new QueryExpression("contact") { ColumnSet = new ColumnSet("…") }` | from the query the expression is nested in |
| `query.Criteria.AddCondition("…", …)` | from where `query` was created |
| `foreach (var row in service.RetrieveMultiple(query).Entities) row["…"]` | from the query that produced the rows |
| `rows.Select(row => row.GetAttributeValue<string>("…"))` | from the collection the lambda runs over |
| `new LinkEntity("account", "contact", "…", "…", …)` | the first name from `account`, the second from `contact` |
| `link.Columns.AddColumns("…")` | the table the link goes to |
| `new ConditionExpression("contact", "…", …)` | the table named next to it |

When none of this applies, for example for an entity that arrives as a method parameter, every
column of every table is offered, and each entry says which tables have it.

Columns the platform computes from another one, such as `primarycontactidname`, are left out.

Your own helper methods take part when they work with SDK types and name their parameters
`entityName`, `entityLogicalName`, `tableName`, `attributeName`, `attributeLogicalName`,
`columnName` or `fieldName`.

## Relationship names

```csharp
new Relationship("…")
```

## FetchXML

Inside a string that contains FetchXML, in any form of string literal (regular, verbatim, raw,
interpolated, or concatenated from pieces):

| Position | Offered |
| --- | --- |
| `<entity name='…'`, `<link-entity name='…'` | Tables |
| `<attribute name='…'`, `<order attribute='…'`, `<condition attribute='…'` | Columns of the enclosing `entity` or `link-entity` |
| `<link-entity from='…'` | Columns of the linked table |
| `<link-entity to='…'` | Columns of the enclosing table |
| `<condition entityname='c' attribute='…'` | Columns of the table with that alias or name |
| `<condition operator='…'` | The FetchXML operators |
| `<condition attribute='statuscode' value='…'`, `<value>…</value>` | The options of the choice column |
| `<link-entity link-type='…'`, `<filter type='…'` | The allowed values |

## Choice values

Where the number of an option is expected:

```csharp
entity["statuscode"] = new OptionSetValue(…);
new Entity("account") { ["statuscode"] = new OptionSetValue(…) };
query.Criteria.AddCondition("statuscode", ConditionOperator.Equal, …);
new ConditionExpression("statuscode", ConditionOperator.In, 1, …);
if (entity.GetAttributeValue<OptionSetValue>("statuscode").Value == …)
```

The list shows the labels with their numbers, in Dataverse's order, above the ordinary entries.
Typing a label or a number filters it. Picking an option inserts
`(int)Account_StatusCode.Inactive` when the generated enum is available in the project, with its
namespace when that is not imported, and `2` otherwise. In FetchXML it inserts the number.

Options are offered only when the column is known to be a choice. When the table is unknown,
they are offered if every table that has the column uses the same choice.

## Limits

- IDEs load completion providers from NuGet packages in the background. The first completion
  after opening a solution can come up without these entries; the next one has them.
- In VS Code, typing inside a string does not open suggestions by itself unless
  `editor.quickSuggestions` has `strings` on. An opening double quote does. For FetchXML
  attributes written in single quotes, press Ctrl+Space.
- Finding the table follows the common shapes of SDK code within one method. It does not follow
  values through fields, method calls or other files.
- FetchXML is recognised by its elements (`<fetch`, `<entity`, `<attribute`, …). Element and
  attribute names of FetchXML itself are not completed.
