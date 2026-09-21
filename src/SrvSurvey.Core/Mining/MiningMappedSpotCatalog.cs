using System.Text.Json;

namespace SrvSurvey.Core.Mining;

public sealed record MiningMappedSpot(string System, string Ring, string Note, double? YieldPercent, string Kind);

/// <summary>Snapshot of the edtools.cc high-yield Platinum and Platinum RES lists.</summary>
public static class MiningMappedSpotCatalog
{
    private static readonly Lazy<IReadOnlyList<MiningMappedSpot>> Data = new(Load);

    public static IReadOnlyList<MiningMappedSpot> Spots => Data.Value;

    public static string Describe(string system, string body)
    {
        string[] notes = Spots
            .Where(spot =>
                spot.System.Equals(system, StringComparison.OrdinalIgnoreCase)
                && body.Contains(spot.Ring, StringComparison.OrdinalIgnoreCase)
            )
            .Select(spot => spot.Note)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join(" · ", notes);
    }

    private static MiningMappedSpot[] Load()
    {
        using Stream stream = typeof(MiningMappedSpotCatalog).Assembly.GetManifestResourceStream(
            "SrvSurvey.Core.Resources.edtools-mining-spots.json"
        )!;
        return JsonSerializer.Deserialize<MiningMappedSpot[]>(stream) ?? [];
    }
}
