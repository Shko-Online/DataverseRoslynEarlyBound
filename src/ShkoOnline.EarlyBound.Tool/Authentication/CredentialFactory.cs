using System.Security.Cryptography;
using System.Text;
using Azure.Core;
using Azure.Identity;
using ShkoOnline.EarlyBound.Tool.Dataverse;

namespace ShkoOnline.EarlyBound.Tool.Authentication;

/// <summary>How the tool signs in to Dataverse.</summary>
internal enum AuthenticationMethod
{
    /// <summary>The system browser opens a Microsoft sign-in page.</summary>
    Browser,

    /// <summary>A code is shown that is entered at microsoft.com/devicelogin on any device.</summary>
    DeviceCode,

    /// <summary>The account signed in with <c>az login</c>.</summary>
    AzureCli,

    /// <summary>A service principal from AZURE_TENANT_ID, AZURE_CLIENT_ID and AZURE_CLIENT_SECRET (or a certificate).</summary>
    Environment,

    /// <summary>Whatever is available without interaction: environment, workload identity, managed identity, Azure CLI.</summary>
    Default,
}

internal sealed class AuthenticationOptions
{
    public AuthenticationMethod Method { get; set; } = AuthenticationMethod.Browser;

    /// <summary>Directory (tenant) ID or domain. Needed for guest accounts; otherwise the account's home tenant is used.</summary>
    public string? TenantId { get; set; }

    /// <summary>App registration used for interactive sign-in instead of the built-in one.</summary>
    public string? ClientId { get; set; }
}

/// <summary>Creates Azure Identity credentials and remembers interactive sign-ins between runs.</summary>
internal static class CredentialFactory
{
    /// <summary>
    /// Microsoft's public client ID for Dataverse development tools. It lets users sign in without
    /// an app registration of their own; pass --client-id to use one.
    /// </summary>
    public const string DefaultClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    private const string TokenCacheName = "ShkoOnline.EarlyBound";

