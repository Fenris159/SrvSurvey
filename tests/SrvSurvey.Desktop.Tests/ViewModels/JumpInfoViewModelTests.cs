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
            new JumpInfoApplyUpdateRequest(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            CreateNavRoute(),
            [Event("Loadout", "\"MaxJumpRange\":25"), FsdTarget("Beta", 3, "N")],
            status,
            null));
        await viewModel.PendingSummaryLoad;

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.TargetName);
        Assert.False(viewModel.IsQuestTagged);
        viewModel.UpdateQuestTags(["beta"]);
        Assert.True(viewModel.IsQuestTagged);
        Assert.Equal("STAR CLASS N", viewModel.StarClass);
        Assert.False(viewModel.IsScoopableStarClass);
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
    public async Task WitchspaceKeepsKnownStarClassUntilArrival()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-13T12:00:00Z"));
        var client = new FakeSummaryClient(
            CreateSummary() with { StarClass = null });
        using var viewModel = CreateViewModel(client, time);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, "N")],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));
        await viewModel.PendingSummaryLoad;
        viewModel.BeginOverlayPresentation();

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [
                    Event(
                        "StartJump",
                        "\"JumpType\":\"Hyperspace\""),
                    Event(
                        "FSDTarget",
                        "\"Name\":\"Beta\",\"SystemAddress\":3"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal("STAR CLASS N", viewModel.StarClass);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [
                    Event(
                        "FSDJump",
                        "\"StarSystem\":\"Beta\",\"SystemAddress\":3"),
                    FsdTarget("Gamma", 4, "K"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.Equal("STAR CLASS N", viewModel.StarClass);
        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.AdvanceTimedTransitions();
        viewModel.EndOverlayPresentation();

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));
        viewModel.BeginOverlayPresentation();
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Gamma", viewModel.TargetName);
        Assert.Equal("STAR CLASS K", viewModel.StarClass);
        Assert.True(viewModel.IsScoopableStarClass);
        Assert.Equal([("Beta", 3L), ("Gamma", 4L)], client.Requests);
    }

    [Fact]
    public async Task WitchspaceUsesDisplayedStarClassWhenTargetOmitsIt()
    {
        var client = new FakeSummaryClient(
            CreateSummary() with { StarClass = null });
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                CreateNavRoute(),
                [FsdTarget("Beta", 3, string.Empty)],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));
        await viewModel.PendingSummaryLoad;
        Assert.Equal("STAR CLASS N", viewModel.StarClass);

        viewModel.BeginOverlayPresentation();
        viewModel.EndOverlayPresentation();
        Assert.True(viewModel.ShouldShow);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [Event("StartJump", "\"JumpType\":\"Hyperspace\"")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.Equal("STAR CLASS N", viewModel.StarClass);
    }

    [Fact]
    public async Task WitchspaceAdoptsStarClassReceivedAfterJumpStarts()
    {
        var client = new FakeSummaryClient(
            CreateSummary() with { StarClass = null });
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, string.Empty)],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));
        await viewModel.PendingSummaryLoad;
        Assert.Equal("STAR CLASS UNKNOWN", viewModel.StarClass);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [
                    Event("StartJump", "\"JumpType\":\"Hyperspace\""),
                    FsdTarget("Beta", 3, "N"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.Equal("STAR CLASS N", viewModel.StarClass);
    }

    [Theory]
    [InlineData("K", true)]
    [InlineData("G", true)]
    [InlineData("B", true)]
    [InlineData("F", true)]
    [InlineData("O", true)]
    [InlineData("A", true)]
    [InlineData("M", true)]
    [InlineData("k", true)]
    [InlineData("N", false)]
    [InlineData("L", false)]
    [InlineData("T", false)]
    [InlineData("K Giant", false)]
    public void ScoopablePillOnlyAppliesToFuelStarClasses(
        string starClass,
        bool expected)
    {
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()));

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, starClass)],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.Equal(expected, viewModel.IsScoopableStarClass);
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
            new JumpInfoApplyUpdateRequest(
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
            followedRoute));
        await viewModel.PendingSummaryLoad;

        Assert.True(viewModel.ShouldShow);
        var followedRouteLine = Assert.Single(
            viewModel.DetailLines,
            line => line.Label == "Followed route");
        Assert.Contains("HOP 1 / 1", followedRouteLine.Value);
        Assert.Contains("Survey the A ring", followedRouteLine.Value);
        Assert.DoesNotContain(
            viewModel.DetailLines,
            line => line.Value.Contains("Neutron boost")
                || line.Value.Contains("Refuel stop"));
        Assert.True(followedRouteLine.Neutron);
        Assert.True(followedRouteLine.Refuel);
        Assert.True(followedRouteLine.HasRouteBadges);
        Assert.True(viewModel.HasNeutronGuidance);
        Assert.True(viewModel.HasRefuelGuidance);
        Assert.True(viewModel.HasRouteGuidanceBadges);
        Assert.True(viewModel.HasDiscoveryOrRouteGuidance);

        var destination = new StatusDestination
        {
            Name = "Beta",
            System = 3,
            Body = 0,
        };
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            null,
            [],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
                GuiFocus = GuiFocus.ExternalPanel,
                Destination = destination,
            },
            followedRoute));
        Assert.False(viewModel.ShouldShow);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            null,
            [Event("Music", "\"MusicTrack\":\"GalaxyMap\"")],
            new EliteStatus
            {
                Flags = StatusFlags.InMainShip | StatusFlags.Supercruise,
                GuiFocus = GuiFocus.NoFocus,
                Destination = destination,
            },
            followedRoute));
        Assert.False(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.True(viewModel.ShouldShow);
    }

    [Fact]
    public async Task ShortcutForcesOverlayButFssStillSuppressesIt()
    {
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()));
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
            "Sol",
            1,
            new GalacticCoordinate(0, 0, 0),
            CreateNavRoute(),
            [FsdTarget("Beta", 3, "N")],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            null));
        await viewModel.PendingSummaryLoad;

        Assert.False(viewModel.ShouldShow);
        Assert.True(viewModel.ToggleForcedVisibility());
        Assert.True(viewModel.ShouldShow);
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
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
            null));

        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public async Task BootstrapIgnoresHistoricalTargetAndUsesLiveStatusDestination()
    {
        var client = new FakeSummaryClient(CreateSummary());
        using var viewModel = CreateViewModel(client);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
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
            IsBootstrapRead: true));
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal([("Beta", 3L)], client.Requests);
    }

    [Fact]
    public async Task DifferentDestinationRegionIsIncludedInSpecialDetails()
    {
        var colonia = CreateSummary() with
        {
            SystemName = "Colonia",
            SystemAddress = 32_382_960_970_595,
            Position = new GalacticCoordinate(-9530.5, -910.28125, 19808.125),
        };
        using var viewModel = CreateViewModel(new FakeSummaryClient(colonia));

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
            "Sol",
            10_477_373_803,
            new GalacticCoordinate(0, 0, 0),
            null,
            [FsdTarget("Colonia", 32_382_960_970_595, "K")],
            new EliteStatus { Flags = StatusFlags.InMainShip },
            null));
        await viewModel.PendingSummaryLoad;

        Assert.Contains(
            viewModel.DetailLines,
            line => line.Label == "Now entering"
                && line.Value == "Inner Scutum-Centaurus Arm");
    }

    [Theory]
    [InlineData("FSDJump")]
    [InlineData("CarrierJump")]
    public async Task CompletedJumpQueuesNextContentUntilNextPresentation(
        string eventName)
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-12T12:00:00Z"));
        var client = new FakeSummaryClient(CreateSummary());
        using var viewModel = CreateViewModel(client, time);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                CreateNavRoute(),
                [FsdTarget("Beta", 3, "N")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));
        await viewModel.PendingSummaryLoad;
        viewModel.BeginOverlayPresentation();

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                new NavRouteSnapshot(
                    time.GetUtcNow(),
                    "NavRoute",
                    [
                        new NavRouteEntry(
                            "Beta",
                            3,
                            new GalacticCoordinate(45, 0, 0),
                            "N"),
                        new NavRouteEntry(
                            "Gamma",
                            4,
                            new GalacticCoordinate(60, 0, 0),
                            "K"),
                    ]),
                [
                    Event(eventName, "\"StarSystem\":\"Beta\""),
                    FsdTarget("Gamma", 4, "K"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));

        Assert.Equal("Beta", viewModel.TargetName);
        Assert.True(viewModel.ShouldShow);
        Assert.Equal([("Beta", 3L)], client.Requests);

        time.Advance(TimeSpan.FromMilliseconds(999));
        viewModel.AdvanceTimedTransitions();
        Assert.Equal("Beta", viewModel.TargetName);

        time.Advance(TimeSpan.FromMilliseconds(1));
        viewModel.AdvanceTimedTransitions();

        Assert.False(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal([("Beta", 3L)], client.Requests);

        viewModel.EndOverlayPresentation();
        Assert.Equal("Beta", viewModel.TargetName);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));

        Assert.True(viewModel.ShouldShow);
        Assert.Equal("Beta", viewModel.TargetName);

        viewModel.BeginOverlayPresentation();
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Gamma", viewModel.TargetName);
        Assert.Equal([("Beta", 3L), ("Gamma", 4L)], client.Requests);
    }

    [Fact]
    public async Task CompletedJumpQueuesWithoutAnActivePresentation()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-13T12:00:00Z"));
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()),
            time);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, "N")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));
        await viewModel.PendingSummaryLoad;

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [
                    Event("FSDJump", "\"StarSystem\":\"Beta\""),
                    FsdTarget("Gamma", 4, "K"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null));
        Assert.Equal("Beta", viewModel.TargetName);

        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.AdvanceTimedTransitions();
        Assert.Equal("Beta", viewModel.TargetName);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                null));
        viewModel.BeginOverlayPresentation();
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Gamma", viewModel.TargetName);
    }

    [Fact]
    public async Task QueuedTargetCanLeaveAndThenResumeFollowedRoute()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-12T12:00:00Z"));
        var client = new FakeSummaryClient(CreateSummary());
        using var viewModel = CreateViewModel(client, time);
        var followedRoute = new FollowRouteDocument(
            "F123",
            "route.json",
            true,
            true,
            0,
            [
                Hop("Sol", 1, 0),
                Hop("Beta", 3, 45),
                Hop("Gamma", 4, 60, "Resume survey route"),
            ]);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, "N")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                followedRoute));
        await viewModel.PendingSummaryLoad;
        viewModel.BeginOverlayPresentation();

        var routeAfterBeta = followedRoute with { LastReachedIndex = 1 };
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [
                    Event(
                        "FSDJump",
                        "\"StarSystem\":\"Beta\",\"SystemAddress\":3"),
                    FsdTarget("Gamma", 4, "K"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                routeAfterBeta));

        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal("HOP 1 / 2", viewModel.JumpProgress);

        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.AdvanceTimedTransitions();
        Assert.Equal("Beta", viewModel.TargetName);
        Assert.Equal("HOP 1 / 2", viewModel.JumpProgress);
        viewModel.EndOverlayPresentation();

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [FsdTarget("Delta", 5, "M")],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                routeAfterBeta));
        viewModel.BeginOverlayPresentation();
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Delta", viewModel.TargetName);
        Assert.DoesNotContain(
            viewModel.DetailLines,
            line => line.Label == "Followed route");

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Delta",
                5,
                new GalacticCoordinate(50, 0, 0),
                null,
                [
                    Event(
                        "FSDJump",
                        "\"StarSystem\":\"Delta\",\"SystemAddress\":5"),
                    FsdTarget("Gamma", 4, "K"),
                ],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                routeAfterBeta));

        time.Advance(TimeSpan.FromSeconds(1));
        viewModel.AdvanceTimedTransitions();
        viewModel.EndOverlayPresentation();

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Delta",
                5,
                new GalacticCoordinate(50, 0, 0),
                null,
                [FsdTarget("Gamma", 4, "K")],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                },
                routeAfterBeta));
        viewModel.BeginOverlayPresentation();
        await viewModel.PendingSummaryLoad;

        Assert.Equal("Gamma", viewModel.TargetName);
        var routeLine = Assert.Single(
            viewModel.DetailLines,
            line => line.Label == "Followed route");
        Assert.Contains("Resume survey route", routeLine.Value);
        Assert.Equal(
            [("Beta", 3L), ("Delta", 5L), ("Gamma", 4L)],
            client.Requests);
    }

    [Fact]
    public async Task FinalFollowedRouteJumpShowsFinishedForThreeSeconds()
    {
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-12T12:00:00Z"));
        using var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()),
            time);
        var followedRoute = new FollowRouteDocument(
            "F123",
            "route.json",
            true,
            true,
            0,
            [
                Hop("Sol", 1, 0),
                Hop("Beta", 3, 45),
            ]);
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [FsdTarget("Beta", 3, "N")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                followedRoute));
        await viewModel.PendingSummaryLoad;
        viewModel.BeginOverlayPresentation();

        Assert.Equal("HOP 1 / 1", viewModel.JumpProgress);
        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [Event(
                    "FSDJump",
                    "\"StarSystem\":\"Beta\",\"SystemAddress\":3")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                followedRoute));

        Assert.Equal("FINISHED", viewModel.JumpProgress);
        Assert.True(viewModel.ShouldShow);

        viewModel.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Beta",
                3,
                new GalacticCoordinate(45, 0, 0),
                null,
                [],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                followedRoute with
                {
                    IsActive = false,
                    LastReachedIndex = 1,
                }));
        Assert.Equal("FINISHED", viewModel.JumpProgress);

        time.Advance(TimeSpan.FromMilliseconds(2999));
        viewModel.AdvanceTimedTransitions();
        Assert.Equal("FINISHED", viewModel.JumpProgress);

        time.Advance(TimeSpan.FromMilliseconds(1));
        viewModel.AdvanceTimedTransitions();
        Assert.False(viewModel.ShouldShow);
        Assert.Equal("FINISHED", viewModel.JumpProgress);

        viewModel.EndOverlayPresentation();
        Assert.Equal("FINISHED", viewModel.JumpProgress);
        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public void PresentationCallbacksAreNoOpsAfterDisposal()
    {
        var viewModel = CreateViewModel(
            new FakeSummaryClient(CreateSummary()));
        viewModel.Dispose();

        var exception = Record.Exception(() =>
        {
            viewModel.BeginOverlayPresentation();
            viewModel.EndOverlayPresentation();
        });

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JumpInfoViewModel CreateViewModel(
        ISystemSummaryClient client,
        TimeProvider? timeProvider = null)
    {
        return new JumpInfoViewModel(
            client,
            new JumpInfoSettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")),
            timeProvider: timeProvider);
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

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public void Advance(TimeSpan duration)
        {
            value += duration;
        }
    }
}
