using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class PowerplayMeritRankTests
{
    [Fact]
    public void EdtoolsNotesAttachToTheMatchingRing()
    {
        string note = MiningMappedSpotCatalog.Describe("Paesia", "Paesia 2 A Ring");
        Assert.Contains("22.19%", note);
        Assert.Contains("Haz", note);
    }

    [Fact]
    public void LocationsAreOrderedByBestSellPriceAndOmitIncompleteRows()
    {
        MiningSystemResult[] systems =
        [
            new("Cheap", 1, "", "", "", "", "", "", "Exploited", 0),
            new("Rich", 4, "", "", "", "", "", "", "Fortified", 0),
            new("NoBuyer", 2, "", "", "", "", "", "", "Exploited", 0),
        ];
        MiningRing[] rings =
        [
            new()
            {
                System = "Cheap",
                Body = "Cheap A Ring",
                RingType = "Metallic",
                Hotspots = new() { ["Platinum"] = 2 },
            },
            new()
            {
                System = "Rich",
                Body = "Rich 2 A Ring",
                RingType = "Metallic",
                Hotspots = new() { ["Platinum"] = 1 },
            },
            new()
            {
                System = "NoBuyer",
                Body = "NoBuyer A Ring",
                RingType = "Metallic",
                Hotspots = new() { ["Platinum"] = 1 },
            },
        ];
        MiningMarketResult[] markets =
        [
            new("Cheap", "Market", "Starport", 1, 10, 100_000, 500, 0, DateTimeOffset.UtcNow, 1),
            new("Rich", "Hub", "Starport", 4, 20, 250_000, 800, 0, DateTimeOffset.UtcNow, 2),
        ];

        IReadOnlyList<PowerplayMeritSystem> ranked = PowerplayMeritRank.Compose(systems, rings, markets, 10);

        Assert.Equal(["Rich", "Cheap"], ranked.Select(row => row.Name).ToArray());
        Assert.Equal(250_000, ranked[0].BestPrice);
        Assert.DoesNotContain(ranked, row => row.Name == "NoBuyer");
    }
}
