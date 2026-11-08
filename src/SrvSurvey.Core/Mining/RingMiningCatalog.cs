namespace SrvSurvey.Core.Mining;

public enum RingMiningMethod
{
    Laser,
    Core,
}

public sealed record RingMiningCommodity(string Name, int AverageSellPrice);

public sealed record RingMiningComposition(
    string RingType,
    IReadOnlyList<RingMiningCommodity> Laser,
    IReadOnlyList<RingMiningCommodity> Core
);

/// <summary>Shared ring composition reference for extraction filters and the mining guide.</summary>
public static class RingMiningCatalog
{
    private static readonly CatalogData Data = Load();

    public static IReadOnlyList<RingMiningComposition> All => Data.Rings;

    internal static IReadOnlyDictionary<string, string> LocalizedCommodityNames => Data.LegacyNames;

    private static CatalogData Load()
    {
        using Stream stream =
            typeof(RingMiningCatalog).Assembly.GetManifestResourceStream(
                "SrvSurvey.Core.Resources.ring-mining-compositions.json"
            ) ?? throw new InvalidOperationException("The ring-mining reference resource is missing.");
        return System.Text.Json.JsonSerializer.Deserialize<CatalogData>(stream)
            ?? throw new InvalidOperationException("The ring-mining reference resource is empty.");
    }

    public static RingMiningComposition? Find(string ringType)
    {
        string key = NormalizeRing(ringType);
        return All.FirstOrDefault(ring => NormalizeRing(ring.RingType) == key);
    }

    public static bool Supports(string ringType, string mineral, RingMiningMethod method)
    {
        RingMiningComposition? ring = Find(ringType);
        if (ring is null)
        {
            return false;
        }
        IReadOnlyList<RingMiningCommodity> minerals = method == RingMiningMethod.Core ? ring.Core : ring.Laser;
        return minerals.Any(item => MiningCommodityName.Same(item.Name, mineral));
    }

    public static bool SupportsAny(
        string ringType,
        IEnumerable<string> observedMinerals,
        RingMiningMethod method,
        IReadOnlyList<string> selectedMinerals
    ) =>
        observedMinerals.Any(name =>
            (selectedMinerals.Count == 0 || selectedMinerals.Any(selected => MiningCommodityName.Same(name, selected)))
            && Supports(ringType, name, method)
        );

    private static string NormalizeRing(string value) =>
        new(
            value
                .Replace("eRingClass_", "", StringComparison.OrdinalIgnoreCase)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray()
        );

    private sealed record CatalogData(
        IReadOnlyList<RingMiningComposition> Rings,
        IReadOnlyDictionary<string, string> LegacyNames
    );
}
