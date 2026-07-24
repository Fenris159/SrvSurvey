using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Journal;

public static class CargoFileReader
{
    public const string FileName = "Cargo.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<CargoReadResult> ReadAsync(
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
                var data = JsonSerializer.Deserialize<CargoData>(
                    bytes,
                    SerializerOptions)
                    ?? throw new JsonException(
                        "Cargo.json contained no JSON value.");
                var items = (data.Inventory ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                        && item.Count > 0)
                    .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new CargoItem(
                        group.Key,
                        group.Select(item => item.LocalizedName)
                            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                        group.Sum(item => item.Count),
                        group.Sum(item => item.Stolen)))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var snapshot = new CargoSnapshot(
                    data.Timestamp,
                    data.EventName ?? string.Empty,
                    data.Vessel ?? string.Empty,
                    items.Sum(item => item.Count),
                    items);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new CargoReadResult(snapshot, hash, null, attempt);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException
                    or OverflowException)
            {
                lastException = exception;
                if (attempt < maximumAttempts)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new CargoReadResult(
            null,
            null,
            $"Could not read {path} after {maximumAttempts} attempts: "
                + lastException?.Message,
            maximumAttempts);
    }

    private sealed record CargoData(
        DateTimeOffset Timestamp,
        [property: JsonPropertyName("event")]
        string? EventName,
        string? Vessel,
        IReadOnlyList<CargoItemData>? Inventory);

    private sealed record CargoItemData(
        string? Name,
        [property: JsonPropertyName("Name_Localised")]
        string? LocalizedName,
        int Count,
        int Stolen);
}

public sealed record CargoSnapshot(
    DateTimeOffset Timestamp,
    string EventName,
    string Vessel,
    int Count,
    IReadOnlyList<CargoItem> Inventory)
{
    public int GetCount(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Inventory.FirstOrDefault(item => string.Equals(
            item.Name,
            name,
            StringComparison.OrdinalIgnoreCase))?.Count ?? 0;
    }
}

public sealed record CargoItem(
    string Name,
    string? LocalizedName,
    int Count,
    int Stolen);

public sealed record CargoReadResult(
    CargoSnapshot? Snapshot,
    string? ContentHash,
    string? Error,
    int Attempts)
{
    public bool IsSuccess => Snapshot is not null;
}
