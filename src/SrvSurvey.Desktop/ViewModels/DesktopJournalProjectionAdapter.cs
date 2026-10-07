using System.Text.Json;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Edsm;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Translates one module-selected journal operation to concrete desktop workspaces.</summary>
internal sealed class DesktopJournalProjectionAdapter : IJournalProjectionAdapter
{
    private readonly JournalProjectionDependencies dependencies;
    private readonly JournalProjectionState state;
    private readonly JournalProjectionShell shell;
    private readonly Dictionary<
        JournalProjectionOperation,
        Func<JournalProjectionContext, JournalProjectionTick, Task>
    > operations = [];

    internal DesktopJournalProjectionAdapter(JournalProjectionDependencies dependencies)
    {
        this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        state = dependencies.State;
        shell = dependencies.Shell;
        BindIdle();
        BindSession();
        BindCargoAndStatus();
        BindCommanderContext();
        BindNavigation();
        BindActivity();
        BindExploration();
        BindSurvey();
        BindShell();
    }

    public Task ApplyAsync(
        JournalProjectionOperation operation,
        JournalProjectionContext context,
        JournalProjectionTick tick
    ) => operations[operation](context, tick);

    private static Func<JournalProjectionContext, JournalProjectionTick, Task> Sync(
        Action<JournalProjectionContext, JournalProjectionTick> action
    ) =>
        (context, tick) =>
        {
            action(context, tick);
            return Task.CompletedTask;
        };

    private void BindIdle()
    {
        operations[JournalProjectionOperation.MiningIdle] = Sync((_, _) => dependencies.MiningWorkspace.Tick());
        operations[JournalProjectionOperation.ColonizationIdle] = (context, tick) =>
            dependencies.Colonization.SynchronizeLiveProjectsAsync(
                [],
                allowPublishing: !dependencies.IsDiagnosticReplay(),
                cancellationToken: tick.WorkCancellationToken
            );
        operations[JournalProjectionOperation.IdleHousekeeping] = (context, tick) =>
            ApplyIdleHousekeepingAsync(context.Update);
    }

    private void BindSession()
    {
        operations[JournalProjectionOperation.Timeline] = (context, tick) =>
            AppendCompanionTimelineAsync(context.Update);
        operations[JournalProjectionOperation.SessionBaseline] = Sync(
            (context, tick) => ApplySessionBaseline(context, tick)
        );
        operations[JournalProjectionOperation.JournalInspector] = Sync(
            (context, tick) => dependencies.JournalInspector.ApplyUpdate(context.JournalEvents, context.Update.Status)
        );
        operations[JournalProjectionOperation.SessionState] = Sync(
            (context, tick) => ApplyJournalSessionState(context.Update)
        );
        operations[JournalProjectionOperation.Firegroups] = Sync(
            (context, tick) => dependencies.Firegroups.Apply(context.Update, state.Session, state.LatestStatus)
        );
        operations[JournalProjectionOperation.BoardedVehicle] = Sync(
            (_, _) => dependencies.OverlayExceptions.UpdateBoardedVehicle(state.Session, state.LatestStatus)
        );
        operations[JournalProjectionOperation.MusicTrack] = Sync(
            (_, _) =>
            {
                dependencies.Colonization.UpdateMusicTrack(state.Session.MusicTrack);
                dependencies.StationInfo.UpdateMusicTrack(state.Session.MusicTrack);
                dependencies.GroundTarget.UpdateMusicTrack(state.Session.MusicTrack);
            }
        );
        operations[JournalProjectionOperation.CommanderChange] = (context, tick) =>
            ApplyCommanderChangeIfNeededAsync(context.Update, tick.PreviousFrontierId, tick.PreviousCommanderName);
    }

    private void BindCargoAndStatus()
    {
        operations[JournalProjectionOperation.CargoInventory] = Sync(
            (context, tick) =>
            {
                tick.AllowSharedCargo = !dependencies.CommanderInstances.HasMultipleGameWindows;
                tick.CargoChanged = ApplyCargoInventoryUpdate(context.Update, tick.AllowSharedCargo);
            }
        );
        operations[JournalProjectionOperation.MiningWorkspace] = Sync(
            (context, tick) =>
                dependencies.MiningWorkspace.Apply(context.Update, state.Session, state.LatestCargo, state.LatestStatus)
        );
        operations[JournalProjectionOperation.ShipLocker] = Sync(
            (context, tick) => ApplyShipLockerIfAllowed(context.Update, tick.AllowSharedCargo)
        );
        operations[JournalProjectionOperation.FrontierInventory] = Sync(
            (_, tick) =>
                dependencies.FrontierProfile.UpdateLocalInventory(
                    state.LatestCargo,
                    state.LatestShipLocker,
                    isSuppressed: !tick.AllowSharedCargo
                )
        );
        operations[JournalProjectionOperation.DockToDock] = Sync(
            (context, tick) =>
                dependencies.DockToDock.ApplyUpdate(context.JournalEvents, state.LatestCargo, context.IsBootstrapRead)
        );
        operations[JournalProjectionOperation.DesktopBehavior] = Sync(
            (context, tick) =>
                dependencies.DesktopBehavior.ApplyJournalEvents(context.JournalEvents, !tick.AllowLiveEffects)
        );
        operations[JournalProjectionOperation.StatusConsumers] = Sync(
            (context, tick) => ApplyStatusToStatusConsumers(context.Update.Status)
        );
        operations[JournalProjectionOperation.GroundTargetEvents] = (context, tick) =>
            dependencies.GroundTarget.ApplyJournalEventsAsync(
                context.JournalEvents,
                allowCommands: tick.AllowLiveEffects
            );
        operations[JournalProjectionOperation.GreenGasGiant] = (context, tick) =>
            ApplyGreenGasGiantPublicationAsync(context.Update, tick.AllowLiveEffects);
        operations[JournalProjectionOperation.FrontierJournal] = Sync(
            (context, tick) =>
            {
                dependencies.FrontierProfile.UpdateJournalReputation(
                    state.Session.CommanderName,
                    context.JournalEvents
                );
                dependencies.FrontierProfile.UpdateJournalCommunityGoals(
                    state.Session.CommanderName,
                    context.JournalEvents
                );
                dependencies.FrontierProfile.UpdateJournalCarrierJump(
                    state.Session.CommanderName,
                    context.JournalEvents
                );
            }
        );
        operations[JournalProjectionOperation.OverlayBehavior] = Sync((_, _) => ApplyOverlayBehaviorContext());
        operations[JournalProjectionOperation.PostProcessorCommander] = Sync(
            (_, _) => dependencies.JournalPostProcessor.SelectCommander(state.Session.FrontierId)
        );
    }

