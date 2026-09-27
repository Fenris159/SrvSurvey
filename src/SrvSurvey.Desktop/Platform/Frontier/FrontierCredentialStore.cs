using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Desktop.Platform.Frontier;

public interface IFrontierCredentialStore
{
    Task<FrontierCredentialDocument?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(FrontierCredentialDocument document, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken = default);
}

public sealed record FrontierCredentialDocument
{
    public int Version { get; init; } = 3;

    // Linux secret-tool lookup returns an arbitrary item when several share the
    // same attributes. Saves stamp this so the newest authorization wins.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long KeyringRevision { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string KeyringSlot { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, FrontierAccountCredential> Accounts { get; init; } =
        new Dictionary<string, FrontierAccountCredential>(StringComparer.OrdinalIgnoreCase);

    // These top-level fields are retained only to migrate the original
    // single-account credential document without discarding authorization.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string AccessToken { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string RefreshToken { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string TokenType { get; init; } = "Bearer";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? AuthorizedAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? LastCapiRefreshAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public DateTimeOffset? LastCapiAttemptAt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LegacyFrontierId { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LegacyCommanderName { get; init; } = string.Empty;

    public FrontierPendingAuthorization? PendingAuthorization { get; init; }

    public FrontierAuthorizationResult? AuthorizationResult { get; init; }

    public IReadOnlyDictionary<string, FrontierPendingAuthorization> PendingAuthorizations { get; init; } =
        new Dictionary<string, FrontierPendingAuthorization>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, FrontierAuthorizationResult> AuthorizationResults { get; init; } =
        new Dictionary<string, FrontierAuthorizationResult>(StringComparer.Ordinal);

    public bool IsLinked => !string.IsNullOrWhiteSpace(AccessToken) || !string.IsNullOrWhiteSpace(RefreshToken);

    public FrontierAccountCredential LegacyCredential =>
        new()
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            TokenType = TokenType,
            ExpiresAt = ExpiresAt,
            AuthorizedAt = AuthorizedAt,
            LastCapiRefreshAt = LastCapiRefreshAt,
            LastCapiAttemptAt = LastCapiAttemptAt,
        };
}

public sealed record FrontierAccountCredential
{
    public string AccessToken { get; init; } = string.Empty;

    public string RefreshToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? AuthorizedAt { get; init; }

    public DateTimeOffset? LastCapiRefreshAt { get; init; }

    public DateTimeOffset? LastCapiAttemptAt { get; init; }

    public bool IsLinked => !string.IsNullOrWhiteSpace(AccessToken) || !string.IsNullOrWhiteSpace(RefreshToken);
}

public sealed record FrontierPendingAuthorization(
    string State,
    string CodeVerifier,
    DateTimeOffset StartedAt,
    string FrontierId = "",
    string CommanderName = ""
);

public sealed record FrontierAuthorizationResult(
    string State,
    bool Succeeded,
    string Error,
    DateTimeOffset CompletedAt
);

public static class FrontierCredentialStore
{
    public static IFrontierCredentialStore CreateCurrent(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFrontierCredentialStore(Path.Combine(dataDirectory, "frontier-auth.dat"));
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceFrontierCredentialStore(Path.Combine(dataDirectory, "frontier-auth.lock"));
        }

        return new UnsupportedFrontierCredentialStore();
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsFrontierCredentialStore(string path) : IFrontierCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SrvSurvey Frontier OAuth v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FrontierCredentialDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        try
        {
            byte[] plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<FrontierCredentialDocument>(plaintext, JsonOptions);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "The locally encrypted Frontier authorization could not be read by this Windows account.",
                exception
            );
        }
    }

    public async Task SaveAsync(FrontierCredentialDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        string directory =
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Frontier authorization storage has no parent directory.");
        Directory.CreateDirectory(directory);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        byte[] encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken = default) =>
        CredentialStoreLease.AcquireAsync(path + ".lock", cancellationToken);
}

