using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;

namespace SrvSurvey.Desktop.Platform.Frontier;

public interface IFrontierCredentialStore
{
    Task<FrontierCredentialDocument?> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        FrontierCredentialDocument document,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> AcquireLeaseAsync(
        CancellationToken cancellationToken = default);
}

public sealed record FrontierCredentialDocument
{
    public int Version { get; init; } = 2;

    public IReadOnlyDictionary<string, FrontierAccountCredential> Accounts
    {
        get;
        init;
    } = new Dictionary<string, FrontierAccountCredential>(
        StringComparer.OrdinalIgnoreCase);

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

    public bool IsLinked => !string.IsNullOrWhiteSpace(AccessToken)
        || !string.IsNullOrWhiteSpace(RefreshToken);

    public FrontierAccountCredential LegacyCredential => new()
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

    public bool IsLinked => !string.IsNullOrWhiteSpace(AccessToken)
        || !string.IsNullOrWhiteSpace(RefreshToken);
}

public sealed record FrontierPendingAuthorization(
    string State,
    string CodeVerifier,
    DateTimeOffset StartedAt,
    string FrontierId = "",
    string CommanderName = "");

public sealed record FrontierAuthorizationResult(
    string State,
    bool Succeeded,
    string Error,
    DateTimeOffset CompletedAt);

public static class FrontierCredentialStore
{
    public static IFrontierCredentialStore CreateCurrent(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFrontierCredentialStore(Path.Combine(
                dataDirectory,
                "frontier-auth.dat"));
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxSecretServiceFrontierCredentialStore(Path.Combine(
                dataDirectory,
                "frontier-auth.lock"));
        }

        return new UnsupportedFrontierCredentialStore();
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsFrontierCredentialStore(string path)
    : IFrontierCredentialStore
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("SrvSurvey Frontier OAuth v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FrontierCredentialDocument?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var plaintext = ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<FrontierCredentialDocument>(
                plaintext,
                JsonOptions);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException(
                "The locally encrypted Frontier authorization could not be read by this Windows account.",
                exception);
        }
    }

    public async Task SaveAsync(
        FrontierCredentialDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Frontier authorization storage has no parent directory.");
        Directory.CreateDirectory(directory);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var encrypted = ProtectedData.Protect(
            plaintext,
            Entropy,
            DataProtectionScope.CurrentUser);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                    temporaryPath,
                    encrypted,
                    cancellationToken)
                .ConfigureAwait(false);
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

    public Task<IAsyncDisposable> AcquireLeaseAsync(
        CancellationToken cancellationToken = default) =>
        CredentialStoreLease.AcquireAsync(path + ".lock", cancellationToken);
}

internal sealed class LinuxSecretServiceFrontierCredentialStore(string leasePath)
    : IFrontierCredentialStore
{
    private const string UnavailableMessage =
        "Secure Frontier token storage is unavailable. Install the 'secret-tool' utility and unlock a Secret Service compatible keyring, then try again.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FrontierCredentialDocument?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
                ["lookup", "application", "SrvSurvey", "service", "frontier-capi"],
                standardInput: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                throw new InvalidOperationException(
                    $"{UnavailableMessage} {result.Error.Trim()}");
            }

            return null;
        }

        if (string.IsNullOrWhiteSpace(result.Output))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FrontierCredentialDocument>(
                result.Output.Trim(),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Frontier authorization stored in the Linux keyring is invalid.",
                exception);
        }
    }

    public async Task SaveAsync(
        FrontierCredentialDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var result = await RunAsync(
                [
                    "store", "--label=SrvSurvey Frontier authorization",
                    "application", "SrvSurvey", "service", "frontier-capi",
                ],
                json,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(result.Error)
                    ? UnavailableMessage
                    : $"{UnavailableMessage} {result.Error.Trim()}");
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
                ["clear", "application", "SrvSurvey", "service", "frontier-capi"],
                standardInput: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Error))
        {
            throw new InvalidOperationException(
                $"{UnavailableMessage} {result.Error.Trim()}");
        }
    }

    public Task<IAsyncDisposable> AcquireLeaseAsync(
        CancellationToken cancellationToken = default) =>
        CredentialStoreLease.AcquireAsync(leasePath, cancellationToken);

    private static async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? standardInput,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(UnavailableMessage);
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new ProcessResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(UnavailableMessage, exception);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

internal sealed class UnsupportedFrontierCredentialStore
    : IFrontierCredentialStore
{
    private static PlatformNotSupportedException CreateException()
    {
        return new PlatformNotSupportedException(
            "Secure Frontier account storage is currently supported on Windows and Linux.");
    }

    public Task<FrontierCredentialDocument?> LoadAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromException<FrontierCredentialDocument?>(CreateException());

    public Task SaveAsync(
        FrontierCredentialDocument document,
        CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(CreateException());

    public Task<IAsyncDisposable> AcquireLeaseAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromException<IAsyncDisposable>(CreateException());
}

internal static class CredentialStoreLease
{
    public static async Task<IAsyncDisposable> AcquireAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "Frontier credential lock has no parent directory.");
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new Lease(new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    useAsync: true));
            }
            catch (IOException)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(100),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class Lease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }
}
