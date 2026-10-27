using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationCarrierReadinessTests
{
    /// <summary>Locks capped shares and deterministic whole-tonne remainders to the exact readiness total.</summary>
    [Theory]
    [InlineData(100, 80, 80, 50, 50)]
    [InlineData(101, 80, 80, 51, 50)]
    [InlineData(100, 10, 20, 10, 20)]
    [InlineData(0, 10, 20, 0, 0)]
    [InlineData(100, 0, 0, 0, 0)]
    public void ProportionalSharesCapSurplusAndRoundToTheExactReadyTotal(
        int need,
        int first,
        int second,
        long expectedFirst,
        long expectedSecond
    )
    {
        ColonizationProjectPreviewData data = Data(need, first, second);
        var report = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        Assert.Equal(expectedFirst, report.GetCarrierReadiness().Single(item => item.Carrier.MarketId == 1).Ready);
        Assert.Equal(expectedSecond, report.GetCarrierReadiness().Single(item => item.Carrier.MarketId == 2).Ready);
        Assert.Equal(report.ReadyOnCarriers, report.GetCarrierReadiness().Sum(item => item.Ready));
        var reordered = new ColonizationProjectPreview(
            data with
            {
                Project = data.Project with
                {
                    LinkedFleetCarriers = data.Project.LinkedFleetCarriers.AsEnumerable().Reverse().ToList(),
                },
            },
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(expectedFirst, reordered.GetCarrierReadiness().Single(item => item.Carrier.MarketId == 1).Ready);
    }

    /// <summary>Prevents unrelated surplus or repeated carrier links from inflating report readiness.</summary>
    [Fact]
    public void SurplusCannotCoverAnotherCommodityAndSharedCarriersAreOnlyRepresentedOnce()
    {
        ColonizationProjectPreviewData data = Data(100, 1000, 0);
        data.Project.Commodities["water"] = 100;
        data.CarrierCargo!["2"]["water"] = 50;
        var report = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        Assert.Equal(100, report.GetCarrierReadiness()[0].Ready);
        Assert.Equal(50, report.GetCarrierReadiness()[1].Ready);
        var combined = ColonizationProjectPreview.CreateCombined(
            [data, data with { Project = data.Project with { BuildId = "other" } }],
            DateTimeOffset.UnixEpoch
        );
        Assert.Equal(2, combined.GetCarrierReadiness().Count);
        Assert.Equal(250, combined.GetCarrierReadiness().Sum(item => item.Ready));
    }

    /// <summary>Keeps missing inventory unknown instead of presenting fabricated per-carrier shares.</summary>
    [Fact]
    public void UnavailableCargoDoesNotInventPerCarrierCoverage()
    {
        ColonizationProjectPreviewData data = Data(100, 80, 80);
        data.CarrierCargo!.Remove("2");
        var report = new ColonizationProjectPreview(data, DateTimeOffset.UnixEpoch);
        Assert.Null(report.ReadyOnCarriers);
        Assert.All(report.GetCarrierReadiness(), item => Assert.Null(item.Ready));
        var empty = new ColonizationProjectPreview(
            data with
            {
                Project = data.Project with { LinkedFleetCarriers = [] },
            },
            DateTimeOffset.UnixEpoch
        );
        Assert.Empty(empty.GetCarrierReadiness());
    }

    /// <summary>Verifies wide Nexus requirements and filtered systems use the same exact allocation policy.</summary>
    [Fact]
    public void NexusUsesTheSameAllocationForLargeSystemNeedsAndSystemFilters()
    {
        var nexus = new ColonizationNexus
        {
            FleetCarriers =
            [
                new()
                {
                    MarketId = 1,
                    Cargo = new() { ["steel"] = 2_000_000_000 },
                },
                new()
                {
                    MarketId = 2,
                    Cargo = new() { ["steel"] = 2_000_000_000 },
                },
            ],
            Systems =
            [
                new()
                {
                    Name = "Alpha",
                    Needs = new() { ["steel"] = 3_000_000_001 },
                },
                new()
                {
                    Name = "Beta",
                    Needs = new() { ["steel"] = 1_000_000_000 },
                },
            ],
        };
        var report = ColonizationProjectPreview.CreateNexusReport(nexus, "Alpha", DateTimeOffset.UnixEpoch);
        Assert.Equal(1_500_000_001, report.GetCarrierReadiness()[0].Ready);
        Assert.Equal(1_500_000_000, report.GetCarrierReadiness()[1].Ready);
        var all = ColonizationProjectPreview.CreateNexusReport(nexus, null, DateTimeOffset.UnixEpoch);
        Assert.Equal(4_000_000_000, all.GetCarrierReadiness().Sum(item => item.Ready));
    }

    /// <summary>
    /// Builds a public-preview fixture with aligned carrier identities, stock maps, and commodity requirements.
    /// </summary>
    private static ColonizationProjectPreviewData Data(int need, int first, int second) =>
        new(
            new()
            {
                BuildId = "build",
                MaximumRequired = need,
                Commodities = new() { ["steel"] = need },
                LinkedFleetCarriers = [new() { MarketId = 1 }, new() { MarketId = 2 }],
            },
            new()
            {
                ["1"] = new() { ["steel"] = first },
                ["2"] = new() { ["steel"] = second },
            },
            null
        );
}
