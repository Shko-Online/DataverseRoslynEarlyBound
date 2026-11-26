; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SOEB001 | EarlyBound | Warning | The project does not reference Microsoft.Xrm.Sdk
SOEB002 | EarlyBound | Warning | No metadata files were found
SOEB100 | EarlyBound | Error | A metadata file is not valid YAML
SOEB101 | EarlyBound | Error | A value in a metadata file is missing or invalid
SOEB102 | EarlyBound | Warning | A table or choice is defined more than once
SOEB103 | EarlyBound | Warning | A column uses a global choice that is not defined
SOEB104 | EarlyBound | Warning | Two generated names collided
SOEB105 | EarlyBound | Warning | A setting is not recognised
SOEB900 | EarlyBound | Error | The generator failed unexpectedly
