using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SurfaceTracker = SrvSurvey.Core.Exobiology.SurfaceSurveyJournalTracker;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Builds editor data contexts for shared presentation templates, feeding
/// representative simulated Elite session data into real view-models.
/// </summary>
internal static class OverlayEditorPreviewFactories
{
    private const string UiSettingsFileName = "ui-settings.json";

    private static readonly OverlayPreviewSimulationState State =
        OverlayPreviewSimulationState.Default;

    private static string SettingsDir(string leaf) =>
        Path.Combine(Path.GetTempPath(), "SrvSurvey-OverlayEditorPreview", leaf);

    private static OverlayPlatformCapabilities Caps() =>
        OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);

    public static RouteBioOverlayViewModel CreateRouteBio()
    {
        var temporaryDirectory = SettingsDir("route-bio");
        var vm = new RouteBioOverlayViewModel(
            new RouteWorkspaceViewModel(
                new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
                new RouteNameImporter(new EmptySystemResolver()),
                new EmptySpanshRouteClient()),
            Caps());
        var content = OverlayPreviewSimulationProjector.Project(
            OverlayLayoutCatalog.GetRequired("PlotRouteBio"),
            State);
        var targets = content.Rows
            .Select(row => row.RouteBody)
            .OfType<RouteBioTargetItemViewModel>()
            .ToArray();
        vm.InstallEditorPreview(State.CurrentSystem, targets);
        return vm;
    }

    public static ColonizationCommodityOverlayViewModel CreateColonization()
    {
        var vm = new ColonizationCommodityOverlayViewModel();
        vm.Apply(
            new ColonizationCommodityPlan
            {
                Title = State.ColonyProjectName,
                ProjectNames = [State.ColonyProjectName, "Relay Hub"],
                Rows =
                [
                    Row("steel", "Steel", "Metals", 2450, 96, 620),
                    Row("power_generators", "Power generators", "Technology", 840, 32, 210),
                    Row("polymers", "Polymers", "Chemicals", 610, 24, 180),
                    Row("water_purifiers", "Water purifiers", "Technology", 420, 16, 120),
                    Row("cmm_composites", "CMM composites", "Metals", 300, 12, 80),
                    Row("emergency_power_cells", "Emergency power cells", "Technology", 200, 12, 30),
                ],
                FleetCarriers =
                [
                    new ColonizationFleetCarrier
                    {
                        MarketId = 1,
                        Name = "Raven's Reach",
                        DisplayName = "Raven's Reach",
                        Cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["steel"] = 620,
                            ["polymers"] = 180,
                        },
                    },
                ],
                TotalRemaining = 4820,
                TripsInCurrentShip = 12,
                FleetCarrierDeficit = 1240,
                FleetCarrierDeficitTrips = 4,
                IsAtConstructionSite = true,
            },
            updatedStatus: null,
            updatedHasMarketSinceDocking: true);
        vm.ApplyPreferences(ColonizationOverlayPreferences.Default with
        {
            ShowFleetCarrierCargo = true,
        });
        return vm;

        static ColonizationCommodityPlanRow Row(
            string commodity,
            string display,
            string category,
            int needed,
            int ship,
            int fc) =>
            new()
            {
                Commodity = commodity,
                DisplayName = display,
                Category = category,
                Needed = needed,
                InShip = ship,
                OnFleetCarriers = fc,
                IsAvailableAtCurrentMarket = true,
            };
    }

    public static NotificationViewModel CreateNotification()
    {
        var vm = new NotificationViewModel(
            new NotificationSettingsStore(
                Path.Combine(SettingsDir("notification"), UiSettingsFileName)));
        vm.Enabled = true;
        vm.ShowMessage("First footfall confirmed on Synuefe NL-N C23-4 B 3");
        vm.ShowMessage("Codex: Bacterium Acies recorded");
        return vm;
    }

    public static CombatOverlayViewModel CreateCombat()
    {
        var combat = new CombatViewModel(
            new CombatSettingsStore(
                Path.Combine(SettingsDir("combat"), UiSettingsFileName)),
            new CommanderProfileStore(SettingsDir("combat-profile")));
        combat.InstallEditorPreview();
        return new CombatOverlayViewModel(combat, Caps());
    }

    public static GalaxyMapOverlayViewModel CreateGalaxyMap()
    {
        var nicknameDir = SettingsDir("nicknames");
        var vm = new GalaxyMapOverlayViewModel(
            new EmptySystemSummaryClient(),
            new GalaxyMapSettingsStore(
                Path.Combine(SettingsDir("galmap"), UiSettingsFileName)),
            new SystemNicknameViewModel(
                SystemNicknameCatalog.Load(nicknameDir),
                new SystemNicknameSettingsStore(
                    Path.Combine(nicknameDir, UiSettingsFileName))));
        vm.InstallEditorPreview(State);
        return vm;
    }

    public static SurfaceSurveyOverlayViewModel CreateSurfaceSurvey()
    {
        var root = SettingsDir("surface");
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(Path.Combine(root, UiSettingsFileName)));
        var store = new SystemSurfaceStore(root);
        var surface = new SurfaceSurveyViewModel(
            survey,
            store,
            new SurfaceTracker(
                store,
                ExobiologyReferenceCatalog.LoadEmbedded()));
        var acies = Marker(
            "Bacterium Acies",
            SurfaceRadarMarkerKind.ActiveSample,
            146,
            68,
            -6,
            500);
        var tussock = Marker(
            "Tussock Capillum",
            SurfaceRadarMarkerKind.Bookmark,
            412,
            91,
            17,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters("Tussock"));
        var ship = Marker(
            "Ship",
            SurfaceRadarMarkerKind.Ship,
            860,
            184,
            110,
            0);
        surface.InstallEditorPreview(
            State.CurrentBody,
            "HEADING 074°",
            "12 scan circles · 4 trackers",
            [acies, tussock, ship],
            [
                new SurfaceTrackerGroupViewModel(
                    "#1 Bacterium",
                    IsActive: true,
                    [acies]),
                new SurfaceTrackerGroupViewModel(
                    "#2 Tussock",
                    IsActive: true,
                    [tussock]),
                new SurfaceTrackerGroupViewModel(
                    "Ship",
                    IsActive: false,
                    [ship]),
            ]);
        return new SurfaceSurveyOverlayViewModel(surface, Caps());

        static SurfaceRadarMarkerViewModel Marker(
            string name,
            SurfaceRadarMarkerKind kind,
            double distance,
            double bearing,
            double relative,
            double radius) =>
            new()
            {
                Name = name,
                Kind = kind,
                DistanceMeters = distance,
                BearingDegrees = bearing,
                RelativeBearingDegrees = relative,
                RadiusMeters = radius,
                Location = new SurfaceCoordinate(-18.4216, 74.0921),
                IsActive = true,
            };
    }

    public static HumanSiteOverlayViewModel CreateHumanSite()
    {
        var humanSite = new HumanSiteViewModel();
        humanSite.InstallEditorPreview(new HumanSiteEditorPreview
        {
            SiteName = State.SettlementName,
            TemplateText = "Military M2 · threat 2",
            GeometryStatus = "Settlement map aligned",
            FactionText = "Blue Fortune Corp · Anarchy",
            DockingStatusText = "Docking granted · pad 02",
            DistanceText = "186 m from origin",
            ApproachDistanceText = "1.8 km approach distance",
            CommanderPositionText = "x +42.0 m · y -18.0 m · 164°",
            ThreatLevelText = "Threat level 2 · full shield",
            IsQuestTagged = true,
        });
        return new HumanSiteOverlayViewModel(humanSite, Caps());
    }

    public static JumpInfoOverlayViewModel CreateJumpInfo()
    {
        var jump = new JumpInfoViewModel(
            new EmptySystemSummaryClient(),
            new JumpInfoSettingsStore(
                Path.Combine(SettingsDir("jump-info"), UiSettingsFileName)));
        var plan = new JumpInfoRoutePlan(
            new JumpTarget(State.DestinationSystem, 99, "K"),
            JumpInfoRouteSource.FollowedRoute,
            TargetLegIndex: 3,
            Legs:
            [
                new JumpInfoRouteLeg(State.CurrentSystem, "Waypoint A", 42.1, true, false),
                new JumpInfoRouteLeg("Waypoint A", "Waypoint B", 38.4, true, false),
                new JumpInfoRouteLeg("Waypoint B", "Waypoint C", 29.7, false, true),
                new JumpInfoRouteLeg("Waypoint C", State.DestinationSystem, 28.5, true, false),
            ],
            TargetPosition: new GalacticCoordinate(100, 20, -40));
        var summary = new SystemSummary(
            State.DestinationSystem,
            99,
            new GalacticCoordinate(100, 20, -40),
            StarClass: "K",
            IsKnown: true,
            ScannedBodyCount: 12,
            TotalBodyCount: 18,
            DiscoveredBy: "CMDR Raven",
            DiscoveredAt: DateTimeOffset.UtcNow.AddDays(-40),
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            Traffic: new SystemTrafficSummary(12, 84, 1200),
            PointsOfInterest: new SystemPoiSummary(18, 6, 2, 1, 1, 0, 0),
            Specials: []);
        jump.InstallEditorPreview(
            plan,
            summary,
            [
                new JumpInfoDetailLineViewModel("Next hop", "Waypoint C", Refuel: true),
                new JumpInfoDetailLineViewModel("Neutron", "Use boost on B → C", Neutron: true),
                new JumpInfoDetailLineViewModel("Scoopable", "K-class star"),
            ]);
        return new JumpInfoOverlayViewModel(jump, Caps());
    }

    public static FleetCarrierRouteOverlayViewModel CreateFleetCarrierRoute(
        FleetCarrierRouteEditorPreviewState state =
            FleetCarrierRouteEditorPreviewState.Cooldown)
    {
        var temporaryDirectory = SettingsDir("fc-route");
        var vm = new FleetCarrierRouteOverlayViewModel(
            new RouteWorkspaceViewModel(
                new FollowRouteService(new FollowRouteStore(
                    temporaryDirectory,
                    FollowRouteKind.FleetCarrier)),
                new RouteNameImporter(new EmptySystemResolver()),
                new EmptySpanshRouteClient(),
                FollowRouteKind.FleetCarrier),
            Caps());
        var preview = new FleetCarrierRouteEditorPreview(
            HopProgress: "HOP 2 / 46",
            SystemName: "Col 359 Sector EE-X b16-1",
            JumpSummary: "499.76 LY JUMP  •  21,502.09 LY REMAINING",
            JumpsLeft: "44 JUMPS LEFT",
            FuelLeft: "1,000 t",
            TritiumInMarket: "2,799 t",
            JumpFuel: "93 t",
            HasIcyRing: true,
            IcyRingLabel: "PRISTINE ICY RING",
            HasRestockWarning: true,
            RestockAmount: "3,892 t",
            HasCountdown: state is not FleetCarrierRouteEditorPreviewState.RouteOnly,
            CountdownTitle: state == FleetCarrierRouteEditorPreviewState.Scheduled
                ? "JUMP DEPARTURE"
                : "JUMP COOLDOWN",
            Countdown: state == FleetCarrierRouteEditorPreviewState.Scheduled
                ? "12:45"
                : "4:32",
            CountdownPhase: state == FleetCarrierRouteEditorPreviewState.Scheduled
                ? "LOCKED"
                : "LOCKING",
            CountdownPhaseTime: state == FleetCarrierRouteEditorPreviewState.Scheduled
                ? "0:45"
                : "0:18",
            HasCountdownPhaseTime:
                state is not FleetCarrierRouteEditorPreviewState.RouteOnly);
        vm.InstallEditorPreview(preview);
        return vm;
    }

    public static CommanderInstancesViewModel CreateMultiCommander()
    {
        var vm = new CommanderInstancesViewModel(
            new CommanderProfileCatalog(SettingsDir("cmdr")),
            new NoopLauncher(),
            Path.GetTempPath(),
            currentFrontierId: "FDEV-RAVEN");
        vm.UpdateCurrent("FDEV-RAVEN", State.CommanderName.Replace("CMDR ", "", StringComparison.Ordinal));
        return vm;
    }

    public static PriorScansOverlayViewModel CreatePriorScans()
    {
        var survey = new SystemSurveyViewModel(
            new SystemSurveySettingsStore(
                Path.Combine(SettingsDir("prior-scans"), UiSettingsFileName)));
        var vm = new PriorScansOverlayViewModel(
            survey,
            new EmptyCanonnClient(),
            ExobiologyReferenceCatalog.LoadEmbedded(),
            () => State.CommanderName,
            Caps());
        var targetClose = new PriorScanTargetViewModel(
            DistanceMeters: 412,
            RelativeBearingDegrees: 12,
            DistanceText: "412 m",
            BearingText: "074°",
            IsClose: true,
            IsFar: false,
            IsAnalyzed: false);
        var targetFar = new PriorScanTargetViewModel(
            DistanceMeters: 1240,
            RelativeBearingDegrees: -48,
            DistanceText: "1.2 km",
            BearingText: "312°",
            IsClose: false,
            IsFar: true,
            IsAnalyzed: false);
        vm.InstallEditorPreview(
            State.CurrentBody,
            "HEADING 074°",
            [
                new PriorScanSpeciesViewModel(
                    DisplayName: "Bacterium Acies",
                    RewardText: "7.62 M CR",
                    IsAnalyzed: false,
                    IsActive: true,
                    RowOpacity: 1,
                    SampleRadiusMeters: 500,
                    ApproachText: "-28°",
                    HasShallowApproach: true,
                    HasIdealApproach: false,
                    HasSteepApproach: false,
                    HasTooSteepApproach: false,
                    Targets: [targetClose]),
                new PriorScanSpeciesViewModel(
                    DisplayName: "Tussock Capillum",
                    RewardText: "19.01 M CR",
                    IsAnalyzed: true,
                    IsActive: false,
                    RowOpacity: 0.5,
                    SampleRadiusMeters: 150,
                    ApproachText: string.Empty,
                    HasShallowApproach: false,
                    HasIdealApproach: false,
                    HasSteepApproach: false,
                    HasTooSteepApproach: false,
                    Targets: [targetFar]),
                new PriorScanSpeciesViewModel(
                    DisplayName: "Stratum Tectonicas",
                    RewardText: "95.19 M CR",
                    IsAnalyzed: false,
                    IsActive: false,
                    RowOpacity: 1,
                    SampleRadiusMeters: 500,
                    ApproachText: "-54°",
                    HasShallowApproach: false,
                    HasIdealApproach: false,
                    HasSteepApproach: true,
                    HasTooSteepApproach: false,
                    Targets: [targetFar]),
            ],
            [
                new PriorScanRadarTargetViewModel(412, 12, 500, true, true),
                new PriorScanRadarTargetViewModel(1240, -48, 150, false, false),
            ]);
        return vm;
    }

    public static PulseOverlayViewModel CreatePulse(
        PulseEditorPreviewState state = PulseEditorPreviewState.ScoCooling)
    {
        var previewTime = new FrozenTimeProvider(
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));
        var vm = new PulseOverlayViewModel(
            new PulseOverlaySettingsStore(
                Path.Combine(SettingsDir("pulse"), UiSettingsFileName)),
            previewTime);
        vm.Enabled = true;
        vm.InstallEditorPreview(state);
        return vm;
    }

    private sealed class FrozenTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    public static QuestIndicatorViewModel CreateQuest()
    {
        var vm = new QuestIndicatorViewModel();
        vm.Update(
            [
                new QuestRuntimeSnapshot(
                    new RavenQuestReference("Raven", "decrypt-guardian-logs", 1),
                    Title: "Decrypt Guardian logs",
                    Subtitle: "Ram Tah research mission",
                    IsDevelopment: false,
                    IsPaused: false,
                    TerminalState: null,
                    UnreadMessageCount: 2,
                    Objectives: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tech"] = "visible,6,10",
                        ["culture"] = "visible,4,8",
                        ["language"] = "visible,2,10",
                    },
                    ObjectiveLabels: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["tech"] = "Technology logs",
                        ["culture"] = "Culture logs",
                        ["language"] = "Language logs",
                    },
                    Messages: [],
                    Tags: new HashSet<string>(StringComparer.Ordinal)
                    {
                        State.GuardianSiteName,
                    },
                    BodyLocations: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["next"] = "-18.4,74.1,500",
                    },
                    Routes: []),
            ],
            status: null,
            enabled: true);
        return vm;
    }

    public static SphericalSearchOverlayViewModel CreateSphericalSearch()
    {
        var temporaryDirectory = SettingsDir("spherical");
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        var resolver = new EmptySystemResolver();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(temporaryDirectory)),
            new RouteNameImporter(resolver),
            new EmptySpanshRouteClient());
        var vm = new SphericalSearchOverlayViewModel(
            new SphereLimitViewModel(profileStore, resolver),
            new BoxelSearchViewModel(PreviewBoxelSearchSession.Instance),
            route,
            Caps());
        vm.InstallEditorPreview(
            sphereCenter: State.CurrentSystem,
            sphereDestination: State.DestinationSystem,
            boxelNext: "Eol Prou AA-A h23",
            routeNext: State.DestinationSystem);
        return vm;
    }

    public static StationInfoOverlayViewModel CreateStationInfo()
    {
        var stationInfo = new StationInfoViewModel(new EmptySystemSummaryClient());
        stationInfo.InstallEditorPreview(new StationInfoEditorPreview
        {
            StationName = State.StationName,
            StationType = "Coriolis starport",
            LargestPad = "Largest pad: Large",
            PrimaryEconomy = "Primary economy: High Tech",
            Faction = "Raven Colonial Initiative · Confederacy",
            Updated = "Spansh data updated just now",
            IsQuestTagged = true,
            Economies =
            [
                new StationInfoLineViewModel("High Tech", "62%"),
                new StationInfoLineViewModel("Industrial", "38%"),
            ],
            Services =
            [
                "Shipyard",
                "Outfitting",
                "Vista Genomics",
                "Universal Cartographics",
            ],
            Prohibited =
            [
                "Narcotics",
                "Slaves",
            ],
        });
        return new StationInfoOverlayViewModel(stationInfo, Caps());
    }

    public static GroundTargetOverlayViewModel CreateGroundTarget()
    {
        var ground = new GroundTargetViewModel(
            new GroundTargetSettingsStore(SettingsDir("ground-target")));
        ground.InstallEditorPreview(new GroundTargetEditorPreview
        {
            Coordinates = "18.4216°S  74.0921°E",
            Distance = "146 m",
            Bearing = "068°",
            RelativeHeadingText = "+6°",
            Descent = "28°",
            ApproachStatusText = "Ideal approach corridor",
            RelativeBearing = 6,
            AttackAngle = 28,
            ApproachKind = GroundTargetApproach.Ideal,
        });
        return new GroundTargetOverlayViewModel(ground, Caps());
    }

    private sealed class EmptySystemSummaryClient : ISystemSummaryClient
    {
        public Task<SystemSummaryLoadResult> GetAsync(
            string systemName,
            long systemAddress,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SystemSummaryLoadResult(
                new SystemSummary(
                    systemName,
                    systemAddress,
                    Position: null,
                    StarClass: null,
                    IsKnown: null,
                    ScannedBodyCount: 0,
                    TotalBodyCount: 0,
                    DiscoveredBy: null,
                    DiscoveredAt: null,
                    LastUpdatedAt: null,
                    Traffic: null,
                    PointsOfInterest: new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
                    Specials: []),
                []));
    }

    private sealed class EmptyCanonnClient : ICanonnSystemPoiClient
    {
        public Task<CanonnSystemPoiResult> GetAsync(
            string systemName,
            string commanderName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CanonnSystemPoiResult(systemName, []));
    }

    private sealed class EmptySystemResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class EmptySpanshRouteClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }

    private sealed class NoopLauncher : ICommanderInstanceLauncher
    {
        public Task LaunchAsync(
            string frontierId,
            string journalDirectory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PreviewBoxelSearchSession : IBoxelSearchSession
    {
        public static PreviewBoxelSearchSession Instance { get; } = new();

        public BoxelSearchSessionSnapshot Current =>
            BoxelSearchSessionSnapshot.Empty;

        public event EventHandler<BoxelSearchSessionChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<BoxelSearchOutcome> SwitchProfileAsync(
            BoxelSearchProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateRejectedOutcome(
                BoxelSearchMessageCode.SearchNotConfigured));

        public Task<BoxelSearchOutcome> ClearProfileAsync(
            BoxelSearchMessageCode reason = BoxelSearchMessageCode.ProfileUnavailable,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateRejectedOutcome(reason));

        public Task<BoxelSearchOutcome> ApplyAsync(
            BoxelSearchUpdate update,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateRejectedOutcome(
                BoxelSearchMessageCode.SearchNotConfigured));

        public Task<BoxelSearchOutcome> ExecuteAsync(
            BoxelSearchAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateRejectedOutcome(
                BoxelSearchMessageCode.SearchNotConfigured));

        public Task<BoxelSearchLibrarySnapshot> GetLibraryAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoxelSearchLibrarySnapshot(0, []));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private BoxelSearchOutcome CreateRejectedOutcome(
            BoxelSearchMessageCode code)
        {
            var snapshot = Current;
            return new BoxelSearchOutcome(
                BoxelSearchOutcomeKind.Rejected,
                code,
                snapshot.Version,
                snapshot.Search.Version,
                snapshot.Context.Version,
                snapshot.Activity.Version,
                snapshot.Health.Version,
                snapshot.LibraryRevision);
        }
    }
}