    private void BindCommanderContext()
    {
        operations[JournalProjectionOperation.CommanderCodex] = async (context, tick) =>
        {
            CommanderCodexJournalTrackResult result = await ApplyCommanderCodexUpdateAsync(context.Update);
            tick.CodexDiscoveryChanged = result.DiscoveryEventCount > 0;
        };
        operations[JournalProjectionOperation.ColonizationContext] = Sync(
            (context, tick) =>
            {
                dependencies.Colonization.UpdateSystemContext(
                    state.Session.SystemName,
                    state.Session.StarPosition,
                    state.Session.SystemAddress
                );
                dependencies.Colonization.ApplyJournalEvents(context.JournalEvents, state.Session.CommanderName);
            }
        );
        operations[JournalProjectionOperation.SearchContext] = Sync(
            (_, _) =>
                dependencies.Search.UpdateCurrentSystem(
                    state.Session.SystemName,
                    state.Session.StarPosition,
                    state.Session.SystemAddress
                )
        );
        operations[JournalProjectionOperation.MiningSystem] = Sync(
            (_, _) => dependencies.MiningWorkspace.UseCommanderSystem(state.Session.SystemName)
        );
        operations[JournalProjectionOperation.MineMapSystem] = Sync(
            (_, _) => dependencies.MineMap.UpdateCurrentSystem(state.Session.SystemName)
        );
        operations[JournalProjectionOperation.NearestSystems] = Sync(
            (_, _) =>
                dependencies.NearestSystems.UpdateContext(
                    state.Session.SystemName,
                    state.Session.StarPosition,
                    state.Session.CommanderName,
                    state.Session.SystemAddress
                )
        );
        operations[JournalProjectionOperation.CodexBingo] = (_, tick) =>
            dependencies.CodexBingo.UpdateContextAsync(
                state.Session.FrontierId,
                state.Session.CommanderName,
                state.Session.SystemName,
                state.Session.StarPosition,
                forceRefresh: tick.CodexDiscoveryChanged
            );
        operations[JournalProjectionOperation.SystemNotes] = Sync(
            (_, _) =>
                dependencies.SystemNotes.UpdateContext(
                    state.Session.FrontierId,
                    state.Session.CommanderName,
                    state.Session.SystemName,
                    state.Session.SystemAddress,
                    state.Session.StarPosition
                )
        );
        operations[JournalProjectionOperation.BoxelContext] = (_, _) =>
            dependencies.BoxelSearch.UpdateCurrentSystemAsync(
                state.Session.SystemName,
                state.Session.StarPosition,
                state.Session.SystemAddress
            );
        operations[JournalProjectionOperation.GuardianContext] = Sync(
            (_, _) => dependencies.Guardian.UpdateCurrentSystem(state.Session.SystemName, state.Session.StarPosition)
        );
        operations[JournalProjectionOperation.HumanSiteContext] = Sync(
            (_, _) =>
                dependencies.HumanSite.UpdateContext(
                    state.Session.FrontierId,
                    state.Session.CommanderName,
                    state.Session.SystemName,
                    state.Session.SystemAddress ?? 0,
                    state.Session.StarPosition
                )
        );
        operations[JournalProjectionOperation.StationInfoContext] = Sync(
            (_, _) =>
                _ = dependencies.StationInfo.UpdateCurrentSystemAsync(
                    state.Session.SystemName,
                    state.Session.SystemAddress ?? 0
                )
        );
        operations[JournalProjectionOperation.CommanderProfile] = async (context, tick) =>
        {
            bool loadedExistingProfile = await shell.EnsureCommanderProfileAsync();
            tick.LoadedExistingProfile = loadedExistingProfile;
        };
        operations[JournalProjectionOperation.Quests] = (context, tick) =>
            ApplyQuestUpdateAsync(context.Update, tick.AllowSharedCargo);
        operations[JournalProjectionOperation.ColonizationCommander] = (_, _) =>
            dependencies.Colonization.SetCommanderAsync(state.Session.CommanderName);
        operations[JournalProjectionOperation.ColonizationProjects] = (context, tick) =>
            SynchronizeColonizationLiveProjectsAsync(context, tick);
        operations[JournalProjectionOperation.Journey] = (context, tick) =>
            ApplyJourneyUpdateAsync(context.JournalEvents);
    }

    private void BindNavigation()
    {
        operations[JournalProjectionOperation.RouteContext] = (_, _) =>
            dependencies.Route.UpdateContextAsync(
                state.Session.FrontierId,
                state.Session.SystemName,
                state.Session.SystemAddress,
                state.Session.StarPosition
            );
        operations[JournalProjectionOperation.RouteManagerContext] = (_, _) =>
            dependencies.RouteManager.UpdateContextAsync(state.Session.FrontierId);
        operations[JournalProjectionOperation.FleetCarrierRouteContext] = (_, _) =>
            dependencies.FleetCarrierRoute.UpdateContextAsync(
                state.Session.FrontierId,
                state.Session.SystemName,
                state.Session.SystemAddress,
                state.Session.StarPosition
            );
        operations[JournalProjectionOperation.FleetCarrierManagerContext] = (_, _) =>
            dependencies.FleetCarrierRouteManager.UpdateContextAsync(state.Session.FrontierId);
        operations[JournalProjectionOperation.RouteAutoCopy] = (_, _) =>
            dependencies.routeAutoCopyCoordinator.ReconcileAsync();
        operations[JournalProjectionOperation.RouteEvents] = (context, tick) => ApplyRouteJournalEventsAsync(context);
        operations[JournalProjectionOperation.FleetCarrierEvents] = (context, tick) =>
            ApplyFleetCarrierRouteJournalEventsAsync(context);
        operations[JournalProjectionOperation.FleetCarrierBootstrapEvents] = Sync(
            (context, tick) => dependencies.FleetCarrierRoute.ApplyFleetCarrierJumpEvents(context.JournalEvents)
        );
        operations[JournalProjectionOperation.ExplorationBaseline] = Sync(
            (_, tick) =>
            {
                tick.ExplorationBefore = state.Exploration.CreateSnapshot();
                tick.ExobiologyVersionBefore = state.Exobiology.Version;
                tick.BoxelBefore = dependencies.BoxelSearch.CreateNotificationState();
            }
        );
        operations[JournalProjectionOperation.BoxelRoute] = (context, tick) =>
            ApplyBoxelSearchRouteAsync(context.Update.NavRoute);
        operations[JournalProjectionOperation.SearchNavigation] = (context, tick) =>
            dependencies.Search.UpdateNavigationAsync(
                context.Update.NavRoute,
                context.Update.Status,
                state.Session.MusicTrack
            );
        operations[JournalProjectionOperation.BoxelEvents] = (context, tick) =>
            ApplyBoxelSearchJournalEventsAsync(context);
        operations[JournalProjectionOperation.JournalNotifications] = Sync(
            (context, tick) =>
                dependencies.Notifications.ApplyJournalEvents(
                    context.JournalEvents,
                    allowNotifications: tick.AllowLiveEffects
                )
        );
        operations[JournalProjectionOperation.Pulse] = Sync(
            (context, tick) =>
                dependencies.PulseOverlay.ApplyUpdate(
                    context.JournalEvents,
                    context.Update.Status,
                    context.IsBootstrapRead
                )
        );
        operations[JournalProjectionOperation.BoxelNotifications] = Sync(
            (context, tick) =>
                dependencies.Notifications.ReportBoxelUpdate(
                    tick.BoxelBefore,
                    dependencies.BoxelSearch.CreateNotificationState(),
                    context.JournalEvents.Any(journalEvent => journalEvent.EventName == "FSSAllBodiesFound"),
                    allowNotifications: tick.AllowLiveEffects
                )
        );
    }

