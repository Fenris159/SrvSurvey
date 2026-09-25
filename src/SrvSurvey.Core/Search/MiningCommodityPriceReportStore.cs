using System.Text.Json;

namespace SrvSurvey.Core.Search;

public sealed record MiningCommodityPriceReportSnapshot(
    DateTimeOffset FetchedAt,
    DateTimeOffset? SourceUpdatedAt,
    string LastCommodityName,
    IReadOnlyList<MiningCommodityPriceSummary> Prices,
    int Version = 0,
    int LiveSurfaceQuoteCount = 0,
    DateTimeOffset? LiveQuoteUpdatedAt = null
);

/// <summary>Persists the last successful Ardent commodity report in the user's data directory.</summary>
public sealed class MiningCommodityPriceReportStore(string dataDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Path { get; } = System.IO.Path.Combine(dataDirectory, "ardent-commodity-prices.json");

    public MiningCommodityPriceReportSnapshot? Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            MiningCommodityPriceReportSnapshot? snapshot =
                JsonSerializer.Deserialize<MiningCommodityPriceReportSnapshot>(File.ReadAllText(Path));
            return snapshot is { Version: 2, Prices.Count: > 0 } && snapshot.FetchedAt != default ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(MiningCommodityPriceReportSnapshot snapshot)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
            File.Move(temporary, Path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
