using System.IO.Compression;
using System.Text.Json;

namespace SrvSurvey.Core.Mining;

public static class MiningReferenceData
{
    public static IReadOnlyDictionary<string, string[]> Commodities { get; } = LoadCommodities();
    private static Dictionary<string, string[]> LoadCommodities()
    {
        using var stream = typeof(MiningReferenceData).Assembly.GetManifestResourceStream("SrvSurvey.Core.Resources.mining-commodities.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? new();
    }
    private static readonly Lazy<IReadOnlyList<MiningRing>> Data = new(Load);
    public static IReadOnlyList<MiningRing> Rings => Data.Value;
    private static MiningRing[] Load()
    {
        using var resource = typeof(MiningReferenceData).Assembly.GetManifestResourceStream("SrvSurvey.Core.Resources.mining-rings.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<MiningRing[]>(gzip) ?? [];
    }
}
