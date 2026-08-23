using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Travel;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan IdleHousekeepingInterval =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultSystemBodyDataRetryDelay =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumSystemBodyDataRetryDelay =
        TimeSpan.FromMinutes(4);
    private const int MaximumSystemBodyDataRetryAttempts = 5;

    private const string Unavailable = "—";
    private const string ExplorationNavigationKey = "exploration";
    private const string ExobiologyNavigationKey = "exobiology";
    private const string TravelNavigationKey = "travel";
    private const string BoxelNavigationKey = "boxel";
    private const string SearchNavigationKey = "search";
    private const string GuardianNavigationKey = "guardian";
    private const string QuestsNavigationKey = "quests";
    private const string ColonisationNavigationKey = "colonisation";
    private const string DiagnosticsNavigationKey = "diagnostics";
    private const string SettingsNavigationKey = "settings";
    private const string SurveyNavigationGroup = "survey";
    private const string NavigationNavigationGroup = "navigation";
    private const string ActivitiesNavigationGroup = "activities";
    private const string CommanderProfileLoadFailedMessage =
        "The commander profile could not be loaded.";

    private readonly JournalFolderResolution folderResolution;
    private readonly JournalDirectoryMonitor? journalMonitor;
    private readonly JournalSessionState journalState = new();
    private readonly ExplorationState explorationState = new();
    private readonly ExobiologyState exobiologyState;
    private readonly CommanderProfileStore commanderProfileStore;
    private readonly CommanderCodexStore commanderCodexStore;
    private readonly CommanderCodexJournalTracker commanderCodexJournalTracker;
    private readonly SystemScanPersistenceStore systemScanPersistenceStore;
    private readonly ISystemBodyDataClient? systemBodyDataClient;
    private readonly TimeSpan systemBodyDataRetryDelay;
    private readonly CargoInventoryState cargoInventoryState = new();
    private readonly FirstFootfallInferenceSettingsStore
        firstFootfallInferenceSettingsStore;
    private readonly IFirstFootfallInferenceService
        firstFootfallInferenceService;
    // DisposeAsync releases these owned resources through failure-isolating
    // helpers. The analyzers cannot follow the delegated cleanup calls.
#pragma warning disable CA2213, S2930
    private readonly CancellationTokenSource firstFootfallInferenceCancellation =
        new();
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The system-body worker disposes the captured source in its finally block.")]
    private CancellationTokenSource? systemBodyDataCancellation;
    private readonly RouteAutoCopyCoordinator routeAutoCopyCoordinator;
    private readonly BoxelSearchSession boxelSearchSession;
    private readonly BoxelSurveyStatsCoordinator boxelSurveyStats;