    private void BindActivity()
    {
        operations[JournalProjectionOperation.GuardianEvents] = async (context, tick) =>
            tick.GuardianScreenshotContexts = await dependencies.Guardian.ApplyJournalEventsAsync(
                context.JournalEvents,
                state.ProfileCommanderName,
                allowLiveCommands: tick.AllowLiveEffects,
                status: state.LatestStatus,
                cancellationToken: dependencies.firstFootfallInferenceCancellation.Token
            );
        operations[JournalProjectionOperation.GuardianCargo] = Sync((_, tick) => ApplyGuardianCargo(tick));
        operations[JournalProjectionOperation.ColonizationCargo] = (context, tick) =>
            ApplyColonizationCargoAsync(context, tick);
        operations[JournalProjectionOperation.ColonizationMarket] = (context, tick) =>
            dependencies.Colonization.UpdateMarketAsync(context.Update.Market);
        operations[JournalProjectionOperation.BuildProjects] = Sync(
            (_, _) =>
            {
                dependencies.SystemSurvey.SetActiveBuildProjects(dependencies.Colonization.HasProjects);
                dependencies.Combat.SetActiveBuildProjects(dependencies.Colonization.HasProjects);
                dependencies.Guardian.SetActiveBuildProjects(dependencies.Colonization.HasProjects);
                dependencies.HumanSite.SetActiveBuildProjects(dependencies.Colonization.HasProjects);
            }
        );
        operations[JournalProjectionOperation.Combat] = (context, tick) =>
            dependencies.Combat.ApplyUpdateAsync(
                context.JournalEvents,
                context.Update.Status,
                processHistoricalProgress: !tick.SkipPersistedBootstrapEvents
            );
        operations[JournalProjectionOperation.GuardianStatus] = (context, tick) =>
            ApplyGuardianStatusAsync(context, tick);
        operations[JournalProjectionOperation.StationInfoStatus] = Sync(
            (context, tick) => ApplyStationInfoStatus(context.Update.Status)
        );
        operations[JournalProjectionOperation.NavigationStatus] = (_, _) => ApplyRouteAndBoxelStatusAsync();
        operations[JournalProjectionOperation.HumanSite] = (context, tick) =>
            dependencies.HumanSite.ApplyUpdateAsync(
                context.JournalEvents,
                context.Update.Status,
                state.Session.ShipType,
                allowExternalData: tick.AllowLiveEffects
            );
        operations[JournalProjectionOperation.DesktopCommands] = async (context, tick) =>
            tick.RequestShutdown = await shell.ApplyDesktopTextCommandsAsync(context.JournalEvents);
        operations[JournalProjectionOperation.Screenshots] = (context, tick) =>
            ApplyScreenshotProcessingAsync(context, tick);
    }

    private void BindExploration()
    {
        operations[JournalProjectionOperation.JumpInfo] = Sync(
            (context, tick) =>
                dependencies.JumpInfo.ApplyUpdate(
                    new JumpInfoApplyUpdateRequest(
                        state.Session.SystemName,
                        state.Session.SystemAddress,
                        state.Session.StarPosition,
                        context.Update.NavRoute,
                        context.JournalEvents,
                        context.Update.Status,
                        dependencies.Route.CreateSnapshot(),
                        context.IsBootstrapRead
                    )
                )
        );
        operations[JournalProjectionOperation.GalaxyMap] = Sync(
            (context, tick) =>
                dependencies.GalaxyMap.ApplyUpdate(
                    state.Session.SystemName,
                    state.Session.SystemAddress,
                    context.Update.NavRoute,
                    context.JournalEvents,
                    context.Update.Status,
                    context.IsBootstrapRead,
                    state.Session.MusicTrack
                )
        );
        operations[JournalProjectionOperation.ExplorationEvents] = Sync(
            (context, tick) =>
                ApplyExplorationAndExobiologyJournalEvents(
                    context.JournalEvents,
                    tick.SkipPersistedBootstrapEvents,
                    tick.ScansLostToDeath
                )
        );
        operations[JournalProjectionOperation.ExplorationPersistence] = (_, tick) =>
            PersistExplorationIfChangedAsync(tick.ExplorationBefore);
    }

