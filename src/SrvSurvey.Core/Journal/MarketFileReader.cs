using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Journal;

public static class MarketFileReader
{
    public const string FileName = "Market.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<MarketReadResult> ReadAsync(
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
                var data = JsonSerializer.Deserialize<MarketData>(
                    bytes,
                    SerializerOptions)
                    ?? throw new JsonException(
                        "Market.json contained no JSON value.");
                var items = (data.Items ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => new MarketItem(
                        item.Id,
                        item.Name!,
                        item.LocalizedName,
                        item.Category ?? string.Empty,
                        item.LocalizedCategory,
                        item.BuyPrice,
                        item.SellPrice,
                        item.MeanPrice,
                        item.StockBracket,
                        item.DemandBracket,
                        item.Stock,
                        item.Demand,
                        item.Producer,
                        item.Consumer,
                        item.Rare))
                    .ToArray();
                var snapshot = new MarketSnapshot(
                    data.Timestamp,
                    data.EventName ?? string.Empty,
                    data.MarketId,
                    data.StationName ?? string.Empty,
                    data.StationType ?? string.Empty,
                    data.CarrierDockingAccess ?? string.Empty,
                    data.StarSystem ?? string.Empty,
                    items);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                return new MarketReadResult(snapshot, hash, null, attempt);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                lastException = exception;
                if (attempt < maximumAttempts)
                {
                    await Task.Delay(delay, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return new MarketReadResult(
            null,
            null,
            $"Could not read {path} after {maximumAttempts} attempts: "
                + lastException?.Message,
            maximumAttempts);
    }

    private sealed record MarketData(
        DateTimeOffset Timestamp,
        [property: JsonPropertyName("event")]
        string? EventName,
        long MarketId,
        string? StationName,
        string? StationType,
        string? CarrierDockingAccess,
        string? StarSystem,
        IReadOnlyList<MarketItemData>? Items);

    private sealed record MarketItemData(
        [property: JsonPropertyName("id")]
        long Id,
        string? Name,
        [property: JsonPropertyName("Name_Localised")]
        string? LocalizedName,
        string? Category,
        [property: JsonPropertyName("Category_Localised")]
        string? LocalizedCategory,
        int BuyPrice,
        int SellPrice,
        int MeanPrice,
        int StockBracket,
        int DemandBracket,
        int Stock,
        int Demand,
        bool Producer,
        bool Consumer,
        bool Rare);
}

public sealed record MarketSnapshot(
    DateTimeOffset Timestamp,
    string EventName,
    long MarketId,
    string StationName,
    string StationType,
    string CarrierDockingAccess,
    string StarSystem,
    IReadOnlyList<MarketItem> Items)
{
    public MarketItem? FindItem(string commodity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commodity);
        var normalized = NormalizeCommodityName(commodity);
        return Items.FirstOrDefault(item => string.Equals(
            NormalizeCommodityName(item.Name),
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeCommodityName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.StartsWith('$'))
        {
            normalized = normalized[1..];
        }

        if (normalized.EndsWith(
                "_name;",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^6];
        }

        return normalized.ToLowerInvariant();
    }
}

public sealed record MarketItem(
    long Id,
    string Name,
    string? LocalizedName,
    string Category,
    string? LocalizedCategory,
    int BuyPrice,
    int SellPrice,
    int MeanPrice,
    int StockBracket,
    int DemandBracket,
    int Stock,
    int Demand,
    bool Producer,
    bool Consumer,
    bool Rare)
{
    public string Commodity => MarketSnapshot.NormalizeCommodityName(Name);
}

public sealed record MarketReadResult(
    MarketSnapshot? Snapshot,
    string? ContentHash,
    string? Error,
    int Attempts)
{
    public bool IsSuccess => Snapshot is not null;
}
