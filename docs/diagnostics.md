# Diagnostics

The source generator reports these in the Error List and in build output. Problems in a YAML
file are located on the line they come from.

## SOEB001

**The Dataverse SDK is not referenced.** Warning.

The project has metadata but no reference to `Microsoft.Xrm.Sdk`, which the generated classes
derive from. Nothing is generated. Add `Microsoft.CrmSdk.CoreAssemblies` (plug-ins, .NET
Framework) or `Microsoft.PowerPlatform.Dataverse.Client` (modern .NET).

## SOEB002

**No Dataverse metadata was found.** Warning.

The package is installed but no metadata file reached the generator. Run `dotnet earlybound
pull`. If the files exist, check that they are in the `Metadata` folder next to the project
file, or that `EarlyBoundMetadataPath` points to where they are.

## SOEB100

**Invalid YAML.** Error.

A metadata file could not be parsed. The other files are still used.

## SOEB101

**Invalid metadata value.** Error.

A value is missing or has the wrong shape: a table or column without `logicalName`, text where
a number is expected, a list where a single value is expected. What can be read from the file
is still used.

## SOEB102

**Duplicate definition.** Warning.

A table or a global choice is defined in more than one file, or more than one file has a
`generator` section. The first one in path order is used.

## SOEB103

**Global choice is not defined.** Warning.

A column refers to a global choice with `globalOptionSet`, and no file defines that choice. The
property is typed as `OptionSetValue`. Pull again; the tool writes the global choices used by
the tables it pulls.

## SOEB104

**Generated name collision.** Warning for types, information for members.

Two tables, choices or members ended up with the same C# name, and one of them got a number
appended. Give one of them a name of your own with `entityClassNameOverrides`, `optionSetNames`
or `entityAttributeSpecifiedNames`.

## SOEB105

**Unknown setting.** Warning.

A key in the `pull` or `generator` section of `earlybound.yaml` is not a setting, most likely a
typo. See [Configuration](configuration.md).

## SOEB900

**Early Bound generator failure.** Error.

The generator failed in a way it should not. Please
[report it](https://github.com/Shko-Online/DataverseRoslynEarlyBound/issues) with the message.

## Turning a diagnostic off

Like any compiler diagnostic, in the project file or an `.editorconfig`:

```xml
<NoWarn>$(NoWarn);SOEB104</NoWarn>
```
