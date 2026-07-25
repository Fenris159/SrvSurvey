using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class StationInfoViewModelTests
{
    [Fact]
    public async Task SelectedStationUsesLegacyPanelGateAndPresentsDetails()
    {
        using var viewModel = new StationInfoViewModel(
            new FakeSummaryClient(Summary()));
        await viewModel.UpdateCurrentSystemAsync("Test", 42);
        viewModel.UpdateStatus(Status("Raven Port", GuiFocus.ExternalPanel));

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Raven Port", viewModel.StationName);
        Assert.Equal("Planetary Port", viewModel.StationType);
        Assert.Equal("Largest pad: Large", viewModel.LargestPadText);
        Assert.Equal("Cooperative · Democracy", viewModel.FactionText);
        Assert.Equal(
            new StationInfoLineViewModel("High Tech", "75%"),
            viewModel.EconomyLines[0]);
        Assert.Equal(
            ["Shipyard", "Market", "Material Trader"],
            viewModel.RelevantServices);
        Assert.Equal(["Narcotics"], viewModel.ProhibitedCommodities);
    }

    [Fact]
    public async Task ToggleHidesAutomaticOverlayAndForcesItOutsidePanel()
    {
        using var viewModel = new StationInfoViewModel(
            new FakeSummaryClient(Summary()));
        await viewModel.UpdateCurrentSystemAsync("Test", 42);
        viewModel.UpdateStatus(Status("Raven Port", GuiFocus.ExternalPanel));

        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.False(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.True(viewModel.ShouldShow);

        viewModel.UpdateStatus(Status("Raven Port", GuiFocus.NoFocus));
        Assert.False(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.True(viewModel.IsForced);
        Assert.True(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public async Task ConstructionSitesAndForeignDestinationsAreExcluded()
    {
        using var viewModel = new StationInfoViewModel(
            new FakeSummaryClient(Summary()));
        await viewModel.UpdateCurrentSystemAsync("Test", 42);

        viewModel.UpdateStatus(Status(
            "Planetary Construction Site: Hope",
            GuiFocus.ExternalPanel));
        Assert.False(viewModel.ShouldShow);

        viewModel.UpdateStatus(Status(
            "Raven Port",
            GuiFocus.ExternalPanel,
            systemAddress: 99));
        Assert.False(viewModel.ShouldShow);
    }

    private static EliteStatus Status(
        string name,
        GuiFocus focus,
        long systemAddress = 42)
    {
        return new EliteStatus
        {
            GuiFocus = focus,
            Destination = new StatusDestination
            {
                System = systemAddress,
                Name = name,
            },
        };
    }

    private static SystemSummary Summary()
    {
        return new SystemSummary(
            "Test",
            42,
            null,
            null,
            true,
            0,
            0,
            null,
            null,
            null,
            null,
            new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
            [])
        {
            Stations =
            [
                new SystemStationSummary(
                    1,
                    "Raven Port",
                    "Planetary Port",
                    "High Tech",
                    new Dictionary<string, double>
                    {
                        ["Industrial"] = 25,
                        ["High Tech"] = 75,
                    },
                    "Cooperative",
                    "Democracy",
                    ["Market", "Material Trader", "Shipyard", "Dock"],
                    new StationLandingPadSummary(2, 1, 1),
                    ["Narcotics"],
                    DateTimeOffset.Parse("2026-07-25T00:00:00Z")),
            ],
        };
    }

    private sealed class FakeSummaryClient(SystemSummary summary)
        : ISystemSummaryClient
    {
        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SystemSummaryLoadResult(summary, []));
        }
    }
}