#pragma warning restore CA2213, S2930
    private readonly GreenGasGiantPublicationCoordinator
        greenGasGiantPublicationCoordinator;
    private readonly IEddnPublisher eddnPublisher;
    private readonly IVoxStellarPublisher voxStellarPublisher;
    private readonly IInaraPublisher inaraPublisher;
    private readonly RavenThemeService? themeService;
    private readonly LegacyProfileImporter profileImporter;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification =
            "DisposeAsync awaits this coordinator through the failure-isolating cleanup helper.")]
    private readonly QuestRuntimeCoordinator questRuntimeCoordinator;
    private readonly QuestSettingsStore questSettingsStore;
    private readonly HttpClient? visitedStarsHttpClient;
    private readonly ApplicationLogService? applicationLogService;
    private Func<DirectoryInfo, Task<bool>>? journalCommandDirectoryLauncher;
    private Func<Task>? journalCommandShutdownRequester;
    private Func<string, Task>? journalCommandClipboardWriter;
    private readonly AsyncCommand importLegacyProfileCommand;
    private readonly AsyncCommand resetExplorationCommand;
    private readonly AsyncCommand cancelResetExplorationCommand;
    private readonly AsyncCommand resetExobiologyCommand;
    private readonly AsyncCommand cancelResetExobiologyCommand;
    private readonly AsyncCommand clearSurfaceTrackersCommand;
    private readonly AsyncCommand toggleFirstFootfallCommand;
    private bool isBusy;
    private bool isImportingProfile;
    private string statusMessage;
    private string commanderName = Unavailable;
    private string frontierId = Unavailable;
    private string gameDescription = Unavailable;
    private string gameMode = Unavailable;
    private string systemDescription = Unavailable;
    private string overviewSystemName = Unavailable;
    private long? overviewSystemAddress;
    private string bodyName = Unavailable;
    private string sessionState = "Waiting for journal";
    private string lastUpdated = string.Empty;
    private string themeStatusMessage = string.Empty;
    private string vehicleState = Unavailable;
    private string surfacePosition = Unavailable;
    private string headingAndAltitude = Unavailable;
    private string gameUiFocus = Unavailable;
    private string estimatedExplorationValue = "0 CR";
    private string explorationJumps = "0";
    private string explorationDistance = "0.0 ly";
    private string explorationBodies = "Scanned: 0, DSS: 0, Landed: 0";
    private string explorationStatusMessage = "Waiting for commander profile.";
    private bool isResetExplorationPending;
    private string unclaimedBioRewards = "0 CR";
    private string unclaimedBioScans = "0 samples";
    private string organicScanProgress = "Ready for sample 1 of 3";
    private string activeOrganicSpecies = Unavailable;
    private string organicSampleRange = Unavailable;
    private string bioFirstFootfall = "Unknown";
    private bool isCurrentBodyFirstFootfall;
    private bool canToggleCurrentBodyFirstFootfall;
    private bool isOrganicSample1Complete;
    private bool isOrganicSample2Complete;
    private string exobiologyStatusMessage = "Waiting for commander profile.";
    private string commanderCodexStatusMessage =
        "Waiting for Commander Codex journal entries.";
    private bool isResetExobiologyPending;
    private string? activeProfileFrontierId;
    private string? activeProfileCommanderName;
    private bool activeProfileIsOdyssey = true;
    private NavigationItemViewModel? selectedNavigation;
    private string? expandedNavigationGroup = SurveyNavigationGroup;
    private DiagnosticsWorkspaceTab selectedDiagnosticsTab =
        DiagnosticsWorkspaceTab.Source;
    private bool isProfileSelected;
    private ThemeOptionViewModel selectedTheme;
    private LegacyProfileOptionViewModel? selectedLegacyProfile;
    private string legacyProfileSourcePath;
    private string profileStatusMessage;
    private string settingsLinkStatusMessage = string.Empty;
    private string questStatusMessage = "Quests are disabled.";
    private string? activeProfileRavenApiKey;
    private string? surveyCodexFrontierId;
    private int? surveyCodexRegionId;
    private long? surveyCodexSystemAddress;
    private long? activeSystemVisitAddress;
    private DateTimeOffset? activeSystemVisitedAt;
    private string? loadedSystemHistoryKey;
    private string? loadedSystemBodyDataKey;
    private DateTimeOffset? systemBodyDataRetryAt;
    private int systemBodyDataRetryAttempts;
    private EliteStatus? latestStatus;
    private CargoSnapshot? latestCargo;
    private ShipLockerSnapshot? latestShipLocker;
    private bool awaitFreshCargoSnapshot;
    private DateTimeOffset? companionIdentityChangedAt;
    private DateTimeOffset lastIdleHousekeepingAt;
    private bool isAwaitingCommanderIdentity;
    private bool disposed;

    public MainWindowViewModel(string? configuredJournalDirectory)
        : this(
            configuredJournalDirectory,
            new MainWindowViewModelConstructionContext())
    {
    }

    internal MainWindowViewModel(
        string? configuredJournalDirectory,
        MainWindowViewModelConstructionContext construction)
    {
        ArgumentNullException.ThrowIfNull(construction);
        var foundation = construction.Foundation;
        var overlay = construction.Overlay;
        var exploration = construction.Exploration;
        var travel = construction.Travel;
        var online = construction.Online;
        // Locals that would shadow instance fields use a resolved* prefix (S1117).
        var resolvedThemeService = foundation.ThemeService;
        var appDataPaths = foundation.AppDataPaths;
        var boxelSystemResolver = exploration.BoxelSystemResolver;
        var inputSettings = foundation.InputSettings;
        var guardianOverlaySettingsStore = overlay.GuardianOverlaySettingsStore;
        var stationInfoSettingsStore = travel.StationInfoSettingsStore;
        var humanSiteSettingsStore = exploration.HumanSiteSettingsStore;
        var resolvedApplicationLogService = foundation.ApplicationLogService;
        var overlayLayoutStore = overlay.OverlayLayoutStore;
        var overlayLayout = overlay.OverlayLayout;
        var screenshotProcessingService = overlay.ScreenshotProcessingService;
        var targetFrontierId = foundation.TargetFrontierId;
        var gameWindowSwitcher = travel.GameWindowSwitcher;
        var resolvedGreenGasGiantPublicationCoordinator =
            online.GreenGasGiantPublicationCoordinator;
        var desktopBehaviorSettingsStore =
            overlay.DesktopBehaviorSettingsStore;
        var commanderPreferenceSettingsStore =
            foundation.CommanderPreferenceSettingsStore;
        var commanderPreferenceCommandLineOverride =
            foundation.CommanderPreferenceCommandLineOverride;
        var commanderPreferenceInitialStatus =
            foundation.CommanderPreferenceInitialStatus;
        var resolvedFirstFootfallInferenceService =
            exploration.FirstFootfallInferenceService;
        var overlayThemeSettings = overlay.OverlayThemeSettings;
        var overlayInteraction = overlay.OverlayInteraction;
        var canonnHumanSiteClient = online.CanonnHumanSiteClient;
        var canonnHumanSitePublisher = online.CanonnHumanSitePublisher;
        var resolvedEddnPublisher = online.EddnPublisher;
        var resolvedVoxStellarPublisher = online.VoxStellarPublisher;
        var resolvedSystemBodyDataClient = exploration.SystemBodyDataClient;
        var resolvedInaraPublisher = online.InaraPublisher;
        var frontierProfile = foundation.FrontierProfile;

        var rollback = new MainWindowViewModelConstructionRollback(
            resolvedApplicationLogService);
        rollback.Add(firstFootfallInferenceCancellation.Dispose);
        rollback.Add(frontierProfile);
        rollback.Add(overlayInteraction);
        rollback.Add(resolvedFirstFootfallInferenceService);
        var gameWindowOwnership =
            new MainWindowViewModelConstructionOwnership<IGameWindowSwitcher>(
                gameWindowSwitcher);
        rollback.Add(gameWindowOwnership);
        rollback.Add(resolvedInaraPublisher);
        rollback.Add(resolvedEddnPublisher as IDisposable);
        rollback.Add(resolvedVoxStellarPublisher as IDisposable);

        try
        {
            this.themeService = resolvedThemeService;
            this.profileImporter = new LegacyProfileImporter();
            this.applicationLogService = resolvedApplicationLogService;
            AppDataPaths = appDataPaths ?? AppDataPaths.ResolveCurrent();
            var sharedJournalSettingsStore = new JournalSettingsStore(
                AppDataPaths.UiSettingsPath);
            folderResolution = JournalFolderLocator.ResolveCurrent(
                configuredJournalDirectory
                    ?? sharedJournalSettingsStore.Load().Directory);
            FrontierProfile = frontierProfile ?? new CommanderProfileViewModel(
                FrontierAccountService.CreateCurrent(AppDataPaths.DataDirectory),
                communityGoalHistoryReader: CreateCommunityGoalHistoryReader(
                    folderResolution));
            rollback.AddIfCreated(frontierProfile, FrontierProfile);
            var legacyReferences = LegacyReferenceCatalogLoader.Load(
                AppDataPaths.DataDirectory);
            var regionalCodexCandidates = RegionalCodexCandidateCatalog.Load(
                AppDataPaths.DataDirectory);
            var knownSystems = KnownSystemAddressCatalog.Load(
                AppDataPaths.DataDirectory);
            AppendReferenceCatalogWarnings(
                resolvedApplicationLogService,
                legacyReferences.Warnings,
                regionalCodexCandidates.Warnings,
                knownSystems.Warnings);
            ReferenceDataStatus = BuildReferenceDataStatus(
                legacyReferences,
                regionalCodexCandidates,
                knownSystems);

            ReferenceDataUpdates = new ReferenceDataUpdateViewModel(
                new PublishedReferenceUpdateService(),
                AppDataPaths.DataDirectory,
                ReferenceDataStatus,
                CreateReferenceUpdateLogger(resolvedApplicationLogService));
            Localization = new LocalizationViewModel(
                new LocalizationSettingsStore(
                    AppDataPaths.UiSettingsPath,
                    AppDataPaths.DataDirectory));

            var ravenServiceUri = new RavenServiceSettingsStore(
                    AppDataPaths.UiSettingsPath)
                .LoadServiceUri();
            this.questSettingsStore = new QuestSettingsStore(
                AppDataPaths.UiSettingsPath);
            this.questRuntimeCoordinator = new QuestRuntimeCoordinator(
                new LegacyQuestStateStore(AppDataPaths.DataDirectory),
                new RavenQuestClient(serviceUri: ravenServiceUri),
                message => resolvedApplicationLogService?.Append(message));
            rollback.Add(this.questRuntimeCoordinator.DisposeAsync);
            QuestWorkspace = new QuestWorkspaceViewModel(
                this.questRuntimeCoordinator,
                this.questSettingsStore);
            rollback.Add(QuestWorkspace.Dispose);
            QuestIndicator = new QuestIndicatorViewModel();
            this.questRuntimeCoordinator.Changed += OnQuestCoordinatorChanged;
            rollback.Add(() =>
                this.questRuntimeCoordinator.Changed -= OnQuestCoordinatorChanged);
            SystemNicknames = new SystemNicknameViewModel(
                SystemNicknameCatalog.Load(AppDataPaths.DataDirectory),
                new SystemNicknameSettingsStore(AppDataPaths.UiSettingsPath));
            DiagnosticsLog = new DiagnosticsLogViewModel(resolvedApplicationLogService);
            rollback.Add(DiagnosticsLog.Dispose);
            ReleaseUpdates = new ReleaseUpdateViewModel(
                new ReleaseUpdateService(),
                ReleaseVersion.FromAssembly(typeof(MainWindowViewModel).Assembly),
                new ReleaseUpdateSettingsStore(AppDataPaths.UiSettingsPath));
            JournalInspector = new JournalInspectorViewModel(
                ReplayQuestJournalEventAsync);
            JournalSettings = new JournalSettingsViewModel(
                sharedJournalSettingsStore,
                configuredJournalDirectory);
            commanderProfileStore = new CommanderProfileStore(
                AppDataPaths.DataDirectory);
            commanderCodexStore = new CommanderCodexStore(
                AppDataPaths.DataDirectory);
            commanderCodexJournalTracker = new CommanderCodexJournalTracker(
                commanderCodexStore);
            this.systemScanPersistenceStore = new SystemScanPersistenceStore(
                AppDataPaths.DataDirectory);
            this.systemBodyDataClient = resolvedSystemBodyDataClient;
            systemBodyDataRetryDelay = exploration.SystemBodyDataRetryDelay
                ?? DefaultSystemBodyDataRetryDelay;
            this.firstFootfallInferenceSettingsStore =
                new FirstFootfallInferenceSettingsStore(
                    AppDataPaths.UiSettingsPath);
            this.firstFootfallInferenceService = resolvedFirstFootfallInferenceService
                ?? new UnavailableFirstFootfallInferenceService();
            rollback.AddIfCreated(
                resolvedFirstFootfallInferenceService,
                this.firstFootfallInferenceService);
            construction.Checkpoint?.Invoke(
                MainWindowViewModelConstructionCheckpoint.FoundationReady);
            InputSettings = inputSettings ?? new GlobalInputSettingsViewModel(
                new GlobalInputSettingsStore(AppDataPaths.UiSettingsPath),
                OverlayPlatformCapabilities.DetectCurrent());
            OverlayPanelVisibility = new OverlayPanelVisibilityViewModel(
                new OverlayPanelVisibilitySettingsStore(AppDataPaths.UiSettingsPath),
                InputSettings);
            var sharedGameWindowSwitcher = gameWindowSwitcher
                ?? GameWindowSwitcher.CreateCurrent();
            gameWindowOwnership.Own(sharedGameWindowSwitcher);
            DesktopBehavior = new DesktopBehaviorViewModel(
                desktopBehaviorSettingsStore
                    ?? new DesktopBehaviorSettingsStore(AppDataPaths.UiSettingsPath),
                sharedGameWindowSwitcher);
            var sharedOverlayLayoutStore = overlayLayoutStore
                ?? new LegacyOverlayLayoutStore(AppDataPaths.DataDirectory);
            var activeOverlayLayout = overlayLayout
                ?? sharedOverlayLayoutStore.Load();
            OverlayLayout = new OverlayLayoutSettingsViewModel(
                sharedOverlayLayoutStore,
                activeOverlayLayout);
            OverlayScale = new OverlayScaleSettingsViewModel(
                new OverlayScaleSettingsStore(AppDataPaths.UiSettingsPath),
                activeOverlayLayout);
            OverlayBehavior = new OverlayBehaviorViewModel(
                new OverlayBehaviorSettingsStore(AppDataPaths.UiSettingsPath));
            OverlayInteraction = overlayInteraction ?? new OverlayInteractionViewModel(
                OverlayPlatformCapabilities.DetectCurrent());
            rollback.AddIfCreated(overlayInteraction, OverlayInteraction);
            OverlayInteractionBinding = InputSettings.Bindings.Single(binding =>
                binding.Definition.Action
                    == GlobalInputAction.ToggleOverlayInteraction);
            OverlayTheme = overlayThemeSettings ?? new OverlayThemeSettingsViewModel(
                new LegacyOverlayThemeStore(
                    Path.Combine(AppDataPaths.DataDirectory, "theme.json")),
                new OverlayThemeStateStore(
                    Path.Combine(
                        AppDataPaths.DataDirectory,
                        "overlay-theme-states.json")),
                resolvedThemeService);
            ScreenshotProcessing = new ScreenshotProcessingViewModel(
                new ScreenshotProcessingSettingsStore(AppDataPaths.UiSettingsPath),
                screenshotProcessingService);
            construction.Checkpoint?.Invoke(
                MainWindowViewModelConstructionCheckpoint.OverlayReady);
            DockToDock = new DockToDockViewModel(
                new DockToDockSettingsStore(AppDataPaths.UiSettingsPath),
                new DockToDockLogService(
                    DockToDockCsvWriter.GetDefaultPath()));
            Notifications = new NotificationViewModel(
                new NotificationSettingsStore(AppDataPaths.UiSettingsPath));
            PulseOverlay = new PulseOverlayViewModel(
                new PulseOverlaySettingsStore(AppDataPaths.UiSettingsPath));
            StreamOverlay = new StreamOverlayViewModel(
                new StreamOverlaySettingsStore(AppDataPaths.UiSettingsPath));
            VrOverlay = new VrOverlayViewModel(
                new VrOverlaySettingsStore(AppDataPaths.UiSettingsPath),
                new VrOverlayCalibrationStore(AppDataPaths.DataDirectory));
            NetworkPrivacy = new NetworkPrivacyViewModel(
                new NetworkPrivacySettingsStore(AppDataPaths.UiSettingsPath));
            Inara = new InaraSettingsViewModel(commanderProfileStore);
            this.inaraPublisher = resolvedInaraPublisher ?? new InaraPublisher(
                (typeof(MainWindowViewModel).Assembly.GetName().Version
                    ?? new Version(0, 0)).ToString());
            rollback.AddIfCreated(resolvedInaraPublisher, this.inaraPublisher);
            Inara.ApiKeyChanged += OnInaraApiKeyChanged;
            rollback.Add(() => Inara.ApiKeyChanged -= OnInaraApiKeyChanged);
            this.eddnPublisher = resolvedEddnPublisher ?? new EddnPublisher(
                (typeof(MainWindowViewModel).Assembly.GetName().Version
                    ?? new Version(0, 0)).ToString(),
                outboxPath: Path.Combine(
                    AppDataPaths.DataDirectory,
                    "eddn-outbox-v1.json"),
                log: message => resolvedApplicationLogService?.Append(message));
            rollback.AddIfCreated(
                resolvedEddnPublisher,
                this.eddnPublisher as IDisposable);
            NetworkPrivacy.EddnUploadEnabledChanged += OnEddnUploadEnabledChanged;
            rollback.Add(() =>
                NetworkPrivacy.EddnUploadEnabledChanged -=
                    OnEddnUploadEnabledChanged);
            this.eddnPublisher.SetEnabled(NetworkPrivacy.EddnUploadEnabled);
            this.voxStellarPublisher = resolvedVoxStellarPublisher
                ?? new VoxStellarPublisher(
                    (typeof(MainWindowViewModel).Assembly.GetName().Version
                        ?? new Version(0, 0)).ToString(),
                    VoxStellarSharedKeyProvider.GetSharedKey(),
                    log: message => resolvedApplicationLogService?.Append(message));
            VoxStellar = new VoxStellarSharingViewModel(
                new VoxStellarSettingsStore(AppDataPaths.UiSettingsPath),
                this.voxStellarPublisher.IsConfigured);
            rollback.AddIfCreated(
                resolvedVoxStellarPublisher,
                this.voxStellarPublisher as IDisposable);
            VoxStellar.UploadEnabledChanged += OnVoxStellarUploadEnabledChanged;
            rollback.Add(() =>
                VoxStellar.UploadEnabledChanged -= OnVoxStellarUploadEnabledChanged);
            this.voxStellarPublisher.SetEnabled(
                VoxStellar.JournalUploadEnabled);
            this.greenGasGiantPublicationCoordinator =
                resolvedGreenGasGiantPublicationCoordinator
                    ?? new GreenGasGiantPublicationCoordinator(
                        legacyReferences.GreenGasGiants,
                        new GreenGasGiantClient(serviceUri: ravenServiceUri));
            Colonization = new ColonizationViewModel(
                new ColonizationSettingsStore(AppDataPaths.UiSettingsPath),
                client: new RavenColonialClient(serviceUri: ravenServiceUri),
                commanderProfileStore: commanderProfileStore,
                legacyProfileStore: new LegacyColonizationProfileStore(
                    AppDataPaths.DataDirectory));
            rollback.Add(Colonization.Dispose);
            var sharedSystemResolver = new SpanshStarSystemResolver();
            var sharedExobiologyCatalog = legacyReferences.Exobiology;
            var defaultCodexImageCache = Path.Combine(
                AppDataPaths.CacheDirectory,
                "codex-images");
            CodexImages = new CodexImageSettingsViewModel(
                new CodexImageSettingsStore(
                    AppDataPaths.UiSettingsPath,
                    defaultCodexImageCache),
                sharedExobiologyCatalog,
                defaultCodexImageCache);
            var systemNoteStore = new SystemNoteStore(AppDataPaths.DataDirectory);
            var systemNotesSettingsStore = new SystemNotesSettingsStore(
                AppDataPaths.DataDirectory);
            var journeyService = new JourneyService(
                new JourneyStore(AppDataPaths.DataDirectory),
                new JourneyJournalHistoryReader(
                    ResolveJournalPathOrDefault(
                        folderResolution,
                        AppDataPaths.DataDirectory)),
                commanderProfileStore,
                sharedExobiologyCatalog);
            Search = new SphereLimitViewModel(
                commanderProfileStore,
                sharedSystemResolver);
            NearestSystems = new NearestSystemsViewModel(
                new NearestSystemsClient(),
                sharedSystemResolver);
            boxelSurveyStats = new BoxelSurveyStatsCoordinator(
                new BoxelSurveyStatsStore(AppDataPaths.DataDirectory));
            rollback.Add(boxelSurveyStats.DisposeAsync);
            boxelSurveyStats.TreatNavBeaconAsFullyScanned =
                new BoxelSurveyStatsSettingsStore(AppDataPaths.UiSettingsPath)
                    .Load()
                    .TreatNavBeaconAsFullyScanned;
            BoxelClipboard = new BoxelClipboardAdapter();
            boxelSearchSession = new BoxelSearchSession(
                commanderProfileStore,
                new LegacySystemDataReader(AppDataPaths.DataDirectory),
                new EmptyBoxelStore(AppDataPaths.DataDirectory),
                new SavedBoxelSearchStore(AppDataPaths.DataDirectory),
                boxelSystemResolver ?? new SpanshBoxelClient(),
                new BoxelSearchSessionServices
                {
                    Clipboard = BoxelClipboard,
                    Diagnostics = resolvedApplicationLogService is null
                        ? null
                        : new ApplicationLogBoxelSearchDiagnosticSink(
                            resolvedApplicationLogService),
                });
            rollback.Add(boxelSearchSession.DisposeAsync);
            BoxelSearch = new BoxelSearchViewModel(
                boxelSearchSession,
                knownSystems: knownSystems,
                systemNameSuggestionClient:
                    new FallbackSystemNameSuggestionClient(
                        new EdsmSystemNameSuggestionClient(),
                        new ArdentSystemNameSuggestionClient()),
                surveyStats: boxelSurveyStats);
            rollback.Add(BoxelSearch.CancelPendingOperations);
            construction.Checkpoint?.Invoke(
                MainWindowViewModelConstructionCheckpoint.ExplorationReady);
            GroundTarget = new GroundTargetViewModel(
                new GroundTargetSettingsStore(AppDataPaths.DataDirectory));
            SystemNotes = new SystemNotesViewModel(
                systemNoteStore,
                systemNotesSettingsStore,
                journeyService);
            Journey = new JourneyWorkspaceViewModel(
                journeyService,
                sharedSystemResolver,
                systemNoteStore,
                systemNotesSettingsStore);
            var spanshRouteClient = new SpanshRouteClient();
            var routeNameImporter = new RouteNameImporter(sharedSystemResolver);
            var routeService = new FollowRouteService(
                new FollowRouteStore(AppDataPaths.DataDirectory));
            Route = new RouteWorkspaceViewModel(
                routeService,
                routeNameImporter,
                spanshRouteClient);
            RouteManager = new RouteManagerViewModel(routeService, Route);
            var fleetCarrierRouteService = new FollowRouteService(
                new FollowRouteStore(
                    AppDataPaths.DataDirectory,
                    FollowRouteKind.FleetCarrier));
            FleetCarrierRoute = new RouteWorkspaceViewModel(
                fleetCarrierRouteService,
                routeNameImporter,
                spanshRouteClient,
                FollowRouteKind.FleetCarrier);
            FleetCarrierRouteManager = new RouteManagerViewModel(
                fleetCarrierRouteService,
                FleetCarrierRoute);
            routeAutoCopyCoordinator = new RouteAutoCopyCoordinator(
                Route,
                FleetCarrierRoute,
                boxelSearchSession);
            rollback.Add(routeAutoCopyCoordinator.Dispose);
            var sharedJumpInfoSettingsStore = new JumpInfoSettingsStore(
                AppDataPaths.UiSettingsPath);
            var sharedSystemSummaryClient = new SystemSummaryClient(
                useSpanshLastUpdated: () => sharedJumpInfoSettingsStore
                    .Load()
                    .UseSpanshLastUpdated);
            JumpInfo = new JumpInfoViewModel(
                sharedSystemSummaryClient,
                sharedJumpInfoSettingsStore,
                legacyReferences.GuardianSites);
            rollback.Add(JumpInfo.Dispose);
            GalaxyMap = new GalaxyMapOverlayViewModel(
                sharedSystemSummaryClient,
                new GalaxyMapSettingsStore(AppDataPaths.UiSettingsPath),
                SystemNicknames);
            rollback.Add(GalaxyMap.Dispose);
            StationInfo = new StationInfoViewModel(
                sharedSystemSummaryClient,
                stationInfoSettingsStore
                    ?? new StationInfoSettingsStore(AppDataPaths.UiSettingsPath));
            rollback.Add(StationInfo.Dispose);
            construction.Checkpoint?.Invoke(
                MainWindowViewModelConstructionCheckpoint.TravelReady);
            BiologyRewards = new BiologyRewardSettingsViewModel(
                new BiologyRewardSettingsStore(AppDataPaths.UiSettingsPath));
            SystemSurvey = new SystemSurveyViewModel(
                new SystemSurveySettingsStore(AppDataPaths.UiSettingsPath),
                biologyCatalog: sharedExobiologyCatalog,
                biologyRewardThresholds: BiologyRewards.Thresholds,
                biologyCriteria: legacyReferences.BiologyCriteria,
                regionalCodexCandidates: regionalCodexCandidates);
            HumanSite = new HumanSiteViewModel(
                new HumanSiteViewModelOptions
                {
                    SettingsStore = humanSiteSettingsStore
                        ?? new HumanSiteSettingsStore(AppDataPaths.UiSettingsPath),
                    KnowledgeStore = new HumanSiteKnowledgeStore(
                        AppDataPaths.DataDirectory),
                    MaterialStore = new HumanSiteMaterialStore(
                        AppDataPaths.DataDirectory),
                    TemplateCatalog = legacyReferences.HumanSiteTemplates,
                    CanonnClient = canonnHumanSiteClient,
                    UseExternalData = () => SystemSurvey.UseExternalData,
                    CanonnPublisher = canonnHumanSitePublisher,
                    PublishCanonnGeometry = () =>
                        NetworkPrivacy.UploadHumanSettlementGeometry,
                    ReportCanonnPublication = result =>
                    {
                        NetworkPrivacy.ReportPublicationResult(result);
                        if (!string.IsNullOrWhiteSpace(result.Warning))
                        {
                            applicationLogService?.Append(result.Warning);
                        }
                    },
                });
            BiologyRewards.PropertyChanged += OnBiologyRewardsChanged;
            rollback.Add(() =>
                BiologyRewards.PropertyChanged -= OnBiologyRewardsChanged);
            Combat = new CombatViewModel(
                new CombatSettingsStore(AppDataPaths.UiSettingsPath),
                commanderProfileStore);
            var systemSurfaceStore = new SystemSurfaceStore(
                AppDataPaths.DataDirectory);
            SurfaceSurvey = new SurfaceSurveyViewModel(
                SystemSurvey,
                systemSurfaceStore,
                new SurfaceSurveyJournalTracker(
                    systemSurfaceStore,
                    sharedExobiologyCatalog));
            rollback.Add(SurfaceSurvey.Dispose);
            BiologyPredictions = new BiologyPredictionsViewModel(
                SystemSurvey,
                new BiologyPredictionsSettingsStore(
                    AppDataPaths.UiSettingsPath));
            rollback.Add(BiologyPredictions.Dispose);
            BiologyCodex = new BiologyCodexViewModel(
                SystemSurvey,
                sharedExobiologyCatalog,
                legacyReferences.BiologyCriteria,
                () => activeProfileCommanderName ?? journalState.CommanderName);
            rollback.Add(BiologyCodex.Dispose);
            var journalImportDirectory = ResolveJournalPathOrDefault(
                folderResolution,
                AppDataPaths.DataDirectory);
            ProfileBackupDirectory = Path.Combine(
                Path.GetDirectoryName(AppDataPaths.DataDirectory)
                    ?? AppDataPaths.ConfigDirectory,
                "legacy-backups");
            CodexBingo = new BiologyCodexBingoViewModel(
                commanderCodexStore,
                sharedExobiologyCatalog,
                new CanonnCodexChallengeImporter(
                    new CanonnCodexChallengeClient(),
                    commanderCodexStore,
                    sharedExobiologyCatalog),
                new CommanderCodexJournalImporter(
                    journalImportDirectory,
                    commanderCodexStore),
                new CodexDiscoveryLocationClient());
            rollback.Add(CodexBingo.Dispose);
            JournalPostProcessor = new JournalPostProcessorViewModel(
                new CommanderProfileCatalog(AppDataPaths.DataDirectory),
                new JournalHistoryAnalyzer(journalImportDirectory),
                new LegacySystemBiologyAnalyzer(AppDataPaths.DataDirectory),
                new HistoricalSystemRebuildService(
                    AppDataPaths.DataDirectory,
                    journalImportDirectory,
                    Path.Combine(
                        ProfileBackupDirectory,
                        "historical-systems")),
                new CommanderCodexJournalImporter(
                    journalImportDirectory,
                    commanderCodexStore),
                new GreenGasGiantClient(serviceUri: ravenServiceUri),
                () => NetworkPrivacy.UploadGreenGasGiantCandidates);
            rollback.Add(JournalPostProcessor.Cancel);
            RamTah = new RamTahViewModel(commanderProfileStore);
            Guardian = new GuardianViewModel(
                AppDataPaths.DataDirectory,
                new GuardianViewModelOptions
                {
                    References = legacyReferences.GuardianSites,
                    PublishedSites = legacyReferences.GuardianPublishedSites,
                    Templates = legacyReferences.GuardianTemplates,
                    RamTah = RamTah,
                    OverlaySettingsStore = guardianOverlaySettingsStore
                        ?? new GuardianOverlaySettingsStore(
                            AppDataPaths.UiSettingsPath),
                    GesturePreferences = new GuardianGestureSettingsStore(
                        AppDataPaths.UiSettingsPath).Load(),
                    AerialAltitudeProvider = () => new GuardianAerialAltitudes(
                        ScreenshotProcessing.AerialAltitudeAlpha,
                        ScreenshotProcessing.AerialAltitudeBeta,
                        ScreenshotProcessing.AerialAltitudeGamma),
                    ScreenshotTargetFolderProvider = () =>
                        ScreenshotProcessing.TargetFolder,
                });
            rollback.Add(Guardian.Dispose);
            ScreenshotProcessing.PropertyChanged += OnScreenshotProcessingChanged;
            rollback.Add(() =>
                ScreenshotProcessing.PropertyChanged -=
                    OnScreenshotProcessingChanged);
            exobiologyState = new ExobiologyState(sharedExobiologyCatalog);
            LegacyProfiles = LegacyProfileLocator.Discover(
                    AppDataPaths.LegacyProfileCandidates)
                .Select(discovery => new LegacyProfileOptionViewModel(discovery))
                .ToArray();
            selectedLegacyProfile = SelectInitialLegacyProfile(LegacyProfiles);
            legacyProfileSourcePath = selectedLegacyProfile?.Path ?? string.Empty;
            profileStatusMessage = GetInitialProfileStatus();
            importLegacyProfileCommand = new AsyncCommand(
                ImportLegacyProfileAsync,
                CanImportLegacyProfile);
            ImportLegacyProfileCommand = importLegacyProfileCommand;
            JournalFolderPath = ResolvePrimaryJournalPath(folderResolution)
                ?? "No journal location is configured.";
            CandidatePaths = FormatCandidatePathsDisplay(folderResolution);
            TargetFrontierId = NormalizeOptionalId(targetFrontierId);
            var commanderProfileCatalog = new CommanderProfileCatalog(
                AppDataPaths.DataDirectory);
            CommanderPreference = new CommanderPreferenceViewModel(
                commanderPreferenceSettingsStore
                    ?? new CommanderPreferenceSettingsStore(
                        AppDataPaths.UiSettingsPath),
                commanderProfileCatalog,
                commanderPreferenceCommandLineOverride,
                commanderPreferenceInitialStatus);
            CommanderInstances = new CommanderInstancesViewModel(
                commanderProfileCatalog,
                new ApplicationCommanderInstanceLauncher(),
                JournalFolderPath,
                TargetFrontierId,
                sharedGameWindowSwitcher);
            CommanderInstances.PropertyChanged += OnCommanderInstancesPropertyChanged;
            rollback.Add(CommanderInstances.Dispose);
            rollback.Add(() =>
                CommanderInstances.PropertyChanged -=
                    OnCommanderInstancesPropertyChanged);
            gameWindowOwnership.Transfer();
            SetSharedCargoSuppressed(CommanderInstances.HasMultipleGameWindows);
            this.eddnPublisher.SetSuspended(
                CommanderInstances.HasMultipleGameWindows);
            (visitedStarsHttpClient, VisitedStarsCache) = CreateVisitedStarsCache(
                provided: null,
                appDataPaths: AppDataPaths);
            rollback.Add(visitedStarsHttpClient);
            statusMessage = BuildJournalReadyStatus(
                folderResolution.IsFound,
                TargetFrontierId);
            journalMonitor = CreateJournalMonitor(folderResolution, TargetFrontierId);
            RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
            ShowProfileCommand = new AsyncCommand(ShowProfileAsync, () => true);
            resetExplorationCommand = new AsyncCommand(
                ResetExplorationAsync,
                () => activeProfileFrontierId is not null);
            ResetExplorationCommand = resetExplorationCommand;
            cancelResetExplorationCommand = new AsyncCommand(
                CancelResetExplorationAsync,
                () => IsResetExplorationPending);
            CancelResetExplorationCommand = cancelResetExplorationCommand;
            resetExobiologyCommand = new AsyncCommand(
                ResetExobiologyAsync,
                () => activeProfileFrontierId is not null);
            ResetExobiologyCommand = resetExobiologyCommand;
            cancelResetExobiologyCommand = new AsyncCommand(
                CancelResetExobiologyAsync,
                () => IsResetExobiologyPending);
            CancelResetExobiologyCommand = cancelResetExobiologyCommand;
            clearSurfaceTrackersCommand = new AsyncCommand(
                ClearSurfaceTrackersAsync,
                () => activeProfileFrontierId is not null);
            ClearSurfaceTrackersCommand = clearSurfaceTrackersCommand;
            toggleFirstFootfallCommand = new AsyncCommand(
                async () =>
                {
                    await ToggleCurrentBodyFirstFootfallAsync();
                },
                () => CanToggleCurrentBodyFirstFootfall);
            ToggleFirstFootfallCommand = toggleFirstFootfallCommand;

            NavigationItems =
            [
                new("overview", "Overview", "Commander and current journal state"),
                new(
                    ExplorationNavigationKey,
                    "Exploration",
                    "Trip totals and body scans",
                    true),
                new(
                    ExobiologyNavigationKey,
                    "Exobiology",
                    "Organic scans and unclaimed rewards",
                    true),
                new(
                    TravelNavigationKey,
                    "Travel",
                    "Ground targets, journeys, and routes",
                    true),
                new(
                    BoxelNavigationKey,
                    "Boxel",
                    "Procedural boxel searches and completion tracking",
                    true),
                new(
                    SearchNavigationKey,
                    "Search",
                    "Spherical limits and nearby biology"),
                new(
                    GuardianNavigationKey,
                    "Guardian",
                    "Sites, maps, and Ram Tah",
                    true),
                new(
                    QuestsNavigationKey,
                    "Quests",
                    "Communications and active objectives",
                    true),
                new(
                    ColonisationNavigationKey,
                    "Colonization",
                    "Raven Colonial projects",
                    true),
                new(
                    DiagnosticsNavigationKey,
                    "Diagnostics",
                    "Journal source and parsed state"),
                new(SettingsNavigationKey, "Settings", "Application and integration options"),
                new("theme", "Theme", "Application and in-game appearance"),
                new("guides", "Guides", "Help documentation and overlay icon glossary"),
            ];
            selectedNavigation = NavigationItems[0];
            selectedNavigation.IsSelected = true;
            OverviewNavigationItems = NavigationItems
                .Where(item => item.Key == "overview")
                .ToArray();
            SurveyNavigationItems = NavigationItems
                .Where(item => item.Key is ExplorationNavigationKey
                    or ExobiologyNavigationKey
                    or BoxelNavigationKey)
                .ToArray();
            NavigationWorkspaceItems = NavigationItems
                .Where(item => item.Key is TravelNavigationKey
                    or SearchNavigationKey)
                .ToArray();
            ActivityNavigationItems = NavigationItems
                .Where(item => item.Key is GuardianNavigationKey
                    or QuestsNavigationKey
                    or ColonisationNavigationKey)
                .ToArray();
            UtilityNavigationItems = new[]
            {
                SettingsNavigationKey,
                "theme",
                "guides",
                DiagnosticsNavigationKey,
            }
                .Select(key => NavigationItems.Single(item => item.Key == key))
                .ToArray();
            Guides = new GuidesViewModel(GuideCatalog.Create());
            SettingsWorkspace = new SettingsWorkspaceViewModel();

            var currentTheme = themeService?.Current
                ?? RavenThemeCatalog.Get(RavenThemeCatalog.DefaultThemeKey);
            ThemeOptions = RavenThemeCatalog.All
                .Select(theme => new ThemeOptionViewModel(theme, SelectTheme))
                .ToArray();
            selectedTheme = ThemeOptions.Single(
                option => option.Definition.Key == currentTheme.Key);
            construction.Checkpoint?.Invoke(
                MainWindowViewModelConstructionCheckpoint.OnlineAndShellReady);
            rollback.Commit();
        }
        catch
        {
            rollback.Rollback();
            throw;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IReadOnlyList<NavigationItemViewModel> OverviewNavigationItems { get; }

    public IReadOnlyList<NavigationItemViewModel> SurveyNavigationItems { get; }

    public IReadOnlyList<NavigationItemViewModel> NavigationWorkspaceItems { get; }

    public IReadOnlyList<NavigationItemViewModel> ActivityNavigationItems { get; }

    public IReadOnlyList<NavigationItemViewModel> UtilityNavigationItems { get; }

    public bool IsSurveyNavigationExpanded =>
        expandedNavigationGroup == SurveyNavigationGroup;

    public bool IsNavigationNavigationExpanded =>
        expandedNavigationGroup == NavigationNavigationGroup;

    public bool IsActivitiesNavigationExpanded =>
        expandedNavigationGroup == ActivitiesNavigationGroup;

    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; }

    public GuidesViewModel Guides { get; }

    public CommanderProfileViewModel FrontierProfile { get; }

    public GlobalInputSettingsViewModel InputSettings { get; }

    public OverlayPanelVisibilityViewModel OverlayPanelVisibility { get; }

    public DesktopBehaviorViewModel DesktopBehavior { get; }

    public SettingsWorkspaceViewModel SettingsWorkspace { get; }

    public BiologyRewardSettingsViewModel BiologyRewards { get; }

    public OverlayLayoutSettingsViewModel OverlayLayout { get; }

    public OverlayScaleSettingsViewModel OverlayScale { get; }

    public OverlayBehaviorViewModel OverlayBehavior { get; }

    public OverlayInteractionViewModel OverlayInteraction { get; }

    public InputBindingViewModel OverlayInteractionBinding { get; }

    public OverlayThemeSettingsViewModel OverlayTheme { get; }

    public ScreenshotProcessingViewModel ScreenshotProcessing { get; }

    public DockToDockViewModel DockToDock { get; }

    public NotificationViewModel Notifications { get; }

    public PulseOverlayViewModel PulseOverlay { get; }

    public StreamOverlayViewModel StreamOverlay { get; }

    public VrOverlayViewModel VrOverlay { get; }

    public GalaxyMapOverlayViewModel GalaxyMap { get; }

    public NetworkPrivacyViewModel NetworkPrivacy { get; }

    public VoxStellarSharingViewModel VoxStellar { get; }

    public InaraSettingsViewModel Inara { get; }

    public QuestWorkspaceViewModel QuestWorkspace { get; }

    public QuestIndicatorViewModel QuestIndicator { get; }

    public CommanderInstancesViewModel CommanderInstances { get; }

    public bool IsSharedCargoSuppressed =>
        CommanderInstances.HasMultipleGameWindows;

    internal CargoSnapshot? CurrentCargo => latestCargo;

    internal bool IsWaitingForFreshCargoSnapshot => awaitFreshCargoSnapshot;

    public CommanderPreferenceViewModel CommanderPreference { get; }

    public VisitedStarsCacheViewModel VisitedStarsCache { get; }

    public IReadOnlyList<QuestRuntimeSnapshot> Quests =>
        questRuntimeCoordinator.Snapshot;

    public int QuestUnreadMessageCount => Quests.Sum(
        quest => quest.UnreadMessageCount);

    public string QuestStatusMessage
    {
        get => questStatusMessage;
        private set => SetField(ref questStatusMessage, value);
    }

    public AppDataPaths AppDataPaths { get; }

    public Task PendingSystemBodyDataLoad { get; private set; } =
        Task.CompletedTask;

    public GroundTargetViewModel GroundTarget { get; }

    public SystemNotesViewModel SystemNotes { get; }

    public JourneyWorkspaceViewModel Journey { get; }

    public RouteWorkspaceViewModel Route { get; }

    public RouteManagerViewModel RouteManager { get; }

    public RouteWorkspaceViewModel FleetCarrierRoute { get; }

    public RouteManagerViewModel FleetCarrierRouteManager { get; }

    public JumpInfoViewModel JumpInfo { get; }

    public StationInfoViewModel StationInfo { get; }

    public HumanSiteViewModel HumanSite { get; }

    public SystemSurveyViewModel SystemSurvey { get; }

    public SurfaceSurveyViewModel SurfaceSurvey { get; }

    public CombatViewModel Combat { get; }

    public BiologyPredictionsViewModel BiologyPredictions { get; }

    public BiologyCodexViewModel BiologyCodex { get; }

    public CodexImageSettingsViewModel CodexImages { get; }

    public BiologyCodexBingoViewModel CodexBingo { get; }

    public SphereLimitViewModel Search { get; }

    public BoxelSearchViewModel BoxelSearch { get; }

    public IBoxelSearchSession BoxelSearchSession => boxelSearchSession;

    public BoxelClipboardAdapter BoxelClipboard { get; }

    public BoxelSurveyStatsCoordinator BoxelSurveyStats => boxelSurveyStats;

    public NearestSystemsViewModel NearestSystems { get; }

    public GuardianViewModel Guardian { get; }

    public RamTahViewModel RamTah { get; }

    public ColonizationViewModel Colonization { get; }

    public SystemNicknameViewModel SystemNicknames { get; }

    public DiagnosticsLogViewModel DiagnosticsLog { get; }

    public string ReferenceDataStatus { get; }

    public ReferenceDataUpdateViewModel ReferenceDataUpdates { get; }

    public LocalizationViewModel Localization { get; }

    public ReleaseUpdateViewModel ReleaseUpdates { get; }

    public JournalInspectorViewModel JournalInspector { get; }

    public JournalSettingsViewModel JournalSettings { get; }

    public JournalPostProcessorViewModel JournalPostProcessor { get; }

    public IReadOnlyList<LegacyProfileOptionViewModel> LegacyProfiles { get; }

    public string? TargetFrontierId { get; }

    public string ProfileDataDirectory => AppDataPaths.DataDirectory;

    public string ProfileBackupDirectory { get; }

    public ICommand ImportLegacyProfileCommand { get; }

    public event Func<Task>? ProfileImportPreparing;

    public event Func<Task>? ProfileImportCompleted;

    public LegacyProfileOptionViewModel? SelectedLegacyProfile
    {
        get => selectedLegacyProfile;
        set
        {
            if (SetField(ref selectedLegacyProfile, value))
            {
                if (value is not null)
                {
                    LegacyProfileSourcePath = value.Path;
                }

                importLegacyProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LegacyProfileSourcePath
    {
        get => legacyProfileSourcePath;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (!SetField(ref legacyProfileSourcePath, normalized))
            {
                return;
            }

            if (!HasCompletedLegacyImport && !IsImportingProfile)
            {
                ProfileStatusMessage = string.IsNullOrWhiteSpace(normalized)
                    ? "Choose the original SrvSurvey profile folder to import."
                    : (Directory.Exists(normalized)) switch
                    {
                        true => "The selected legacy profile is ready for verified import.",
                        false => "The selected legacy profile folder does not exist or is unavailable."
                    };
            }

            importLegacyProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProfileStatusMessage
    {
        get => profileStatusMessage;
        private set => SetField(ref profileStatusMessage, value);
    }

    public string SettingsLinkStatusMessage
    {
        get => settingsLinkStatusMessage;
        private set => SetField(ref settingsLinkStatusMessage, value);
    }

    public void ReportSettingsLinkResult(
        string description,
        bool launched,
        string? error = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (launched)
        {
            SettingsLinkStatusMessage = $"Opened {description} in the default browser.";
            return;
        }

        var reason = string.IsNullOrWhiteSpace(error)
            ? "the desktop launcher declined the request."
            : error;
        SettingsLinkStatusMessage = $"Could not open {description}: {reason}";
    }

    public string ImportProfileButtonText => IsImportingProfile
        ? "Importing profile..."
        : (HasCompletedLegacyImport) switch
        {
            true => "Legacy profile imported",
            false => "Back up, verify, and import"
        };

    public bool HasCompletedLegacyImport => File.Exists(
        Path.Combine(
            AppDataPaths.DataDirectory,
            LegacyProfileImporter.ManifestFileName));

    public bool IsImportingProfile
    {
        get => isImportingProfile;
        private set
        {
            if (SetField(ref isImportingProfile, value))
            {
                importLegacyProfileCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ImportProfileButtonText));
            }
        }
    }

    public string JournalFolderPath { get; }

    public string? CurrentJournalPath => journalMonitor?.CurrentJournalPath;

    public string CandidatePaths { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ShowProfileCommand { get; }

    public NavigationItemViewModel? SelectedNavigation
    {
        get => selectedNavigation;
        set
        {
            var previous = selectedNavigation;
            if (!SetField(ref selectedNavigation, value))
            {
                return;
            }

            if (previous is not null)
            {
                previous.IsSelected = false;
            }

            if (value is not null)
            {
                value.IsSelected = true;
                ExpandNavigationGroupFor(value.Key);
            }

            if (value is not null)
            {
                isProfileSelected = false;
            }

            RaiseNavigationSelectionChanged();
        }
    }

    public bool IsProfileSelected => isProfileSelected;

    public bool IsOverviewSelected => SelectedNavigation?.Key == "overview"
        && !IsProfileSelected;

    public bool IsExplorationSelected =>
        SelectedNavigation?.Key == ExplorationNavigationKey
        && !IsProfileSelected;

    public bool IsExobiologySelected =>
        SelectedNavigation?.Key == ExobiologyNavigationKey
        && !IsProfileSelected;

    public bool IsTravelSelected =>
        SelectedNavigation?.Key == TravelNavigationKey
        && !IsProfileSelected;

    public bool IsBoxelSelected =>
        SelectedNavigation?.Key == BoxelNavigationKey
        && !IsProfileSelected;

    public bool IsSearchSelected =>
        SelectedNavigation?.Key == SearchNavigationKey
        && !IsProfileSelected;

    public bool IsGuardianSelected =>
        SelectedNavigation?.Key == GuardianNavigationKey
        && !IsProfileSelected;

    public bool IsQuestsSelected =>
        SelectedNavigation?.Key == QuestsNavigationKey
        && !IsProfileSelected;

    public bool IsColonizationSelected =>
        SelectedNavigation?.Key == ColonisationNavigationKey
        && !IsProfileSelected;

    public bool IsDiagnosticsSelected =>
        SelectedNavigation?.Key == DiagnosticsNavigationKey
        && !IsProfileSelected;

    public DiagnosticsWorkspaceTab SelectedDiagnosticsTab
    {
        get => selectedDiagnosticsTab;
        set
        {
            if (!SetField(ref selectedDiagnosticsTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedDiagnosticsTabIndex));
            OnPropertyChanged(nameof(IsDiagnosticsSourceSelected));
            OnPropertyChanged(nameof(IsDiagnosticsUpdatesSelected));
            OnPropertyChanged(nameof(IsDiagnosticsProcessingSelected));
            OnPropertyChanged(nameof(IsDiagnosticsInspectorSelected));
            OnPropertyChanged(nameof(IsDiagnosticsLogsSelected));
            OnPropertyChanged(nameof(DiagnosticsTabTitle));
            OnPropertyChanged(nameof(DiagnosticsTabDescription));
        }
    }

    public int SelectedDiagnosticsTabIndex
    {
        get => (int)SelectedDiagnosticsTab;
        set
        {
            if (Enum.IsDefined(typeof(DiagnosticsWorkspaceTab), value))
            {
                SelectedDiagnosticsTab = (DiagnosticsWorkspaceTab)value;
            }
        }
    }

    public bool IsDiagnosticsSourceSelected =>
        SelectedDiagnosticsTab == DiagnosticsWorkspaceTab.Source;

    public bool IsDiagnosticsUpdatesSelected =>
        SelectedDiagnosticsTab == DiagnosticsWorkspaceTab.Updates;

    public bool IsDiagnosticsProcessingSelected =>
        SelectedDiagnosticsTab == DiagnosticsWorkspaceTab.Processing;

    public bool IsDiagnosticsInspectorSelected =>
        SelectedDiagnosticsTab == DiagnosticsWorkspaceTab.Inspector;

    public bool IsDiagnosticsLogsSelected =>
        SelectedDiagnosticsTab == DiagnosticsWorkspaceTab.Logs;

    public string DiagnosticsTabTitle => SelectedDiagnosticsTab switch
    {
        DiagnosticsWorkspaceTab.Source => "Journal source",
        DiagnosticsWorkspaceTab.Updates => "Updates",
        DiagnosticsWorkspaceTab.Processing => "Processing",
        DiagnosticsWorkspaceTab.Inspector => "Inspector",
        DiagnosticsWorkspaceTab.Logs => "Logs",
        _ => "Diagnostics",
    };

    public string DiagnosticsTabDescription => SelectedDiagnosticsTab switch
    {
        DiagnosticsWorkspaceTab.Source =>
            "The active bootstrap source and the locations checked on this platform.",
        DiagnosticsWorkspaceTab.Updates =>
            "Check application and reference-data releases without changing profile data.",
        DiagnosticsWorkspaceTab.Processing =>
            "Process historical journals and maintain supporting reference data.",
        DiagnosticsWorkspaceTab.Inspector =>
            "Inspect parsed journal events and the current live status state.",
        DiagnosticsWorkspaceTab.Logs =>
            "Review, copy, and open this application's diagnostic logs.",
        _ => string.Empty,
    };

    public bool IsSettingsSelected => SelectedNavigation?.Key == SettingsNavigationKey
        && !IsProfileSelected;

    public bool IsThemeSelected => SelectedNavigation?.Key == "theme"
        && !IsProfileSelected;

    public bool IsGuidesSelected => SelectedNavigation?.Key == "guides"
        && !IsProfileSelected;

    public async Task ShowProfileAsync()
    {
        if (!isProfileSelected)
        {
            isProfileSelected = true;
            selectedNavigation?.IsSelected = false;
            selectedNavigation = null;
            OnPropertyChanged(nameof(SelectedNavigation));
            RaiseNavigationSelectionChanged();
        }

        await FrontierProfile.OpenAsync(CancellationToken.None);
    }

    public void ToggleNavigationGroup(string groupKey)
    {
        if (groupKey is not (SurveyNavigationGroup
            or NavigationNavigationGroup
            or ActivitiesNavigationGroup))
        {
            return;
        }

        SetExpandedNavigationGroup(
            expandedNavigationGroup == groupKey ? null : groupKey);
    }

    private void ExpandNavigationGroupFor(string navigationKey)
    {
        var group = navigationKey switch
        {
            ExplorationNavigationKey
                or ExobiologyNavigationKey
                or BoxelNavigationKey => SurveyNavigationGroup,
            TravelNavigationKey
                or SearchNavigationKey => NavigationNavigationGroup,
            GuardianNavigationKey
                or QuestsNavigationKey
                or ColonisationNavigationKey =>
                ActivitiesNavigationGroup,
            _ => null,
        };
        if (group is not null)
        {
            SetExpandedNavigationGroup(group);
        }
    }

    private void SetExpandedNavigationGroup(string? groupKey)
    {
        if (expandedNavigationGroup == groupKey)
        {
            return;
        }

        expandedNavigationGroup = groupKey;
        OnPropertyChanged(nameof(IsSurveyNavigationExpanded));
        OnPropertyChanged(nameof(IsNavigationNavigationExpanded));
        OnPropertyChanged(nameof(IsActivitiesNavigationExpanded));
    }

    private void RaiseNavigationSelectionChanged()
    {
        OnPropertyChanged(nameof(IsProfileSelected));
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsExplorationSelected));
        OnPropertyChanged(nameof(IsExobiologySelected));
        OnPropertyChanged(nameof(IsTravelSelected));
        OnPropertyChanged(nameof(IsBoxelSelected));
        OnPropertyChanged(nameof(IsSearchSelected));
        OnPropertyChanged(nameof(IsGuardianSelected));
        OnPropertyChanged(nameof(IsQuestsSelected));
        OnPropertyChanged(nameof(IsColonizationSelected));
        OnPropertyChanged(nameof(IsDiagnosticsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsThemeSelected));
        OnPropertyChanged(nameof(IsGuidesSelected));
    }

    public void ShowDiagnostics(DiagnosticsWorkspaceTab? selectedTab = null)
    {
        if (selectedTab is not null)
        {
            SelectedDiagnosticsTab = selectedTab.Value;
        }

        SelectedNavigation = NavigationItems.Single(
            item => item.Key == DiagnosticsNavigationKey);
    }

    public void ShowSettings()
    {
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == SettingsNavigationKey);
    }

    public bool BeginVrAdjustment()
    {
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == SettingsNavigationKey);
        return VrOverlay.BeginAdjustment();
    }

    public string? CurrentVrOverlayMode
    {
        get
        {
            var status = latestStatus;
            if (status is null)
            {
                return journalState.ShipType;
            }

            return status.GuiFocus switch
            {
                GuiFocus.GalaxyMap => "GalaxyMap",
                GuiFocus.SystemMap => "SystemMap",
                GuiFocus.Orrery => "Orrery",
                GuiFocus.Fss => "FSS",
                GuiFocus.Saa => "SAA",
                _ when status.OnFoot => "OnFoot",
                _ when status.InFighter => "fighter",
                _ when status.InSrv => journalState.ActiveSrvType
                    ?? "testbuggy",
                _ => journalState.ShipType,
            };
        }
    }

    public void ShowQuests()
    {
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == QuestsNavigationKey);
    }

    public async Task OpenCodexBingoNearestSearchAsync(
        CodexBingoNearestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == SearchNavigationKey);
        if (request.Mode == CodexBingoNearestMode.Signal
            && !string.IsNullOrWhiteSpace(request.Signal))
        {
            await NearestSystems.SearchCodexSignalAsync(request.Signal);
            return;
        }

        if (request.Mode == CodexBingoNearestMode.MissingVariants
            && !string.IsNullOrWhiteSpace(request.Genus)
            && !string.IsNullOrWhiteSpace(request.Species))
        {
            await NearestSystems.SearchCodexVariantsAsync(
                request.Genus,
                request.Species,
                request.Variants);
        }
    }

    public string SelectedThemeName => selectedTheme.DisplayName;

    public string ThemeStatusMessage
    {
        get => themeStatusMessage;
        private set => SetField(ref themeStatusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                ((AsyncCommand)RefreshCommand).RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(RefreshButtonText));
            }
        }
    }

    public string RefreshButtonText => IsBusy ? "Refreshing…" : "Refresh";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string CommanderName
    {
        get => commanderName;
        private set => SetField(ref commanderName, value);
    }

    public string FrontierId
    {
        get => frontierId;
        private set => SetField(ref frontierId, value);
    }

    public string GameDescription
    {
        get => gameDescription;
        private set => SetField(ref gameDescription, value);
    }

    public string GameMode
    {
        get => gameMode;
        private set => SetField(ref gameMode, value);
    }

    public string SystemDescription
    {
        get => systemDescription;
        private set => SetField(ref systemDescription, value);
    }

    public string OverviewSystemName
    {
        get => overviewSystemName;
        private set => SetField(ref overviewSystemName, value);
    }

    public long? OverviewSystemAddress
    {
        get => overviewSystemAddress;
        private set
        {
            if (SetField(ref overviewSystemAddress, value))
            {
                OnPropertyChanged(nameof(HasOverviewSystemAddress));
                OnPropertyChanged(nameof(OverviewSystemAddressText));
            }
        }
    }

    public bool HasOverviewSystemAddress => OverviewSystemAddress is > 0;

    public string OverviewSystemAddressText => SystemAddressFormatter.Format(
        OverviewSystemAddress);

    public string BodyName
    {
        get => bodyName;
        private set => SetField(ref bodyName, value);
    }

    public string SessionState
    {
        get => sessionState;
        private set => SetField(ref sessionState, value);
    }

    public string LastUpdated
    {
        get => lastUpdated;
        private set => SetField(ref lastUpdated, value);
    }

    public string VehicleState
    {
        get => vehicleState;
        private set => SetField(ref vehicleState, value);
    }

    public string SurfacePosition
    {
        get => surfacePosition;
        private set => SetField(ref surfacePosition, value);
    }

    public string HeadingAndAltitude
    {
        get => headingAndAltitude;
        private set => SetField(ref headingAndAltitude, value);
    }

    public string GameUiFocus
    {
        get => gameUiFocus;
        private set => SetField(ref gameUiFocus, value);
    }

    public string EstimatedExplorationValue
    {
        get => estimatedExplorationValue;
        private set => SetField(ref estimatedExplorationValue, value);
    }

    public string ExplorationJumps
    {
        get => explorationJumps;
        private set => SetField(ref explorationJumps, value);
    }

    public string ExplorationDistance
    {
        get => explorationDistance;
        private set => SetField(ref explorationDistance, value);
    }

    public string ExplorationBodies
    {
        get => explorationBodies;
        private set => SetField(ref explorationBodies, value);
    }

    public string ExplorationStatusMessage
    {
        get => explorationStatusMessage;
        private set => SetField(ref explorationStatusMessage, value);
    }

    public ICommand ResetExplorationCommand { get; }

    public ICommand CancelResetExplorationCommand { get; }

    public bool IsResetExplorationPending
    {
        get => isResetExplorationPending;
        private set
        {
            if (SetField(ref isResetExplorationPending, value))
            {
                OnPropertyChanged(nameof(ResetExplorationButtonText));
                cancelResetExplorationCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResetExplorationButtonText => IsResetExplorationPending
        ? "Confirm reset"
        : "Reset totals";

    public string UnclaimedBioRewards
    {
        get => unclaimedBioRewards;
        private set => SetField(ref unclaimedBioRewards, value);
    }

    public string UnclaimedBioScans
    {
        get => unclaimedBioScans;
        private set => SetField(ref unclaimedBioScans, value);
    }

    public string OrganicScanProgress
    {
        get => organicScanProgress;
        private set => SetField(ref organicScanProgress, value);
    }

    public string ActiveOrganicSpecies
    {
        get => activeOrganicSpecies;
        private set => SetField(ref activeOrganicSpecies, value);
    }

    public string OrganicSampleRange
    {
        get => organicSampleRange;
        private set => SetField(ref organicSampleRange, value);
    }

    public string BioFirstFootfall
    {
        get => bioFirstFootfall;
        private set => SetField(ref bioFirstFootfall, value);
    }

    /// <summary>
    /// Current-body first-footfall state for the Exobiology workspace checkbox
    /// (legacy Main <c>checkFirstFootFall</c>).
    /// </summary>
    public bool IsCurrentBodyFirstFootfall
    {
        get => isCurrentBodyFirstFootfall;
        private set => SetField(ref isCurrentBodyFirstFootfall, value);
    }

    public bool CanToggleCurrentBodyFirstFootfall
    {
        get => canToggleCurrentBodyFirstFootfall;
        private set
        {
            if (SetField(ref canToggleCurrentBodyFirstFootfall, value))
            {
                toggleFirstFootfallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsOrganicSample1Complete
    {
        get => isOrganicSample1Complete;
        private set => SetField(ref isOrganicSample1Complete, value);
    }

    public bool IsOrganicSample2Complete
    {
        get => isOrganicSample2Complete;
        private set => SetField(ref isOrganicSample2Complete, value);
    }

    public bool HasActiveOrganicSample => IsOrganicSample1Complete;

    public string ExobiologyStatusMessage
    {
        get => exobiologyStatusMessage;
        private set => SetField(ref exobiologyStatusMessage, value);
    }

    public string CommanderCodexStatusMessage
    {
        get => commanderCodexStatusMessage;
        private set => SetField(ref commanderCodexStatusMessage, value);
    }

    public ICommand ResetExobiologyCommand { get; }

    public ICommand CancelResetExobiologyCommand { get; }

    public ICommand ClearSurfaceTrackersCommand { get; }

    public ICommand ToggleFirstFootfallCommand { get; }

    public bool IsResetExobiologyPending
    {
        get => isResetExobiologyPending;
        private set
        {
            if (SetField(ref isResetExobiologyPending, value))
            {
                OnPropertyChanged(nameof(ResetExobiologyButtonText));
                cancelResetExobiologyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ResetExobiologyButtonText => IsResetExobiologyPending
        ? "Confirm clear"
        : "Clear unclaimed";

    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await Task.WhenAll(
            CommanderPreference.RefreshAsync(),
            CommanderInstances.RefreshAsync(),
            VisitedStarsCache.RefreshAsync(),
            JournalPostProcessor.RefreshCommandersAsync());
        if (journalMonitor is null)
        {
            StatusMessage = $"Journal folder not found. Set "
                + $"{JournalFolderLocator.EnvironmentVariableName} or use "
                + "--journal-directory <path>.";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Reading journal and status updates…";

            var update = await journalMonitor.PollAsync(
                CancellationToken.None);
            await ApplyMonitorUpdateAsync(update, isManualRefresh: true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            LastUpdated = $"Last refresh: {DateTimeOffset.Now:G}";
        }
    }

    public async Task MonitorAsync(
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        if (journalMonitor is null)
        {
            return;
        }

        var interval = pollingInterval ?? TimeSpan.FromMilliseconds(250);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var update = await journalMonitor.PollAsync(cancellationToken);
                await ApplyMonitorUpdateAsync(update, isManualRefresh: false);
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal desktop shutdown.
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            StatusMessage = "Live journal monitoring stopped: " + exception.Message;
        }
    }

    public void SetJournalCommandPlatformServices(
        Func<DirectoryInfo, Task<bool>>? launchDirectory,
        Func<Task>? requestShutdown,
        Func<string, Task>? writeClipboard)
    {
        journalCommandDirectoryLauncher = launchDirectory;
        journalCommandShutdownRequester = requestShutdown;
        journalCommandClipboardWriter = writeClipboard;
    }

    public async Task ImportLegacyProfileAsync()
    {
        if (!CanImportLegacyProfile())
        {
            return;
        }

        try
        {
            IsImportingProfile = true;
            ProfileStatusMessage =
                "Creating verified backups of the legacy and current profiles...";
            await PrepareForProfileImportAsync();
            var result = await profileImporter.ImportAsync(
                LegacyProfileSourcePath,
                AppDataPaths.DataDirectory,
                ProfileBackupDirectory,
                CancellationToken.None);
            var overlayLayoutMigration = LegacyOverlayLayoutImportMigrator
                .MigrateIfNeeded(AppDataPaths);
            var settingsMigration = new LegacyUiSettingsMigrator()
                .MigrateIfNeeded(AppDataPaths);
            var organicMigration = await new LegacyOrganicProfileMigrator(
                    AppDataPaths.DataDirectory)
                .MigrateAsync(CancellationToken.None);
            foreach (var error in organicMigration.Errors)
            {
                applicationLogService?.Append(
                    "Legacy organic history was preserved without conversion: "
                        + error);
            }
            var retainedFiles = result.Manifest.PreviousDestinationEntries.Count
                - result.Manifest.Conflicts.Count;
            var importedBytes = result.Manifest.Entries.Sum(entry => entry.Length);
            ProfileStatusMessage = $"Imported {result.Manifest.Entries.Count:N0} legacy files, "
                + $"checksum-verified {importedBytes:N0} bytes, "
                + $"retained {retainedFiles:N0} current-only files, and recorded "
                + $"{result.Manifest.Conflicts.Count:N0} path collisions. "
                + GetOverlayLayoutMigrationStatus(overlayLayoutMigration)
                + " "
                + GetSettingsMigrationStatus(settingsMigration)
                + " "
                + GetOrganicMigrationStatus(organicMigration)
                + $"Verified backups: {result.BackupDirectory}";
            OnPropertyChanged(nameof(HasCompletedLegacyImport));
            OnPropertyChanged(nameof(ImportProfileButtonText));
            await CompleteProfileImportAsync();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            ProfileStatusMessage = $"Profile import failed without changing the legacy data: "
                + exception.Message;
        }
        finally
        {
            IsImportingProfile = false;
            importLegacyProfileCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task PrepareForProfileImportAsync()
    {
        var preparingHandlers = ProfileImportPreparing;
        if (preparingHandlers is null)
        {
            return;
        }

        foreach (var handler in preparingHandlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }

    private async Task CompleteProfileImportAsync()
    {
        var completedHandlers = ProfileImportCompleted;
        if (completedHandlers is null)
        {
            ProfileStatusMessage +=
                " Restart SrvSurvey to load the migrated profile.";
            return;
        }

        ProfileStatusMessage +=
            " Verification complete; restarting SrvSurvey with the migrated profile...";
        try
        {
            foreach (var handler in completedHandlers.GetInvocationList().Cast<Func<Task>>())
            {
                await handler();
            }
        }
        catch (Exception exception)
        {
            ProfileStatusMessage += " Automatic restart failed: "
                + exception.Message
                + " Close and reopen SrvSurvey manually; the verified import is safe.";
        }
    }

    private static string GetSettingsMigrationStatus(
        LegacyUiSettingsMigrationResult migration)
    {
        if (migration.Error is not null)
        {
            return "Player data is byte-verified, but legacy UI preferences could "
                + "not be translated; the current Avalonia settings were left "
                + $"unchanged. {migration.Error}";
        }

        return migration.Migrated
            ? $"Translated {migration.MappedPreferenceCount:N0} legacy UI preferences."
            : "No legacy UI preference translation was required.";
    }

    private static string GetOverlayLayoutMigrationStatus(
        LegacyOverlayLayoutImportMigrationResult migration)
    {
        if (migration.Error is not null)
        {
            return "Legacy overlay positions were preserved, but absolute desktop "
                + "anchors could not be converted. Reposition affected panels in "
                + $"the overlay editor. {migration.Error}";
        }

        return migration.Migrated
            ? $"Converted {migration.NormalizedPlacementCount:N0} absolute overlay "
                + "placement(s) to game-window-relative anchors."
            : "No absolute overlay placement conversion was required.";
    }

    private static string GetOrganicMigrationStatus(
        LegacyOrganicProfileMigrationResult migration)
    {
        var status = migration.Migrated
            ? "Converted retired organic history without changing its source: "
                + $"{migration.MigratedProfileCount:N0} profile(s), "
                + $"{migration.MigratedBodyCount:N0} body file(s), "
                + $"{migration.MigratedScanCount:N0} scan(s), and "
                + $"{migration.MigratedOrganismCount:N0} organism(s). "
            : "No retired organic-history conversion was required. ";
        if (migration.Errors.Count > 0)
        {
            status += $"Preserved {migration.Errors.Count:N0} unconverted "
                + "organic-history file(s); see Diagnostics for details. ";
        }

        return status;
    }

    private bool CanImportLegacyProfile()
    {
        return !IsImportingProfile
            && Directory.Exists(LegacyProfileSourcePath)
            && !HasCompletedLegacyImport
            && !File.Exists(AppDataPaths.DataDirectory);
    }

    private string GetInitialProfileStatus()
    {
        if (HasCompletedLegacyImport)
        {
            return $"Legacy profile data has already been imported into "
                + $"{AppDataPaths.DataDirectory}. The verified backup and conflict "
                + "manifest are retained for recovery.";
        }

        if (File.Exists(AppDataPaths.DataDirectory))
        {
            return $"The cross-platform profile path is occupied by a file and cannot "
                + $"be imported: {AppDataPaths.DataDirectory}";
        }

        return LegacyProfiles.Count == 0
            ? "No legacy Windows profile was detected automatically. Choose its profile "
                + "folder manually; copied Windows profiles can also be imported on Linux."
            : $"Found {LegacyProfiles.Count:N0} legacy profile source(s). "
                + "Import creates checksum-verified backups, preserves current-only files, "
                + "records collisions, and activates the merged copy transactionally.";
    }

    // Constructor helper methods keep nested conditionals out of the MainWindow
    // constructor so Sonar S3776 stays at or below the complexity budget.
    private static CommunityGoalJournalHistoryReader? CreateCommunityGoalHistoryReader(
        JournalFolderResolution resolution) =>
        resolution.SelectedPath is { } journalPath
            ? new CommunityGoalJournalHistoryReader(journalPath)
            : null;

    private static Action<string>? CreateReferenceUpdateLogger(
        ApplicationLogService? logService) =>
        logService is null
            ? null
            : message => logService.Append(message);

    private static string? ResolvePrimaryJournalPath(
        JournalFolderResolution resolution)
    {
        if (resolution.SelectedPath is not null)
        {
            return resolution.SelectedPath;
        }

        return resolution.CandidatePaths.Count > 0
            ? resolution.CandidatePaths[0]
            : null;
    }

    private static string ResolveJournalPathOrDefault(
        JournalFolderResolution resolution,
        string dataDirectory) =>
        ResolvePrimaryJournalPath(resolution)
            ?? Path.Combine(dataDirectory, "journals");

    private static string FormatCandidatePathsDisplay(
        JournalFolderResolution resolution) =>
        resolution.CandidatePaths.Count == 0
            ? "No default locations are available for this platform."
            : string.Join(Environment.NewLine, resolution.CandidatePaths);

    private static string? NormalizeOptionalId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LegacyProfileOptionViewModel? SelectInitialLegacyProfile(
        IReadOnlyList<LegacyProfileOptionViewModel> profiles) =>
        profiles.Count > 0 ? profiles[0] : null;

    private static string BuildJournalReadyStatus(
        bool isFound,
        string? targetFrontierId)
    {
        if (!isFound)
        {
            return $"Journal folder not found. Set {JournalFolderLocator.EnvironmentVariableName} "
                + "or start with --journal-directory <path>.";
        }

        return targetFrontierId is null
            ? "Ready to read the newest Journal.*.log file."
            : $"Ready to read journals for {targetFrontierId}.";
    }

    private static JournalDirectoryMonitor? CreateJournalMonitor(
        JournalFolderResolution resolution,
        string? targetFrontierId) =>
        resolution.SelectedPath is null
            ? null
            : new JournalDirectoryMonitor(
                resolution.SelectedPath,
                targetFrontierId);

    private static (HttpClient? Client, VisitedStarsCacheViewModel Cache)
        CreateVisitedStarsCache(
            VisitedStarsCacheViewModel? provided,
            AppDataPaths appDataPaths)
    {
        if (provided is not null)
        {
            return (null, provided);
        }

        var processDetector = new EliteGameProcessDetector();
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        var cache = new VisitedStarsCacheViewModel(
            new CommanderProfileCatalog(appDataPaths.DataDirectory),
            new VisitedStarsCacheService(
                client,
                Path.Combine(appDataPaths.CacheDirectory, "star-cache"),
                processDetector.IsRunning),
            VisitedStarsCacheTargetLocator.ResolveCurrent,
            processDetector.IsRunning);
        return (client, cache);
    }

    private void SelectTheme(ThemeOptionViewModel option)
    {
        try
        {
            themeService?.Select(option.Definition.Key);
            selectedTheme = option;
            ThemeStatusMessage = string.Empty;
            OnPropertyChanged(nameof(SelectedThemeName));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            ThemeStatusMessage = $"The theme changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void ApplySnapshot(JournalSnapshot snapshot)
    {
        CommanderInstances.UpdateCurrent(
            snapshot.FrontierId,
            snapshot.CommanderName);
        VisitedStarsCache.UpdateContext(
            snapshot.FrontierId,
            snapshot.CommanderName,
            snapshot.SystemName);
        CommanderName = Display(snapshot.CommanderName);
        FrontierId = Display(snapshot.FrontierId);
        GameDescription = string.Join(
            " ",
            new[]
            {
                snapshot.GameVersion,
                snapshot.GameBuild is null ? null : $"({snapshot.GameBuild})",
                snapshot.IsOdyssey switch
                {
                    true => "Odyssey",
                    false => "Horizons",
                    null => null,
                },
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(GameDescription))
        {
            GameDescription = Unavailable;
        }

        GameMode = Display(snapshot.GameMode);
        SystemDescription = snapshot.SystemAddress is null
            ? Display(snapshot.SystemName)
            : $"{Display(snapshot.SystemName)} ({snapshot.SystemAddress})";
        OverviewSystemName = Display(snapshot.SystemName);
        OverviewSystemAddress = snapshot.SystemAddress is > 0
            ? snapshot.SystemAddress
            : null;
        BodyName = Display(snapshot.BodyName);
        SessionState = snapshot.IsShutdown ? "Session closed" : "Session active";

        var malformedSuffix = snapshot.MalformedLineCount == 0
            ? string.Empty
            : $"; ignored {snapshot.MalformedLineCount} malformed/partial line(s)";
        StatusMessage = $"Loaded {snapshot.ValidLineCount} events from "
            + $"{Path.GetFileName(snapshot.SourcePath)}; "
            + $"{snapshot.RecognizedEventCount} bootstrap events recognized"
            + malformedSuffix
            + ".";
    }

    private async Task ApplyMonitorUpdateAsync(
        JournalMonitorUpdate update,
        bool isManualRefresh)
    {
        if (!update.HasChanges && !isManualRefresh)
        {
            await ApplyIdleHousekeepingAsync(update);
            return;
        }

        var previousFrontierId = journalState.FrontierId;
        var previousCommanderName = journalState.CommanderName;
        ApplyJournalAndStatusBaseline(update);
        await ApplyCommanderChangeIfNeededAsync(
            update,
            previousFrontierId,
            previousCommanderName);

        var allowSharedCargo = !IsSharedCargoSuppressed;
        var cargoChanged = ApplyCargoInventoryUpdate(update, allowSharedCargo);
        ApplyShipLockerIfAllowed(update, allowSharedCargo);
        ApplyLocalInventoryAndDesktopBehaviors(update, allowSharedCargo);
        await ApplyStatusAndGroundTargetAsync(update);

        var scansLostToDeath = new HashSet<string>(StringComparer.Ordinal);
        await ApplyGreenGasGiantAndReputationAsync(update);
        ApplyOverlayAndJournalPostProcessorContext();
        var commanderCodexResult = await ApplyCommanderCodexUpdateAsync(update);
        var codexDiscoveryChanged = commanderCodexResult.DiscoveryEventCount > 0;

        Colonization.ApplyJournalEvents(update.JournalEvents);
        Colonization.UpdateSystemContext(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.SystemAddress);
        await UpdateFeatureSystemContextsAsync(codexDiscoveryChanged);

        var loadedExistingProfile = await EnsureCommanderProfileAsync();
        await ApplyQuestUpdateAsync(update, allowSharedCargo);
        await Colonization.SetCommanderAsync(journalState.CommanderName);
        await SynchronizeColonizationAndJourneyAsync(
            update,
            allowSharedCargo,
            cargoChanged);
        await ApplyRouteContextAndEventsAsync(update);

        var explorationBefore = explorationState.CreateSnapshot();
        var exobiologyVersionBefore = exobiologyState.Version;
        var boxelBefore = BoxelSearch.CreateNotificationState();
        var skipPersistedBootstrapEvents = update.IsBootstrapRead
            && loadedExistingProfile;
        await ApplySearchAndBoxelUpdatesAsync(
            update,
            skipPersistedBootstrapEvents);
        ApplyNotificationAndPulseUpdates(update, boxelBefore);

        var guardianScreenshotContexts = await ApplyGuardianCombatAndSitesAsync(
            update,
            allowSharedCargo,
            cargoChanged,
            skipPersistedBootstrapEvents);
        await ApplyRouteAndBoxelStatusAsync();
        await HumanSite.ApplyUpdateAsync(
            update.JournalEvents,
            update.Status,
            journalState.ShipType,
            allowExternalData: !update.IsBootstrapRead);
        var requestShutdown = !update.IsBootstrapRead
            && await ApplyDesktopTextCommandsAsync(update.JournalEvents);
        await ApplyScreenshotProcessingAsync(update, guardianScreenshotContexts);
        ApplyJumpInfoGalaxyAndExplorationEvents(
            update,
            skipPersistedBootstrapEvents,
            scansLostToDeath);

        await PersistExplorationIfChangedAsync(explorationBefore);
        await ApplyExobiologyAndSurfaceSurveyAsync(
            update,
            isManualRefresh,
            skipPersistedBootstrapEvents,
            scansLostToDeath,
            exobiologyVersionBefore,
            codexDiscoveryChanged);
        ApplyMonitorStatusMessages(update, isManualRefresh);

        // External publication runs after every local reducer and persistence
        // path so an unavailable gateway cannot delay live state projection.
        await ApplyExternalPublicationAsync(update, allowSharedCargo);
        await RequestShutdownIfNeededAsync(requestShutdown);
    }

    private void ApplyJournalAndStatusBaseline(JournalMonitorUpdate update)
    {
        isAwaitingCommanderIdentity = update.IsAwaitingCommanderIdentity;
        if (update.IsBootstrapRead || update.Status is not null)
        {
            latestStatus = update.Status;
        }

        JournalInspector.ApplyUpdate(update.JournalEvents, update.Status);
        foreach (var journalEvent in update.JournalEvents)
        {
            journalState.Apply(journalEvent);
        }

        if (update.Status is { } status)
        {
            journalState.ReconcileVehicleStatus(status);
        }

        Colonization.UpdateMusicTrack(journalState.MusicTrack);
        StationInfo.UpdateMusicTrack(journalState.MusicTrack);
        GroundTarget.UpdateMusicTrack(journalState.MusicTrack);
    }

    private async Task ApplyCommanderChangeIfNeededAsync(
        JournalMonitorUpdate update,
        string? previousFrontierId,
        string? previousCommanderName)
    {
        var commanderChanged = !string.Equals(
                previousFrontierId,
                journalState.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                previousCommanderName,
                journalState.CommanderName,
                StringComparison.OrdinalIgnoreCase);
        if (!commanderChanged)
        {
            return;
        }

        awaitFreshCargoSnapshot = true;
        companionIdentityChangedAt = update.JournalEvents
            .Where(journalEvent => journalEvent.EventName is "Commander" or "LoadGame")
            .Select(journalEvent => journalEvent.Timestamp)
            .LastOrDefault(timestamp => timestamp is not null)
            ?? journalState.LastEventTimestamp;
        cargoInventoryState.Reset(null);
        latestCargo = null;
        latestShipLocker = null;
        await FrontierProfile.SetCommanderContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            refreshIfOpen: IsProfileSelected,
            CancellationToken.None);
    }

    private void ApplyShipLockerIfAllowed(
        JournalMonitorUpdate update,
        bool allowSharedCargo)
    {
        if (allowSharedCargo
            && update.ShipLocker is not null
            && IsCurrentCommanderCompanionSnapshot(update.ShipLocker.Timestamp))
        {
            latestShipLocker = update.ShipLocker;
        }
    }

    private void ApplyLocalInventoryAndDesktopBehaviors(
        JournalMonitorUpdate update,
        bool allowSharedCargo)
    {
        FrontierProfile.UpdateLocalInventory(
            latestCargo,
            latestShipLocker,
            isSuppressed: !allowSharedCargo);
        DockToDock.ApplyUpdate(
            update.JournalEvents,
            latestCargo,
            update.IsBootstrapRead);
        DesktopBehavior.ApplyJournalEvents(
            update.JournalEvents,
            update.IsBootstrapRead);
    }

    private async Task ApplyStatusAndGroundTargetAsync(JournalMonitorUpdate update)
    {
        if (update.Status is not null)
        {
            exobiologyState.UpdateStatus(update.Status);
            GroundTarget.UpdateStatus(update.Status);
            Colonization.UpdateStatus(update.Status);
        }

        await GroundTarget.ApplyJournalEventsAsync(
            update.JournalEvents,
            allowCommands: !update.IsBootstrapRead);
    }

    private async Task ApplyGreenGasGiantAndReputationAsync(
        JournalMonitorUpdate update)
    {
        var greenGasGiantResult =
            await greenGasGiantPublicationCoordinator.ApplyAsync(
                update.JournalEvents,
                NetworkPrivacy.UploadGreenGasGiantCandidates,
                allowPublishing: !update.IsBootstrapRead,
                CancellationToken.None);
        NetworkPrivacy.ReportPublicationResult(greenGasGiantResult);
        if (!update.IsBootstrapRead)
        {
            Notifications.ReportGreenGasGiantUploads(greenGasGiantResult);
        }

        foreach (var warning in greenGasGiantResult.Warnings)
        {
            applicationLogService?.Append(warning);
        }

        FrontierProfile.UpdateJournalReputation(
            journalState.CommanderName,
            update.JournalEvents);
        FrontierProfile.UpdateJournalCommunityGoals(
            journalState.CommanderName,
            update.JournalEvents);
    }

    private void ApplyOverlayAndJournalPostProcessorContext()
    {
        OverlayBehavior.UpdateContext(
            journalState.CurrentSuit,
            latestStatus?.OnFoot == true);
        OverlayBehavior.UpdateSessionContext(
            latestStatus is not null,
            !string.IsNullOrWhiteSpace(journalState.CommanderName),
            journalState.IsShutdown,
            journalState.IsAtMainMenu || isAwaitingCommanderIdentity,
            journalState.IsAtCarrierManagement);
        JournalPostProcessor.SelectCommander(journalState.FrontierId);
    }

    private async Task<CommanderCodexJournalTrackResult> ApplyCommanderCodexUpdateAsync(
        JournalMonitorUpdate update)
    {
        var commanderCodexResult =
            await commanderCodexJournalTracker.ApplyAsync(
                update.JournalEvents,
                CancellationToken.None);
        if (commanderCodexResult.Warnings.Count > 0)
        {
            CommanderCodexStatusMessage = string.Join(
                Environment.NewLine,
                commanderCodexResult.Warnings);
        }
        else if (commanderCodexResult.DiscoveryEventCount > 0)
        {
            CommanderCodexStatusMessage = commanderCodexResult.HasChanges
                ? $"Recorded {commanderCodexResult.ChangedEntryCount:N0} "
                    + "Commander Codex ledger entries across "
                    + $"{commanderCodexResult.ChangedFileCount:N0} files."
                : "Commander Codex is current; no earlier firsts were found.";
        }

        return commanderCodexResult;
    }

    private async Task SynchronizeColonizationAndJourneyAsync(
        JournalMonitorUpdate update,
        bool allowSharedCargo,
        bool cargoChanged)
    {
        var cargoActivity = allowSharedCargo
            && (cargoChanged
                || update.Cargo is not null
                || update.JournalEvents.Any(journalEvent =>
                    journalEvent.EventName is "Cargo"
                        or "CargoTransfer"
                        or "MarketBuy"
                        or "MarketSell"));
        var isCurrentCargoInventoryAvailable =
            !awaitFreshCargoSnapshot
            || update.Cargo is not null;
        await Colonization.SynchronizeLiveProjectsAsync(
            update.JournalEvents,
            allowPublishing: !update.IsBootstrapRead,
            cargoInventory: allowSharedCargo
                ? cargoInventoryState
                : null,
            preferShipCargoDiffForSquadron: isCurrentCargoInventoryAvailable,
            cargoActivity: cargoActivity);
        var initializedJourney = await Journey.UpdateContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.IsOdyssey ?? true,
            journalState.SystemName,
            journalState.SystemAddress);
        if (!initializedJourney)
        {
            await Journey.ApplyJournalEventsAsync(update.JournalEvents);
        }
    }

    private void ApplyNotificationAndPulseUpdates(
        JournalMonitorUpdate update,
        BoxelSearchNotificationState boxelBefore)
    {
        Notifications.ApplyJournalEvents(
            update.JournalEvents,
            allowNotifications: !update.IsBootstrapRead);
        PulseOverlay.ApplyUpdate(
            update.JournalEvents,
            update.Status,
            update.IsBootstrapRead);
        Notifications.ReportBoxelUpdate(
            boxelBefore,
            BoxelSearch.CreateNotificationState(),
            update.JournalEvents.Any(journalEvent =>
                journalEvent.EventName == "FSSAllBodiesFound"),
            allowNotifications: !update.IsBootstrapRead);
    }

    private async Task<IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>>
        ApplyGuardianCombatAndSitesAsync(
            JournalMonitorUpdate update,
            bool allowSharedCargo,
            bool cargoChanged,
            bool skipPersistedBootstrapEvents)
    {
        var guardianScreenshotContexts = await Guardian.ApplyJournalEventsAsync(
            update.JournalEvents,
            activeProfileCommanderName,
            allowLiveCommands: !update.IsBootstrapRead,
            status: latestStatus,
            cancellationToken: firstFootfallInferenceCancellation.Token);
        if (!allowSharedCargo)
        {
            Guardian.ClearCargo();
        }
        else if (cargoChanged && latestCargo is not null)
        {
            Guardian.UpdateCargo(latestCargo);
        }

        if (cargoChanged && latestCargo is not null)
        {
            await Colonization.UpdateCargoAsync(
                latestCargo,
                publishCurrentShipCargo: update.Cargo is not null);
        }

        await Colonization.UpdateMarketAsync(update.Market);
        SystemSurvey.SetActiveBuildProjects(Colonization.HasProjects);
        Combat.SetActiveBuildProjects(Colonization.HasProjects);
        Guardian.SetActiveBuildProjects(Colonization.HasProjects);
        HumanSite.SetActiveBuildProjects(Colonization.HasProjects);
        await Combat.ApplyUpdateAsync(
            update.JournalEvents,
            update.Status,
            processHistoricalProgress: !skipPersistedBootstrapEvents);

        if (update.Status is not null)
        {
            await Guardian.UpdateStatusAsync(
                update.Status,
                allowGesture: !update.IsBootstrapRead,
                cancellationToken: CancellationToken.None);
            StationInfo.UpdateStatus(update.Status);
        }

        return guardianScreenshotContexts;
    }

    private async Task ApplyRouteAndBoxelStatusAsync()
    {
        if (latestStatus is null)
        {
            return;
        }

        await Route.UpdateStatusAsync(
            latestStatus,
            journalState.MusicTrack);
        await FleetCarrierRoute.UpdateStatusAsync(
            latestStatus,
            journalState.MusicTrack);
        await BoxelSearch.UpdateStatusAsync(
            latestStatus,
            allowAutoCopy: !Route.ShouldAutoCopyNextHop
                && !FleetCarrierRoute.ShouldAutoCopyNextHop,
            nextMusicTrack: journalState.MusicTrack);
    }

    private async Task ApplyScreenshotProcessingAsync(
        JournalMonitorUpdate update,
        IReadOnlyDictionary<JournalEventEnvelope, ScreenshotGuardianContext>
            guardianScreenshotContexts)
    {
        if (update.IsBootstrapRead)
        {
            return;
        }

        var screenshotResult =
            await ScreenshotProcessing.ProcessJournalEventsAsync(
            update.JournalEvents,
            journalState.CommanderName,
            guardianScreenshotContexts,
            latestStatus is { } screenshotStatus
                ? new ScreenshotNavigationContext(
                    DateTimeOffset.UtcNow,
                    screenshotStatus.Latitude,
                    screenshotStatus.Longitude,
                    screenshotStatus.NormalizedHeading,
                    screenshotStatus.HasLatitudeLongitude)
                : null,
            CancellationToken.None);
        Notifications.ReportScreenshotResult(
            screenshotResult,
            ScreenshotProcessing.AddBanner);
    }

    private void ApplyJumpInfoGalaxyAndExplorationEvents(
        JournalMonitorUpdate update,
        bool skipPersistedBootstrapEvents,
        HashSet<string> scansLostToDeath)
    {
        JumpInfo.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                journalState.SystemName,
                journalState.SystemAddress,
                journalState.StarPosition,
                update.NavRoute,
                update.JournalEvents,
                update.Status,
                Route.CreateSnapshot(),
                update.IsBootstrapRead));
        GalaxyMap.ApplyUpdate(
            journalState.SystemName,
            journalState.SystemAddress,
            update.NavRoute,
            update.JournalEvents,
            update.Status,
            update.IsBootstrapRead,
            journalState.MusicTrack);
        ApplyExplorationAndExobiologyJournalEvents(
            update.JournalEvents,
            skipPersistedBootstrapEvents,
            scansLostToDeath);
    }

    private async Task PersistExplorationIfChangedAsync(
        ExplorationSnapshot explorationBefore)
    {
        var explorationAfter = explorationState.CreateSnapshot();
        if (explorationAfter == explorationBefore)
        {
            return;
        }

        UpdateExplorationDisplay(explorationAfter);
        await SaveExplorationAsync(explorationAfter);
    }

    private async Task RequestShutdownIfNeededAsync(bool requestShutdown)
    {
        if (requestShutdown
            && journalCommandShutdownRequester is { } requestShutdownAsync)
        {
            await requestShutdownAsync();
        }
    }

    private bool ApplyCargoInventoryUpdate(
        JournalMonitorUpdate update,
        bool allowSharedCargo)
    {
        var cargoChanged = false;
        if (!allowSharedCargo)
        {
            cargoChanged = cargoInventoryState.Reset(null);
            latestCargo = null;
            latestShipLocker = null;
            return cargoChanged;
        }

        if (awaitFreshCargoSnapshot)
        {
            if (update.Cargo is not null
                && IsCurrentCommanderCompanionSnapshot(update.Cargo.Timestamp))
            {
                cargoChanged = cargoInventoryState.Reset(update.Cargo);
                awaitFreshCargoSnapshot = false;
                latestCargo = cargoInventoryState.CreateSnapshot();
            }

            return cargoChanged;
        }

        foreach (var journalEvent in update.JournalEvents)
        {
            // Squadron linked FCs freeze the true before-state before CargoTransfer mutates
            // live inventory so the later GetDiff cannot collapse to a zero delta.
            if (string.Equals(
                    journalEvent.EventName,
                    "CargoTransfer",
                    StringComparison.Ordinal))
            {
                Colonization.PrepareSquadronCargoTransferSnapshot(
                    cargoInventoryState);
            }

            cargoChanged |= cargoInventoryState.Apply(
                journalEvent,
                latestStatus?.InSrv == true);
        }

        if (update.Cargo is not null
            && IsCurrentCommanderCompanionSnapshot(update.Cargo.Timestamp))
        {
            cargoChanged |= cargoInventoryState.Reset(update.Cargo);
        }

        if (cargoChanged || latestCargo is null)
        {
            latestCargo = cargoInventoryState.CreateSnapshot();
        }

        return cargoChanged;
    }

    private async Task UpdateFeatureSystemContextsAsync(bool forceCodexBingoRefresh)
    {
        Search.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.SystemAddress);
        NearestSystems.UpdateContext(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.CommanderName,
            journalState.SystemAddress);
        await CodexBingo.UpdateContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.StarPosition,
            forceRefresh: forceCodexBingoRefresh);
        SystemNotes.UpdateContext(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        await BoxelSearch.UpdateCurrentSystemAsync(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.SystemAddress);
        Guardian.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);
        HumanSite.UpdateContext(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.SystemAddress ?? 0,
            journalState.StarPosition);
        _ = StationInfo.UpdateCurrentSystemAsync(
            journalState.SystemName,
            journalState.SystemAddress ?? 0);
    }

    private async Task ApplyRouteContextAndEventsAsync(JournalMonitorUpdate update)
    {
        await Route.UpdateContextAsync(
            journalState.FrontierId,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        await RouteManager.UpdateContextAsync(journalState.FrontierId);
        await FleetCarrierRoute.UpdateContextAsync(
            journalState.FrontierId,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        await FleetCarrierRouteManager.UpdateContextAsync(
            journalState.FrontierId);
        await routeAutoCopyCoordinator.ReconcileAsync();
        if (update.IsBootstrapRead)
        {
            FleetCarrierRoute.ApplyFleetCarrierJumpEvents(
                update.JournalEvents);
            return;
        }

        await Route.ApplyJournalEventsAsync(update.JournalEvents);
        await FleetCarrierRoute.ApplyJournalEventsAsync(
            update.JournalEvents);
    }

    private async Task ApplySearchAndBoxelUpdatesAsync(
        JournalMonitorUpdate update,
        bool skipPersistedBootstrapEvents)
    {
        if (update.NavRoute is not null)
        {
            await BoxelSearch.UpdateRouteAsync(update.NavRoute);
        }

        await Search.UpdateNavigationAsync(
            update.NavRoute,
            update.Status,
            journalState.MusicTrack);

        if (!skipPersistedBootstrapEvents)
        {
            await BoxelSearch.ApplyJournalEventsAsync(update.JournalEvents);
        }
    }

    private void ApplyExplorationAndExobiologyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool skipPersistedBootstrapEvents,
        HashSet<string> scansLostToDeath)
    {
        foreach (var journalEvent in journalEvents)
        {
            if (!skipPersistedBootstrapEvents
                || journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                explorationState.Apply(journalEvent);
            }

            if (!skipPersistedBootstrapEvents
                || IsExobiologyContextEvent(journalEvent.EventName))
            {
                if (journalEvent.EventName == "Died")
                {
                    scansLostToDeath.UnionWith(
                        exobiologyState.CreateSnapshot().ScannedBioEntryIds);
                }

                exobiologyState.Apply(journalEvent);
            }
        }
    }

    private async Task<ExobiologySnapshot> ApplyExobiologyAndSurfaceSurveyAsync(
        JournalMonitorUpdate update,
        bool isManualRefresh,
        bool skipPersistedBootstrapEvents,
        HashSet<string> scansLostToDeath,
        int exobiologyVersionBefore,
        bool forceCodexRefresh)
    {
        var exobiologyAfter = exobiologyState.CreateSnapshot();
        var exobiologyChanged =
            exobiologyState.Version != exobiologyVersionBefore;
        if (update.JournalEvents.Count > 0
            || update.Status is not null
            || exobiologyChanged
            || isManualRefresh)
        {
            SystemSurvey.ApplyUpdate(
                update.JournalEvents,
                update.Status,
                exobiologyAfter,
                journalState.ActiveSrvType);
        }

        await LoadCurrentSystemHistoryAsync();
        await ApplyBoxelSurveyStatsAsync(
            update,
            exobiologyChanged,
            skipPersistedBootstrapEvents);

        PendingSystemBodyDataLoad = LoadCurrentSystemBodyDataAsync();
        if (!update.IsBootstrapRead
            && await ApplyFirstFootfallTextCommandsAsync(update.JournalEvents) > 0)
        {
            exobiologyAfter = exobiologyState.CreateSnapshot();
            SystemSurvey.ApplyUpdate([], null, exobiologyAfter);
        }

        if (await TryInferFirstFootfallAsync(update))
        {
            exobiologyAfter = exobiologyState.CreateSnapshot();
            SystemSurvey.ApplyUpdate([], null, exobiologyAfter);
        }

        exobiologyChanged =
            exobiologyState.Version != exobiologyVersionBefore;

        await PersistSystemScanAsync(update.JournalEvents);
        await RefreshSystemSurveyCommanderCodexAsync(
            forceRefresh: forceCodexRefresh);
        if (!update.IsBootstrapRead
            && SystemSurvey.LatestBiologyEntryId is { } entryId
            && update.JournalEvents.Any(IsShowCodexCommand))
        {
            await BiologyCodex.OpenEntryAsync(entryId);
        }

        var surfaceSession = CreateSurfaceSurveySessionContext();
        if (update.JournalEvents.Count > 0
            || update.Status is not null
            || exobiologyChanged
            || isManualRefresh)
        {
            await SurfaceSurvey.ApplyUpdateAsync(
                surfaceSession,
                update.JournalEvents,
                update.Status,
                exobiologyAfter,
                processJournalMutations: !skipPersistedBootstrapEvents,
                scansLostToDeath: scansLostToDeath.ToArray(),
                cancellationToken: CancellationToken.None);
        }

        if (exobiologyChanged)
        {
            await SaveExobiologyAsync(exobiologyAfter);
        }

        if (update.JournalEvents.Count > 0 || update.Status is not null)
        {
            UpdateExobiologyDisplay(exobiologyAfter);
        }

        return exobiologyAfter;
    }

    private async Task ApplyBoxelSurveyStatsAsync(
        JournalMonitorUpdate update,
        bool exobiologyChanged,
        bool skipPersistedBootstrapEvents)
    {
        if (exobiologyChanged || update.JournalEvents.Count > 0)
        {
            await boxelSurveyStats.IngestSnapshotAsync(
                SystemSurvey.Snapshot,
                cancellationToken: CancellationToken.None);
        }

        if (!skipPersistedBootstrapEvents)
        {
            await boxelSurveyStats.ApplyJournalEventsAsync(
                update.JournalEvents,
                CancellationToken.None);
        }
        else
        {
            await boxelSurveyStats.ApplyBootstrapContextAsync(
                update.JournalEvents,
                CancellationToken.None);
        }
    }

    private SurfaceSurveySessionContext? CreateSurfaceSurveySessionContext()
    {
        if (string.IsNullOrWhiteSpace(activeProfileFrontierId)
            || string.IsNullOrWhiteSpace(journalState.SystemName)
            || journalState.SystemAddress is not > 0)
        {
            return null;
        }

        var surfaceBody = SystemSurvey.Snapshot.CurrentBodyId is { } bodyId
            ? SystemSurvey.Snapshot.Bodies.FirstOrDefault(body =>
                body.BodyId == bodyId)
            : null;
        surfaceBody ??= latestStatus?.BodyName is { Length: > 0 } statusBodyName
            ? SystemSurvey.Snapshot.Bodies.FirstOrDefault(body =>
                string.Equals(
                    body.Name,
                    statusBodyName,
                    StringComparison.OrdinalIgnoreCase))
            : null;
        return new SurfaceSurveySessionContext(
            activeProfileFrontierId,
            activeProfileCommanderName ?? journalState.CommanderName,
            journalState.SystemName,
            journalState.SystemAddress.Value,
            journalState.StarPosition,
            surfaceBody?.BodyId,
            surfaceBody?.Name,
            latestStatus?.PlanetRadius is > 0
                ? (double)latestStatus.PlanetRadius
                : surfaceBody?.RadiusMeters ?? 0,
            journalState.KnownNomadVehicleId);
    }

    private void ApplyMonitorStatusMessages(
        JournalMonitorUpdate update,
        bool isManualRefresh)
    {
        if (update.JournalEvents.Count > 0)
        {
            ApplySnapshot(journalState.CreateSnapshot(update.JournalPath));
        }
        else if (isManualRefresh)
        {
            StatusMessage = update.JournalPath is null
                ? $"No Journal.*.log files were found in {JournalFolderPath}."
                : $"Monitoring {Path.GetFileName(update.JournalPath)}; no new events.";
        }

        if (update.Status is not null)
        {
            ApplyStatus(update.Status);
        }

        if (update.Errors.Count > 0)
        {
            StatusMessage = string.Join(Environment.NewLine, update.Errors);
        }
        else if (update.StatusReadErrorRecovered
            && update.JournalEvents.Count == 0
            && !isManualRefresh)
        {
            if (journalState.IsShutdown)
            {
                StatusMessage =
                    "Elite session closed normally; waiting for the next journal session.";
            }
            else if (update.JournalPath is null)
            {
                StatusMessage = "Status.json is readable again.";
            }
            else
            {
                StatusMessage = $"Status.json is readable again; monitoring "
                    + $"{Path.GetFileName(update.JournalPath)}.";
            }
        }

        if (update.JournalEvents.Count > 0
            || update.Status is not null
            || update.NavRoute is not null
            || update.Cargo is not null
            || update.ShipLocker is not null
            || update.Market is not null
            || update.Errors.Count > 0
            || update.StatusReadErrorRecovered
            || isManualRefresh)
        {
            LastUpdated = $"Last update: {DateTimeOffset.Now:G}";
        }
    }

    private async Task ApplyIdleHousekeepingAsync(JournalMonitorUpdate update)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - lastIdleHousekeepingAt < IdleHousekeepingInterval)
        {
            return;
        }

        lastIdleHousekeepingAt = now;
        await ApplyExternalPublicationAsync(
            update,
            allowSharedCargo: !IsSharedCargoSuppressed);
        StartSystemBodyDataRetryIfDue();
    }

    private async Task ApplyExternalPublicationAsync(
        JournalMonitorUpdate update,
        bool allowSharedCargo)
    {
        lastIdleHousekeepingAt = DateTimeOffset.UtcNow;
        var canShareCargo = allowSharedCargo;
        try
        {
            CommanderInstances.RefreshGameWindowCount();
            var hasMultipleGameWindows =
                CommanderInstances.HasMultipleGameWindows;
            canShareCargo &= !hasMultipleGameWindows;
            eddnPublisher.SetSuspended(hasMultipleGameWindows);
            var eddnResult = await eddnPublisher.ApplyAsync(
                new EddnApplyRequest
                {
                    JournalEvents = update.JournalEvents,
                    Status = latestStatus,
                    Enabled = NetworkPrivacy.EddnUploadEnabled,
                    UseTestSchemas = NetworkPrivacy.EddnUseTestSchemas,
                    AllowPublishing = !update.IsBootstrapRead
                        && !hasMultipleGameWindows,
                    JournalDirectory = folderResolution.SelectedPath,
                    JournalPath = update.JournalPath,
                    AllowSharedData = !hasMultipleGameWindows
                },
                cancellationToken: CancellationToken.None);
            NetworkPrivacy.ReportPublicationResult(eddnResult);
            foreach (var warning in eddnResult.Warnings)
            {
                applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            applicationLogService?.Append(
                "EDDN processing was isolated from journal tracking: "
                    + exception.Message);
        }

        try
        {
            var voxStellarResult = await voxStellarPublisher.ApplyAsync(
                new VoxStellarApplyRequest
                {
                    JournalEvents = update.JournalEvents,
                    CommanderName = activeProfileCommanderName
                        ?? journalState.CommanderName,
                    Enabled = VoxStellar.JournalUploadEnabled,
                    AllowPublishing = !update.IsBootstrapRead
                        && !CommanderInstances.HasMultipleGameWindows,
                },
                CancellationToken.None);
            VoxStellar.ReportPublicationResult(voxStellarResult);
            foreach (var warning in voxStellarResult.Warnings)
            {
                applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            applicationLogService?.Append(
                "VoxStellar processing was isolated from journal tracking: "
                    + exception.Message);
        }

        try
        {
            var inaraResult = await inaraPublisher.ApplyAsync(
                new InaraPublicationUpdate(
                    update.JournalEvents,
                    latestStatus,
                    latestCargo,
                    update.JournalPath,
                    AllowPublishing: !update.IsBootstrapRead,
                    AllowSharedData: canShareCargo,
                    journalState.SystemName,
                    journalState.StationName,
                    journalState.BodyName,
                    journalState.ShipType,
                    journalState.ShipId,
                    journalState.ShipName,
                    journalState.ShipIdent,
                    new InaraPublicationOptions(
                        Inara.StoredApiKey,
                        activeProfileCommanderName
                            ?? journalState.CommanderName,
                        activeProfileFrontierId
                            ?? journalState.FrontierId,
                        journalState.GameVersion,
                        journalState.IsOdyssey ?? true)),
                CancellationToken.None);
            Inara.ReportPublicationResult(inaraResult);
            foreach (var warning in inaraResult.Warnings)
            {
                applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            Inara.ReportPublicationFailure(exception);
            applicationLogService?.Append(
                "Inara processing was isolated from journal tracking: "
                    + exception.Message);
        }
    }

    private async Task RefreshSystemSurveyCommanderCodexAsync(
        bool forceRefresh)
    {
        var resolvedFrontierId = activeProfileFrontierId ?? journalState.FrontierId;
        var resolvedCommanderName = activeProfileCommanderName
            ?? journalState.CommanderName;
        var systemAddress = journalState.SystemAddress;
        var regionId = journalState.StarPosition is { } position
            ? GalacticRegionMap.Find(position)?.Id
            : null;
        if (string.IsNullOrWhiteSpace(resolvedFrontierId)
            || systemAddress is null)
        {
            surveyCodexFrontierId = null;
            surveyCodexRegionId = null;
            surveyCodexSystemAddress = null;
            SystemSurvey.UpdateCommanderCodexContext(null, null);
            return;
        }

        if (!forceRefresh
            && string.Equals(
                surveyCodexFrontierId,
                resolvedFrontierId,
                StringComparison.OrdinalIgnoreCase)
            && surveyCodexRegionId == regionId
            && surveyCodexSystemAddress == systemAddress)
        {
            return;
        }

        var global = await commanderCodexStore.LoadAsync(
            resolvedFrontierId,
            resolvedCommanderName,
            cancellationToken: CancellationToken.None);
        var regional = regionId is > 0
            ? await commanderCodexStore.LoadAsync(
                resolvedFrontierId,
                resolvedCommanderName,
                regionId.Value,
                CancellationToken.None)
            : null;
        surveyCodexFrontierId = resolvedFrontierId;
        surveyCodexRegionId = regionId;
        surveyCodexSystemAddress = systemAddress;
        SystemSurvey.UpdateCommanderCodexContext(
            global.Data,
            regional?.Data,
            regionId);

        var warnings = global.Warnings
            .Concat(regional?.Warnings ?? [])
            .ToArray();
        if (warnings.Length > 0)
        {
            CommanderCodexStatusMessage = string.Join(
                Environment.NewLine,
                warnings);
        }
    }

    private async Task ApplyQuestUpdateAsync(
        JournalMonitorUpdate update,
        bool allowCargoFile)
    {
        if (string.IsNullOrWhiteSpace(journalState.FrontierId)
            || string.IsNullOrWhiteSpace(journalState.CommanderName)
            || folderResolution.SelectedPath is null)
        {
            QuestStatusMessage = "Waiting for a commander journal session.";
            return;
        }

        try
        {
            var enabled = questSettingsStore.LoadEnabled();
            var previousQuestSnapshot = questRuntimeCoordinator.Snapshot;
            var result = await questRuntimeCoordinator.ApplyUpdateAsync(
                new QuestRuntimeConfiguration(
                    enabled,
                    journalState.FrontierId,
                    journalState.CommanderName,
                    activeProfileRavenApiKey,
                    latestStatus),
                folderResolution.SelectedPath,
                update.JournalEvents,
                update.IsBootstrapRead,
                allowCargoFile: allowCargoFile,
                cancellationToken: CancellationToken.None);
            QuestWorkspace.ApplyRuntimeResult(result, enabled);
            if (ReferenceEquals(previousQuestSnapshot, result.Quests))
            {
                // Status can move quest overlay markers without changing the
                // quest rows. Snapshot changes are handled by the coordinator
                // event and must not be projected a second time here.
                UpdateQuestOverlayPresentation(result.Quests, enabled);
            }
            if (!enabled)
            {
                QuestStatusMessage = "Quests are disabled.";
            }
            else if (result.Warnings.Count > 0)
            {
                QuestStatusMessage = string.Join(
                    Environment.NewLine,
                    result.Warnings);
            }
            else
            {
                QuestStatusMessage = result.Quests.Count == 0
                    ? "No active quests."
                    : $"{result.Quests.Count:N0} active quest(s); "
                        + $"{QuestUnreadMessageCount:N0} unread message(s).";
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or HttpRequestException)
        {
            QuestStatusMessage = "Quest update failed without changing imported "
                + "source data: " + exception.Message;
            applicationLogService?.Append(QuestStatusMessage);
        }
    }

    private async Task<QuestRuntimeUpdateResult> ReplayQuestJournalEventAsync(
        JournalEventEnvelope journalEvent)
    {
        if (folderResolution.SelectedPath is null)
        {
            throw new InvalidOperationException(
                "A journal folder is required to replay quest events.");
        }

        var enabled = questSettingsStore.LoadEnabled();
        if (!enabled)
        {
            throw new InvalidOperationException(
                "Quests must be enabled before replaying an event.");
        }

        var result = await questRuntimeCoordinator.ReplayEventAsync(
            folderResolution.SelectedPath,
            journalEvent,
            allowCargoFile: !IsSharedCargoSuppressed,
            cancellationToken: CancellationToken.None);
        QuestWorkspace.ApplyRuntimeResult(result, enabled);
        UpdateQuestOverlayPresentation(result.Quests, enabled);
        OnPropertyChanged(nameof(Quests));
        OnPropertyChanged(nameof(QuestUnreadMessageCount));
        QuestStatusMessage = result.Warnings.Count > 0
            ? string.Join(Environment.NewLine, result.Warnings)
            : (result.Quests.Count == 0) switch
            {
                true => "No active quests received the replayed event.",
                false => $"Replayed {journalEvent.EventName}; "
                                                                                           + $"{result.Quests.Count:N0} active quest(s), "
                                                                                           + $"{QuestUnreadMessageCount:N0} unread message(s)."
            };
        return result;
    }

    private async Task<bool> EnsureCommanderProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(journalState.FrontierId))
        {
            return false;
        }

        var isOdyssey = journalState.IsOdyssey ?? true;
        if (string.Equals(
                activeProfileFrontierId,
                journalState.FrontierId,
                StringComparison.OrdinalIgnoreCase)
            && activeProfileIsOdyssey == isOdyssey)
        {
            activeProfileCommanderName = journalState.CommanderName
                ?? activeProfileCommanderName;
            return false;
        }

        var result = await commanderProfileStore.LoadAsync(
            journalState.FrontierId,
            isOdyssey,
            CancellationToken.None);
        loadedSystemHistoryKey = null;
        loadedSystemBodyDataKey = null;
        ResetSystemBodyDataRetry();
        CancelSystemBodyDataRequest();
        activeProfileFrontierId = journalState.FrontierId;
        activeProfileCommanderName = journalState.CommanderName
            ?? result.Data?.CommanderName;
        activeProfileIsOdyssey = isOdyssey;
        resetExplorationCommand.RaiseCanExecuteChanged();
        resetExobiologyCommand.RaiseCanExecuteChanged();
        clearSurfaceTrackersCommand.RaiseCanExecuteChanged();

        if (result.Data is null)
        {
            await boxelSurveyStats.SwitchCommanderAsync(
                journalState.FrontierId,
                CancellationToken.None);
            activeProfileRavenApiKey = null;
            Inara.SetCommanderProfile(
                null,
                journalState.CommanderName,
                isOdyssey,
                inaraApiKey: null);
            SurfaceSurvey.Reset();
            Combat.LoadProfile(null, null, isOdyssey, CombatSnapshot.Empty);
            Colonization.SetCommanderProfile(null, isOdyssey, apiKey: null);
            ExplorationStatusMessage = result.Error
                ?? CommanderProfileLoadFailedMessage;
            ExobiologyStatusMessage = result.Error
                ?? CommanderProfileLoadFailedMessage;
            Search.SetProfileError(
                result.Error ?? CommanderProfileLoadFailedMessage);
            await BoxelSearch.SetProfileErrorAsync(
                result.Error ?? CommanderProfileLoadFailedMessage);
            Guardian.SetProfileError(
                result.Error ?? CommanderProfileLoadFailedMessage);
            RamTah.SetProfileError(
                result.Error ?? CommanderProfileLoadFailedMessage);
            return false;
        }

        activeProfileRavenApiKey = result.Data.RavenColonialApiKey;
        Inara.SetCommanderProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.InaraApiKey);
        Colonization.SetCommanderProfile(
            result.Data.FrontierId,
            result.Data.IsOdyssey,
            result.Data.RavenColonialApiKey);

        explorationState.Reset(result.Data.Exploration);
        exobiologyState.Reset(result.Data.Exobiology);
        SurfaceSurvey.Reset(result.Data.Exobiology);
        Combat.LoadProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.Combat);
        Search.LoadProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.SphereLimit);
        await BoxelSearch.LoadProfileAsync(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.BoxelSearch);
        await boxelSurveyStats.SwitchCommanderAsync(
            result.Data.FrontierId,
            CancellationToken.None);
        await Guardian.LoadProfileAsync(
            result.Data.FrontierId,
            result.Data.IsOdyssey,
            CancellationToken.None);
        RamTah.LoadProfile(
            result.Data.FrontierId,
            activeProfileCommanderName,
            result.Data.IsOdyssey,
            result.Data.RamTah);
        UpdateExplorationDisplay(result.Data.Exploration);
        UpdateExobiologyDisplay(result.Data.Exobiology);
        ExplorationStatusMessage = result.Exists
            ? $"Loaded compatible totals from {Path.GetFileName(result.Path)}."
            : $"No existing profile was found; session totals will be saved to "
                + Path.GetFileName(result.Path)
                + ".";
        ExobiologyStatusMessage = result.Exists
            ? $"Loaded legacy-compatible organic scan state from "
                + Path.GetFileName(result.Path)
                + "."
            : $"No existing profile was found; organic scan state will be saved to "
                + Path.GetFileName(result.Path)
                + ".";
        return result.Exists;
    }

    private async Task SaveExplorationAsync(ExplorationSnapshot snapshot)
    {
        if (activeProfileFrontierId is null)
        {
            return;
        }

        try
        {
            await commanderProfileStore.SaveExplorationAsync(
                activeProfileFrontierId,
                activeProfileCommanderName,
                activeProfileIsOdyssey,
                snapshot,
                CancellationToken.None);
            ExplorationStatusMessage = $"Totals saved to "
                + Path.GetFileName(commanderProfileStore.GetProfilePath(
                    activeProfileFrontierId,
                    activeProfileIsOdyssey))
                + ".";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            ExplorationStatusMessage = "Totals changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateExplorationDisplay(ExplorationSnapshot snapshot)
    {
        EstimatedExplorationValue = $"{snapshot.EstimatedRewards:N0} CR";
        ExplorationJumps = snapshot.JumpCount.ToString("N0");
        ExplorationDistance = $"{snapshot.DistanceTravelled:N1} ly";
        ExplorationBodies = $"Scanned: {snapshot.ScanCount:N0}, "
            + $"DSS: {snapshot.DetailedSurfaceScanCount:N0}, "
            + $"Landed: {snapshot.LandedBodyCount:N0}";
    }

    private async Task SaveExobiologyAsync(ExobiologySnapshot snapshot)
    {
        if (activeProfileFrontierId is null)
        {
            return;
        }

        try
        {
            await commanderProfileStore.SaveExobiologyAsync(
                activeProfileFrontierId,
                activeProfileCommanderName,
                activeProfileIsOdyssey,
                snapshot,
                CancellationToken.None);
            ExobiologyStatusMessage = $"Organic scan state saved to "
                + Path.GetFileName(commanderProfileStore.GetProfilePath(
                    activeProfileFrontierId,
                    activeProfileIsOdyssey))
                + ".";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            ExobiologyStatusMessage =
                "Organic scan state changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private void UpdateExobiologyDisplay(ExobiologySnapshot snapshot)
    {
        UnclaimedBioRewards = $"{snapshot.OrganicRewards:N0} CR";
        UnclaimedBioScans = snapshot.ScannedBioEntryIds.Count == 1
            ? "1 organism"
            : $"{snapshot.ScannedBioEntryIds.Count:N0} organisms";
        var activeSample = snapshot.ScanTwo ?? snapshot.ScanOne;
        ActiveOrganicSpecies = activeSample is null
            ? Unavailable
            : exobiologyState.ActiveSpeciesDisplayName
                ?? activeSample.Species;
        if (activeSample is null)
        {
            OrganicSampleRange = Unavailable;
        }
        else if (exobiologyState.NearestActiveSampleDistance is not { } distance)
        {
            OrganicSampleRange = $"{activeSample.Radius:N0} m minimum separation";
        }
        else if (exobiologyState.RemainingSampleDistance > 0)
        {
            OrganicSampleRange = $"{distance:N0} m from nearest sample · {exobiologyState.RemainingSampleDistance:N0} m remaining";
        }
        else
        {
            OrganicSampleRange = $"{distance:N0} m from nearest sample · clear to sample";
        }
        if (snapshot.ScanOne is null)
        {
            OrganicScanProgress = "Ready for sample 1 of 3";
        }
        else if (snapshot.ScanTwo is null)
        {
            OrganicScanProgress = "Sample 1 of 3 recorded";
        }
        else
        {
            OrganicScanProgress = "Samples 1 and 2 of 3 recorded";
        }
        BioFirstFootfall = exobiologyState.CurrentBodyFirstFootfall switch
        {
            true => "Confirmed; 5x reward applies",
            false => "Not first footfall",
            null => "Unknown for current body",
        };
        IsCurrentBodyFirstFootfall =
            exobiologyState.CurrentBodyFirstFootfall == true;
        CanToggleCurrentBodyFirstFootfall =
            exobiologyState.CurrentBodySystemAddress is not null
            && exobiologyState.CurrentBodyId is not null
            && SystemSurvey.Snapshot.SystemAddress
                == exobiologyState.CurrentBodySystemAddress;
        IsOrganicSample1Complete = snapshot.ScanOne is not null;
        IsOrganicSample2Complete = snapshot.ScanTwo is not null;
        OnPropertyChanged(nameof(HasActiveOrganicSample));
    }

    private static bool IsExobiologyContextEvent(string eventName)
    {
        return eventName is "Location"
            or "FSDJump"
            or "CarrierJump"
            or "ApproachBody"
            or "Scan"
            or "Disembark";
    }

    private async Task LoadCurrentSystemHistoryAsync()
    {
        var current = SystemSurvey.Snapshot;
        if (string.IsNullOrWhiteSpace(activeProfileFrontierId)
            || string.IsNullOrWhiteSpace(current.SystemName)
            || current.SystemAddress is not { } systemAddress
            || systemAddress <= 0)
        {
            return;
        }

        var key = activeProfileFrontierId + "\n" + systemAddress;
        if (string.Equals(
            loadedSystemHistoryKey,
            key,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedSystemHistoryKey = key;
        var result = await systemScanPersistenceStore.LoadAsync(
            activeProfileFrontierId,
            activeProfileCommanderName ?? journalState.CommanderName,
            current.SystemName,
            systemAddress,
            current.StarPosition,
            CancellationToken.None);
        if (result.Error is not null)
        {
            var message = "Imported system history was preserved but could not "
                + "be loaded safely from "
                + Path.GetFileName(result.Path)
                + ": "
                + result.Error;
            applicationLogService?.Append(message);
            StatusMessage = message;
            return;
        }

        if (result.Snapshot is { } history)
        {
            SystemSurvey.MergeKnownSystemData(history);
        }
    }

    private async Task LoadCurrentSystemBodyDataAsync()
    {
        if (systemBodyDataClient is null)
        {
            return;
        }

        if (!SystemSurvey.UseExternalData)
        {
            loadedSystemBodyDataKey = null;
            ResetSystemBodyDataRetry();
            CancelSystemBodyDataRequest();
            return;
        }

        var current = SystemSurvey.Snapshot;
        if (string.IsNullOrWhiteSpace(current.SystemName)
            || current.SystemAddress is not { } systemAddress
            || systemAddress <= 0)
        {
            loadedSystemBodyDataKey = null;
            ResetSystemBodyDataRetry();
            CancelSystemBodyDataRequest();
            return;
        }

        var key = systemAddress
            + "\nbiology="
            + SystemSurvey.UseExternalBioData;
        var sameKey = string.Equals(
            loadedSystemBodyDataKey,
            key,
            StringComparison.Ordinal);
        if (sameKey
            && (systemBodyDataRetryAt is null
                || systemBodyDataRetryAt > DateTimeOffset.UtcNow))
        {
            return;
        }

        if (!sameKey)
        {
            ResetSystemBodyDataRetry();
        }

        CancelSystemBodyDataRequest();
        var cancellation = new CancellationTokenSource();
        systemBodyDataCancellation = cancellation;
        loadedSystemBodyDataKey = key;
        systemBodyDataRetryAt = null;
        try
        {
            var result = await systemBodyDataClient.GetAsync(
                current.SystemName,
                systemAddress,
                cancellation.Token);
            if (cancellation.IsCancellationRequested
                || SystemSurvey.Snapshot.SystemAddress != systemAddress
                || !SystemSurvey.UseExternalData)
            {
                return;
            }

            var changed = false;
            foreach (var provider in result.Providers)
            {
                changed |= SystemSurvey.MergeKnownSystemData(
                    provider.Snapshot,
                    SystemSurvey.UseExternalBioData);
            }

            foreach (var warning in result.Warnings)
            {
                applicationLogService?.Append(warning);
            }

            ScheduleSystemBodyDataRetry(result.NotIndexedProviders);

            if (changed)
            {
                await PersistCurrentSystemScanAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer system or preference replaced this request.
        }
        finally
        {
            if (ReferenceEquals(systemBodyDataCancellation, cancellation))
            {
                systemBodyDataCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ScheduleSystemBodyDataRetry(
        IReadOnlyList<string> notIndexedProviders)
    {
        if (notIndexedProviders.Count == 0)
        {
            ResetSystemBodyDataRetry();
            return;
        }

        systemBodyDataRetryAttempts++;
        if (systemBodyDataRetryAttempts > MaximumSystemBodyDataRetryAttempts)
        {
            systemBodyDataRetryAt = null;
            applicationLogService?.Append(
                $"External body data remains unindexed by {string.Join(", ", notIndexedProviders)}; "
                    + "automatic retries are paused until the system context changes.");
            return;
        }

        var multiplier = 1L << (systemBodyDataRetryAttempts - 1);
        var delayTicks = Math.Min(
            systemBodyDataRetryDelay.Ticks * multiplier,
            MaximumSystemBodyDataRetryDelay.Ticks);
        var delay = TimeSpan.FromTicks(delayTicks);
        systemBodyDataRetryAt = DateTimeOffset.UtcNow + delay;
        applicationLogService?.Append(
            $"External body data is not indexed yet by {string.Join(", ", notIndexedProviders)}; "
                + $"retry {systemBodyDataRetryAttempts:N0} of "
                + $"{MaximumSystemBodyDataRetryAttempts:N0} is scheduled in "
                + $"{delay.TotalSeconds:N0} seconds.");
    }

    private void StartSystemBodyDataRetryIfDue()
    {
        if (systemBodyDataRetryAt is null
            || systemBodyDataRetryAt > DateTimeOffset.UtcNow)
        {
            return;
        }

        PendingSystemBodyDataLoad = LoadCurrentSystemBodyDataAsync();
    }

    private void ResetSystemBodyDataRetry()
    {
        systemBodyDataRetryAt = null;
        systemBodyDataRetryAttempts = 0;
    }

    private void CancelSystemBodyDataRequest()
    {
        var cancellation = systemBodyDataCancellation;
        systemBodyDataCancellation = null;
        cancellation?.Cancel();
    }

    private async Task PersistSystemScanAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        var snapshot = SystemSurvey.Snapshot;
        if (string.IsNullOrWhiteSpace(activeProfileFrontierId)
            || snapshot.SystemAddress is not { } systemAddress
            || systemAddress <= 0
            || string.IsNullOrWhiteSpace(snapshot.SystemName))
        {
            return;
        }

        foreach (var journalEvent in journalEvents)
        {
            if (!IsSystemVisitEvent(journalEvent.EventName)
                || journalEvent.Timestamp is not { } timestamp
                || !TryGetSystemAddress(journalEvent, out var eventAddress)
                || eventAddress != systemAddress)
            {
                continue;
            }

            activeSystemVisitAddress = eventAddress;
            activeSystemVisitedAt = timestamp;
        }

        if (activeSystemVisitAddress != systemAddress
            || activeSystemVisitedAt is not { } visitedAt
            || !journalEvents.Any(journalEvent =>
                IsSystemScanPersistenceEvent(journalEvent.EventName)))
        {
            return;
        }

        await PersistCurrentSystemScanAsync(snapshot, visitedAt);
    }

    private async Task PersistCurrentSystemScanAsync(
        (int BodyId, bool Value)? firstFootfallCorrection = null)
    {
        var snapshot = SystemSurvey.Snapshot;
        if (snapshot.SystemAddress is not { } systemAddress
            || activeSystemVisitAddress != systemAddress
            || activeSystemVisitedAt is not { } visitedAt)
        {
            return;
        }

        await PersistCurrentSystemScanAsync(
            snapshot,
            visitedAt,
            firstFootfallCorrection);
    }

    private async Task PersistCurrentSystemScanAsync(
        SystemScanSnapshot snapshot,
        DateTimeOffset visitedAt,
        (int BodyId, bool Value)? firstFootfallCorrection = null)
    {
        if (string.IsNullOrWhiteSpace(activeProfileFrontierId))
        {
            return;
        }

        try
        {
            var context = new SystemScanPersistenceContext(
                activeProfileFrontierId,
                activeProfileCommanderName ?? journalState.CommanderName,
                visitedAt);
            var result = firstFootfallCorrection is { } correction
                ? await systemScanPersistenceStore
                    .SaveFirstFootfallCorrectionAsync(
                        context,
                        snapshot,
                        correction.BodyId,
                        correction.Value,
                        CancellationToken.None)
                : await systemScanPersistenceStore.SaveAsync(
                    context,
                    snapshot,
                    CancellationToken.None);
            SystemSurvey.SetRepeatVisitBiologySuppression(
                result.ShouldSuppressBiologyOverlays);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            var message = "System survey history was not updated because its "
                + "legacy-compatible data file could not be written safely: "
                + exception.Message;
            applicationLogService?.Append(message);
            StatusMessage = message;
        }
    }

    private static bool IsSystemVisitEvent(string eventName)
    {
        return eventName is "Location" or "FSDJump" or "CarrierJump";
    }

    private static bool IsSystemScanPersistenceEvent(string eventName)
    {
        return eventName is "Location"
            or "FSDJump"
            or "CarrierJump"
            or "FSSDiscoveryScan"
            or "FSSAllBodiesFound"
            or "Scan"
            or "ScanBaryCentre"
            or "SAAScanComplete"
            or "FSSBodySignals"
            or "SAASignalsFound"
            or "ScanOrganic"
            or "CodexEntry"
            or "FSSSignalDiscovered"
            or "ApproachBody"
            or "Touchdown"
            or "SupercruiseExit"
            or "Disembark";
    }

    private static bool TryGetSystemAddress(
        JournalEventEnvelope journalEvent,
        out long systemAddress)
    {
        systemAddress = 0;
        if (!journalEvent.Payload.TryGetProperty(
                "SystemAddress",
                out var address))
        {
            return false;
        }

        if (address.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return address.TryGetInt64(out systemAddress);
        }

        return address.ValueKind == System.Text.Json.JsonValueKind.String
            && long.TryParse(address.GetString(), out systemAddress);
    }

    public async Task ResetExplorationAsync()
    {
        if (!IsResetExplorationPending)
        {
            IsResetExplorationPending = true;
            ExplorationStatusMessage = "Select Confirm reset to clear all six exploration totals.";
            return;
        }

        explorationState.Reset();
        var snapshot = explorationState.CreateSnapshot();
        UpdateExplorationDisplay(snapshot);
        IsResetExplorationPending = false;
        await SaveExplorationAsync(snapshot);
    }

    private Task CancelResetExplorationAsync()
    {
        IsResetExplorationPending = false;
        ExplorationStatusMessage = "Reset cancelled; totals were not changed.";
        return Task.CompletedTask;
    }

    public async Task ResetExobiologyAsync()
    {
        if (!IsResetExobiologyPending)
        {
            IsResetExobiologyPending = true;
            ExobiologyStatusMessage = "Select Confirm clear to remove all unclaimed "
                + "organic rewards. Active sample progress will be kept.";
            return;
        }

        exobiologyState.ClearUnclaimedRewards();
        var snapshot = exobiologyState.CreateSnapshot();
        UpdateExobiologyDisplay(snapshot);
        IsResetExobiologyPending = false;
        await SaveExobiologyAsync(snapshot);
    }

    public async Task ClearSurfaceTrackersAsync()
    {
        try
        {
            await SurfaceSurvey.ClearAllTrackersAsync(
                firstFootfallInferenceCancellation.Token);
            ExobiologyStatusMessage = SurfaceSurvey.StatusText;
        }
        catch (OperationCanceledException)
        {
            // Disposal/cancellation must not fault the async-void command.
        }
        finally
        {
            clearSurfaceTrackersCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task<bool> ToggleCurrentBodyFirstFootfallAsync()
    {
        var system = SystemSurvey.Snapshot;
        if (exobiologyState.CurrentBodySystemAddress is not { } systemAddress
            || exobiologyState.CurrentBodyId is not { } bodyId
            || system.SystemAddress != systemAddress)
        {
            ExobiologyStatusMessage =
                "First-footfall state cannot be changed until the current body is known.";
            return false;
        }

        var value = exobiologyState.CurrentBodyFirstFootfall != true;
        if (!SystemSurvey.SetBodyFirstFootfall(bodyId, value))
        {
            ExobiologyStatusMessage =
                "First-footfall state cannot be changed until the current body is known.";
            return false;
        }

        exobiologyState.SetFirstFootfall(systemAddress, bodyId, value);
        var snapshot = exobiologyState.CreateSnapshot();
        UpdateExobiologyDisplay(snapshot);
        SystemSurvey.ApplyUpdate([], null, snapshot);
        await SaveExobiologyAsync(snapshot);
        await PersistCurrentSystemScanAsync((bodyId, value));
        return true;
    }

    private async Task<int> ApplyFirstFootfallTextCommandsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        var applied = 0;
        foreach (var journalEvent in journalEvents)
        {
            if (!TryGetFirstFootfallCommand(journalEvent, out var requestedBodyName))
            {
                continue;
            }

            if (await TryApplyFirstFootfallCommandAsync(requestedBodyName))
            {
                applied++;
            }
        }

        return applied;
    }

    private static bool TryGetFirstFootfallCommand(
        JournalEventEnvelope journalEvent,
        out string? requestedBodyName)
    {
        requestedBodyName = null;
        if (journalEvent.EventName != "SendText"
            || !journalEvent.Payload.TryGetProperty("Message", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var message = value.GetString()?.Trim().ToLowerInvariant();
        if (message is null
            || !(message.StartsWith(".firstfoot", StringComparison.Ordinal)
                || message.StartsWith(".ff", StringComparison.Ordinal)))
        {
            return false;
        }

        requestedBodyName = message.Split(' ', 2) is { Length: 2 } parts
            ? parts[1].Trim()
            : null;
        return true;
    }

    private async Task<bool> TryApplyFirstFootfallCommandAsync(
        string? requestedBodyName)
    {
        var system = SystemSurvey.Snapshot;
        if (system.SystemAddress is not { } systemAddress)
        {
            ExobiologyStatusMessage =
                "First-footfall state cannot be changed until the current system is known.";
            return false;
        }

        var body = ResolveFirstFootfallBody(system, requestedBodyName);
        if (body is null)
        {
            ExobiologyStatusMessage =
                "First-footfall state cannot be changed until the current body is known.";
            return false;
        }

        var firstFootfall = !body.IsFirstFootfall;
        if (!SystemSurvey.SetBodyFirstFootfall(body.BodyId, firstFootfall))
        {
            return false;
        }

        exobiologyState.SetFirstFootfall(
            systemAddress,
            body.BodyId,
            firstFootfall);
        await PersistCurrentSystemScanAsync((body.BodyId, firstFootfall));
        ExobiologyStatusMessage = firstFootfall
            ? $"Recorded first footfall for {body.Name}."
            : $"Cleared first footfall for {body.Name}.";
        return true;
    }

    private static SystemScanBodySnapshot? ResolveFirstFootfallBody(
        SystemScanSnapshot system,
        string? requestedBodyName)
    {
        var body = string.IsNullOrWhiteSpace(requestedBodyName)
            ? null
            : system.Bodies.FirstOrDefault(candidate =>
                BodyNameMatchesCommand(
                    candidate,
                    system.SystemName,
                    requestedBodyName));
        return body
            ?? (system.CurrentBodyId is { } currentBodyId
                ? system.Bodies.FirstOrDefault(candidate =>
                    candidate.BodyId == currentBodyId)
                : null);
    }

    private async Task<bool> ApplyDesktopTextCommandsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        var requestShutdown = false;
        foreach (var journalEvent in journalEvents)
        {
            if (!TryGetDesktopTextCommand(journalEvent, out var command))
            {
                continue;
            }

            requestShutdown |= await ExecuteDesktopTextCommandAsync(command);
        }

        return requestShutdown;
    }

    private static bool TryGetDesktopTextCommand(
        JournalEventEnvelope journalEvent,
        out string command)
    {
        command = string.Empty;
        if (journalEvent.EventName != "SendText"
            || !journalEvent.Payload.TryGetProperty("Message", out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        command = value.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        return command.Length > 0;
    }

    private async Task<bool> ExecuteDesktopTextCommandAsync(string command)
    {
        switch (command)
        {
            case ".imgs":
                await OpenCurrentSystemScreenshotFolderAsync();
                return false;
            case ".kill":
                return HandleKillDesktopCommand();
            case "!" when HumanSite.ActiveSite is { } site:
                await GroundTarget.SetTargetAsync(
                    new SurfaceCoordinate(
                        site.Location.Latitude,
                        site.Location.Longitude),
                    "The active settlement origin is now the ground target.");
                return false;
            case "@@":
                await CaptureShipCockpitOffsetAsync();
                return false;
            case "!!":
                await CopyGroundTargetOffsetAsync();
                return false;
            case "..":
                await CopySettlementOffsetAsync();
                return false;
            case "//":
                CompareSettlementOffsetCalculations();
                return false;
            default:
                return false;
        }
    }

    private bool HandleKillDesktopCommand()
    {
        if (journalCommandShutdownRequester is null)
        {
            StatusMessage = "The desktop shutdown service is not available.";
            return false;
        }

        return true;
    }

    private async Task CaptureShipCockpitOffsetAsync()
    {
        if (!TryGetSurfaceCommandContext(
                out var currentStatus,
                out var currentLocation,
                out var radius)
            || string.IsNullOrWhiteSpace(journalState.ShipType))
        {
            StatusMessage =
                "A current ship and surface position are required to calibrate its cockpit offset.";
            return;
        }

        var shipType = journalState.ShipType;
        var offset = HumanSiteNavigation.GetSiteOffset(
            GroundTarget.Target,
            currentLocation,
            radius,
            currentStatus.NormalizedHeading);
        HumanSiteVehicleOffsets.Set(shipType, offset);
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{{ \"{shipType}\", new HumanSiteMapPoint({offset.X:R}, {offset.Y:R}) }}, ");
        applicationLogService?.Append("Cockpit offset: " + text);
        if (await WriteJournalCommandClipboardAsync(text))
        {
            StatusMessage =
                $"Captured and copied the {shipType} cockpit offset for this session.";
        }
    }

    private async Task CopyGroundTargetOffsetAsync()
    {
        if (!TryGetAlignedSettlementCommandContext(
                out var currentStatus,
                out var currentLocation,
                out var radius,
                out var siteHeading))
        {
            return;
        }

        var offset = HumanSiteNavigation.GetSiteOffset(
            GroundTarget.Target,
            currentLocation,
            radius,
            siteHeading);
        var rotation = SurfaceNavigation.NormalizeDegrees(
            currentStatus.NormalizedHeading - siteHeading);
        var text = "\"offset\": " + FormatMapPoint(offset);
        if (rotation != 0)
        {
            text += string.Create(
                CultureInfo.InvariantCulture,
                $", \"rot\": {rotation:R}");
        }

        applicationLogService?.Append(text);
        if (await WriteJournalCommandClipboardAsync(text))
        {
            StatusMessage =
                "Copied the ground-target offset and settlement-relative rotation.";
        }
    }

    private async Task CopySettlementOffsetAsync()
    {
        if (!TryGetAlignedSettlementCommandContext(
                out _,
                out var currentLocation,
                out var radius,
                out var siteHeading)
            || HumanSite.ActiveSite is not { } site)
        {
            return;
        }

        var offset = HumanSiteNavigation.GetSiteOffset(
            new SurfaceCoordinate(
                site.Location.Latitude,
                site.Location.Longitude),
            currentLocation,
            radius,
            siteHeading);
        var text = FormatMapPoint(offset);
        applicationLogService?.Append(
            "Relative to settlement origin: " + text);
        if (await WriteJournalCommandClipboardAsync(text))
        {
            StatusMessage = "Copied the current settlement-relative offset.";
        }
    }

    private void CompareSettlementOffsetCalculations()
    {
        if (!TryGetAlignedSettlementCommandContext(
                out _,
                out var currentLocation,
                out var radius,
                out var siteHeading)
            || HumanSite.ActiveSite is not { } site)
        {
            return;
        }

        var siteLocation = new SurfaceCoordinate(
            site.Location.Latitude,
            site.Location.Longitude);
        var direct = HumanSiteNavigation.GetSiteOffset(
            siteLocation,
            currentLocation,
            radius,
            siteHeading);
        var distance = SurfaceNavigation.GetDistance(
            siteLocation,
            currentLocation,
            radius);
        var angle = SurfaceNavigation.NormalizeDegrees(
            SurfaceNavigation.GetBearing(siteLocation, currentLocation)
                - siteHeading);
        var legacyRadians = (180 - angle) * Math.PI / 180;
        var alternate = new HumanSiteMapPoint(
            Math.Sin(legacyRadians) * distance,
            Math.Cos(legacyRadians) * distance);
        applicationLogService?.Append(
            "Settlement offset comparison: alternate "
                + FormatMapPoint(alternate)
                + " vs direct "
                + FormatMapPoint(direct));
        StatusMessage =
            "Settlement offset comparison was written to the application log.";
    }

    private bool TryGetAlignedSettlementCommandContext(
        out EliteStatus currentStatus,
        out SurfaceCoordinate currentLocation,
        out double radius,
        out double siteHeading)
    {
        siteHeading = 0;
        if (!TryGetSurfaceCommandContext(
                out currentStatus,
                out currentLocation,
                out radius)
            || HumanSite.ActiveSite is not { Heading: { } heading })
        {
            StatusMessage =
                "An aligned settlement and current surface position are required for this measurement.";
            return false;
        }

        siteHeading = heading;
        return true;
    }

    private bool TryGetSurfaceCommandContext(
        out EliteStatus currentStatus,
        out SurfaceCoordinate currentLocation,
        out double radius)
    {
        currentStatus = latestStatus!;
        currentLocation = default;
        radius = 0;
        if (latestStatus is not { HasLatitudeLongitude: true } status
            || status.PlanetRadius <= 0)
        {
            return false;
        }

        try
        {
            currentLocation = new SurfaceCoordinate(
                status.Latitude,
                status.Longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        currentStatus = status;
        radius = (double)status.PlanetRadius;
        return double.IsFinite(radius) && radius > 0;
    }

    private async Task<bool> WriteJournalCommandClipboardAsync(string text)
    {
        if (journalCommandClipboardWriter is null)
        {
            StatusMessage = "The desktop clipboard is not available.";
            return false;
        }

        try
        {
            await journalCommandClipboardWriter(text);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The measurement could not be copied: "
                + exception.Message;
            return false;
        }
    }

    private static string FormatMapPoint(HumanSiteMapPoint point)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{ \"X\": {point.X:R}, \"Y\": {point.Y:R} }}");
    }

    private async Task<bool> OpenCurrentSystemScreenshotFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(journalState.SystemName))
        {
            StatusMessage =
                "The screenshot folder cannot be opened until the current system is known.";
            return false;
        }

        var folder = Path.Combine(
            ScreenshotProcessing.TargetFolder,
            SystemNoteStore.MakeSafeFileName(journalState.SystemName));
        if (!Directory.Exists(folder))
        {
            StatusMessage =
                $"No screenshot folder exists for {journalState.SystemName}.";
            return false;
        }

        if (journalCommandDirectoryLauncher is null)
        {
            StatusMessage = "The desktop folder launcher is not available.";
            return false;
        }

        try
        {
            var launched = await journalCommandDirectoryLauncher(
                new DirectoryInfo(folder));
            StatusMessage = launched
                ? "Opened the current system screenshot folder."
                : "The operating system could not open the screenshot folder.";
            return launched;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The screenshot folder could not be opened: "
                + exception.Message;
            return false;
        }
    }

    private static bool BodyNameMatchesCommand(
        SystemScanBodySnapshot body,
        string? systemName,
        string requestedName)
    {
        var localName = !string.IsNullOrWhiteSpace(systemName)
            && body.Name.StartsWith(systemName, StringComparison.OrdinalIgnoreCase)
                ? body.Name[systemName.Length..].Trim()
                : body.Name;
        return string.Equals(
                localName,
                requestedName,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                localName.Replace(" ", string.Empty, StringComparison.Ordinal),
                requestedName,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                body.ShortName,
                requestedName,
                StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> TryInferFirstFootfallAsync(
        JournalMonitorUpdate update)
    {
        var preferences = firstFootfallInferenceSettingsStore.Load();
        var system = SystemSurvey.Snapshot;
        var body = system.CurrentBodyId is { } bodyId
            ? system.Bodies.FirstOrDefault(candidate =>
                candidate.BodyId == bodyId)
            : null;
        if (!CanAttemptFirstFootfallInference(update, preferences, system, body))
        {
            return false;
        }

        var systemAddress = system.SystemAddress!.Value;
        var result = await DetectFirstFootfallAsync(preferences);
        if (result is null || !result.Detected)
        {
            return false;
        }

        if (!IsFirstFootfallContextStillValid(systemAddress, body!))
        {
            applicationLogService?.Append(
                "Ignored a first-footfall notification because the active "
                    + "system or body changed during detection.");
            return false;
        }

        if (!SystemSurvey.SetCurrentBodyFirstFootfall(true))
        {
            return false;
        }

        exobiologyState.SetFirstFootfall(systemAddress, body!.BodyId, true);
        var message = "First footfall inferred from Elite's on-screen notification "
            + $"after {result.SampleCount:N0} sample(s); match ratio "
            + $"{result.MaximumMatchRatio:P3}.";
        applicationLogService?.Append(message);
        ExobiologyStatusMessage = message;
        return true;
    }

    private bool CanAttemptFirstFootfallInference(
        JournalMonitorUpdate update,
        FirstFootfallInferencePreferences preferences,
        SystemScanSnapshot system,
        SystemScanBodySnapshot? body) =>
        !update.IsBootstrapRead
        && preferences.Enabled
        && Guardian.ActiveSite is null
        && update.JournalEvents.Any(IsSurfaceDisembark)
        && system.SystemAddress is not null
        && body is not null
        && system.Population == 0
        && !body.IsFirstFootfall
        && body.WasFootfalled != false
        && !IsKnownLegacyValuableBody(body.Kind);

    private async Task<FirstFootfallInferenceResult?> DetectFirstFootfallAsync(
        FirstFootfallInferencePreferences preferences)
    {
        try
        {
            return await firstFootfallInferenceService.DetectAsync(
                preferences,
                firstFootfallInferenceCancellation.Token);
        }
        catch (OperationCanceledException) when (
            firstFootfallInferenceCancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException)
        {
            applicationLogService?.Append(
                "First-footfall notification detection stopped safely: "
                    + exception.Message);
            return null;
        }
    }

    private bool IsFirstFootfallContextStillValid(
        long systemAddress,
        SystemScanBodySnapshot body)
    {
        var current = SystemSurvey.Snapshot;
        return Guardian.ActiveSite is null
            && current.SystemAddress == systemAddress
            && current.CurrentBodyId == body.BodyId
            && current.Population == 0;
    }

    private static bool IsSurfaceDisembark(
        JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName != "Disembark")
        {
            return false;
        }

        var root = journalEvent.Payload;
        return root.TryGetProperty("OnPlanet", out var onPlanet)
            && onPlanet.ValueKind is JsonValueKind.True
            && (!root.TryGetProperty("OnStation", out var onStation)
                || onStation.ValueKind is not JsonValueKind.True);
    }

    private static bool IsKnownLegacyValuableBody(SystemBodyKind kind)
    {
        return kind is SystemBodyKind.Star
            or SystemBodyKind.GasGiant
            or SystemBodyKind.Planet
            or SystemBodyKind.LandablePlanet;
    }

    private Task CancelResetExobiologyAsync()
    {
        IsResetExobiologyPending = false;
        ExobiologyStatusMessage = "Clear cancelled; unclaimed rewards were not changed.";
        return Task.CompletedTask;
    }

    private void ApplyStatus(EliteStatus status)
    {
        VehicleState = DescribeVehicleState(status, journalState.ActiveSrvType);
        SurfacePosition = status.HasLatitudeLongitude
            ? $"{status.Latitude:F6}, {status.Longitude:F6}"
            : Unavailable;
        HeadingAndAltitude = status.HasLatitudeLongitude
            ? $"{status.NormalizedHeading}° / {status.Altitude:N0} m"
            : Unavailable;
        GameUiFocus = status.GuiFocus.ToString();
    }

    private static string DescribeVehicleState(
        EliteStatus status,
        string? activeSrvType)
    {
        if (status.OnFoot)
        {
            return "On foot";
        }

        if (status.InSrv)
        {
            return EliteSrvTypes.IsNomad(activeSrvType)
                ? "Nomad"
                : "SRV";
        }

        if (status.InFighter)
        {
            return "Fighter";
        }

        if (status.InMainShip)
        {
            return "Main ship";
        }

        if (status.InTaxi)
        {
            return "Taxi / shuttle";
        }

        return "Unknown";
    }

    private static void AppendReferenceCatalogWarnings(
        ApplicationLogService? applicationLogService,
        IReadOnlyList<string> legacyWarnings,
        IReadOnlyList<string> regionalWarnings,
        IReadOnlyList<string> knownSystemWarnings)
    {
        foreach (var warning in legacyWarnings)
        {
            applicationLogService?.Append(warning);
        }

        foreach (var warning in regionalWarnings)
        {
            applicationLogService?.Append(warning);
        }

        foreach (var warning in knownSystemWarnings)
        {
            applicationLogService?.Append(warning);
        }
    }

    private static string BuildReferenceDataStatus(
        LegacyReferenceCatalogLoadResult legacyReferences,
        RegionalCodexCandidateCatalog regionalCodexCandidates,
        KnownSystemAddressCatalog knownSystems)
    {
        var status = legacyReferences.LocalCatalogCount == 0
            ? "Validated embedded reference catalogs are active."
            : $"Using {legacyReferences.LocalCatalogCount:N0} validated catalog(s) "
                + "from the imported legacy profile; all others use embedded defaults.";
        if (legacyReferences.Warnings.Count > 0)
        {
            status += $" {legacyReferences.Warnings.Count:N0} incompatible "
                + "or incomplete legacy catalog(s) were ignored safely; see logs.";
        }

        status += DescribeRegionalCodexStatus(regionalCodexCandidates);
        status += DescribeKnownSystemsStatus(knownSystems);
        return status;
    }

    private static string DescribeRegionalCodexStatus(
        RegionalCodexCandidateCatalog regionalCodexCandidates)
    {
        if (regionalCodexCandidates.HasData)
        {
            return $" Imported regional Codex candidates: "
                + $"{regionalCodexCandidates.Count:N0}.";
        }

        if (regionalCodexCandidates.Warnings.Count > 0)
        {
            return " The imported regional Codex candidate "
                + "catalog was incompatible and ignored safely; see logs.";
        }

        return string.Empty;
    }

    private static string DescribeKnownSystemsStatus(
        KnownSystemAddressCatalog knownSystems)
    {
        if (knownSystems.HasData)
        {
            return $" Imported known system addresses: "
                + $"{knownSystems.Count:N0}.";
        }

        if (knownSystems.Warnings.Count > 0)
        {
            return " The imported known-system address "
                + "catalog was incompatible and ignored safely; see logs.";
        }

        return string.Empty;
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unavailable : value;
    }

    private void OnInaraApiKeyChanged(object? sender, EventArgs eventArgs)
    {
        inaraPublisher.CancelPendingPublication();
    }

    private void OnVoxStellarUploadEnabledChanged(bool enabled)
    {
        voxStellarPublisher.SetEnabled(enabled);
    }

    public void Dispose()
    {
        Task.Run(() => DisposeAsync().AsTask(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        List<Exception> failures = [];

        void TryDispose(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        async Task TryDisposeAsync(Func<ValueTask> cleanup)
        {
            try
            {
                await cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        TryDispose(routeAutoCopyCoordinator.Dispose);
        await TryDisposeAsync(boxelSurveyStats.DisposeAsync);
        TryDispose(BoxelSearch.CancelPendingOperations);
        await TryDisposeAsync(boxelSearchSession.DisposeAsync);
        TryDispose(JournalPostProcessor.Cancel);
        TryDispose(CancelSystemBodyDataRequest);
        await TryDisposeAsync(
            () => new ValueTask(PendingSystemBodyDataLoad));
        await TryDisposeAsync(() => new ValueTask(
            firstFootfallInferenceCancellation.CancelAsync()));
        TryDispose(firstFootfallInferenceService.Dispose);
        TryDispose(firstFootfallInferenceCancellation.Dispose);
        TryDispose(DiagnosticsLog.Dispose);
        TryDispose(JumpInfo.Dispose);
        TryDispose(BiologyPredictions.Dispose);
        TryDispose(BiologyCodex.Dispose);
        TryDispose(SurfaceSurvey.Dispose);
        TryDispose(CodexBingo.Dispose);
        TryDispose(StationInfo.Dispose);
        TryDispose(Colonization.Dispose);
        TryDispose(GalaxyMap.Dispose);
        ScreenshotProcessing.PropertyChanged -= OnScreenshotProcessingChanged;
        TryDispose(Guardian.Dispose);
        TryDispose(QuestWorkspace.Dispose);
        Inara.ApiKeyChanged -= OnInaraApiKeyChanged;
        using var inaraShutdownCancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(45));
        await TryDisposeAsync(
            () => new ValueTask(
                inaraPublisher.StopAsync(inaraShutdownCancellation.Token)));
        CommanderInstances.PropertyChanged -= OnCommanderInstancesPropertyChanged;
        TryDispose(CommanderInstances.Dispose);
        BiologyRewards.PropertyChanged -= OnBiologyRewardsChanged;
        TryDispose(OverlayInteraction.Dispose);
        TryDispose(FrontierProfile.Dispose);
        TryDispose(() => visitedStarsHttpClient?.Dispose());
        NetworkPrivacy.EddnUploadEnabledChanged -= OnEddnUploadEnabledChanged;
        if (eddnPublisher is IDisposable disposableEddnPublisher)
        {
            TryDispose(disposableEddnPublisher.Dispose);
        }
        VoxStellar.UploadEnabledChanged -= OnVoxStellarUploadEnabledChanged;
        if (voxStellarPublisher is IDisposable disposableVoxStellarPublisher)
        {
            TryDispose(disposableVoxStellarPublisher.Dispose);
        }
        questRuntimeCoordinator.Changed -= OnQuestCoordinatorChanged;
        await TryDisposeAsync(questRuntimeCoordinator.DisposeAsync);
        ThrowDisposalFailures(failures);
    }

    private static void ThrowDisposalFailures(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        throw new AggregateException(
            "One or more main-window resources failed to dispose.",
            failures);
    }

    private void OnEddnUploadEnabledChanged(bool enabled)
    {
        eddnPublisher.SetEnabled(enabled);
    }

    private void OnScreenshotProcessingChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        Guardian.RefreshAerialGuidance();
        if (eventArgs.PropertyName == nameof(
                ScreenshotProcessingViewModel.TargetFolder))
        {
            Guardian.RefreshScreenshotAvailability();
        }
    }

    private void OnCommanderInstancesPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName !=
            nameof(CommanderInstancesViewModel.HasMultipleGameWindows))
        {
            return;
        }

        var hasMultipleGameWindows =
            CommanderInstances.HasMultipleGameWindows;
        SetSharedCargoSuppressed(hasMultipleGameWindows);
        eddnPublisher.SetSuspended(hasMultipleGameWindows);
        OnPropertyChanged(nameof(IsSharedCargoSuppressed));
    }

    private void SetSharedCargoSuppressed(bool value)
    {
        if (value)
        {
            awaitFreshCargoSnapshot = true;
            cargoInventoryState.Reset(null);
            latestCargo = null;
            latestShipLocker = null;
            Guardian.ClearCargo();
        }

        FrontierProfile.UpdateLocalInventory(
            latestCargo,
            latestShipLocker,
            isSuppressed: value);

        DockToDock.SetSharedCargoSuppressed(value);
        Colonization.SetSharedCargoSuppressed(value);
    }

    private bool IsCurrentCommanderCompanionSnapshot(DateTimeOffset timestamp) =>
        companionIdentityChangedAt is not { } changedAt || timestamp >= changedAt;

    private void OnQuestCoordinatorChanged(object? sender, EventArgs eventArgs)
    {
        UpdateQuestOverlayPresentation(
            questRuntimeCoordinator.Snapshot,
            questSettingsStore.LoadEnabled());
        OnPropertyChanged(nameof(Quests));
        OnPropertyChanged(nameof(QuestUnreadMessageCount));
    }

    private void UpdateQuestOverlayPresentation(
        IReadOnlyList<QuestRuntimeSnapshot> quests,
        bool enabled)
    {
        QuestIndicator.Update(
            quests,
            latestStatus,
            enabled,
            journalState.MusicTrack);
        HumanSite.UpdateQuests(quests);
        var tags = enabled
            ? quests.SelectMany(quest => quest.Tags).ToArray()
            : [];
        GalaxyMap.UpdateQuestTags(tags);
        JumpInfo.UpdateQuestTags(tags);
        StationInfo.UpdateQuestTags(tags);
    }

    private void OnBiologyRewardsChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName ==
            nameof(BiologyRewardSettingsViewModel.Thresholds))
        {
            SystemSurvey.UpdateBiologyRewardThresholds(
                BiologyRewards.Thresholds);
        }
    }

    private static bool IsShowCodexCommand(JournalEventEnvelope journalEvent)
    {
        return journalEvent.EventName == "SendText"
            && journalEvent.Payload.TryGetProperty("Message", out var message)
            && message.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(
                message.GetString()?.Trim(),
                ".show",
                StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            try
            {
                await execute();
            }
            catch (OperationCanceledException)
            {
                // Command disposal/cancellation is not a user-facing failure.
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
