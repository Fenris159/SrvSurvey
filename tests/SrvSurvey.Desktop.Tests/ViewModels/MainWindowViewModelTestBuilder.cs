using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

internal sealed class MainWindowViewModelTestBuilder
{
    private AppDataPaths? appDataPaths;
    private ApplicationLogService? applicationLogService;
    private IBoxelSystemResolver? boxelSystemResolver;
    private DesktopBehaviorSettingsStore? desktopBehaviorSettingsStore;
    private IEddnPublisher? eddnPublisher;
    private IFirstFootfallInferenceService? firstFootfallInferenceService;
    private CommanderProfileViewModel? frontierProfile;
    private IGameWindowSwitcher? gameWindowSwitcher;
    private GreenGasGiantPublicationCoordinator?
        greenGasGiantPublicationCoordinator;
    private GuardianOverlaySettingsStore? guardianOverlaySettingsStore;
    private HumanSiteSettingsStore? humanSiteSettingsStore;
    private IInaraPublisher? inaraPublisher;
    private OverlayThemeSettingsViewModel? overlayThemeSettings;
    private IScreenshotProcessingService? screenshotProcessingService;
    private StationInfoSettingsStore? stationInfoSettingsStore;
    private ISystemBodyDataClient? systemBodyDataClient;
    private IEliteGameProcessDetector? eliteGameProcessDetector;
    private TimeSpan? systemBodyDataRetryDelay;
    private string? targetFrontierId;
    private IVoxStellarPublisher? voxStellarPublisher;
    private Action<MainWindowViewModelConstructionCheckpoint>? checkpoint;

    public static MainWindowViewModel Create(
        string? configuredJournalDirectory,
        Action<MainWindowViewModelTestBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MainWindowViewModelTestBuilder();
        configure(builder);
        return builder.Build(configuredJournalDirectory);
    }

    private MainWindowViewModel Build(string? configuredJournalDirectory)
    {
        return MainWindowViewModelFactory.CreateForTesting(
            configuredJournalDirectory,
            new MainWindowViewModelConstructionContext
            {
                Foundation = new MainWindowFoundationInputs
                {
                    AppDataPaths = appDataPaths ?? CreateIsolatedPaths(),
                    ApplicationLogService = applicationLogService,
                    TargetFrontierId = targetFrontierId,
                    FrontierProfile = frontierProfile,
                },
                Overlay = new MainWindowOverlayInputs
                {
                    OverlayThemeSettings = overlayThemeSettings,
                    ScreenshotProcessingService = screenshotProcessingService,
                    GuardianOverlaySettingsStore =
                        guardianOverlaySettingsStore,
                    DesktopBehaviorSettingsStore =
                        desktopBehaviorSettingsStore,
                },
                Exploration = new MainWindowExplorationInputs
                {
                    BoxelSystemResolver = boxelSystemResolver,
                    FirstFootfallInferenceService =
                        firstFootfallInferenceService,
                    SystemBodyDataClient = systemBodyDataClient,
                    EliteGameProcessDetector = eliteGameProcessDetector,
                    SystemBodyDataRetryDelay = systemBodyDataRetryDelay,
                    HumanSiteSettingsStore = humanSiteSettingsStore,
                },
                Travel = new MainWindowTravelInputs
                {
                    GameWindowSwitcher = gameWindowSwitcher,
                    StationInfoSettingsStore = stationInfoSettingsStore,
                },
                Online = new MainWindowOnlineInputs
                {
                    EddnPublisher = eddnPublisher,
                    VoxStellarPublisher = voxStellarPublisher,
                    InaraPublisher = inaraPublisher,
                    GreenGasGiantPublicationCoordinator =
                        greenGasGiantPublicationCoordinator,
                },
                Checkpoint = checkpoint,
            });
    }

    public MainWindowViewModelTestBuilder WithAppDataPaths(AppDataPaths value)
        => Set(ref appDataPaths, value);

    public MainWindowViewModelTestBuilder WithApplicationLogService(
        ApplicationLogService value)
        => Set(ref applicationLogService, value);

    public MainWindowViewModelTestBuilder WithBoxelSystemResolver(
        IBoxelSystemResolver value)
        => Set(ref boxelSystemResolver, value);

    public MainWindowViewModelTestBuilder WithDesktopBehaviorSettingsStore(
        DesktopBehaviorSettingsStore value)
        => Set(ref desktopBehaviorSettingsStore, value);

    public MainWindowViewModelTestBuilder WithEddnPublisher(IEddnPublisher value)
        => Set(ref eddnPublisher, value);

    public MainWindowViewModelTestBuilder WithFirstFootfallInferenceService(
        IFirstFootfallInferenceService value)
        => Set(ref firstFootfallInferenceService, value);

    public MainWindowViewModelTestBuilder WithFrontierProfile(
        CommanderProfileViewModel value)
        => Set(ref frontierProfile, value);

    public MainWindowViewModelTestBuilder WithGameWindowSwitcher(
        IGameWindowSwitcher value)
        => Set(ref gameWindowSwitcher, value);

    public MainWindowViewModelTestBuilder
        WithGreenGasGiantPublicationCoordinator(
            GreenGasGiantPublicationCoordinator value)
        => Set(ref greenGasGiantPublicationCoordinator, value);

    public MainWindowViewModelTestBuilder WithGuardianOverlaySettingsStore(
        GuardianOverlaySettingsStore value)
        => Set(ref guardianOverlaySettingsStore, value);

    public MainWindowViewModelTestBuilder WithHumanSiteSettingsStore(
        HumanSiteSettingsStore value)
        => Set(ref humanSiteSettingsStore, value);

    public MainWindowViewModelTestBuilder WithInaraPublisher(
        IInaraPublisher value)
        => Set(ref inaraPublisher, value);

    public MainWindowViewModelTestBuilder WithOverlayThemeSettings(
        OverlayThemeSettingsViewModel value)
        => Set(ref overlayThemeSettings, value);

    public MainWindowViewModelTestBuilder WithScreenshotProcessingService(
        IScreenshotProcessingService value)
        => Set(ref screenshotProcessingService, value);

    public MainWindowViewModelTestBuilder WithStationInfoSettingsStore(
        StationInfoSettingsStore value)
        => Set(ref stationInfoSettingsStore, value);

    public MainWindowViewModelTestBuilder WithSystemBodyDataClient(
        ISystemBodyDataClient value)
        => Set(ref systemBodyDataClient, value);

    public MainWindowViewModelTestBuilder WithEliteGameProcessDetector(
        IEliteGameProcessDetector value)
        => Set(ref eliteGameProcessDetector, value);

    public MainWindowViewModelTestBuilder WithSystemBodyDataRetryDelay(
        TimeSpan value)
    {
        systemBodyDataRetryDelay = value;
        return this;
    }

    public MainWindowViewModelTestBuilder WithTargetFrontierId(string? value)
    {
        targetFrontierId = value;
        return this;
    }

    public MainWindowViewModelTestBuilder WithVoxStellarPublisher(
        IVoxStellarPublisher value)
        => Set(ref voxStellarPublisher, value);

    public MainWindowViewModelTestBuilder FailAt(
        MainWindowViewModelConstructionCheckpoint value,
        Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        checkpoint = current =>
        {
            if (current == value)
            {
                throw failure;
            }
        };
        return this;
    }

    private MainWindowViewModelTestBuilder Set<T>(ref T? field, T value)
        where T : class
    {
        field = value ?? throw new ArgumentNullException(nameof(value));
        return this;
    }

    private static AppDataPaths CreateIsolatedPaths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-main-window-{Guid.NewGuid():N}");
        return new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []);
    }
}
