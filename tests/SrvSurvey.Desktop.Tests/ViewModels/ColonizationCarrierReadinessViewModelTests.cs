using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationCarrierReadinessViewModelTests
{
    /// <summary>Keeps carrier labels meaningful and removes stale legend data when a report disappears.</summary>
    [Fact]
    public async Task LegendNamesHaveFallbacksAndMissingReportsClearAllCarrierShares()
    {
        ColonizationProjectPreviewData? data = ColonizationProjectPreviewViewModelTests.Data();
        data = data with
        {
            Project = data.Project with
            {
                LinkedFleetCarriers =
                [
                    new() { MarketId = 1, Name = "ABC-123" },
                    new() { MarketId = 2, DisplayName = "Display only" },
                    new() { MarketId = 3 },
                    new()
                    {
                        MarketId = 4,
                        Name = "Same",
                        DisplayName = "Same",
                    },
                ],
            },
            CarrierCargo = new()
            {
                ["1"] = new() { ["steel"] = 10 },
                ["2"] = new() { ["steel"] = 10 },
                ["3"] = new() { ["steel"] = 0 },
                ["4"] = new() { ["steel"] = 10 },
            },
        };
        using var model = new ColonizationProjectPreviewViewModel(
            new ColonizationProjectPreviewViewModelTests.Reader(
                (_, _) => Task.FromResult<ColonizationProjectPreviewData?>(data)
            ),
            "build"
        );
        await model.RefreshAsync();
        Assert.Equal(["ABC-123", "Display only", "3", "Same"], model.ReadyCarriers.Select(carrier => carrier.Label));
        Assert.Equal("10 t", model.ReadyCarriers[0].QuantityText);
        Assert.Equal("0 t", model.ReadyCarriers[2].QuantityText);
        data.CarrierCargo.Remove("2");
        await model.RefreshAsync();
        Assert.All(model.ReadyCarriers, carrier => Assert.Equal("Unknown", carrier.QuantityText));
        data = null;
        await model.RefreshAsync();
        Assert.Empty(model.ReadyCarriers);
    }

    /// <summary>
    /// Recalculates filtered Nexus quantities without changing the carrier IDs that determine their colors.
    /// </summary>
    [Fact]
    public async Task NexusSystemSelectionRecalculatesSharesWithoutChangingCarrierIdentity()
    {
        var nexus = new ColonizationNexus
        {
            FleetCarriers =
            [
                new()
                {
                    MarketId = 1,
                    Name = "ABC-123",
                    Cargo = new() { ["steel"] = 80 },
                },
                new()
                {
                    MarketId = 2,
                    Name = "DEF-456",
                    Cargo = new() { ["steel"] = 80 },
                },
            ],
            Systems =
            [
                new()
                {
                    Name = "Alpha",
                    Needs = new() { ["steel"] = 100 },
                },
                new()
                {
                    Name = "Beta",
                    Needs = new() { ["steel"] = 100 },
                },
            ],
        };
        using var model = new ColonizationProjectPreviewViewModel(
            _ => Task.FromResult<ColonizationNexus?>(nexus),
            null,
            () => true,
            () => 128
        );
        await model.RefreshAsync();
        Assert.All(model.ReadyCarriers, carrier => Assert.Equal(80, carrier.Ready));
        long[] ids = model.ReadyCarriers.Select(carrier => carrier.MarketId).ToArray();
        model.SelectedSystem = model.SystemOptions.Single(option => option.SystemName == "Alpha");
        Assert.All(model.ReadyCarriers, carrier => Assert.Equal(50, carrier.Ready));
        Assert.Equal(ids, model.ReadyCarriers.Select(carrier => carrier.MarketId));
    }
}
