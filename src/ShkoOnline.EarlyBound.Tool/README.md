# Shko Online's Early Bound CLI

`earlybound` downloads the metadata of Dataverse tables into YAML files. The
[ShkoOnline.EarlyBound](https://www.nuget.org/packages/ShkoOnline.EarlyBound) package generates
early-bound classes from those files when your project builds.

```console
dotnet new tool-manifest
dotnet tool install ShkoOnline.EarlyBound.Tool

dotnet earlybound init --environment https://yourorg.crm4.dynamics.com --entities account contact "sho_*"
dotnet earlybound pull
```

| Command | What it does |
| --- | --- |
| `init` | Creates `earlybound.yaml`, which says what to pull and how to generate. |
| `pull` | Downloads the metadata and writes one YAML file per table. |
| `tables`, `solutions` | List what an environment has. |
| `generate` | Writes the generated C# to a folder, for projects that cannot use the source generator. |
| `logout` | Forgets remembered sign-ins. |

Sign-in is through the browser by default. `--auth devicecode`, `azurecli`, `environment`
(a service principal from `AZURE_*` variables) and `default` cover terminals without a browser
and pipelines.

Pulling the same environment twice changes nothing on disk, so the files diff cleanly in source
control.

Documentation and source: <https://github.com/Shko-Online/DataverseRoslynEarlyBound>

Apache-2.0. Copyright 2026 Shko Online LLC.
