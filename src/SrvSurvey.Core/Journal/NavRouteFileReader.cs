using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Journal;

public static class NavRouteFileReader
{
    public const string FileName = "NavRoute.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<NavRouteReadResult> ReadAsync(
        string path,
        int maximumAttempts = 3,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(25);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var content = new MemoryStream();
                await stream.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                var bytes = content.ToArray();
                var data = JsonSerializer.Deserialize<NavRouteData>(
                    bytes,
                    SerializerOptions)
                    ?? throw new JsonException(
                        "NavRoute.json contained no JSON value.");
                var entries = (data.Route ?? [])
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.StarSystem))
                    .Select(entry => new NavRouteEntry(
                        entry.StarSystem!,
                        entry.SystemAddress,
                        GetCoordinate(entry.StarPos),
                        entry.StarClass))
                    .ToArray();
                var snapshot = new NavRouteSnapshot(
                    data.Timestamp,
                    data.EventName ?? string.Empty,
                    entries);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new NavRouteReadResult(snapshot, hash, null, attempt);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                lastException = exception;
                if (attempt < maximumAttempts)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new NavRouteReadResult(
            null,
            null,
            $"Could not read {path} after {maximumAttempts} attempts: "
                + lastException?.Message,
            maximumAttempts);
    }

    private static GalacticCoordinate? GetCoordinate(IReadOnlyList<double>? position)
    {
        return position is { Count: >= 3 }
            && double.IsFinite(position[0])
            && double.IsFinite(position[1])
            && double.IsFinite(position[2])
                ? new GalacticCoordinate(position[0], position[1], position[2])
                : null;
    }

    private sealed record NavRouteData(
        DateTimeOffset Timestamp,
        [property: JsonPropertyName("event")]
        string? EventName,
        IReadOnlyList<NavRouteEntryData>? Route);

    private sealed record NavRouteEntryData(
        string? StarSystem,
        long SystemAddress,
        IReadOnlyList<double>? StarPos,
        string? StarClass);
}

public sealed record NavRouteSnapshot(
    DateTimeOffset Timestamp,
    string EventName,
    IReadOnlyList<NavRouteEntry> Route);

public sealed record NavRouteEntry(
    string StarSystem,
    long SystemAddress,
    GalacticCoordinate? Position,
    string? StarClass)
{
    public BoxelSystemObservation? ToBoxelObservation()
    {
        return BoxelAddress.TryParse(StarSystem, out var boxel)
            && boxel is not null
                ? new BoxelSystemObservation(
                    boxel with { SystemAddress = SystemAddress },
                    Position,
                    null,
                    null,
                    false)
                : null;
    }
}

public sealed record NavRouteReadResult(
    NavRouteSnapshot? Snapshot,
    string? ContentHash,
    string? Error,
    int Attempts)
{
    public bool IsSuccess => Snapshot is not null;
}