internal sealed class LinuxSecretServiceFrontierCredentialStore(
    string leasePath,
    IReadOnlyList<string>? secretToolPaths = null,
    Func<IReadOnlyList<string>, string?, CancellationToken, Task<SecretToolResult>>? runTool = null
) : IFrontierCredentialStore
{
    private const int PartitionedStorageVersion = 4;
    private const string LegacyService = "frontier-capi";
    private const string StateService = "frontier_capi_state";
    private const string AccountService = "frontier_capi_account";
    private static readonly string[] Slots = ["a", "b"];
    internal const string UnavailableMessage =
        "Secure Frontier token storage is unavailable: Secret Service is inaccessible. secret-tool is installed, but a keyring must run and be unlocked in this login session. On KDE Plasma, enable 'Use KWallet for the Secret Service interface' in System Settings > KDE Wallet and unlock the wallet. If activation still fails, sign out and back in; on SDDM systems, check KWallet/ksecretd login integration. On other desktops, start and unlock GNOME Keyring or another provider. Retry Connect to Frontier.";
    internal const string MissingSecretToolMessage =
        "Secure Frontier token storage is unavailable because secret-tool was not found. Debian/Ubuntu: sudo apt install libsecret-tools. Arch/Manjaro/CachyOS: sudo pacman -S --needed libsecret.";
    internal const string SecretTooLargeMessage =
        "Frontier authorization could not be stored because secret-tool on Linux accepts at most 8192 bytes. Remove an old linked commander and connect again.";
    internal const string InvalidKeyringMessage = "The Frontier authorization stored in the Linux keyring is invalid.";
    internal const string VerificationFailedMessage =
        "Secret Service did not return the Frontier authorization after storing it. Check that the default keyring is unlocked and writable, then retry Connect to Frontier.";
    internal const int MaximumSecretBytes = 8192;
    private static readonly string[] SecretToolPaths =
    [
        "/usr/bin/secret-tool",
        "/bin/secret-tool",
        "/usr/local/bin/secret-tool",
        "/home/linuxbrew/.linuxbrew/bin/secret-tool",
        "/run/current-system/sw/bin/secret-tool",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record StoredAccount(
        string FrontierId,
        FrontierAccountCredential Credential,
        long KeyringRevision,
        string Slot
    );

    public async Task<FrontierCredentialDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        FrontierCredentialDocument? state = ReadPreferredDocument(
            await SearchSecretsAsync(StateService, cancellationToken).ConfigureAwait(false)
        );
        if (state is null)
        {
            return ReadPreferredDocument(
                await SearchSecretsAsync(LegacyService, cancellationToken).ConfigureAwait(false)
            );
        }

        if (state.Version != PartitionedStorageVersion)
        {
            throw new InvalidDataException(InvalidKeyringMessage);
        }

        List<StoredAccount> storedAccounts = ReadAccounts(
            await SearchSecretsAsync(AccountService, cancellationToken).ConfigureAwait(false)
        );
        var accounts = new Dictionary<string, FrontierAccountCredential>(StringComparer.OrdinalIgnoreCase);
        foreach (string frontierId in state.Accounts.Keys)
        {
            StoredAccount? account = PreferredAccount(storedAccounts, frontierId);
            if (account is null)
            {
                throw new InvalidDataException(InvalidKeyringMessage);
            }

            accounts[frontierId] = account.Credential;
        }

        return state with
        {
            Accounts = accounts,
        };
    }

    public async Task SaveAsync(FrontierCredentialDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StoredAccount> storedAccounts = ReadAccounts(
            await SearchSecretsAsync(AccountService, cancellationToken).ConfigureAwait(false)
        );
        foreach ((string frontierId, FrontierAccountCredential credential) in document.Accounts)
        {
            StoredAccount? previous = PreferredAccount(storedAccounts, frontierId);
            if (previous?.Credential == credential)
            {
                continue;
            }

            string slot = OtherSlot(previous?.Slot);
            var account = new StoredAccount(frontierId, credential, NextRevision(previous?.KeyringRevision ?? 0), slot);
            await StoreAndVerifyAsync(
                    JsonSerializer.Serialize(account, JsonOptions),
                    AccountService,
                    frontierId,
                    slot,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        IReadOnlyList<string> stateSecrets = await SearchSecretsAsync(StateService, cancellationToken)
            .ConfigureAwait(false);
        FrontierCredentialDocument? previousState = ReadPreferredDocument(stateSecrets);
        string stateSlot = OtherSlot(previousState?.KeyringSlot);
        var accountIndex = document.Accounts.Keys.ToDictionary(
            frontierId => frontierId,
            _ => new FrontierAccountCredential(),
            StringComparer.OrdinalIgnoreCase
        );
        string stateJson = JsonSerializer.Serialize(
            document with
            {
                Version = PartitionedStorageVersion,
                KeyringRevision = NextRevision(previousState?.KeyringRevision ?? 0),
                KeyringSlot = stateSlot,
                Accounts = accountIndex,
            },
            JsonOptions
        );
        await StoreAndVerifyAsync(stateJson, StateService, null, stateSlot, cancellationToken).ConfigureAwait(false);
        await ClearAsync(LegacyService, cancellationToken).ConfigureAwait(false);
        foreach (
            string removedId in storedAccounts
                .Select(account => account.FrontierId)
                .Distinct()
                .Except(document.Accounts.Keys)
        )
        {
            await ClearAsync(AccountService, cancellationToken, removedId).ConfigureAwait(false);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await ClearAsync(AccountService, cancellationToken).ConfigureAwait(false);
        await ClearAsync(StateService, cancellationToken).ConfigureAwait(false);
        await ClearAsync(LegacyService, cancellationToken).ConfigureAwait(false);
    }

    public Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken = default) =>
        CredentialStoreLease.AcquireAsync(leasePath, cancellationToken);

    internal static IReadOnlyList<string> ParseSearchSecrets(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        List<string> secrets = [];
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            // Char span avoids a localizable string literal for secret-tool's field label.
            ReadOnlySpan<char> prefix = ['s', 'e', 'c', 'r', 'e', 't', ' ', '=', ' '];
            if (line.AsSpan().StartsWith(prefix, StringComparison.Ordinal))
            {
                secrets.Add(line[prefix.Length..]);
            }
        }

        return secrets;
    }

    internal static string? SelectPreferredSecret(IReadOnlyList<string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        string? selected = null;
        long selectedRevision = long.MinValue;
        foreach (string secret in secrets)
        {
            if (!TryReadKeyringRevision(secret, out long revision))
            {
                continue;
            }

            if (selected is null || revision >= selectedRevision)
            {
                selected = secret;
                selectedRevision = revision;
            }
        }

        return selected;
    }

    private async Task<IReadOnlyList<string>> SearchSecretsAsync(
        string service,
        CancellationToken cancellationToken,
        string? frontierId = null,
        string? slot = null
    )
    {
        SecretToolResult result = await RunAsync(SearchArguments(service, frontierId, slot), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                throw new InvalidOperationException($"{UnavailableMessage} {result.Error.Trim()}");
            }

            return [];
        }

        return ParseSearchSecrets(result.Output);
    }

    private static long NextRevision(long previousRevision)
    {
        long now = DateTimeOffset.UtcNow.UtcTicks;
        return now > previousRevision ? now : previousRevision + 1;
    }

    private async Task StoreAndVerifyAsync(
        string payload,
        string service,
        string? frontierId,
        string slot,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException(VerificationFailedMessage);
        }

        if (Encoding.UTF8.GetByteCount(payload) > MaximumSecretBytes)
        {
            throw new InvalidOperationException(SecretTooLargeMessage);
        }

        SecretToolResult result = await RunAsync(StoreArguments(service, frontierId, slot), payload, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Error)
                    ? UnavailableMessage
                    : $"{UnavailableMessage} {result.Error.Trim()}"
            );
        }

        IReadOnlyList<string> readback = await SearchSecretsAsync(service, cancellationToken, frontierId, slot)
            .ConfigureAwait(false);
        if (!readback.Any(secret => string.Equals(secret, payload, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(VerificationFailedMessage);
        }
    }

    private async Task ClearAsync(string service, CancellationToken cancellationToken, string? frontierId = null)
    {
        SecretToolResult result = await RunAsync(ClearArguments(service, frontierId), null, cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Error))
        {
            throw new InvalidOperationException($"{UnavailableMessage} {result.Error.Trim()}");
        }
    }

    private static FrontierCredentialDocument? ReadPreferredDocument(IReadOnlyList<string> secrets)
    {
        if (secrets.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        string selected = SelectPreferredSecret(secrets) ?? throw new InvalidDataException(InvalidKeyringMessage);

        try
        {
            return JsonSerializer.Deserialize<FrontierCredentialDocument>(selected, JsonOptions)
                ?? throw new InvalidDataException(InvalidKeyringMessage);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(InvalidKeyringMessage, exception);
        }
    }

    private static List<StoredAccount> ReadAccounts(IReadOnlyList<string> secrets)
    {
        List<StoredAccount> accounts = [];
        foreach (string secret in secrets.Where(secret => !string.IsNullOrWhiteSpace(secret)))
        {
            try
            {
                StoredAccount? account = JsonSerializer.Deserialize<StoredAccount>(secret, JsonOptions);
                if (
                    account is not null
                    && !string.IsNullOrWhiteSpace(account.FrontierId)
                    && account.Credential is not null
                    && Slots.Contains(account.Slot)
                )
                {
                    accounts.Add(account);
                }
            }
            catch (JsonException)
            {
                // Another valid slot can still preserve the commander credential.
            }
        }

        return accounts;
    }

    private static StoredAccount? PreferredAccount(List<StoredAccount> accounts, string frontierId) =>
        accounts
            .Where(account => string.Equals(account.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(account => account.KeyringRevision)
            .FirstOrDefault();

    private static string OtherSlot(string? current) => current == Slots[0] ? Slots[1] : Slots[0];

    private static bool TryReadKeyringRevision(string secret, out long revision)
    {
        revision = 0;
        try
        {
            FrontierCredentialDocument? document = JsonSerializer.Deserialize<FrontierCredentialDocument>(
                secret,
                JsonOptions
            );
            if (document is null)
            {
                return false;
            }

            revision = document.KeyringRevision;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string[] SearchArguments(string service, string? frontierId, string? slot) =>
        ["search", "--all", "--unlock", .. ItemAttributes(service, frontierId, slot)];

    private static string[] StoreArguments(string service, string? frontierId, string slot) =>
        [
            "store",
            "--collection=default",
            "--label=SrvSurvey Frontier authorization",
            .. ItemAttributes(service, frontierId, slot),
        ];

    private static string[] ClearArguments(string service, string? frontierId) =>
        ["clear", .. ItemAttributes(service, frontierId, null)];

    private static string[] ItemAttributes(string service, string? frontierId, string? slot)
    {
        List<string> attributes = ["application", "SrvSurvey", "service", service];
        if (frontierId is not null)
        {
            attributes.Add("frontierId");
            attributes.Add(frontierId);
        }

        if (slot is not null)
        {
            attributes.Add("keyring_slot");
            attributes.Add(slot);
        }

        return [.. attributes];
    }

    private async Task<SecretToolResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken
    )
    {
        if (runTool is not null)
        {
            return await runTool(arguments, standardInput, cancellationToken).ConfigureAwait(false);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveSecretToolPath(),
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException(UnavailableMessage);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            bool inputFailed = false;
            if (standardInput is not null)
            {
                try
                {
                    await process
                        .StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // secret-tool can exit before reading input when the keyring is unavailable.
                    inputFailed = true;
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new SecretToolResult(
                inputFailed && process.ExitCode == 0 ? -1 : process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false)
            );
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"Secure Frontier token storage is unavailable because secret-tool could not start: {exception.Message}",
                exception
            );
        }
    }

    private string ResolveSecretToolPath() => ResolveSecretToolPath(secretToolPaths, File.Exists);

    internal static string ResolveSecretToolPath(IReadOnlyList<string>? paths, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return (paths ?? SecretToolPaths).FirstOrDefault(fileExists)
            ?? throw new InvalidOperationException(MissingSecretToolMessage);
    }
}

internal sealed record SecretToolResult(int ExitCode, string Output, string Error);

internal sealed class UnsupportedFrontierCredentialStore : IFrontierCredentialStore
{
    private static PlatformNotSupportedException CreateException()
    {
        return new PlatformNotSupportedException(
            "Secure Frontier account storage is currently supported on Windows and Linux."
        );
    }

    public Task<FrontierCredentialDocument?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<FrontierCredentialDocument?>(CreateException());

    public Task SaveAsync(FrontierCredentialDocument document, CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    public Task ClearAsync(CancellationToken cancellationToken = default) => Task.FromException(CreateException());

    public Task<IAsyncDisposable> AcquireLeaseAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IAsyncDisposable>(CreateException());
}

internal static class CredentialStoreLease
{
    public static async Task<IAsyncDisposable> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        string directory =
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Frontier credential lock has no parent directory.");
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new Lease(
                    new FileStream(
                        path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        useAsync: true
                    )
                );
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
