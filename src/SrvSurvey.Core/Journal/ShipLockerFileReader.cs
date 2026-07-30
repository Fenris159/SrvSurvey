using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Journal;

public static class ShipLockerFileReader
{
    public const string FileName = "ShipLocker.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<ShipLockerReadResult> ReadAsync(
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
                await stream.CopyToAsync(content, cancellationToken)
                    .ConfigureAwait(false);
                var bytes = content.ToArray();
                var data = JsonSerializer.Deserialize<ShipLockerData>(
                    bytes,
                    SerializerOptions)
                    ?? throw new JsonException(
                        "ShipLocker.json contained no JSON value.");
                var items = Section("Items", data.Items)
                    .Concat(Section("Components", data.Components))
                    .Concat(Section("Consumables", data.Consumables))
                    .Concat(Section("Data", data.Data))
                    .GroupBy(
                        item => (item.Category, item.Name),
                        StringTupleComparer.OrdinalIgnoreCase)
                    .Select(group => new ShipLockerItem(
                        group.Key.Category,
                        group.Key.Name,
                        group.Select(item => item.LocalizedName)
                            .FirstOrDefault(name =>
                                !string.IsNullOrWhiteSpace(name)),
                        group.Sum(item => item.Count)))
                    .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var snapshot = new ShipLockerSnapshot(
                    data.Timestamp,
                    data.EventName ?? string.Empty,
                    items);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new ShipLockerReadResult(snapshot, hash, null, attempt);
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
                    await Task.Delay(delay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return new ShipLockerReadResult(
            null,
            null,
            $"Could not read {path} after {maximumAttempts} attempts: "
                + lastException?.Message,
            maximumAttempts);
    }

    private static IEnumerable<ShipLockerItem> Section(
        string category,
        IReadOnlyList<ShipLockerItemData>? values)
    {
        return (values ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                && item.Count > 0)
            .Select(item => new ShipLockerItem(
                category,
                item.Name!,
                item.LocalizedName,
                item.Count));
    }

    private sealed record ShipLockerData(
        DateTimeOffset Timestamp,
        [property: JsonPropertyName("event")]
        string? EventName,
        IReadOnlyList<ShipLockerItemData>? Items,
        IReadOnlyList<ShipLockerItemData>? Components,
        IReadOnlyList<ShipLockerItemData>? Consumables,
        IReadOnlyList<ShipLockerItemData>? Data);

    private sealed record ShipLockerItemData(
        string? Name,
        [property: JsonPropertyName("Name_Localised")]
        string? LocalizedName,
        int Count);

    private sealed class StringTupleComparer :
        IEqualityComparer<(string Category, string Name)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals(
            (string Category, string Name) x,
            (string Category, string Name) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Category, y.Category)
                && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);
        }

        public int GetHashCode((string Category, string Name) value)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Category),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
        }
    }
}

public sealed record ShipLockerSnapshot(
    DateTimeOffset Timestamp,
    string EventName,
    IReadOnlyList<ShipLockerItem> Items);

public sealed record ShipLockerItem(
    string Category,
    string Name,
    string? LocalizedName,
    int Count);

public sealed record ShipLockerReadResult(
    ShipLockerSnapshot? Snapshot,
    string? ContentHash,
    string? Error,
    int Attempts)
{
    public bool IsSuccess => Snapshot is not null;
}
