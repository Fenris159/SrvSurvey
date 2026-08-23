using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Storage;

public sealed class SystemBodyDataRetryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string stateDirectory;

    public SystemBodyDataRetryStore(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        stateDirectory = Path.Combine(
            Path.GetFullPath(cacheDirectory),
            "system-body-data-retries");
    }

    public async Task<SystemBodyDataRetryState?> LoadAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        var path = GetPath(frontierId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<SystemBodyDataRetryState>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            Validate(frontierId, state);
            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The external body retry cache is malformed.",
                exception);
        }
    }

    public async Task SaveAsync(
        SystemBodyDataRetryState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        Validate(state.FrontierId, state);
        Directory.CreateDirectory(stateDirectory);
        var path = GetPath(state.FrontierId);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        state,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

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

    private string GetPath(string frontierId)
    {
        var normalizedFrontierId = frontierId.Trim().ToUpperInvariant();
        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(normalizedFrontierId));
        return Path.Combine(stateDirectory, Convert.ToHexString(hash) + ".json");
    }

    private static void Validate(
        string expectedFrontierId,
        SystemBodyDataRetryState? state)
    {
        if (state is null
            || !string.Equals(
                state.FrontierId,
                expectedFrontierId,
                StringComparison.OrdinalIgnoreCase)
            || state.SystemAddress <= 0
            || state.AttemptCount < 0)
        {
            throw new InvalidDataException(
                "The external body retry cache contains invalid state.");
        }
    }
}

public sealed record SystemBodyDataRetryState(
    string FrontierId,
    long SystemAddress,
    DateTimeOffset VisitedAt,
    int AttemptCount,
    DateTimeOffset? RetryAt,
    bool StandardDataComplete,
    bool BiologicalDataComplete)
{
    public bool IsComplete(bool includeBiologicalData) =>
        includeBiologicalData
            ? BiologicalDataComplete
            : StandardDataComplete || BiologicalDataComplete;
}