    private void BindSurvey()
    {
        operations[JournalProjectionOperation.SystemSurvey] = Sync(
            (context, tick) => ApplySystemSurveyUpdate(context, tick)
        );
        operations[JournalProjectionOperation.SystemVisit] = Sync(
            (context, tick) => shell.UpdateActiveSystemVisit(context.JournalEvents)
        );
        operations[JournalProjectionOperation.SystemHistory] = (_, _) => shell.LoadCurrentSystemHistoryAsync();
        operations[JournalProjectionOperation.BoxelStats] = (context, tick) =>
            ApplyBoxelSurveyStatsAsync(context, tick);
        operations[JournalProjectionOperation.BodyData] = Sync(
            (_, _) => shell.PendingBodyData(shell.LoadCurrentSystemBodyDataAsync())
        );
        operations[JournalProjectionOperation.FirstFootfallCommands] = (context, tick) =>
            ApplyLiveFirstFootfallTextCommandsAsync(context, tick);
        operations[JournalProjectionOperation.FirstFootfallInference] = (context, tick) =>
            ApplyFirstFootfallInferenceAsync(context, tick);
        operations[JournalProjectionOperation.ExobiologyVersion] = Sync(
            (_, tick) => tick.ExobiologyChanged = state.Exobiology.Version != tick.ExobiologyVersionBefore
        );
        operations[JournalProjectionOperation.SystemScan] = (context, tick) =>
            shell.PersistSystemScanAsync(context.JournalEvents);
        operations[JournalProjectionOperation.SurveyCodex] = (_, tick) =>
            shell.RefreshSystemSurveyCommanderCodexAsync(tick.CodexDiscoveryChanged);
        operations[JournalProjectionOperation.BiologyCodex] = (context, tick) =>
            OpenRequestedBiologyCodexEntryAsync(context);
        operations[JournalProjectionOperation.SurfaceTracking] = (context, tick) =>
            ApplySurfaceTrackingAsync(context, tick);
        operations[JournalProjectionOperation.ExobiologyPersistence] = (_, tick) => SaveExobiologyIfChangedAsync(tick);
        operations[JournalProjectionOperation.ExobiologyDisplay] = Sync(
            (context, tick) => UpdateExobiologyDisplayIfObserved(context, tick)
        );
    }

    private void BindShell()
    {
        operations[JournalProjectionOperation.MonitorStatus] = Sync(
            (context, tick) => shell.ApplyMonitorStatusMessages(context.Update, context.IsManualRefresh)
        );
        operations[JournalProjectionOperation.ExternalPublication] = (context, tick) =>
            ApplyExternalPublicationAsync(context.Update, tick.AllowSharedCargo);
        operations[JournalProjectionOperation.Shutdown] = (_, tick) =>
            RequestShutdownIfNeededAsync(tick.RequestShutdown);
    }

    private async Task AppendCompanionTimelineAsync(JournalMonitorUpdate update)
    {
        if (dependencies.IsDiagnosticReplay())
        {
            return;
        }

        try
        {
            await dependencies.companionTimelineStore.AppendAsync(update, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            dependencies.applicationLogService?.Append(
                "Companion replay history could not be updated: " + exception.Message
            );
        }
    }

    private void ApplySessionBaseline(JournalProjectionContext context, JournalProjectionTick tick)
    {
        tick.PreviousFrontierId = state.Session.FrontierId;
        tick.PreviousCommanderName = state.Session.CommanderName;
        state.IsAwaitingCommanderIdentity = context.Update.IsAwaitingCommanderIdentity;
        if (context.IsBootstrapRead || context.Update.Status is not null)
        {
            state.LatestStatus = context.Update.Status;
        }
    }

    private void ApplyJournalSessionState(JournalMonitorUpdate update)
    {
        foreach (JournalEventEnvelope journalEvent in update.JournalEvents)
        {
            state.Session.Apply(journalEvent);
        }

        shell.UpdateSystemBodyDataGameSessionConfirmation(update);

        if (update.Status is { } status)
        {
            state.Session.ReconcileVehicleStatus(status);
        }
    }

    private void ApplyStatusToStatusConsumers(EliteStatus? status)
    {
        if (status is not null)
        {
            state.Exobiology.UpdateStatus(status);
            dependencies.GroundTarget.UpdateStatus(status);
            dependencies.Colonization.UpdateStatus(status);
        }
    }

    private void ApplyOverlayBehaviorContext()
    {
        dependencies.OverlayBehavior.UpdateContext(state.Session.CurrentSuit, state.LatestStatus?.OnFoot == true);
        dependencies.OverlayBehavior.UpdateSessionContext(
            state.LatestStatus is not null,
            !string.IsNullOrWhiteSpace(state.Session.CommanderName),
            state.Session.IsShutdown,
            state.Session.IsAtMainMenu || state.IsAwaitingCommanderIdentity,
            state.Session.IsAtCarrierManagement
        );
    }

    /// <summary>Synchronizes event-time Raven state before the commander journey is updated.</summary>
    private async Task SynchronizeColonizationLiveProjectsAsync(
        JournalProjectionContext context,
        JournalProjectionTick tick
    )
    {
        JournalMonitorUpdate update = context.Update;
        bool cargoActivity =
            tick.AllowSharedCargo
            && (
                tick.CargoChanged
                || update.Cargo is not null
                || update.JournalEvents.Any(journalEvent =>
                    journalEvent.EventName is "Cargo" or "CargoTransfer" or "MarketBuy" or "MarketSell"
                )
            );
        bool isCurrentCargoInventoryAvailable = !state.AwaitFreshCargoSnapshot || update.Cargo is not null;
        await dependencies.Colonization.SynchronizeLiveProjectsAsync(
            update.JournalEvents,
            allowPublishing: tick.AllowLiveEffects,
            cargoInventory: tick.AllowSharedCargo ? state.CargoInventory : null,
            preferShipCargoDiffForSquadron: isCurrentCargoInventoryAvailable,
            cargoActivity: cargoActivity,
            cancellationToken: tick.WorkCancellationToken
        );
    }

    private async Task ApplyJourneyUpdateAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        bool initializedJourney = await dependencies.Journey.UpdateContextAsync(
            state.Session.FrontierId,
            state.Session.CommanderName,
            state.Session.IsLegacy != true,
            state.Session.SystemName,
            state.Session.SystemAddress
        );
        if (!initializedJourney)
        {
            await dependencies.Journey.ApplyJournalEventsAsync(journalEvents);
        }
    }

    private Task ApplyRouteJournalEventsAsync(JournalProjectionContext context) =>
        dependencies.Route.ApplyJournalEventsAsync(context.JournalEvents);

    private async Task ApplyFleetCarrierRouteJournalEventsAsync(JournalProjectionContext context)
    {
        await dependencies.FleetCarrierRoute.ApplyJournalEventsAsync(context.JournalEvents);
    }

    private async Task ApplyBoxelSearchRouteAsync(NavRouteSnapshot? navRoute)
    {
        if (navRoute is not null)
        {
            await dependencies.BoxelSearch.UpdateRouteAsync(navRoute);
        }
    }

    private async Task ApplyBoxelSearchJournalEventsAsync(JournalProjectionContext context)
    {
        await dependencies.BoxelSearch.ApplyJournalEventsAsync(context.JournalEvents);
    }

    private void ApplyGuardianCargo(JournalProjectionTick tick)
    {
        if (!tick.AllowSharedCargo)
        {
            dependencies.Guardian.ClearCargo();
        }
        else if (tick.CargoChanged && state.LatestCargo is not null)
        {
            dependencies.Guardian.UpdateCargo(state.LatestCargo);
        }
    }