    public static bool TryParseMethod(string? text, out AuthenticationMethod method)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case null or "" or "browser" or "interactive":
                method = AuthenticationMethod.Browser;
                return true;
            case "devicecode" or "device-code" or "device":
                method = AuthenticationMethod.DeviceCode;
                return true;
            case "azurecli" or "azure-cli" or "az":
                method = AuthenticationMethod.AzureCli;
                return true;
            case "environment" or "env" or "serviceprincipal" or "service-principal":
                method = AuthenticationMethod.Environment;
                return true;
            case "default":
                method = AuthenticationMethod.Default;
                return true;
            default:
                method = AuthenticationMethod.Browser;
                return false;
        }
    }

    /// <summary>Creates the credential and, for interactive methods, signs in.</summary>
    /// <param name="showMessage">Shows sign-in instructions to the user.</param>
    public static async Task<TokenCredential> CreateAsync(Uri environment, AuthenticationOptions options, Action<string> showMessage, CancellationToken cancellationToken)
    {
        var tenantId = string.IsNullOrWhiteSpace(options.TenantId) ? null : options.TenantId!.Trim();
        var clientId = string.IsNullOrWhiteSpace(options.ClientId) ? DefaultClientId : options.ClientId!.Trim();
        var request = new TokenRequestContext(new[] { DataverseApi.Scope(environment) });

        switch (options.Method)
        {
            case AuthenticationMethod.AzureCli:
                return new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId });

            case AuthenticationMethod.Environment:
                return new EnvironmentCredential();

            case AuthenticationMethod.Default:
                return new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    TenantId = tenantId,
                    ExcludeInteractiveBrowserCredential = true,
                });

            case AuthenticationMethod.DeviceCode:
                return await SignInAsync(
                    (record, persist) => new DeviceCodeCredential(new DeviceCodeCredentialOptions
                    {
                        ClientId = clientId,
                        TenantId = tenantId,
                        AuthenticationRecord = record,
                        TokenCachePersistenceOptions = persist ? new TokenCachePersistenceOptions { Name = TokenCacheName } : null,
                        DeviceCodeCallback = (code, _) =>
                        {
                            showMessage(code.Message);
                            return Task.CompletedTask;
                        },
                    }),
                    (credential, token) => credential.AuthenticateAsync(request, token),
                    environment, tenantId, clientId, request, cancellationToken).ConfigureAwait(false);

            default:
                return await SignInAsync(
                    (record, persist) => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                    {
                        ClientId = clientId,
                        TenantId = tenantId,
                        RedirectUri = new Uri("http://localhost"),
                        AuthenticationRecord = record,
                        TokenCachePersistenceOptions = persist ? new TokenCachePersistenceOptions { Name = TokenCacheName } : null,
                    }),
                    (credential, token) =>
                    {
                        showMessage("A browser window is opening for you to sign in to Microsoft…");
                        return credential.AuthenticateAsync(request, token);
                    },
                    environment, tenantId, clientId, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reuses the previous sign-in when its tokens are still in the operating system's secure store,
    /// and otherwise signs in and remembers who did.
    /// </summary>
    private static async Task<TokenCredential> SignInAsync<TCredential>(
        Func<AuthenticationRecord?, bool, TCredential> create,
        Func<TCredential, CancellationToken, Task<AuthenticationRecord>> authenticate,
        Uri environment,
        string? tenantId,
        string clientId,
        TokenRequestContext request,
        CancellationToken cancellationToken)
        where TCredential : TokenCredential
    {
        var recordPath = RecordPath(environment, tenantId, clientId);
        var record = await LoadRecordAsync(recordPath, cancellationToken).ConfigureAwait(false);
        if (record != null)
        {
            try
            {
                var silent = create(record, true);
                await silent.GetTokenAsync(request, cancellationToken).ConfigureAwait(false);
                return silent;
            }
            catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException or AuthenticationRequiredException)
            {
                // The remembered sign-in expired or its tokens are gone; sign in again.
            }
        }

        TCredential credential;
        AuthenticationRecord newRecord;
        try
        {
            credential = create(null, true);
            newRecord = await authenticate(credential, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCachePersistenceFailure(ex))
        {
            // No secure store on this machine (e.g. a Linux box without a keyring): keep tokens in memory only.
            credential = create(null, false);
            await authenticate(credential, cancellationToken).ConfigureAwait(false);
            return credential;
        }

        await SaveRecordAsync(recordPath, newRecord, cancellationToken).ConfigureAwait(false);
        return credential;
    }

    private static bool IsCachePersistenceFailure(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current.GetType().Name.Contains("MsalCachePersistence", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What <see cref="SignOut"/> removed.</summary>
    internal sealed record SignOutResult(int SignIns, bool TokensRemoved, bool TokensMayRemain);

    /// <summary>Forgets every remembered sign-in and removes the cached tokens where it can.</summary>
    public static SignOutResult SignOut()
    {
        var count = 0;
        var directory = RecordDirectory();
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                File.Delete(file);
                count++;
            }
        }

        var removed = RemoveCachedTokens(out var mayRemain);
        return new SignOutResult(count, removed, mayRemain);
    }

    /// <summary>
    /// Azure Identity keeps the token cache in the operating system's secure store under the cache
    /// name, with ".cae" or ".nocae" appended, and offers no way to clear it; this removes it
    /// where the store is reachable.
    /// </summary>
    private static bool RemoveCachedTokens(out bool mayRemain)
    {
        var removed = false;
        mayRemain = false;
        string[] names = { TokenCacheName + ".cae", TokenCacheName + ".nocae" };

        if (OperatingSystem.IsMacOS())
        {
            // On macOS the cache is a Keychain item.
            foreach (var name in names)
            {
                removed |= Run("/usr/bin/security", "delete-generic-password", "-s", KeychainService, "-a", name);
            }
        }

        // On Windows the cache is a DPAPI-protected file; elsewhere the file is at most a marker.
        string[] folders =
        {
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), ".IdentityService"),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".IdentityService"),
        };
        foreach (var folder in folders.Distinct())
        {
            foreach (var name in names)
            {
                var path = Path.Combine(folder, name);
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        removed = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    mayRemain = true;
                }
            }
        }

        // The Linux keyring is not reachable from here; its entry expires with the tokens in it.
        if (OperatingSystem.IsLinux())
        {
            mayRemain = true;
        }

        return removed;
    }

    private const string KeychainService = "Microsoft.Developer.IdentityService";

    private static bool Run(string fileName, params string[] arguments)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(start);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static string RecordDirectory() =>
        Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData, System.Environment.SpecialFolderOption.Create),
            "ShkoOnline",
            "EarlyBound",
            "sign-ins");

    /// <summary>
    /// The record holds who signed in (account and tenant IDs), never a token or secret;
    /// tokens stay in the operating system's secure store.
    /// </summary>
    private static string RecordPath(Uri environment, string? tenantId, string clientId)
    {
        var key = $"{DataverseApi.Normalize(environment).Host}|{tenantId}|{clientId}".ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 16).ToLowerInvariant();
        return Path.Combine(RecordDirectory(), hash + ".json");
    }

    private static async Task<AuthenticationRecord?> LoadRecordAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await AuthenticationRecord.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task SaveRecordAsync(string path, AuthenticationRecord record, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var stream = File.Create(path);
            await record.SerializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the sign-in only means signing in again next time.
        }
    }
}
