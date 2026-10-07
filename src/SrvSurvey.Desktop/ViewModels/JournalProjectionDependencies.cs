using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Quests;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Concrete desktop workspaces and owned dependencies used by journal projection.</summary>
internal sealed class JournalProjectionDependencies
{
    internal required JournalProjectionState State { get; init; }
    internal required JournalProjectionShell Shell { get; init; }
    internal required Func<bool> IsDiagnosticReplay { get; init; }
    internal required CommanderProfileViewModel FrontierProfile { get; init; }
    internal required OverlayExceptionsViewModel OverlayExceptions { get; init; }
    internal required DesktopBehaviorViewModel DesktopBehavior { get; init; }
    internal required OverlayBehaviorViewModel OverlayBehavior { get; init; }
    internal required ScreenshotProcessingViewModel ScreenshotProcessing { get; init; }
    internal required DockToDockViewModel DockToDock { get; init; }
    internal required NotificationViewModel Notifications { get; init; }
    internal required PulseOverlayViewModel PulseOverlay { get; init; }
    internal required GalaxyMapOverlayViewModel GalaxyMap { get; init; }
    internal required NetworkPrivacyViewModel NetworkPrivacy { get; init; }
    internal required VoxStellarSharingViewModel VoxStellar { get; init; }
    internal required InaraSettingsViewModel Inara { get; init; }
    internal required EdsmSettingsViewModel Edsm { get; init; }
    internal required QuestWorkspaceViewModel QuestWorkspace { get; init; }
    internal required CommanderInstancesViewModel CommanderInstances { get; init; }
    internal required GroundTargetViewModel GroundTarget { get; init; }
    internal required SystemNotesViewModel SystemNotes { get; init; }
    internal required JourneyWorkspaceViewModel Journey { get; init; }
    internal required RouteWorkspaceViewModel Route { get; init; }
    internal required RouteManagerViewModel RouteManager { get; init; }
    internal required RouteWorkspaceViewModel FleetCarrierRoute { get; init; }
    internal required RouteManagerViewModel FleetCarrierRouteManager { get; init; }
    internal required JumpInfoViewModel JumpInfo { get; init; }
    internal required StationInfoViewModel StationInfo { get; init; }
    internal required HumanSiteViewModel HumanSite { get; init; }
    internal required SystemSurveyViewModel SystemSurvey { get; init; }
    internal required SurfaceSurveyViewModel SurfaceSurvey { get; init; }
    internal required SurfaceMiningViewModel Mining { get; init; }
    internal required MineMapViewModel MineMap { get; init; }
    internal required MiningWorkspaceViewModel MiningWorkspace { get; init; }
    internal required FiregroupsWorkspaceViewModel Firegroups { get; init; }
    internal required CombatViewModel Combat { get; init; }
    internal required BiologyCodexViewModel BiologyCodex { get; init; }
    internal required BiologyCodexBingoViewModel CodexBingo { get; init; }
    internal required SphereLimitViewModel Search { get; init; }
    internal required BoxelSearchViewModel BoxelSearch { get; init; }
    internal required NearestSystemsViewModel NearestSystems { get; init; }
    internal required GuardianViewModel Guardian { get; init; }
    internal required ColonizationViewModel Colonization { get; init; }
    internal required JournalInspectorViewModel JournalInspector { get; init; }
    internal required JournalPostProcessorViewModel JournalPostProcessor { get; init; }
    internal required CompanionTimelineStore companionTimelineStore { get; init; }
    internal required ApplicationLogService? applicationLogService { get; init; }
    internal required CancellationTokenSource firstFootfallInferenceCancellation { get; init; }
    internal required RouteAutoCopyCoordinator routeAutoCopyCoordinator { get; init; }
    internal required BoxelSurveyStatsCoordinator boxelSurveyStats { get; init; }
    internal required GreenGasGiantPublicationCoordinator greenGasGiantPublicationCoordinator { get; init; }
    internal required CommanderCodexJournalTracker commanderCodexJournalTracker { get; init; }
    internal required IEddnPublisher eddnPublisher { get; init; }
    internal required IVoxStellarPublisher voxStellarPublisher { get; init; }
    internal required IInaraPublisher inaraPublisher { get; init; }
    internal required IEdsmPublisher edsmPublisher { get; init; }
    internal required QuestSettingsStore questSettingsStore { get; init; }
    internal required QuestRuntimeCoordinator questRuntimeCoordinator { get; init; }
    internal required JournalFolderResolution folderResolution { get; init; }
}

/// <summary>Existing shell profile, survey-storage and presentation workflows; no journal stage order lives here.</summary>
internal sealed class JournalProjectionShell
{
    internal required Func<Task<bool>> EnsureCommanderProfileAsync { get; init; }
    internal required Action<JournalMonitorUpdate> UpdateSystemBodyDataGameSessionConfirmation { get; init; }
    internal required Action<IReadOnlyList<JournalEventEnvelope>> UpdateActiveSystemVisit { get; init; }
    internal required Func<Task> LoadCurrentSystemHistoryAsync { get; init; }
    internal required Func<Task> LoadCurrentSystemBodyDataAsync { get; init; }
    internal required Func<IReadOnlyList<JournalEventEnvelope>, Task> PersistSystemScanAsync { get; init; }
    internal required Func<bool, Task> RefreshSystemSurveyCommanderCodexAsync { get; init; }
    internal required Func<
        IReadOnlyList<JournalEventEnvelope>,
        Task<int>
    > ApplyFirstFootfallTextCommandsAsync { get; init; }
    internal required Func<JournalMonitorUpdate, Task<bool>> TryInferFirstFootfallAsync { get; init; }
    internal required Func<IReadOnlyList<JournalEventEnvelope>, Task<bool>> ApplyDesktopTextCommandsAsync { get; init; }
    internal required Func<ExplorationSnapshot, Task> SaveExplorationAsync { get; init; }
    internal required Func<ExobiologySnapshot, Task> SaveExobiologyAsync { get; init; }
    internal required Action<ExplorationSnapshot> UpdateExplorationDisplay { get; init; }
    internal required Action<ExobiologySnapshot> UpdateExobiologyDisplay { get; init; }
    internal required Action<JournalMonitorUpdate, bool> ApplyMonitorStatusMessages { get; init; }
    internal required Action<IReadOnlyList<QuestRuntimeSnapshot>, bool> UpdateQuestOverlayPresentation { get; init; }
    internal required Action StartSystemBodyDataRetryIfDue { get; init; }
    internal required Action<string> CommanderCodexStatus { get; init; }
    internal required Action<string> QuestStatus { get; init; }
    internal required Action<Task> PendingBodyData { get; init; }
    internal required Func<Task> RequestShutdown { get; init; }
}
