using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationBuildSiteRepairTests
{
    [Theory]
    [InlineData(4_310_842_115, true)]
    [InlineData(3_963_024_386, true)]
    [InlineData(3_950_000_001, true)]
    [InlineData(4_200_000_001, true)]
    [InlineData(128_666_762, false)]
    [InlineData(3_710_879_232, false)]
    public void RecognizesPlayerColonyMarketIdPrefixes(
        long marketId,
        bool expected)
    {
        Assert.Equal(
            expected,
            ColonizationBuildSiteRepair.IsPlayerColonyMarketId(marketId));
    }

    [Theory]
    [InlineData("FleetCarrier", "N4W-T0Z", false)]
    [InlineData(
        "SpaceConstructionDepot",
        "Orbital Construction Site: Dampier Gateway",
        false)]
    [InlineData(
        null,
        "Planetary Construction Site: Example Base",
        false)]
    [InlineData("SurfaceStation", "Some ColonisationShip Pad", true)]
    public void SkipsUnsafeDockContexts(
        string? stationType,
        string stationName,
        bool isConstructionShip)
    {
        Assert.True(ColonizationBuildSiteRepair.ShouldSkipDockContext(
            stationType,
            stationName,
            isConstructionShip));
    }

    [Fact]
    public void AllowsCompletedPlayerStationDockContext()
    {
        Assert.False(ColonizationBuildSiteRepair.ShouldSkipDockContext(
            "Dodec",
            "Gold Enterprise"));
    }

    [Theory]
    [InlineData(
        "Orbital Construction Site: Dampier Gateway",
        "Dampier Gateway")]
    [InlineData(
        "$EXT_PANEL_Colonisation;Saez Synthetics Facility",
        "Saez Synthetics Facility")]
    public void NormalizesJournalStationNames(string input, string expected)
    {
        Assert.Equal(
            expected,
            ColonizationBuildSiteRepair.NormalizeDockStationName(input));
    }

    [Fact]
    public void RepairsMissingOrStaleMarketIdByUniqueCompletedName()
    {
        var missing = CompleteSite("x1", "Dampier Gateway", marketId: null);
        var missingPlan = ColonizationBuildSiteRepair.CreatePlan(
            [missing],
            "Dampier Gateway",
            4_310_999_999);
        var stalePlan = ColonizationBuildSiteRepair.CreatePlan(
            [missing with { MarketId = 3_963_024_386 }],
            "Dampier Gateway",
            4_310_999_999);

        Assert.Equal(
            ColonizationBuildSiteRepairField.MarketId,
            missingPlan?.Field);
        Assert.Equal(4_310_999_999, missingPlan?.CreatePatch().MarketId);
        Assert.Equal(
            ColonizationBuildSiteRepairField.MarketId,
            stalePlan?.Field);
    }

    [Fact]
    public void RepairsNameByUniqueMatchingMarketIdFallback()
    {
        var plan = ColonizationBuildSiteRepair.CreatePlan(
            [CompleteSite("x1", "Generic Outpost", 4_310_999_999)],
            "Dampier Gateway",
            4_310_999_999);

        Assert.Equal(ColonizationBuildSiteRepairField.Name, plan?.Field);
        Assert.Equal("Dampier Gateway", plan?.CreatePatch().Name);
        Assert.Null(plan?.CreatePatch().MarketId);
    }

    [Fact]
    public void AllowsStatuslessLegacyRows()
    {
        var site = JsonSerializer.Deserialize<ColonizationSystemSite>(
            """{"id":"x1","name":"Dampier Gateway"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var plan = ColonizationBuildSiteRepair.CreatePlan(
            [Assert.IsType<ColonizationSystemSite>(site)],
            "Dampier Gateway",
            4_310_999_999);

        Assert.False(site!.HasExplicitStatus);
        Assert.Equal(ColonizationBuildSiteRepairField.MarketId, plan?.Field);
    }

    [Fact]
    public void RejectsAmbiguousNamesEvenWhenOnlyOneRowIsEligible()
    {
        var sites = new[]
        {
            CompleteSite("a", "Twin Hub", 100),
            CompleteSite("b", "Twin Hub", null) with
            {
                Status = ColonizationSystemSiteStatus.Build,
            },
        };

        Assert.Null(ColonizationBuildSiteRepair.CreatePlan(
            sites,
            "Twin Hub",
            4_310_999_999));
    }

    [Fact]
    public void RejectsAmbiguousMarketIdNameRepair()
    {
        var sites = new[]
        {
            CompleteSite("a", "Generic Outpost", 4_310_999_999),
            CompleteSite("b", "Other Outpost", 4_310_999_999),
        };

        Assert.Null(ColonizationBuildSiteRepair.CreatePlan(
            sites,
            "Dampier Gateway",
            4_310_999_999));
    }

    [Theory]
    [InlineData(ColonizationSystemSiteStatus.Plan)]
    [InlineData(ColonizationSystemSiteStatus.Build)]
    public void RejectsActiveRows(ColonizationSystemSiteStatus status)
    {
        var site = CompleteSite("x1", "Dampier Gateway", null) with
        {
            Status = status,
        };

        Assert.Null(ColonizationBuildSiteRepair.CreatePlan(
            [site],
            "Dampier Gateway",
            4_310_999_999));
    }

    [Fact]
    public void SkipsRowsThatAlreadyMatch()
    {
        var site = CompleteSite(
            "x1",
            "Dampier Gateway",
            4_310_999_999);

        Assert.Null(ColonizationBuildSiteRepair.CreatePlan(
            [site],
            "Dampier Gateway",
            4_310_999_999));
    }

    private static ColonizationSystemSite CompleteSite(
        string id,
        string name,
        long? marketId)
    {
        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            MarketId = marketId,
            Status = ColonizationSystemSiteStatus.Complete,
        };
    }
}
