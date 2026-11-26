# The `earlybound` command

```console
dotnet tool install ShkoOnline.EarlyBound.Tool             # in a repository, with a tool manifest
dotnet tool install --global ShkoOnline.EarlyBound.Tool    # or for your user
```

Installed in a repository it is run as `dotnet earlybound`; installed globally, as `earlybound`.

## Commands

### `init`

Creates `earlybound.yaml` with comments.

```console
dotnet earlybound init --environment https://yourorg.crm4.dynamics.com --entities account contact "sho_*"
```

| Option | Meaning |
| --- | --- |
| `-e`, `--environment <url>` | The Dataverse environment. |
| `--entities <names>` | Tables to list in the file. |
| `--solutions <names>` | Solutions to list in the file. |
| `--namespace <namespace>` | Namespace of the generated classes. |
| `-d`, `--directory <path>` | Folder to create the file in. Defaults to the current folder. |
| `--force` | Replace an existing file. |

### `pull`

Downloads metadata and writes the YAML files.

```console
dotnet earlybound pull
dotnet earlybound pull --entities account contact --dry-run
```

It reads `earlybound.yaml` from the current folder. Every setting of its `pull` section can be
overridden for one run:

| Option | Meaning |
| --- | --- |
| `-c`, `--config <path>` | The configuration file, or the folder it is in. |
| `-e`, `--environment <url>` | The Dataverse environment. |
| `--entities <names>` | Tables to pull instead of those configured. `*` is a wildcard. |
| `--solutions <names>` | Solutions whose tables to pull instead of those configured. |
| `--exclude <names>` | Tables to leave out. |
| `-o`, `--output <path>` | Folder for the files. |
| `--layout <perEntity\|singleFile>` | One file per table, or one file. |
| `--relationships <included\|all\|none>` | Which relationships to keep. |
| `--language <lcid>` | Language of the labels. |
| `--dry-run` | Show what would be pulled; write nothing. |

Files that did not change are not rewritten. Files of tables that are no longer pulled are
deleted, but only files the tool wrote itself: it recognises them by their first line and leaves
everything else in the folder alone.

The exit code is `0` on success and `1` when nothing could be pulled. Warnings, such as a
pattern that matches no table, go to standard error and do not change the exit code.

### `tables` and `solutions`

Show what an environment has, to help fill in `earlybound.yaml`.

```console
dotnet earlybound tables --filter project
dotnet earlybound tables --custom
dotnet earlybound solutions
```

### `generate`

Writes the C# the source generator would produce to a folder. For toolchains that cannot run
source generators, and for reading the output.

```console
dotnet earlybound generate --output Generated
```

### `logout`

Forgets remembered sign-ins and removes their cached tokens from the Keychain (macOS) or the
protected cache file (Windows). On Linux the keyring entry is left to expire; without the
remembered sign-in the tool can no longer use it.

## Signing in

`pull`, `tables` and `solutions` take the same sign-in options.

| `--auth` | How | Use it for |
| --- | --- | --- |
| `browser` (default) | Opens the system browser on a Microsoft sign-in page. | Working at your own machine. |
| `devicecode` | Prints a code to enter at <https://microsoft.com/devicelogin> on any device. | SSH sessions, containers, machines without a browser. |
| `azurecli` | Uses the account signed in with `az login`. | When you already use the Azure CLI. |
| `environment` | A service principal from `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` and `AZURE_CLIENT_SECRET` (or `AZURE_CLIENT_CERTIFICATE_PATH`). | Pipelines. |
| `default` | Tries, without prompting: environment variables, workload identity, managed identity, Azure CLI. | Pipelines with federated credentials. |

| Option | Meaning |
| --- | --- |
| `--tenant <id or domain>` | The directory to sign in to. Needed when your account is a guest in the environment's tenant. |
| `--client-id <id>` | An app registration of your own for `browser` and `devicecode`, instead of the built-in one. It must allow public client flows and have `http://localhost` as a redirect URI. |

Interactive sign-ins are remembered. The tokens are kept by the operating system (Keychain on
macOS, DPAPI on Windows, the keyring on Linux); the tool itself only stores which account signed
in, under your local application data folder. Where no secure store is available, tokens are
kept in memory for the run and you sign in again next time.

The account needs a security role in the environment; reading metadata needs nothing beyond
that. A service principal has to exist in the environment as an application user.

## In a pipeline

Metadata is committed, so builds do not need Dataverse. A scheduled job that keeps it current
could look like this:

```yaml
- run: dotnet tool restore
- run: dotnet earlybound pull --auth environment
  env:
    AZURE_TENANT_ID: ${{ secrets.AZURE_TENANT_ID }}
    AZURE_CLIENT_ID: ${{ secrets.AZURE_CLIENT_ID }}
    AZURE_CLIENT_SECRET: ${{ secrets.AZURE_CLIENT_SECRET }}
- run: git diff --exit-code Metadata   # fails when the tables changed
```
