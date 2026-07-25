using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JumpInfoViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-JumpInfoViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ChargingJumpBuildsRouteAndLoadsAllMetadata()
    {
        var client = new FakeSummaryClient(CreateSummary());
        using var viewModel = CreateViewModel(client);
        var status = new EliteStatus
        {
            Flags = StatusFlags.InMainShip,
            Flags2 = StatusFlags2.FsdChargingJump,
        };

        viewModel.ApplyUpdate(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            CreateNavRoute(),
            [Event("Loadout", "\"MaxJumpRange\":25"), FsdTarget("Beta", 3, "N")],
            status,
            null);
        await viewModel.PendingSummaryLoad;

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal("STAR CLASS N", viewModel.StarClass);
        Assert.Equal("JUMP 2 OF 2", viewModel.JumpProgress);
        Assert.Equal("45.0 LY", viewModel.TotalDistance);
        Assert.Equal(2, viewModel.RouteLegs.Count);
        Assert.True(viewModel.RouteLegs[1].RequiresBoost);
        Assert.Contains("Pathfinder", viewModel.DiscoveryText);
        Assert.Contains("100 total", viewModel.TrafficText);
        Assert.Contains("Bodies: 7", viewModel.PointsOfInterestText);
        Assert.Contains(
            viewModel.DetailLines,
            line => line.Label == "Encoded Hub"
                && line.Value == "Material Trader - Encoded");
        Assert.Equal([("Beta", 3L)], client.Requests);
    }

    [Fact]
    public async Task FollowedRouteSelectionCanShowOverlayInSupercruise()
    {
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()));
        viewModel.ShowWhenNextHopSelected = true;
        var followedRoute = new FollowRouteDocument(
            "F123",
            "route.json",
            true,
            true,
            0,
            [
                Hop("Sol", 1, 0),
                Hop("Beta", 3, 45, "Survey the A ring", true, true),
            ]);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            null,
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
                Destination = new StatusDestination
                {
                    Name = "Beta",
                    System = 3,
                    Body = 0,
                },
            },
            followedRoute);
        await viewModel.PendingSummaryLoad;

        Assert.True(viewModel.ShouldShow);
        Assert.Contains(
            viewModel.DetailLines,
            line => line.Label == "Followed route"
                && line.Value.Contains("Hop 2 of 2")
                && line.Value.Contains("Survey the A ring")
                && line.Value.Contains("Neutron boost")
                && line.Value.Contains("Refuel stop"));
    }

    [Fact]
    public async Task ShortcutForcesOverlayButFssStillSuppressesIt()
    {
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()));
        viewModel.ApplyUpdate(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            CreateNavRoute(),
            [FsdTarget("Beta", 3, "N")],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            null);
        await viewModel.PendingSummaryLoad;

        Assert.False(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.True(viewModel.ShouldShow);
        viewModel.ApplyUpdate(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            null,
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip,
                GuiFocus = GuiFocus.Fss,
            },
            null);

        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public async Task BootstrapIgnoresHistoricalTargetAndUsesLiveStatusDestination()
    {
        var client = new FakeSummaryClient(CreateSummary());
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            CreateNavRoute(),
            [FsdTarget("Historical", 99, "A")],
            new EliteStatus
            {
                Destination = new StatusDestination
                {
                    Name = "Beta",
                    System = 3,
                    Body = 0,
                },
            },
            null,
            isBootstrapRead: true);
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal([("Beta", 3L)], client.Requests);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JumpInfoViewModel CreateViewModel(ISystemSummaryClient client)
    {
        return new JumpInfoViewModel(
            client,
            new JumpInfoSettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")));
    }

    private static NavRouteSnapshot CreateNavRoute()
    {
        return new NavRouteSnapshot(
            DateTimeOffset.UtcNow,
            "NavRoute",
            [
                new NavRouteEntry(
                    "Sol",
                    1,
                    new GalacticCoordinate(0, 0, 0),
                    "G"),
                new NavRouteEntry(
                    "Alpha",
                    2,
                    new GalacticCoordinate(10, 0, 0),
                    "K"),
                new NavRouteEntry(
                    "Beta",
                    3,
                    new GalacticCoordinate(45, 0, 0),
                    "N"),
            ]);
    }

    private static FollowRouteHop Hop(
        string name,
        long address,
        double x,
        string? notes = null,
        bool refuel = false,
        bool neutron = false)
    {
        return new FollowRouteHop(
            name,
            address,
            new GalacticCoordinate(x, 0, 0),
            notes,
            refuel,
            neutron);
    }

    private static JournalEventEnvelope FsdTarget(
        string name,
        long address,
        string starClass)
    {
        return Event(
            "FSDTarget",
            $"\"Name\":\"{name}\",\"SystemAddress\":{address},"
                + $"\"StarClass\":\"{starClass}\"");
    }

    private static JournalEventEnvelope Event(string name, string properties)
    {
        var json = $"{{\"event\":\"{name}\",{properties}}}";
        Assert.True(JournalEventEnvelope.TryParse(json, out var value, out _));
        return value!;
    }

    private static SystemSummary CreateSummary()
    {
        return new SystemSummary(
            "Beta",
            3,
            new GalacticCoordinate(45, 0, 0),
            "N",
            true,
            5,
            7,
            "Pathfinder",
            DateTimeOffset.Parse("2024-01-02T03:04:05Z"),
            DateTimeOffset.Parse("2025-02-03T04:05:06Z"),
            new SystemTrafficSummary(3, 20, 100),
            new SystemPoiSummary(7, 2, 1, 1, 0, 0, 1),
            [
                new SystemSpecialSummary(
                    "Encoded Hub",
                    ["Material Trader - Encoded"]),
            ]);
    }

    private sealed class FakeSummaryClient(SystemSummary summary)
        : ISystemSummaryClient
    {
        public List<(string Name, long Address)> Requests { get; } = [];

        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((systemName, systemAddress));
            return Task.FromResult(new SystemSummaryLoadResult(summary, []));
        }
    }
}
