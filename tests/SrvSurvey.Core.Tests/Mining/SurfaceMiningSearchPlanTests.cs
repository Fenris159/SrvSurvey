using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class SurfaceMiningSearchPlanTests
{
    [Fact]
    public void DefaultUsesTheHighestAverageSurfaceMaterial()
    {
        string expected = SurfaceMiningCommodityCatalog
            .HuntReferences.OrderByDescending(reference => reference.AverageGalacticPrice)
            .ThenBy(reference => reference.Material, StringComparer.Ordinal)
            .First()
            .Material;

        Assert.Equal(expected, Assert.Single(SurfaceMiningSearchPlan.MaterialsFor(["Default"])));
        Assert.Equal(PlanetaryMiningPlan.Materials.Count, SurfaceMiningSearchPlan.MaterialsFor(["Any"]).Count);
        Assert.Equal(["Diamond", "Painite"], SurfaceMiningSearchPlan.MaterialsFor(["Diamond", "Painite"]));
    }

    [Fact]
    public void BestSellIsTheHighestPriceInsideTheResults()
    {
        MiningMarketResult[] markets =
        [
            new("Near", "Small pad", "Outpost", 10, 20, 100, 5, 0, null, 1, false) { Commodity = "Diamond" },
            new("Far", "Big pad", "Orbis", 40, 5, 900, 8, 0, null, 2, true) { Commodity = "Painite" },
        ];

        MiningMarketResult best = SurfaceMiningSearchPlan.BestSell(markets)!;

        Assert.Equal("Far", best.System);
        Assert.Equal(900, best.Price);
    }

    [Fact]
    public void AllAndUnknownDoNotSendAReserveFilter()
    {
        Assert.Equal("", SurfaceMiningSearchPlan.SpanshReserve("All"));
        Assert.Equal("", SurfaceMiningSearchPlan.SpanshReserve("Unknown"));
        Assert.Equal("Pristine", SurfaceMiningSearchPlan.SpanshReserve("Pristine"));
    }
}
