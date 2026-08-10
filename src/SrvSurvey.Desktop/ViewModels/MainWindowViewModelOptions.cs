using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Travel;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Optional dependencies and settings for <see cref="MainWindowViewModel"/>.
/// Groups constructor parameters so the view model constructor stays within
/// Sonar S107 limits while preserving call-site flexibility.
/// </summary>
public sealed class MainWindowViewModelOptions
{
    public RavenThemeService? ThemeService { get; init; }

    public AppDataPaths? AppDataPaths { get; init; }

    public LegacyProfileImporter? ProfileImporter { get; init; }

    public ExobiologyReferenceCatalog? ExobiologyCatalog { get; init; }

    public IStarSystemResolver? StarSystemResolver { get; init; }

    public IBoxelSystemResolver? BoxelSystemResolver { get; init; }

    public GlobalInputSettingsViewModel? InputSettings { get; init; }

    public ColonizationViewModel? Colonization { get; init; }

    public INearestSystemsClient? NearestSystemsClient { get; init; }

    public ISystemSummaryClient? SystemSummaryClient { get; init; }

    public JumpInfoSettingsStore? JumpInfoSettingsStore { get; init; }

    public SystemSurveySettingsStore? SystemSurveySettingsStore { get; init; }

    public BiologyPredictionsSettingsStore? BiologyPredictionsSettingsStore
    {
        get;
        init;
    }

    public CombatSettingsStore? CombatSettingsStore { get; init; }

    public GuardianOverlaySettingsStore? GuardianOverlaySettingsStore
    {
        get;
        init;
    }

    public StationInfoSettingsStore? StationInfoSettingsStore { get; init; }

    public HumanSiteSettingsStore? HumanSiteSettingsStore { get; init; }

    public ApplicationLogService? ApplicationLogService { get; init; }

    public LegacyOverlayLayoutStore? OverlayLayoutStore { get; init; }

    public LegacyOverlayLayout? OverlayLayout { get; init; }

    public IScreenshotProcessingService? ScreenshotProcessingService
    {
        get;
        init;
    }

    public QuestRuntimeCoordinator? QuestRuntimeCoordinator { get; init; }

    public QuestSettingsStore? QuestSettingsStore { get; init; }

    public string? TargetFrontierId { get; init; }

    public ICommanderInstanceLauncher? CommanderInstanceLauncher { get; init; }

    public IGameWindowSwitcher? GameWindowSwitcher { get; init; }

    public VisitedStarsCacheViewModel? VisitedStarsCache { get; init; }

    public GreenGasGiantPublicationCoordinator?
        GreenGasGiantPublicationCoordinator
    {
        get;
        init;
    }

    public NotificationSettingsStore? NotificationSettingsStore { get; init; }

    public StreamOverlaySettingsStore? StreamOverlaySettingsStore { get; init; }

    public VrOverlaySettingsStore? VrOverlaySettingsStore { get; init; }

    public VrOverlayCalibrationStore? VrOverlayCalibrationStore { get; init; }

    public GalaxyMapSettingsStore? GalaxyMapSettingsStore { get; init; }

    public PulseOverlaySettingsStore? PulseOverlaySettingsStore { get; init; }

    public OverlayBehaviorSettingsStore? OverlayBehaviorSettingsStore
    {
        get;
        init;
    }

    public OverlayScaleSettingsStore? OverlayScaleSettingsStore { get; init; }

    public JournalSettingsStore? JournalSettingsStore { get; init; }

    public SystemScanPersistenceStore? SystemScanPersistenceStore { get; init; }

    public CodexImageSettingsStore? CodexImageSettingsStore { get; init; }

    public DockToDockSettingsStore? DockToDockSettingsStore { get; init; }

    public DockToDockLogService? DockToDockLogService { get; init; }

    public DesktopBehaviorSettingsStore? DesktopBehaviorSettingsStore
    {
        get;
        init;
    }

    public BiologyRewardSettingsStore? BiologyRewardSettingsStore { get; init; }

    public CommanderPreferenceSettingsStore? CommanderPreferenceSettingsStore
    {
        get;
        init;
    }

    public bool CommanderPreferenceCommandLineOverride { get; init; }

    public string? CommanderPreferenceInitialStatus { get; init; }

    public FirstFootfallInferenceSettingsStore?
        FirstFootfallInferenceSettingsStore
    {
        get;
        init;
    }

    public IFirstFootfallInferenceService? FirstFootfallInferenceService
    {
        get;
        init;
    }

    public RavenServiceSettingsStore? RavenServiceSettingsStore { get; init; }

    public ReleaseUpdateViewModel? ReleaseUpdates { get; init; }

    public ReferenceDataUpdateViewModel? ReferenceDataUpdates { get; init; }

    public LocalizationViewModel? Localization { get; init; }

    public OverlayThemeSettingsViewModel? OverlayThemeSettings { get; init; }

    public OverlayInteractionViewModel? OverlayInteraction { get; init; }

    public ICanonnHumanSiteClient? CanonnHumanSiteClient { get; init; }

    public ICanonnHumanSitePublisher? CanonnHumanSitePublisher { get; init; }

    public IEddnPublisher? EddnPublisher { get; init; }

    public ISystemBodyDataClient? SystemBodyDataClient { get; init; }

    internal TimeSpan? SystemBodyDataRetryDelay { get; init; }

    public IInaraPublisher? InaraPublisher { get; init; }

    public CommanderProfileViewModel? FrontierProfile { get; init; }
}
