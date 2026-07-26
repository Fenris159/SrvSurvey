using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const string Unavailable = "—";

    private readonly JournalFolderResolution folderResolution;
    private readonly JournalDirectoryMonitor? journalMonitor;
    private readonly JournalSessionState journalState = new();
    private readonly ExplorationState explorationState = new();
    private readonly ExobiologyState exobiologyState;
    private readonly CommanderProfileStore commanderProfileStore;
    private readonly CommanderCodexStore commanderCodexStore;
    private readonly CommanderCodexJournalTracker commanderCodexJournalTracker;
    private readonly GreenGasGiantPublicationCoordinator
        greenGasGiantPublicationCoordinator;
    private readonly RavenThemeService? themeService;
    private readonly LegacyProfileImporter profileImporter;
    private readonly QuestRuntimeCoordinator questRuntimeCoordinator;
    private readonly QuestSettingsStore questSettingsStore;
    private readonly HttpClient? visitedStarsHttpClient;
    private readonly ApplicationLogService? applicationLogService;
    private readonly AsyncCommand importLegacyProfileCommand;
    private readonly AsyncCommand resetExplorationCommand;
    private readonly AsyncCommand cancelResetExplorationCommand;
    private readonly AsyncCommand resetExobiologyCommand;
    private readonly AsyncCommand cancelResetExobiologyCommand;
    private bool isBusy;
    private bool isImportingProfile;
    private string statusMessage;
    private string commanderName = Unavailable;
    private string frontierId = Unavailable;
    private string gameDescription = Unavailable;
    private string gameMode = Unavailable;
    private string systemDescription = Unavailable;
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
    private string exobiologyStatusMessage = "Waiting for commander profile.";
    private string commanderCodexStatusMessage =
        "Waiting for Commander Codex journal entries.";
    private bool isResetExobiologyPending;
    private string? activeProfileFrontierId;
    private string? activeProfileCommanderName;
    private bool activeProfileIsOdyssey = true;
    private NavigationItemViewModel selectedNavigation;
    private ThemeOptionViewModel selectedTheme;
    private LegacyProfileOptionViewModel? selectedLegacyProfile;
    private string legacyProfileSourcePath;
    private string profileStatusMessage;
    private string questStatusMessage = "Quests are disabled.";
    private string? activeProfileRavenApiKey;
    private string? surveyCodexFrontierId;
    private int? surveyCodexRegionId;
    private long? surveyCodexSystemAddress;
    private EliteStatus? latestStatus;
    private bool disposed;

    public MainWindowViewModel(
        string? configuredJournalDirectory,
        RavenThemeService? themeService = null,
        AppDataPaths? appDataPaths = null,
        LegacyProfileImporter? profileImporter = null,
        ExobiologyReferenceCatalog? exobiologyCatalog = null,
        IStarSystemResolver? starSystemResolver = null,
        IBoxelSystemResolver? boxelSystemResolver = null,
        GlobalInputSettingsViewModel? inputSettings = null,
        ColonizationViewModel? colonization = null,
        INearestSystemsClient? nearestSystemsClient = null,
        ISystemSummaryClient? systemSummaryClient = null,
        JumpInfoSettingsStore? jumpInfoSettingsStore = null,
        SystemSurveySettingsStore? systemSurveySettingsStore = null,
        BiologyPredictionsSettingsStore? biologyPredictionsSettingsStore = null,
        CombatSettingsStore? combatSettingsStore = null,
        GuardianOverlaySettingsStore? guardianOverlaySettingsStore = null,
        StationInfoSettingsStore? stationInfoSettingsStore = null,
        HumanSiteSettingsStore? humanSiteSettingsStore = null,
        ApplicationLogService? applicationLogService = null,
        LegacyOverlayLayoutStore? overlayLayoutStore = null,
        LegacyOverlayLayout? overlayLayout = null,
        IScreenshotProcessingService? screenshotProcessingService = null,
        QuestRuntimeCoordinator? questRuntimeCoordinator = null,
        QuestSettingsStore? questSettingsStore = null,
        string? targetFrontierId = null,
        ICommanderInstanceLauncher? commanderInstanceLauncher = null,
        IGameWindowSwitcher? gameWindowSwitcher = null,
        VisitedStarsCacheViewModel? visitedStarsCache = null,
        GreenGasGiantPublicationCoordinator?
            greenGasGiantPublicationCoordinator = null,
        NotificationSettingsStore? notificationSettingsStore = null,
        StreamOverlaySettingsStore? streamOverlaySettingsStore = null)
    {
        this.themeService = themeService;
        this.profileImporter = profileImporter ?? new LegacyProfileImporter();
        this.applicationLogService = applicationLogService;
        AppDataPaths = appDataPaths ?? AppDataPaths.ResolveCurrent();
        this.questSettingsStore = questSettingsStore
            ?? new QuestSettingsStore(AppDataPaths.UiSettingsPath);
        this.questRuntimeCoordinator = questRuntimeCoordinator
            ?? new QuestRuntimeCoordinator(
                new LegacyQuestStateStore(AppDataPaths.DataDirectory),
                new RavenQuestClient(),
                message => applicationLogService?.Append(message));
        QuestWorkspace = new QuestWorkspaceViewModel(
            this.questRuntimeCoordinator,
            this.questSettingsStore);
        QuestIndicator = new QuestIndicatorViewModel();
        this.questRuntimeCoordinator.Changed += OnQuestCoordinatorChanged;
        SystemNicknames = new SystemNicknameViewModel(
            SystemNicknameCatalog.Load(AppDataPaths.DataDirectory),
            new SystemNicknameSettingsStore(AppDataPaths.UiSettingsPath));
        DiagnosticsLog = new DiagnosticsLogViewModel(applicationLogService);
        JournalInspector = new JournalInspectorViewModel(
            ReplayQuestJournalEventAsync);
        folderResolution = JournalFolderLocator.ResolveCurrent(
            configuredJournalDirectory);
        commanderProfileStore = new CommanderProfileStore(
            AppDataPaths.DataDirectory);
        commanderCodexStore = new CommanderCodexStore(
            AppDataPaths.DataDirectory);
        commanderCodexJournalTracker = new CommanderCodexJournalTracker(
            commanderCodexStore);
        InputSettings = inputSettings ?? new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(AppDataPaths.UiSettingsPath),
            OverlayPlatformCapabilities.DetectCurrent());
        var sharedOverlayLayoutStore = overlayLayoutStore
            ?? new LegacyOverlayLayoutStore(AppDataPaths.DataDirectory);
        OverlayLayout = new OverlayLayoutSettingsViewModel(
            sharedOverlayLayoutStore,
            overlayLayout ?? sharedOverlayLayoutStore.Load());
        ScreenshotProcessing = new ScreenshotProcessingViewModel(
            new ScreenshotProcessingSettingsStore(AppDataPaths.UiSettingsPath),
            screenshotProcessingService);
        Notifications = new NotificationViewModel(
            notificationSettingsStore
                ?? new NotificationSettingsStore(AppDataPaths.UiSettingsPath));
        StreamOverlay = new StreamOverlayViewModel(
            streamOverlaySettingsStore
                ?? new StreamOverlaySettingsStore(AppDataPaths.UiSettingsPath));
        NetworkPrivacy = new NetworkPrivacyViewModel(
            new NetworkPrivacySettingsStore(AppDataPaths.UiSettingsPath));
        this.greenGasGiantPublicationCoordinator =
            greenGasGiantPublicationCoordinator
                ?? new GreenGasGiantPublicationCoordinator(
                    GreenGasGiantCriteriaCatalog.LoadEmbedded(),
                    new GreenGasGiantClient());
        Colonization = colonization ?? new ColonizationViewModel(
            new ColonizationSettingsStore(AppDataPaths.UiSettingsPath),
            commanderProfileStore: commanderProfileStore,
            legacyProfileStore: new LegacyColonizationProfileStore(
                AppDataPaths.DataDirectory));
        var sharedSystemResolver = starSystemResolver
            ?? new SpanshStarSystemResolver();
        var sharedExobiologyCatalog = exobiologyCatalog
            ?? ExobiologyReferenceCatalog.LoadEmbedded();
        var systemNoteStore = new SystemNoteStore(AppDataPaths.DataDirectory);
        var systemNotesSettingsStore = new SystemNotesSettingsStore(
            AppDataPaths.DataDirectory);
        var journeyService = new JourneyService(
            new JourneyStore(AppDataPaths.DataDirectory),
            new JourneyJournalHistoryReader(
                folderResolution.SelectedPath
                    ?? folderResolution.CandidatePaths.FirstOrDefault()
                    ?? Path.Combine(AppDataPaths.DataDirectory, "journals")),
            commanderProfileStore,
            sharedExobiologyCatalog);
        Search = new SphereLimitViewModel(
            commanderProfileStore,
            sharedSystemResolver);
        NearestSystems = new NearestSystemsViewModel(
            nearestSystemsClient ?? new NearestSystemsClient(),
            sharedSystemResolver);
        BoxelSearch = new BoxelSearchViewModel(
            commanderProfileStore,
            new LegacySystemDataReader(AppDataPaths.DataDirectory),
            new EmptyBoxelStore(AppDataPaths.DataDirectory),
            boxelSystemResolver ?? new SpanshBoxelClient());
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
        Route = new RouteWorkspaceViewModel(
            new FollowRouteService(
                new FollowRouteStore(AppDataPaths.DataDirectory)),
            new RouteNameImporter(sharedSystemResolver),
            new SpanshRouteClient());
        var sharedSystemSummaryClient = systemSummaryClient
            ?? new SystemSummaryClient();
        JumpInfo = new JumpInfoViewModel(
            sharedSystemSummaryClient,
            jumpInfoSettingsStore
                ?? new JumpInfoSettingsStore(AppDataPaths.UiSettingsPath));
        StationInfo = new StationInfoViewModel(
            sharedSystemSummaryClient,
            stationInfoSettingsStore
                ?? new StationInfoSettingsStore(AppDataPaths.UiSettingsPath));
        HumanSite = new HumanSiteViewModel(
            humanSiteSettingsStore
                ?? new HumanSiteSettingsStore(AppDataPaths.UiSettingsPath),
            new HumanSiteKnowledgeStore(AppDataPaths.DataDirectory),
            new HumanSiteMaterialStore(AppDataPaths.DataDirectory));
        SystemSurvey = new SystemSurveyViewModel(
            systemSurveySettingsStore
                ?? new SystemSurveySettingsStore(AppDataPaths.UiSettingsPath),
            biologyCatalog: sharedExobiologyCatalog);
        Combat = new CombatViewModel(
            combatSettingsStore
                ?? new CombatSettingsStore(AppDataPaths.UiSettingsPath),
            commanderProfileStore);
        var systemSurfaceStore = new SystemSurfaceStore(
            AppDataPaths.DataDirectory);
        SurfaceSurvey = new SurfaceSurveyViewModel(
            SystemSurvey,
            systemSurfaceStore,
            new SurfaceSurveyJournalTracker(
                systemSurfaceStore,
                sharedExobiologyCatalog));
        BiologyPredictions = new BiologyPredictionsViewModel(
            SystemSurvey,
            biologyPredictionsSettingsStore
                ?? new BiologyPredictionsSettingsStore(
                    AppDataPaths.UiSettingsPath));
        BiologyCodex = new BiologyCodexViewModel(
            SystemSurvey,
            sharedExobiologyCatalog,
            BiologyCriteriaCatalog.LoadEmbedded(),
            () => activeProfileCommanderName ?? journalState.CommanderName);
        var journalImportDirectory = folderResolution.SelectedPath
            ?? folderResolution.CandidatePaths.FirstOrDefault()
            ?? Path.Combine(AppDataPaths.DataDirectory, "journals");
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
                commanderCodexStore));
        RamTah = new RamTahViewModel(commanderProfileStore);
        Guardian = new GuardianViewModel(
            AppDataPaths.DataDirectory,
            ramTah: RamTah,
            overlaySettingsStore: guardianOverlaySettingsStore
                ?? new GuardianOverlaySettingsStore(
                    AppDataPaths.UiSettingsPath),
            aerialAltitudeProvider: () => new GuardianAerialAltitudes(
                ScreenshotProcessing.AerialAltitudeAlpha,
                ScreenshotProcessing.AerialAltitudeBeta,
                ScreenshotProcessing.AerialAltitudeGamma));
        ScreenshotProcessing.PropertyChanged += (_, _) =>
            Guardian.RefreshAerialGuidance();
        exobiologyState = new ExobiologyState(sharedExobiologyCatalog);
        LegacyProfiles = LegacyProfileLocator.Discover(
                AppDataPaths.LegacyProfileCandidates)
            .Select(discovery => new LegacyProfileOptionViewModel(discovery))
            .ToArray();
        selectedLegacyProfile = LegacyProfiles.FirstOrDefault();
        legacyProfileSourcePath = selectedLegacyProfile?.Path ?? string.Empty;
        profileStatusMessage = GetInitialProfileStatus();
        importLegacyProfileCommand = new AsyncCommand(
            ImportLegacyProfileAsync,
            CanImportLegacyProfile);
        ImportLegacyProfileCommand = importLegacyProfileCommand;
        JournalFolderPath = folderResolution.SelectedPath
            ?? folderResolution.CandidatePaths.FirstOrDefault()
            ?? "No journal location is configured.";
        CandidatePaths = folderResolution.CandidatePaths.Count == 0
            ? "No default locations are available for this platform."
            : string.Join(Environment.NewLine, folderResolution.CandidatePaths);
        TargetFrontierId = string.IsNullOrWhiteSpace(targetFrontierId)
            ? null
            : targetFrontierId.Trim();
        CommanderInstances = new CommanderInstancesViewModel(
            new CommanderProfileCatalog(AppDataPaths.DataDirectory),
            commanderInstanceLauncher
                ?? new ApplicationCommanderInstanceLauncher(),
            JournalFolderPath,
            TargetFrontierId,
            gameWindowSwitcher);
        if (visitedStarsCache is null)
        {
            var processDetector = new EliteGameProcessDetector();
            visitedStarsHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(45),
            };
            VisitedStarsCache = new VisitedStarsCacheViewModel(
                new CommanderProfileCatalog(AppDataPaths.DataDirectory),
                new VisitedStarsCacheService(
                    visitedStarsHttpClient,
                    Path.Combine(AppDataPaths.CacheDirectory, "star-cache"),
                    processDetector.IsRunning),
                VisitedStarsCacheTargetLocator.ResolveCurrent,
                processDetector.IsRunning);
        }
        else
        {
            VisitedStarsCache = visitedStarsCache;
        }
        statusMessage = folderResolution.IsFound
            ? TargetFrontierId is null
                ? "Ready to read the newest Journal.*.log file."
                : $"Ready to read journals for {TargetFrontierId}."
            : $"Journal folder not found. Set {JournalFolderLocator.EnvironmentVariableName} "
                + "or start with --journal-directory <path>.";
        journalMonitor = folderResolution.SelectedPath is null
            ? null
            : new JournalDirectoryMonitor(
                folderResolution.SelectedPath,
                TargetFrontierId);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
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

        NavigationItems =
        [
            new("overview", "Overview", "01", "Commander and current journal state", true),
            new("exploration", "Exploration", "02", "Trip totals and body scans", true),
            new("exobiology", "Exobiology", "03", "Organic scans and unclaimed rewards", true),
            new("travel", "Travel", "04", "Ground targets, journeys, and routes", true),
            new("search", "Search", "05", "Spherical and boxel searches", true),
            new("guardian", "Guardian", "06", "Sites, maps, and Ram Tah", true),
            new("quests", "Quests", "07", "Communications and active objectives", true),
            new("colonisation", "Colonisation", "08", "Raven Colonial projects", true),
            new("diagnostics", "Diagnostics", "09", "Journal source and parsed state", true),
            new("settings", "Settings", "10", "Appearance and application options", true),
        ];
        selectedNavigation = NavigationItems[0];

        var currentTheme = themeService?.Current
            ?? RavenThemeCatalog.Get(RavenThemeCatalog.DefaultThemeKey);
        ThemeOptions = RavenThemeCatalog.All
            .Select(theme => new ThemeOptionViewModel(theme, SelectTheme))
            .ToArray();
        selectedTheme = ThemeOptions.Single(
            option => option.Definition.Key == currentTheme.Key);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; }

    public GlobalInputSettingsViewModel InputSettings { get; }

    public OverlayLayoutSettingsViewModel OverlayLayout { get; }

    public ScreenshotProcessingViewModel ScreenshotProcessing { get; }

    public NotificationViewModel Notifications { get; }

    public StreamOverlayViewModel StreamOverlay { get; }

    public NetworkPrivacyViewModel NetworkPrivacy { get; }

    public QuestWorkspaceViewModel QuestWorkspace { get; }

    public QuestIndicatorViewModel QuestIndicator { get; }

    public CommanderInstancesViewModel CommanderInstances { get; }

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

    public GroundTargetViewModel GroundTarget { get; }

    public SystemNotesViewModel SystemNotes { get; }

    public JourneyWorkspaceViewModel Journey { get; }

    public RouteWorkspaceViewModel Route { get; }

    public JumpInfoViewModel JumpInfo { get; }

    public StationInfoViewModel StationInfo { get; }

    public HumanSiteViewModel HumanSite { get; }

    public SystemSurveyViewModel SystemSurvey { get; }

    public SurfaceSurveyViewModel SurfaceSurvey { get; }

    public CombatViewModel Combat { get; }

    public BiologyPredictionsViewModel BiologyPredictions { get; }

    public BiologyCodexViewModel BiologyCodex { get; }

    public BiologyCodexBingoViewModel CodexBingo { get; }

    public SphereLimitViewModel Search { get; }

    public BoxelSearchViewModel BoxelSearch { get; }

    public NearestSystemsViewModel NearestSystems { get; }

    public GuardianViewModel Guardian { get; }

    public RamTahViewModel RamTah { get; }

    public ColonizationViewModel Colonization { get; }

    public SystemNicknameViewModel SystemNicknames { get; }

    public DiagnosticsLogViewModel DiagnosticsLog { get; }

    public JournalInspectorViewModel JournalInspector { get; }

    public JournalPostProcessorViewModel JournalPostProcessor { get; }

    public IReadOnlyList<LegacyProfileOptionViewModel> LegacyProfiles { get; }

    public string? TargetFrontierId { get; }

    public string ProfileDataDirectory => AppDataPaths.DataDirectory;

    public string ProfileBackupDirectory { get; }

    public ICommand ImportLegacyProfileCommand { get; }

    public event Func<Task>? ProfileImportPreparing;

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
                    : Directory.Exists(normalized)
                        ? "The selected legacy profile is ready for verified import."
                        : "The selected legacy profile folder does not exist or is unavailable.";
            }

            importLegacyProfileCommand.RaiseCanExecuteChanged();
        }
    }

    public string ProfileStatusMessage
    {
        get => profileStatusMessage;
        private set => SetField(ref profileStatusMessage, value);
    }

    public string ImportProfileButtonText => IsImportingProfile
        ? "Importing profile..."
        : HasCompletedLegacyImport
            ? "Legacy profile imported"
            : "Back up and import profile";

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

    public NavigationItemViewModel SelectedNavigation
    {
        get => selectedNavigation;
        set
        {
            if (!SetField(ref selectedNavigation, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOverviewSelected));
            OnPropertyChanged(nameof(IsExplorationSelected));
            OnPropertyChanged(nameof(IsExobiologySelected));
            OnPropertyChanged(nameof(IsTravelSelected));
            OnPropertyChanged(nameof(IsSearchSelected));
            OnPropertyChanged(nameof(IsGuardianSelected));
            OnPropertyChanged(nameof(IsQuestsSelected));
            OnPropertyChanged(nameof(IsColonizationSelected));
            OnPropertyChanged(nameof(IsDiagnosticsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsPendingSelected));
            OnPropertyChanged(nameof(PendingPageTitle));
            OnPropertyChanged(nameof(PendingPageDescription));
            OnPropertyChanged(nameof(PendingPageGlyph));
        }
    }

    public bool IsOverviewSelected => SelectedNavigation.Key == "overview";

    public bool IsExplorationSelected => SelectedNavigation.Key == "exploration";

    public bool IsExobiologySelected => SelectedNavigation.Key == "exobiology";

    public bool IsTravelSelected => SelectedNavigation.Key == "travel";

    public bool IsSearchSelected => SelectedNavigation.Key == "search";

    public bool IsGuardianSelected => SelectedNavigation.Key == "guardian";

    public bool IsQuestsSelected => SelectedNavigation.Key == "quests";

    public bool IsColonizationSelected =>
        SelectedNavigation.Key == "colonisation";

    public bool IsDiagnosticsSelected => SelectedNavigation.Key == "diagnostics";

    public bool IsSettingsSelected => SelectedNavigation.Key == "settings";

    public bool IsPendingSelected => !SelectedNavigation.IsImplemented;

    public string PendingPageTitle => SelectedNavigation.Label;

    public string PendingPageDescription => SelectedNavigation.Description;

    public string PendingPageGlyph => SelectedNavigation.Glyph;

    public void ShowDiagnostics()
    {
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == "diagnostics");
    }

    public void ShowQuests()
    {
        SelectedNavigation = NavigationItems.Single(
            item => item.Key == "quests");
    }

    public async Task OpenCodexBingoNearestSearchAsync(
        CodexBingoNearestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        SelectedNavigation = NavigationItems.Single(item => item.Key == "search");
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

            var update = await journalMonitor.PollAsync();
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
                ProfileBackupDirectory);
            var settingsMigration = new LegacyUiSettingsMigrator()
                .MigrateIfNeeded(AppDataPaths);
            var retainedFiles = result.Manifest.PreviousDestinationEntries.Count
                - result.Manifest.Conflicts.Count;
            ProfileStatusMessage = $"Imported {result.Manifest.Entries.Count:N0} legacy files, "
                + $"retained {retainedFiles:N0} current-only files, and recorded "
                + $"{result.Manifest.Conflicts.Count:N0} path collisions. "
                + GetSettingsMigrationStatus(settingsMigration)
                + " Restart SrvSurvey to load the migrated profile. "
                + $"Verified backups: {result.BackupDirectory}";
            OnPropertyChanged(nameof(HasCompletedLegacyImport));
            OnPropertyChanged(nameof(ImportProfileButtonText));
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
        if (ProfileImportPreparing is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
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
        if (update.IsBootstrapRead)
        {
            latestStatus = update.Status;
        }
        else if (update.Status is not null)
        {
            latestStatus = update.Status;
        }

        JournalInspector.ApplyUpdate(update.JournalEvents, latestStatus);

        if (update.Status is not null)
        {
            exobiologyState.UpdateStatus(update.Status);
            GroundTarget.UpdateStatus(update.Status);
            Colonization.UpdateStatus(update.Status);
        }

        var scansLostToDeath = new HashSet<string>(StringComparer.Ordinal);
        var greenGasGiantResult =
            await greenGasGiantPublicationCoordinator.ApplyAsync(
                update.JournalEvents,
                NetworkPrivacy.UploadGreenGasGiantCandidates,
                allowPublishing: !update.IsBootstrapRead);
        NetworkPrivacy.ReportPublicationResult(greenGasGiantResult);
        if (!update.IsBootstrapRead)
        {
            Notifications.ReportGreenGasGiantUploads(greenGasGiantResult);
        }

        foreach (var warning in greenGasGiantResult.Warnings)
        {
            applicationLogService?.Append(warning);
        }
        foreach (var journalEvent in update.JournalEvents)
        {
            journalState.Apply(journalEvent);
        }
        JournalPostProcessor.SelectCommander(journalState.FrontierId);

        var commanderCodexResult =
            await commanderCodexJournalTracker.ApplyAsync(update.JournalEvents);
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

        Colonization.ApplyJournalEvents(update.JournalEvents);
        Colonization.UpdateSystemContext(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.SystemAddress);

        Search.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);
        NearestSystems.UpdateContext(
            journalState.SystemName,
            journalState.StarPosition,
            journalState.CommanderName);
        await CodexBingo.UpdateContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.StarPosition,
            forceRefresh: commanderCodexResult.DiscoveryEventCount > 0);
        SystemNotes.UpdateContext(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        BoxelSearch.UpdateCurrentSystem(
            journalState.SystemName,
            journalState.StarPosition);
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

        var loadedExistingProfile = await EnsureCommanderProfileAsync();
        await ApplyQuestUpdateAsync(update);
        await Colonization.SetCommanderAsync(journalState.CommanderName);
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

        await Route.UpdateContextAsync(
            journalState.FrontierId,
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition);
        if (!update.IsBootstrapRead)
        {
            await Route.ApplyJournalEventsAsync(update.JournalEvents);
        }

        var explorationBefore = explorationState.CreateSnapshot();
        var exobiologyVersionBefore = exobiologyState.Version;
        var boxelBefore = BoxelSearch.CreateNotificationState();
        var skipPersistedBootstrapEvents = update.IsBootstrapRead
            && loadedExistingProfile;
        if (update.NavRoute is not null)
        {
            await BoxelSearch.UpdateRouteAsync(update.NavRoute);
        }

        await Search.UpdateNavigationAsync(update.NavRoute, update.Status);

        if (!skipPersistedBootstrapEvents)
        {
            await BoxelSearch.ApplyJournalEventsAsync(update.JournalEvents);
        }

        Notifications.ApplyJournalEvents(
            update.JournalEvents,
            allowNotifications: !update.IsBootstrapRead);
        Notifications.ReportBoxelUpdate(
            boxelBefore,
            BoxelSearch.CreateNotificationState(),
            update.JournalEvents.Any(journalEvent =>
                journalEvent.EventName == "FSSAllBodiesFound"),
            allowNotifications: !update.IsBootstrapRead);

        await Guardian.ApplyJournalEventsAsync(
            update.JournalEvents,
            activeProfileCommanderName,
            allowLiveCommands: !update.IsBootstrapRead);
        await RamTah.ApplyJournalEventsAsync(update.JournalEvents);
        Guardian.UpdateCargo(update.Cargo);
        Colonization.UpdateCargo(update.Cargo);
        await Colonization.UpdateMarketAsync(update.Market);
        Combat.SetActiveBuildProjects(Colonization.HasProjects);
        Guardian.SetActiveBuildProjects(Colonization.HasProjects);
        HumanSite.SetActiveBuildProjects(Colonization.HasProjects);
        await Combat.ApplyUpdateAsync(
            update.JournalEvents,
            update.Status,
            processHistoricalProgress: !skipPersistedBootstrapEvents);

        if (update.Status is not null)
        {
            Guardian.UpdateStatus(update.Status);
            StationInfo.UpdateStatus(update.Status);
            await Route.UpdateStatusAsync(update.Status);
            await BoxelSearch.UpdateStatusAsync(
                update.Status,
                allowAutoCopy: !Route.ShouldAutoCopyNextHop);
        }

        HumanSite.SetStationInfoVisible(StationInfo.ShouldShow);
        await HumanSite.ApplyUpdateAsync(
            update.JournalEvents,
            update.Status,
            journalState.ShipType);
        if (!update.IsBootstrapRead)
        {
            var guardianScreenshotContext = Guardian.ActiveSite is { } site
                && Guardian.Proximity is { } siteProximity
                && Guardian.CurrentAltitude is double altitude
                    ? new ScreenshotGuardianContext(
                        site.SiteType,
                        siteProximity.DistanceFromSite,
                        altitude)
                    : null;
            var screenshotResult =
                await ScreenshotProcessing.ProcessJournalEventsAsync(
                update.JournalEvents,
                journalState.CommanderName,
                guardianScreenshotContext);
            Notifications.ReportScreenshotResult(
                screenshotResult,
                ScreenshotProcessing.AddBanner);
        }

        JumpInfo.ApplyUpdate(
            journalState.SystemName,
            journalState.SystemAddress,
            journalState.StarPosition,
            update.NavRoute,
            update.JournalEvents,
            update.Status,
            Route.CreateSnapshot(),
            update.IsBootstrapRead);
        foreach (var journalEvent in update.JournalEvents)
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

        var explorationAfter = explorationState.CreateSnapshot();
        if (explorationAfter != explorationBefore)
        {
            UpdateExplorationDisplay(explorationAfter);
            await SaveExplorationAsync(explorationAfter);
        }

        var exobiologyAfter = exobiologyState.CreateSnapshot();
        SystemSurvey.ApplyUpdate(
            update.JournalEvents,
            update.Status,
            exobiologyAfter);
        await RefreshSystemSurveyCommanderCodexAsync(
            forceRefresh: commanderCodexResult.DiscoveryEventCount > 0);
        if (!update.IsBootstrapRead
            && SystemSurvey.LatestBiologyEntryId is { } entryId
            && update.JournalEvents.Any(IsShowCodexCommand))
        {
            await BiologyCodex.OpenEntryAsync(entryId);
        }
        SurfaceSurveySessionContext? surfaceSession = null;
        if (!string.IsNullOrWhiteSpace(activeProfileFrontierId)
            && !string.IsNullOrWhiteSpace(journalState.SystemName)
            && journalState.SystemAddress is > 0)
        {
            surfaceSession = new SurfaceSurveySessionContext(
                activeProfileFrontierId,
                activeProfileCommanderName ?? journalState.CommanderName,
                journalState.SystemName,
                journalState.SystemAddress.Value,
                journalState.StarPosition);
        }

        await SurfaceSurvey.ApplyUpdateAsync(
            surfaceSession,
            update.JournalEvents,
            update.Status,
            exobiologyAfter,
            processJournalMutations: !skipPersistedBootstrapEvents,
            scansLostToDeath: scansLostToDeath.ToArray());
        if (exobiologyState.Version != exobiologyVersionBefore)
        {
            await SaveExobiologyAsync(exobiologyAfter);
        }

        if (update.JournalEvents.Count > 0 || update.Status is not null)
        {
            UpdateExobiologyDisplay(exobiologyAfter);
        }

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

        if (update.JournalEvents.Count > 0
            || update.Status is not null
            || update.NavRoute is not null
            || update.Cargo is not null
            || update.Market is not null
            || update.Errors.Count > 0
            || isManualRefresh)
        {
            LastUpdated = $"Last update: {DateTimeOffset.Now:G}";
        }
    }

    private async Task RefreshSystemSurveyCommanderCodexAsync(
        bool forceRefresh)
    {
        var frontierId = activeProfileFrontierId ?? journalState.FrontierId;
        var commanderName = activeProfileCommanderName
            ?? journalState.CommanderName;
        var systemAddress = journalState.SystemAddress;
        var regionId = journalState.StarPosition is { } position
            ? GalacticRegionMap.Find(position)?.Id
            : null;
        if (string.IsNullOrWhiteSpace(frontierId)
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
                frontierId,
                StringComparison.OrdinalIgnoreCase)
            && surveyCodexRegionId == regionId
            && surveyCodexSystemAddress == systemAddress)
        {
            return;
        }

        var global = await commanderCodexStore.LoadAsync(
            frontierId,
            commanderName);
        var regional = regionId is > 0
            ? await commanderCodexStore.LoadAsync(
                frontierId,
                commanderName,
                regionId.Value)
            : null;
        surveyCodexFrontierId = frontierId;
        surveyCodexRegionId = regionId;
        surveyCodexSystemAddress = systemAddress;
        SystemSurvey.UpdateCommanderCodexContext(
            global.Data,
            regional?.Data);

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

    private async Task ApplyQuestUpdateAsync(JournalMonitorUpdate update)
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
            var result = await questRuntimeCoordinator.ApplyUpdateAsync(
                new QuestRuntimeConfiguration(
                    enabled,
                    journalState.FrontierId,
                    journalState.CommanderName,
                    activeProfileRavenApiKey,
                    latestStatus),
                folderResolution.SelectedPath,
                update.JournalEvents,
                update.IsBootstrapRead);
            QuestWorkspace.ApplyRuntimeResult(result, enabled);
            QuestIndicator.Update(result.Quests, latestStatus, enabled);
            HumanSite.UpdateQuests(result.Quests);
            OnPropertyChanged(nameof(Quests));
            OnPropertyChanged(nameof(QuestUnreadMessageCount));
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
            journalEvent);
        QuestWorkspace.ApplyRuntimeResult(result, enabled);
        QuestIndicator.Update(result.Quests, latestStatus, enabled);
        HumanSite.UpdateQuests(result.Quests);
        OnPropertyChanged(nameof(Quests));
        OnPropertyChanged(nameof(QuestUnreadMessageCount));
        QuestStatusMessage = result.Warnings.Count > 0
            ? string.Join(Environment.NewLine, result.Warnings)
            : result.Quests.Count == 0
                ? "No active quests received the replayed event."
                : $"Replayed {journalEvent.EventName}; "
                    + $"{result.Quests.Count:N0} active quest(s), "
                    + $"{QuestUnreadMessageCount:N0} unread message(s).";
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
            isOdyssey);
        activeProfileFrontierId = journalState.FrontierId;
        activeProfileCommanderName = journalState.CommanderName
            ?? result.Data?.CommanderName;
        activeProfileIsOdyssey = isOdyssey;
        resetExplorationCommand.RaiseCanExecuteChanged();
        resetExobiologyCommand.RaiseCanExecuteChanged();

        if (result.Data is null)
        {
            activeProfileRavenApiKey = null;
            SurfaceSurvey.Reset();
            Combat.LoadProfile(null, null, isOdyssey, CombatSnapshot.Empty);
            Colonization.SetCommanderProfile(null, isOdyssey, apiKey: null);
            ExplorationStatusMessage = result.Error
                ?? "The commander profile could not be loaded.";
            ExobiologyStatusMessage = result.Error
                ?? "The commander profile could not be loaded.";
            Search.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            BoxelSearch.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            Guardian.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            RamTah.SetProfileError(
                result.Error ?? "The commander profile could not be loaded.");
            return false;
        }

        activeProfileRavenApiKey = result.Data.RavenColonialApiKey;
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
        await Guardian.LoadProfileAsync(
            result.Data.FrontierId,
            result.Data.IsOdyssey);
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
                snapshot);
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
                snapshot);
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
        OrganicSampleRange = activeSample is null
            ? Unavailable
            : exobiologyState.NearestActiveSampleDistance is double distance
                ? exobiologyState.RemainingSampleDistance is > 0
                    ? $"{distance:N0} m from nearest sample · "
                        + $"{exobiologyState.RemainingSampleDistance:N0} m remaining"
                    : $"{distance:N0} m from nearest sample · clear to sample"
                : $"{activeSample.Radius:N0} m minimum separation";
        OrganicScanProgress = snapshot.ScanOne is null
            ? "Ready for sample 1 of 3"
            : snapshot.ScanTwo is null
                ? "Sample 1 of 3 recorded"
                : "Samples 1 and 2 of 3 recorded";
        BioFirstFootfall = exobiologyState.CurrentBodyFirstFootfall switch
        {
            true => "Confirmed; 5x reward applies",
            false => "Not first footfall",
            null => "Unknown for current body",
        };
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

    public async Task<bool> ToggleCurrentBodyFirstFootfallAsync()
    {
        if (!exobiologyState.ToggleCurrentBodyFirstFootfall())
        {
            ExobiologyStatusMessage =
                "First-footfall state cannot be changed until the current body is known.";
            return false;
        }

        var snapshot = exobiologyState.CreateSnapshot();
        UpdateExobiologyDisplay(snapshot);
        SystemSurvey.ApplyUpdate([], null, snapshot);
        await SaveExobiologyAsync(snapshot);
        return true;
    }

    private Task CancelResetExobiologyAsync()
    {
        IsResetExobiologyPending = false;
        ExobiologyStatusMessage = "Clear cancelled; unclaimed rewards were not changed.";
        return Task.CompletedTask;
    }

    private void ApplyStatus(EliteStatus status)
    {
        VehicleState = status.OnFoot
            ? "On foot"
            : status.InSrv
                ? "SRV"
                : status.InFighter
                    ? "Fighter"
                    : status.InMainShip
                        ? "Main ship"
                        : status.InTaxi
                            ? "Taxi / shuttle"
                            : "Unknown";
        SurfacePosition = status.HasLatitudeLongitude
            ? $"{status.Latitude:F6}, {status.Longitude:F6}"
            : Unavailable;
        HeadingAndAltitude = status.HasLatitudeLongitude
            ? $"{status.NormalizedHeading}° / {status.Altitude:N0} m"
            : Unavailable;
        GameUiFocus = status.GuiFocus.ToString();
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Unavailable : value;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        QuestWorkspace.Dispose();
        CommanderInstances.Dispose();
        visitedStarsHttpClient?.Dispose();
        questRuntimeCoordinator.Changed -= OnQuestCoordinatorChanged;
        questRuntimeCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void OnQuestCoordinatorChanged(object? sender, EventArgs eventArgs)
    {
        QuestIndicator.Update(
            questRuntimeCoordinator.Snapshot,
            latestStatus,
            questSettingsStore.LoadEnabled());
        HumanSite.UpdateQuests(questRuntimeCoordinator.Snapshot);
        OnPropertyChanged(nameof(Quests));
        OnPropertyChanged(nameof(QuestUnreadMessageCount));
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
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