    private async Task ApplyColonizationCargoAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (tick.CargoChanged && state.LatestCargo is not null)
        {
            await dependencies.Colonization.UpdateCargoAsync(
                state.LatestCargo,
                publishCurrentShipCargo: tick.AllowLiveEffects && context.Update.Cargo is not null
            );
        }
    }

    private async Task ApplyGuardianStatusAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (context.Update.Status is not null)
        {
            await dependencies.Guardian.UpdateStatusAsync(
                context.Update.Status,
                allowGesture: tick.AllowLiveEffects,
                cancellationToken: CancellationToken.None
            );
        }
    }

    private void ApplyStationInfoStatus(EliteStatus? status)
    {
        if (status is not null)
        {
            dependencies.StationInfo.UpdateStatus(status);
        }
    }

    private async Task ApplyRouteAndBoxelStatusAsync()
    {
        if (state.LatestStatus is null)
        {
            return;
        }

        await dependencies.Route.UpdateStatusAsync(state.LatestStatus, state.Session.MusicTrack);
        await dependencies.FleetCarrierRoute.UpdateStatusAsync(state.LatestStatus, state.Session.MusicTrack);
        await dependencies.BoxelSearch.UpdateStatusAsync(
            state.LatestStatus,
            allowAutoCopy: !dependencies.Route.ShouldAutoCopyNextHop
                && !dependencies.FleetCarrierRoute.ShouldAutoCopyNextHop,
            nextMusicTrack: state.Session.MusicTrack
        );
    }

    private async Task ApplyScreenshotProcessingAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        ScreenshotProcessingResult screenshotResult = await dependencies.ScreenshotProcessing.ProcessJournalEventsAsync(
            context.JournalEvents,
            state.Session.CommanderName,
            tick.GuardianScreenshotContexts,
            state.LatestStatus is { } screenshotStatus
                ? new ScreenshotNavigationContext(
                    DateTimeOffset.UtcNow,
                    screenshotStatus.Latitude,
                    screenshotStatus.Longitude,
                    screenshotStatus.NormalizedHeading,
                    screenshotStatus.HasLatitudeLongitude
                )
                : null,
            CancellationToken.None
        );
        dependencies.Notifications.ReportScreenshotResult(
            screenshotResult,
            dependencies.ScreenshotProcessing.AddBanner
        );
    }

    private void ApplySystemSurveyUpdate(JournalProjectionContext context, JournalProjectionTick tick)
    {
        JournalMonitorUpdate update = context.Update;
        tick.ExobiologyAfter = state.Exobiology.CreateSnapshot();
        tick.ExobiologyChanged = state.Exobiology.Version != tick.ExobiologyVersionBefore;
        if (
            update.JournalEvents.Count > 0
            || update.Status is not null
            || tick.ExobiologyChanged
            || context.IsManualRefresh
        )
        {
            dependencies.SystemSurvey.ApplyUpdate(
                update.JournalEvents,
                update.Status,
                tick.ExobiologyAfter,
                state.Session.ActiveSrvType,
                state.Session.ParkedSrvType
            );
        }
    }

    private async Task ApplyBoxelSurveyStatsAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (tick.ExobiologyChanged || context.JournalEvents.Count > 0)
        {
            await dependencies.boxelSurveyStats.IngestSnapshotAsync(
                dependencies.SystemSurvey.Snapshot,
                cancellationToken: CancellationToken.None
            );
        }

        if (!tick.SkipPersistedBootstrapEvents)
        {
            await dependencies.boxelSurveyStats.ApplyJournalEventsAsync(context.JournalEvents, CancellationToken.None);
        }
        else
        {
            await dependencies.boxelSurveyStats.ApplyBootstrapContextAsync(
                context.JournalEvents,
                CancellationToken.None
            );
        }
    }

    private async Task ApplyLiveFirstFootfallTextCommandsAsync(
        JournalProjectionContext context,
        JournalProjectionTick tick
    )
    {
        if (await shell.ApplyFirstFootfallTextCommandsAsync(context.JournalEvents) > 0)
        {
            tick.ExobiologyAfter = state.Exobiology.CreateSnapshot();
            dependencies.SystemSurvey.ApplyUpdate([], null, tick.ExobiologyAfter);
        }
    }

    private async Task ApplyFirstFootfallInferenceAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (await shell.TryInferFirstFootfallAsync(context.Update))
        {
            tick.ExobiologyAfter = state.Exobiology.CreateSnapshot();
            dependencies.SystemSurvey.ApplyUpdate([], null, tick.ExobiologyAfter);
        }
    }

    private async Task OpenRequestedBiologyCodexEntryAsync(JournalProjectionContext context)
    {
        if (
            dependencies.SystemSurvey.LatestBiologyEntryId is { } entryId
            && context.JournalEvents.Any(IsShowCodexCommand)
        )
        {
            await dependencies.BiologyCodex.OpenEntryAsync(entryId);
        }
    }

    /// <summary>Restores surface state while restricting mining chat commands to live journal updates.</summary>
    private async Task ApplySurfaceTrackingAsync(JournalProjectionContext context, JournalProjectionTick tick)
    {
        JournalMonitorUpdate update = context.Update;
        if (
            update.Cargo is null
            && update.JournalEvents.Count == 0
            && update.Status is null
            && !tick.ExobiologyChanged
            && !context.IsManualRefresh
        )
        {
            return;
        }

        SurfaceSurveySessionContext? surfaceSession = CreateSurfaceSurveySessionContext();
        if (!tick.SkipPersistedBootstrapEvents)
        {
            // Clear the mining body's rigs before boarding can remove its live surface context.
            await dependencies.Mining.ClearRigsOnShipBoardingAsync(update.JournalEvents, state.Session.FrontierId);
        }

        await dependencies.SurfaceSurvey.ApplyUpdateAsync(
            surfaceSession,
            update.JournalEvents,
            update.Status,
            tick.ExobiologyAfter,
            processJournalMutations: !tick.SkipPersistedBootstrapEvents,
            scansLostToDeath: tick.ScansLostToDeath.ToArray(),
            cancellationToken: CancellationToken.None
        );
        bool isSessionActive = !state.Session.IsShutdown && !state.Session.IsAtMainMenu;
        await dependencies.MineMap.ApplyUpdateAsync(
            update.JournalEvents,
            CreateMineMapCommandContext(),
            state.LatestStatus,
            allowCommands: tick.AllowLiveEffects
        );
        await dependencies.Mining.ApplyUpdateAsync(
            surfaceSession,
            dependencies.SystemSurvey.Snapshot,
            dependencies.SystemSurvey.CurrentStatus,
            isSessionActive ? state.Session.ActiveSrvType : null,
            new SurfaceMiningMapPresentation(
                dependencies.SurfaceSurvey.RadarMarkers,
                dependencies.MineMap.ActiveLiveSurvey
            ),
            state.LatestCargo,
            isSessionActive ? state.Session.ParkedSrvType : null
        );
        if (tick.AllowLiveEffects && isSessionActive)
        {
            await dependencies.Mining.ClearRigsFromChatAsync(update.JournalEvents, state.Session.FrontierId);
        }
    }

    private async Task SaveExobiologyIfChangedAsync(JournalProjectionTick tick)
    {
        if (tick.ExobiologyChanged)
        {
            await shell.SaveExobiologyAsync(tick.ExobiologyAfter);
        }
    }

    private void UpdateExobiologyDisplayIfObserved(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (context.JournalEvents.Count > 0 || context.Update.Status is not null)
        {
            shell.UpdateExobiologyDisplay(tick.ExobiologyAfter);
        }
    }

    private async Task ApplyCommanderChangeIfNeededAsync(
        JournalMonitorUpdate update,
        string? previousFrontierId,
        string? previousCommanderName
    )
    {
        bool commanderChanged =
            !string.Equals(previousFrontierId, state.Session.FrontierId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(previousCommanderName, state.Session.CommanderName, StringComparison.OrdinalIgnoreCase);
        if (!commanderChanged)
        {
            return;
        }

        state.AwaitFreshCargoSnapshot = true;
        state.CompanionIdentityChangedAt =
            update
                .JournalEvents.Where(journalEvent => journalEvent.EventName is "Commander" or "LoadGame")
                .Select(journalEvent => journalEvent.Timestamp)
                .LastOrDefault(timestamp => timestamp is not null)
            ?? state.Session.LastEventTimestamp;
        state.CargoInventory.Reset(null);
        state.LatestCargo = null;
        state.LatestShipLocker = null;
        await dependencies.FrontierProfile.SetCommanderContextAsync(
            state.Session.FrontierId,
            state.Session.CommanderName,
            refreshIfOpen: false,
            CancellationToken.None
        );
        dependencies.FrontierProfile.LoadAutomatically();
    }

    private void ApplyShipLockerIfAllowed(JournalMonitorUpdate update, bool allowSharedCargo)
    {
        if (
            allowSharedCargo
            && update.ShipLocker is not null
            && IsCurrentCommanderCompanionSnapshot(update.ShipLocker.Timestamp)
        )
        {
            state.LatestShipLocker = update.ShipLocker;
        }
    }

    private async Task ApplyGreenGasGiantPublicationAsync(JournalMonitorUpdate update, bool allowLiveEffects)
    {
        GreenGasGiantPublicationResult greenGasGiantResult =
            await dependencies.greenGasGiantPublicationCoordinator.ApplyAsync(
                update.JournalEvents,
                dependencies.NetworkPrivacy.UploadGreenGasGiantCandidates,
                allowPublishing: allowLiveEffects,
                CancellationToken.None
            );
        dependencies.NetworkPrivacy.ReportPublicationResult(greenGasGiantResult);
        if (allowLiveEffects)
        {
            dependencies.Notifications.ReportGreenGasGiantUploads(greenGasGiantResult);
        }

        foreach (string warning in greenGasGiantResult.Warnings)
        {
            dependencies.applicationLogService?.Append(warning);
        }
    }

    private async Task<CommanderCodexJournalTrackResult> ApplyCommanderCodexUpdateAsync(JournalMonitorUpdate update)
    {
        CommanderCodexJournalTrackResult commanderCodexResult =
            await dependencies.commanderCodexJournalTracker.ApplyAsync(update.JournalEvents, CancellationToken.None);
        if (commanderCodexResult.Warnings.Count > 0)
        {
            shell.CommanderCodexStatus(string.Join(Environment.NewLine, commanderCodexResult.Warnings));
        }
        else if (commanderCodexResult.DiscoveryEventCount > 0)
        {
            shell.CommanderCodexStatus(
                commanderCodexResult.HasChanges
                    ? $"Recorded {commanderCodexResult.ChangedEntryCount:N0} "
                        + "Commander Codex ledger entries across "
                        + $"{commanderCodexResult.ChangedFileCount:N0} files."
                    : "Commander Codex is current; no earlier firsts were found."
            );
        }

        return commanderCodexResult;
    }

    private async Task PersistExplorationIfChangedAsync(ExplorationSnapshot explorationBefore)
    {
        ExplorationSnapshot explorationAfter = state.Exploration.CreateSnapshot();
        if (explorationAfter == explorationBefore)
        {
            return;
        }

        shell.UpdateExplorationDisplay(explorationAfter);
        await shell.SaveExplorationAsync(explorationAfter);
    }

    private async Task RequestShutdownIfNeededAsync(bool requestShutdown)
    {
        if (requestShutdown)
        {
            await shell.RequestShutdown();
        }
    }

    private bool ApplyCargoInventoryUpdate(JournalMonitorUpdate update, bool allowSharedCargo)
    {
        bool cargoChanged = false;
        if (!allowSharedCargo)
        {
            cargoChanged = state.CargoInventory.Reset(null);
            state.LatestCargo = null;
            state.LatestShipLocker = null;
            return cargoChanged;
        }

        if (state.AwaitFreshCargoSnapshot)
        {
            if (update.Cargo is not null && IsCurrentCommanderCompanionSnapshot(update.Cargo.Timestamp))
            {
                cargoChanged = state.CargoInventory.Reset(update.Cargo);
                state.AwaitFreshCargoSnapshot = false;
                state.LatestCargo = state.CargoInventory.CreateSnapshot();
            }

            return cargoChanged;
        }

        foreach (JournalEventEnvelope journalEvent in update.JournalEvents)
        {
            // Squadron linked FCs freeze the true before-state before CargoTransfer mutates
            // live inventory so the later GetDiff cannot collapse to a zero delta.
            if (string.Equals(journalEvent.EventName, "CargoTransfer", StringComparison.Ordinal))
            {
                dependencies.Colonization.PrepareSquadronCargoTransferSnapshot(state.CargoInventory);
            }

            cargoChanged |= state.CargoInventory.Apply(journalEvent, state.LatestStatus?.InSrv == true);
        }

        if (update.Cargo is not null && IsCurrentCommanderCompanionSnapshot(update.Cargo.Timestamp))
        {
            cargoChanged |= state.CargoInventory.Reset(update.Cargo);
        }

        if (cargoChanged || state.LatestCargo is null)
        {
            state.LatestCargo = state.CargoInventory.CreateSnapshot();
        }

        return cargoChanged;
    }

    private void ApplyExplorationAndExobiologyJournalEvents(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool skipPersistedBootstrapEvents,
        HashSet<string> scansLostToDeath
    )
    {
        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            if (!skipPersistedBootstrapEvents || journalEvent.EventName is "Fileheader" or "LoadGame")
            {
                state.Exploration.Apply(journalEvent);
            }

            if (!skipPersistedBootstrapEvents || IsExobiologyContextEvent(journalEvent.EventName))
            {
                if (journalEvent.EventName == "Died")
                {
                    scansLostToDeath.UnionWith(state.Exobiology.CreateSnapshot().ScannedBioEntryIds);
                }

                state.Exobiology.Apply(journalEvent);
            }
        }
    }

    private SurfaceSurveySessionContext? CreateSurfaceSurveySessionContext()
    {
        if (
            string.IsNullOrWhiteSpace(state.ProfileFrontierId)
            || string.IsNullOrWhiteSpace(state.Session.SystemName)
            || state.Session.SystemAddress is not > 0
        )
        {
            return null;
        }

        SystemScanBodySnapshot? surfaceBody = dependencies.SystemSurvey.Snapshot.CurrentBodyId is { } bodyId
            ? dependencies.SystemSurvey.Snapshot.Bodies.FirstOrDefault(body => body.BodyId == bodyId)
            : null;
        surfaceBody ??= state.LatestStatus?.BodyName is { Length: > 0 } statusBodyName
            ? dependencies.SystemSurvey.Snapshot.Bodies.FirstOrDefault(body =>
                string.Equals(body.Name, statusBodyName, StringComparison.OrdinalIgnoreCase)
            )
            : null;
        return new SurfaceSurveySessionContext(
            state.ProfileFrontierId,
            state.ProfileCommanderName ?? state.Session.CommanderName,
            state.Session.SystemName,
            state.Session.SystemAddress.Value,
            state.Session.StarPosition,
            surfaceBody?.BodyId,
            surfaceBody?.Name,
            state.LatestStatus?.PlanetRadius is > 0
                ? (double)state.LatestStatus.PlanetRadius
                : surfaceBody?.RadiusMeters ?? 0,
            state.Session.KnownNomadVehicleId
        );
    }

    private MineMapCommandContext? CreateMineMapCommandContext()
    {
        SystemScanSnapshot survey = dependencies.SystemSurvey.Snapshot;
        string? currentFrontierId = state.ProfileFrontierId ?? state.Session.FrontierId;
        if (
            string.IsNullOrWhiteSpace(currentFrontierId)
            || string.IsNullOrWhiteSpace(state.Session.SystemName)
            || state.Session.SystemAddress is not > 0
            || state.Session.StarPosition is not { } systemPosition
            || state.LatestStatus is not { HasLatitudeLongitude: true } currentStatus
            || currentStatus.PlanetRadius is not > 0
        )
        {
            return null;
        }

        SystemScanBodySnapshot? body = survey.CurrentBodyId is { } bodyId
            ? survey.Bodies.FirstOrDefault(candidate => candidate.BodyId == bodyId)
            : null;
        body ??= currentStatus.BodyName is { Length: > 0 } statusBodyName
            ? survey.Bodies.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, statusBodyName, StringComparison.OrdinalIgnoreCase)
            )
            : null;
        if (body is null)
        {
            return null;
        }

        try
        {
            return new MineMapCommandContext(
                currentFrontierId,
                state.ProfileCommanderName ?? state.Session.CommanderName ?? string.Empty,
                state.Session.SystemName,
                state.Session.SystemAddress.Value,
                systemPosition,
                body.BodyId,
                body.Name,
                NormalizeMineMapBodyType(body.PlanetClass),
                body.DistanceFromArrivalLs,
                (double)currentStatus.PlanetRadius,
                new SurfaceCoordinate(currentStatus.Latitude, currentStatus.Longitude)
            );
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string NormalizeMineMapBodyType(string? planetClass) =>
        planetClass switch
        {
            "Rocky body" => "Rocky",
            "Icy body" => "Ice",
            "High metal content body" => "HMC",
            "Rocky ice body" => "Rocky Ice",
            "Metal rich body" => "Metal Rich",
            { Length: > 0 } value => value,
            _ => "Unknown",
        };

    private async Task ApplyIdleHousekeepingAsync(JournalMonitorUpdate update)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - state.LastIdleHousekeepingAt < TimeSpan.FromSeconds(5))
        {
            return;
        }

        state.LastIdleHousekeepingAt = now;
        await ApplyExternalPublicationAsync(
            update,
            allowSharedCargo: !dependencies.CommanderInstances.HasMultipleGameWindows
        );
        shell.StartSystemBodyDataRetryIfDue();
    }

    private async Task ApplyExternalPublicationAsync(JournalMonitorUpdate update, bool allowSharedCargo)
    {
        state.LastIdleHousekeepingAt = DateTimeOffset.UtcNow;
        bool canShareCargo = allowSharedCargo;
        try
        {
            dependencies.CommanderInstances.RefreshGameWindowCount();
            bool hasMultipleGameWindows = dependencies.CommanderInstances.HasMultipleGameWindows;
            canShareCargo &= !hasMultipleGameWindows;
            dependencies.eddnPublisher.SetSuspended(hasMultipleGameWindows);
            EddnPublicationResult eddnResult = await dependencies.eddnPublisher.ApplyAsync(
                new EddnApplyRequest
                {
                    JournalEvents = update.JournalEvents,
                    Status = state.LatestStatus,
                    Enabled = dependencies.NetworkPrivacy.EddnUploadEnabled,
                    AllowPublishing = !update.IsBootstrapRead && !hasMultipleGameWindows,
                    JournalDirectory = dependencies.folderResolution.SelectedPath,
                    JournalPath = update.JournalPath,
                    AllowSharedData = !hasMultipleGameWindows,
                    CommanderName = state.Session.CommanderName,
                    FrontierId = state.Session.FrontierId,
                    GameVersion = state.Session.GameVersion,
                    GameBuild = state.Session.GameBuild,
                },
                cancellationToken: CancellationToken.None
            );
            dependencies.NetworkPrivacy.ReportPublicationResult(eddnResult);
            foreach (string warning in eddnResult.Warnings)
            {
                dependencies.applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dependencies.applicationLogService?.Append(
                "EDDN processing was isolated from journal tracking: " + exception.Message
            );
        }

        try
        {
            VoxStellarPublicationResult voxStellarResult = await dependencies.voxStellarPublisher.ApplyAsync(
                new VoxStellarApplyRequest
                {
                    JournalEvents = update.JournalEvents,
                    CommanderName = state.ProfileCommanderName ?? state.Session.CommanderName,
                    Enabled = dependencies.VoxStellar.JournalUploadEnabled,
                    AllowPublishing =
                        !update.IsBootstrapRead && !dependencies.CommanderInstances.HasMultipleGameWindows,
                },
                CancellationToken.None
            );
            dependencies.VoxStellar.ReportPublicationResult(voxStellarResult);
            foreach (string warning in voxStellarResult.Warnings)
            {
                dependencies.applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dependencies.applicationLogService?.Append(
                "VoxStellar processing was isolated from journal tracking: " + exception.Message
            );
        }

        try
        {
            InaraPublicationResult inaraResult = await dependencies.inaraPublisher.ApplyAsync(
                new InaraPublicationUpdate(
                    update.JournalEvents,
                    state.LatestStatus,
                    state.LatestCargo,
                    update.JournalPath,
                    AllowPublishing: !update.IsBootstrapRead,
                    AllowSharedData: canShareCargo,
                    state.Session.SystemName,
                    state.Session.StationName,
                    state.Session.BodyName,
                    state.Session.ShipType,
                    state.Session.ShipId,
                    state.Session.ShipName,
                    state.Session.ShipIdent,
                    new InaraPublicationOptions(
                        dependencies.Inara.StoredApiKey,
                        state.ProfileCommanderName ?? state.Session.CommanderName,
                        state.ProfileFrontierId ?? state.Session.FrontierId,
                        state.Session.GameVersion,
                        state.Session.IsLegacy == false
                    )
                ),
                CancellationToken.None
            );
            dependencies.Inara.ReportPublicationResult(inaraResult);
            foreach (string warning in inaraResult.Warnings)
            {
                dependencies.applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dependencies.Inara.ReportPublicationFailure(exception);
            dependencies.applicationLogService?.Append(
                "Inara processing was isolated from journal tracking: " + exception.Message
            );
        }

        try
        {
            EdsmPublicationResult edsmResult = await dependencies.edsmPublisher.ApplyAsync(
                new EdsmPublicationUpdate(
                    update.JournalEvents,
                    update.JournalPath,
                    AllowPublishing: !dependencies.IsDiagnosticReplay()
                        && !update.IsBootstrapRead
                        && !dependencies.CommanderInstances.HasMultipleGameWindows,
                    new EdsmPublicationOptions(
                        dependencies.Edsm.StoredApiKey,
                        dependencies.Edsm.UploadCommanderName,
                        state.ProfileCommanderName ?? state.Session.CommanderName,
                        state.ProfileFrontierId ?? state.Session.FrontierId,
                        state.Session.GameVersion,
                        state.Session.GameBuild,
                        state.Session.IsLegacy == false
                    )
                ),
                CancellationToken.None
            );
            dependencies.Edsm.ReportPublicationResult(edsmResult);
            foreach (string warning in edsmResult.Warnings)
            {
                dependencies.applicationLogService?.Append(warning);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dependencies.Edsm.ReportPublicationFailure(exception);
            dependencies.applicationLogService?.Append(
                "EDSM processing was isolated from journal tracking: " + exception.Message
            );
        }
    }

    private async Task ApplyQuestUpdateAsync(JournalMonitorUpdate update, bool allowCargoFile)
    {
        if (
            string.IsNullOrWhiteSpace(state.Session.FrontierId)
            || string.IsNullOrWhiteSpace(state.Session.CommanderName)
            || dependencies.folderResolution.SelectedPath is null
        )
        {
            shell.QuestStatus("Waiting for a commander journal session.");
            return;
        }

        try
        {
            bool enabled = dependencies.questSettingsStore.LoadEnabled();
            IReadOnlyList<QuestRuntimeSnapshot> previousQuestSnapshot = dependencies.questRuntimeCoordinator.Snapshot;
            QuestRuntimeUpdateResult result = await dependencies.questRuntimeCoordinator.ApplyUpdateAsync(
                new QuestRuntimeConfiguration(
                    enabled,
                    state.Session.FrontierId,
                    state.Session.CommanderName,
                    state.ProfileRavenApiKey,
                    state.LatestStatus
                ),
                dependencies.folderResolution.SelectedPath,
                update.JournalEvents,
                update.IsBootstrapRead,
                allowCargoFile: allowCargoFile,
                cancellationToken: CancellationToken.None
            );
            dependencies.QuestWorkspace.ApplyRuntimeResult(result, enabled);
            if (ReferenceEquals(previousQuestSnapshot, result.Quests))
            {
                // Status can move quest overlay markers without changing the
                // quest rows. Snapshot changes are handled by the coordinator
                // event and must not be projected a second time here.
                shell.UpdateQuestOverlayPresentation(result.Quests, enabled);
            }
            if (!enabled)
            {
                shell.QuestStatus("Quests are disabled.");
            }
            else if (result.Warnings.Count > 0)
            {
                shell.QuestStatus(string.Join(Environment.NewLine, result.Warnings));
            }
            else
            {
                shell.QuestStatus(
                    result.Quests.Count == 0
                        ? "No active quests."
                        : $"{result.Quests.Count:N0} active quest(s); "
                            + $"{dependencies.questRuntimeCoordinator.Snapshot.Sum(quest => quest.UnreadMessageCount):N0} unread message(s)."
                );
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or ArgumentException
                        or HttpRequestException
            )
        {
            shell.QuestStatus("Quest update failed without changing imported " + "source data: " + exception.Message);
            dependencies.applicationLogService?.Append(
                "Quest update failed without changing imported source data: " + exception.Message
            );
        }
    }

    private static bool IsExobiologyContextEvent(string eventName)
    {
        return eventName is "Location" or "FSDJump" or "CarrierJump" or "ApproachBody" or "Scan" or "Disembark";
    }

    private bool IsCurrentCommanderCompanionSnapshot(DateTimeOffset timestamp) =>
        state.CompanionIdentityChangedAt is not { } changedAt || timestamp >= changedAt;

    private static bool IsShowCodexCommand(JournalEventEnvelope journalEvent)
    {
        return journalEvent.EventName == "SendText"
            && journalEvent.Payload.TryGetProperty("Message", out JsonElement message)
            && message.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(message.GetString()?.Trim(), ".show", StringComparison.OrdinalIgnoreCase);
    }
}
