using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class MiningMissionDestinationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FiveOfSevenMissionsSumOnlyTheSelectedCommodityRequirements()
    {
        MiningMissionCardViewModel[] missions =
        [
            Card(1, "Bertrandite", 100, 40, 20),
            Card(2, "$bertrandite_name;", 200, 50, 60),
            Card(3, "Bertrandite", 300, 100, 50),
            Card(4, "Gallite", 80, 10, 20),
            Card(5, "Gallite", 120, 30, 30),
            Card(6, "Bertrandite", 999, tracked: false),
            Card(7, "Gold", 888, tracked: false),
        ];
        MiningMissionDestinationViewModel destination = Assert.Single(
            MiningMissionDestinationViewModel.Create(missions, true)
        );
        Assert.Equal("5/7 missions tracked", destination.TrackedLabel);
        Assert.Equal("Bismark Legacy @", destination.Heading);
        Assert.Equal("Col 285 Sector VF-E c120-5", destination.System);
        Assert.Equal(2, destination.Commodities.Count);
        MiningMissionCommodityTotalsViewModel bertrandite = destination.Commodities[0];
        Assert.Equal("BRT", bertrandite.Code);
        Assert.Equal(
            (600L, 190L, 130L, 280L),
            (bertrandite.Required, bertrandite.Delivered, bertrandite.OnBoard, bertrandite.Remaining)
        );
        Assert.Equal("#B98966", bertrandite.ColorHex);
        Assert.Equal("#111111", bertrandite.ContrastForeground);
        Assert.True(bertrandite.UseShortTags);
        MiningMissionCommodityTotalsViewModel gallite = destination.Commodities[1];
        Assert.Equal("GAL", gallite.Code);
        Assert.Equal((200L, 40L, 50L, 110L), (gallite.Required, gallite.Delivered, gallite.OnBoard, gallite.Remaining));
        Assert.Equal(25, gallite.CargoPercentage);
        Assert.Equal(20, gallite.DeliveryPercentage);
        Assert.False(destination.IsComplete);
    }

    [Fact]
    public void StationsStaySeparateAndUnreportedDestinationsAreNotCombined()
    {
        MiningMissionCardViewModel[] missions =
        [
            Card(1, "Gold", 10, station: " Bismark Legacy "),
            Card(2, "Gold", 20, station: "BISMARK LEGACY"),
            Card(3, "Gold", 30, station: "Another Port"),
            Card(4, "Gold", 40, station: "", system: ""),
            Card(5, "Gold", 50, station: "", system: ""),
            Card(6, "Gold", 60, station: "Untracked Port", tracked: false),
        ];
        IReadOnlyList<MiningMissionDestinationViewModel> destinations = MiningMissionDestinationViewModel.Create(
            missions,
            true
        );
        Assert.Equal(4, destinations.Count);
        Assert.Equal(2, destinations[0].TrackedMissions);
        Assert.Equal(30, Assert.Single(destinations[0].Commodities).Required);
        Assert.Equal("Another Port", destinations[1].Station);
        Assert.Equal("Destination not reported", destinations[2].Heading);
        Assert.Equal("Destination not reported", destinations[3].Heading);
    }

    [Fact]
    public void EarliestExpiryUsesOnlyTrackedActiveMissions()
    {
        MiningMissionCardViewModel[] missions =
        [
            Card(1, "Gold", 10, expiryHours: 6),
            Card(2, "Gold", 10, expiryHours: 2),
            Card(3, "Gold", 10, expiryHours: 1, tracked: false),
            Card(4, "Gold", 10, expiryHours: -1),
            Card(5, "Gold", 10, status: "Completed"),
        ];
        MiningMissionDestinationViewModel destination = Assert.Single(
            MiningMissionDestinationViewModel.Create(missions, false)
        );
        Assert.Equal("Earliest: 2h 0m remaining", destination.ExpiryLabel);
        Assert.Equal(2, destination.TrackedMissions);
        Assert.Equal(3, destination.TotalMissions);
        Assert.False(Assert.Single(destination.Commodities).UseShortTags);
    }

    [Fact]
    public void WorkspaceAndOverlayFollowTrackingAndExpiryWithoutRebuildingIdleRows()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Accept(1);
        DateTimeOffset firstExpiry = fixture.Now.AddDays(1);
        fixture.Now = fixture.Now.AddHours(1);
        fixture.Accept(2);
        using var overlay = new MiningMissionsOverlayViewModel(fixture.Workspace);
        IReadOnlyList<MiningMissionDestinationViewModel> original = overlay.Destinations;
        Assert.Same(fixture.Workspace.TrackedMissionDestinations, original);
        string expiry = Assert.Single(original).TimeRemaining;
        fixture.Now = fixture.Now.AddMinutes(1);
        fixture.Workspace.Tick();
        Assert.Same(original, overlay.Destinations);
        Assert.NotEqual(expiry, original[0].TimeRemaining);
        fixture.Workspace.MissionCards[0].IsTracked = false;
        Assert.Equal("1/2 missions tracked", Assert.Single(overlay.Destinations).TrackedLabel);
        fixture.Now = firstExpiry;
        fixture.Workspace.Tick();
        Assert.Equal("1/1 missions tracked", Assert.Single(overlay.Destinations).TrackedLabel);
        fixture.Now = firstExpiry.AddHours(1);
        fixture.Workspace.Tick();
        Assert.Empty(overlay.Destinations);
        Assert.False(fixture.Workspace.HasTrackedMissionDestinations);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 20)]
    public void OverlayPreferencesPersistPerCommanderAndClampTheDestinationLimit(int requested, int expected)
    {
        using var fixture = new MiningMissionFixture();
        Assert.True(fixture.Workspace.UseCommodityShortTags);
        Assert.Equal(3, fixture.Workspace.MissionDestinationLimit);
        fixture.Workspace.UseCommodityShortTags = false;
        fixture.Workspace.MissionDestinationLimit = requested;
        var store = new MiningOverlaySettingsStore(Path.Combine(fixture.Directory, "mining", "overlay-settings.json"));
        MiningOverlayPreferences saved = store.Load("F1", () => new());
        Assert.False(saved.UseCommodityShortTags);
        Assert.Equal(expected, saved.MissionDestinationLimit);
        Assert.Equal(new MiningOverlayPreferences(), store.Load("F2", () => new()));
        MiningPreferences settings = new();
        saved.ApplyTo(settings);
        Assert.False(settings.UseCommodityShortTags);
        Assert.Equal(expected, settings.MissionDestinationLimit);
    }

    [Theory]
    [InlineData(100, 2L)]
    [InlineData(90, 2L)]
    [InlineData(1000, 1L)]
    [InlineData(0, null)]
    public void EstimatedTripsRoundsUpTheRemainingNeedsAcrossCommodities(int capacity, long? expected)
    {
        MiningMissionCardViewModel[] missions =
        [
            Card(1, "Gold", 120, delivered: 20, onBoard: 20),
            Card(2, "Gallite", 150, delivered: 30, onBoard: 20),
            Card(3, "Gold", 999, tracked: false),
        ];
        MiningMissionDestinationViewModel destination = Assert.Single(
            MiningMissionDestinationViewModel.Create(missions, true, capacity)
        );
        Assert.Equal(expected, destination.EstimatedTrips);
        Assert.Equal(
            expected is null ? "Estimated Trips: —" : $"Estimated Trips: {expected}",
            destination.EstimatedTripsLabel
        );
    }

    [Fact]
    public void FullyCollectedRequirementsNeedZeroMoreTripsEvenWithoutAReportedHold()
    {
        MiningMissionCardViewModel[] missions = [Card(1, "Gold", 100, delivered: 70, onBoard: 30)];
        MiningMissionDestinationViewModel destination = Assert.Single(
            MiningMissionDestinationViewModel.Create(missions, true)
        );
        Assert.Equal(0, destination.EstimatedTrips);
        Assert.Equal("Estimated Trips: 0", destination.EstimatedTripsLabel);
    }

    [Fact]
    public void TripEstimateUpdatesWhenTheShipHoldChangesAndWaitsForReplacementLoadout()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Accept(1);
        using var overlay = new MiningMissionsOverlayViewModel(fixture.Workspace);
        Assert.Null(Assert.Single(overlay.Destinations).EstimatedTrips);
        fixture.Feed("""{"event":"Loadout","Ship":"Python","CargoCapacity":4}""");
        Assert.Equal("Estimated Trips: 3", Assert.Single(overlay.Destinations).EstimatedTripsLabel);
        fixture.Feed("""{"event":"Loadout","Ship":"Python","CargoCapacity":10}""");
        Assert.Equal("Estimated Trips: 1", Assert.Single(overlay.Destinations).EstimatedTripsLabel);
        fixture.Feed("""{"event":"ShipyardSwap","ShipType":"Anaconda","ShipID":2}""");
        Assert.Equal("Estimated Trips: —", Assert.Single(overlay.Destinations).EstimatedTripsLabel);
    }

    private static MiningMissionCardViewModel Card(
        long id,
        string commodity,
        int required,
        int delivered = 0,
        int onBoard = 0,
        bool tracked = true,
        string station = "Bismark Legacy",
        string system = "Col 285 Sector VF-E c120-5",
        int expiryHours = 24,
        string status = "Active"
    ) =>
        new(
            new MiningMission
            {
                Id = id,
                Commodity = commodity,
                Required = required,
                Delivered = delivered,
                OnBoard = onBoard,
                IsTracked = tracked,
                DestinationSystem = system,
                DestinationStation = station,
                Expiry = Now.AddHours(expiryHours).ToString("O"),
                Status = status,
            },
            Now,
            _ => { },
            () => { }
        );
}
