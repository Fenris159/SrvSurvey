using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Registers every journal projection in the order a monitor tick applies them.</summary>
    private JournalProjectionPipeline<JournalTick> CreateJournalProjectionPipeline()
    {
        var pipeline = new JournalProjectionPipeline<JournalTick>();
        RegisterIdleProjections(pipeline);
        RegisterSessionProjections(pipeline);
        RegisterCargoAndStatusProjections(pipeline);
        RegisterCommanderContextProjections(pipeline);
        RegisterNavigationProjections(pipeline);
        RegisterActivityProjections(pipeline);
        RegisterExplorationProjections(pipeline);
        RegisterSurveyProjections(pipeline);
        RegisterShellProjections(pipeline);
        return pipeline;
    }

    /// <summary>Services retained Raven work even when the journal poll is idle.</summary>
    private void RegisterIdleProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .Idle((_, _) => MiningWorkspace.Tick())
            .IdleAsync(
                (context, _) =>
                    Colonization.SynchronizeLiveProjectsAsync(
                        [],
                        allowPublishing: !IsDiagnosticReplay,
                        cancellationToken: context.CancellationToken
                    )
            )
            .IdleAsync((context, _) => ApplyIdleHousekeepingAsync(context.Update));

    private void RegisterSessionProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .FullAsync((context, _) => AppendCompanionTimelineAsync(context.Update))
            .Full(ApplySessionBaseline)
            .Full((context, _) => JournalInspector.ApplyUpdate(context.JournalEvents, context.Update.Status))
            .Full((context, _) => ApplyJournalSessionState(context.Update))
            .Full((context, _) => Firegroups.Apply(context.Update, journalState, latestStatus))
            .Full((_, _) => OverlayExceptions.UpdateBoardedVehicle(journalState, latestStatus))
            .Full(
                (_, _) =>
                {
                    Colonization.UpdateMusicTrack(journalState.MusicTrack);
                    StationInfo.UpdateMusicTrack(journalState.MusicTrack);
                    GroundTarget.UpdateMusicTrack(journalState.MusicTrack);
                }
            )
            .FullAsync(
                (context, tick) =>
                    ApplyCommanderChangeIfNeededAsync(
                        context.Update,
                        tick.PreviousFrontierId,
                        tick.PreviousCommanderName
                    )
            );

    private void RegisterCargoAndStatusProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .Full(
                (context, tick) =>
                {
                    tick.AllowSharedCargo = !IsSharedCargoSuppressed;
                    tick.CargoChanged = ApplyCargoInventoryUpdate(context.Update, tick.AllowSharedCargo);
                }
            )
            .Full((context, _) => MiningWorkspace.Apply(context.Update, journalState, latestCargo, latestStatus))
            .Full((context, tick) => ApplyShipLockerIfAllowed(context.Update, tick.AllowSharedCargo))
            .Full(
                (_, tick) =>
                    FrontierProfile.UpdateLocalInventory(
                        latestCargo,
                        latestShipLocker,
                        isSuppressed: !tick.AllowSharedCargo
                    )
            )
            .Full((context, _) => DockToDock.ApplyUpdate(context.JournalEvents, latestCargo, context.IsBootstrapRead))
            .Full((context, _) => DesktopBehavior.ApplyJournalEvents(context.JournalEvents, context.IsBootstrapRead))
            .Full((context, _) => ApplyStatusToStatusConsumers(context.Update.Status))
            .FullAsync(
                (context, _) =>
                    GroundTarget.ApplyJournalEventsAsync(context.JournalEvents, allowCommands: context.IsLive)
            )
            .FullAsync((context, _) => ApplyGreenGasGiantPublicationAsync(context.Update))
            .Full(
                (context, _) =>
                {
                    FrontierProfile.UpdateJournalReputation(journalState.CommanderName, context.JournalEvents);
                    FrontierProfile.UpdateJournalCommunityGoals(journalState.CommanderName, context.JournalEvents);
                    FrontierProfile.UpdateJournalCarrierJump(journalState.CommanderName, context.JournalEvents);
                }
            )
            .Full((_, _) => ApplyOverlayBehaviorContext())
            .Full((_, _) => JournalPostProcessor.SelectCommander(journalState.FrontierId));

    private void RegisterCommanderContextProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .FullAsync(
                async (context, tick) =>
                {
                    CommanderCodexJournalTrackResult result = await ApplyCommanderCodexUpdateAsync(context.Update);
                    tick.CodexDiscoveryChanged = result.DiscoveryEventCount > 0;
                }
            )
            .Full(
                (context, _) =>
                {
                    Colonization.UpdateSystemContext(
                        journalState.SystemName,
                        journalState.StarPosition,
                        journalState.SystemAddress
                    );
                    Colonization.ApplyJournalEvents(context.JournalEvents, journalState.CommanderName);
                }
            )
            .Full(
                (_, _) =>
                    Search.UpdateCurrentSystem(
                        journalState.SystemName,
                        journalState.StarPosition,
                        journalState.SystemAddress
                    )
            )
            .Full((_, _) => MiningWorkspace.UseCommanderSystem(journalState.SystemName))
            .Full((_, _) => MineMap.UpdateCurrentSystem(journalState.SystemName))
            .Full(
                (_, _) =>
                    NearestSystems.UpdateContext(
                        journalState.SystemName,
                        journalState.StarPosition,
                        journalState.CommanderName,
                        journalState.SystemAddress
                    )
            )
            .FullAsync(
                (_, tick) =>
                    CodexBingo.UpdateContextAsync(
                        journalState.FrontierId,
                        journalState.CommanderName,
                        journalState.SystemName,
                        journalState.StarPosition,
                        forceRefresh: tick.CodexDiscoveryChanged
                    )
            )
            .Full(
                (_, _) =>
                    SystemNotes.UpdateContext(
                        journalState.FrontierId,
                        journalState.CommanderName,
                        journalState.SystemName,
                        journalState.SystemAddress,
                        journalState.StarPosition
                    )
            )
            .FullAsync(
                (_, _) =>
                    BoxelSearch.UpdateCurrentSystemAsync(
                        journalState.SystemName,
                        journalState.StarPosition,
                        journalState.SystemAddress
                    )
            )
            .Full((_, _) => Guardian.UpdateCurrentSystem(journalState.SystemName, journalState.StarPosition))
            .Full(
                (_, _) =>
                    HumanSite.UpdateContext(
                        journalState.FrontierId,
                        journalState.CommanderName,
                        journalState.SystemName,
                        journalState.SystemAddress ?? 0,
                        journalState.StarPosition
                    )
            )
            .Full(
                (_, _) =>
                    _ = StationInfo.UpdateCurrentSystemAsync(journalState.SystemName, journalState.SystemAddress ?? 0)
            )
            .FullAsync(
                async (context, tick) =>
                {
                    bool loadedExistingProfile = await EnsureCommanderProfileAsync();
                    tick.SkipPersistedBootstrapEvents = context.IsBootstrapRead && loadedExistingProfile;
                }
            )
            .FullAsync((context, tick) => ApplyQuestUpdateAsync(context.Update, tick.AllowSharedCargo))
            .FullAsync((_, _) => Colonization.SetCommanderAsync(journalState.CommanderName))
            .FullAsync(SynchronizeColonizationLiveProjectsAsync)
            .FullAsync((context, _) => ApplyJourneyUpdateAsync(context.JournalEvents));

    private void RegisterNavigationProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .FullAsync(
                (_, _) =>
                    Route.UpdateContextAsync(
                        journalState.FrontierId,
                        journalState.SystemName,
                        journalState.SystemAddress,
                        journalState.StarPosition
                    )
            )
            .FullAsync((_, _) => RouteManager.UpdateContextAsync(journalState.FrontierId))
            .FullAsync(
                (_, _) =>
                    FleetCarrierRoute.UpdateContextAsync(
                        journalState.FrontierId,
                        journalState.SystemName,
                        journalState.SystemAddress,
                        journalState.StarPosition
                    )
            )
            .FullAsync((_, _) => FleetCarrierRouteManager.UpdateContextAsync(journalState.FrontierId))
            .FullAsync((_, _) => routeAutoCopyCoordinator.ReconcileAsync())
            .FullAsync((context, _) => ApplyRouteJournalEventsAsync(context))
            .FullAsync((context, _) => ApplyFleetCarrierRouteJournalEventsAsync(context))
            .Full(
                (_, tick) =>
                {
                    tick.ExplorationBefore = explorationState.CreateSnapshot();
                    tick.ExobiologyVersionBefore = exobiologyState.Version;
                    tick.BoxelBefore = BoxelSearch.CreateNotificationState();
                }
            )
            .FullAsync((context, _) => ApplyBoxelSearchRouteAsync(context.Update.NavRoute))
            .FullAsync(
                (context, _) =>
                    Search.UpdateNavigationAsync(
                        context.Update.NavRoute,
                        context.Update.Status,
                        journalState.MusicTrack
                    )
            )
            .FullAsync(ApplyBoxelSearchJournalEventsAsync)
            .Full(
                (context, _) =>
                    Notifications.ApplyJournalEvents(context.JournalEvents, allowNotifications: context.IsLive)
            )
            .Full(
                (context, _) =>
                    PulseOverlay.ApplyUpdate(context.JournalEvents, context.Update.Status, context.IsBootstrapRead)
            )
            .Full(
                (context, tick) =>
                    Notifications.ReportBoxelUpdate(
                        tick.BoxelBefore,
                        BoxelSearch.CreateNotificationState(),
                        context.JournalEvents.Any(journalEvent => journalEvent.EventName == "FSSAllBodiesFound"),
                        allowNotifications: context.IsLive
                    )
            );

    private void RegisterActivityProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .FullAsync(
                async (context, tick) =>
                    tick.GuardianScreenshotContexts = await Guardian.ApplyJournalEventsAsync(
                        context.JournalEvents,
                        activeProfileCommanderName,
                        allowLiveCommands: context.IsLive,
                        status: latestStatus,
                        cancellationToken: firstFootfallInferenceCancellation.Token
                    )
            )
            .Full((_, tick) => ApplyGuardianCargo(tick))
            .FullAsync(ApplyColonizationCargoAsync)
            .FullAsync((context, _) => Colonization.UpdateMarketAsync(context.Update.Market))
            .Full(
                (_, _) =>
                {
                    SystemSurvey.SetActiveBuildProjects(Colonization.HasProjects);
                    Combat.SetActiveBuildProjects(Colonization.HasProjects);
                    Guardian.SetActiveBuildProjects(Colonization.HasProjects);
                    HumanSite.SetActiveBuildProjects(Colonization.HasProjects);
                }
            )
            .FullAsync(
                (context, tick) =>
                    Combat.ApplyUpdateAsync(
                        context.JournalEvents,
                        context.Update.Status,
                        processHistoricalProgress: !tick.SkipPersistedBootstrapEvents
                    )
            )
            .FullAsync((context, _) => ApplyGuardianStatusAsync(context))
            .Full((context, _) => ApplyStationInfoStatus(context.Update.Status))
            .FullAsync((_, _) => ApplyRouteAndBoxelStatusAsync())
            .FullAsync(
                (context, _) =>
                    HumanSite.ApplyUpdateAsync(
                        context.JournalEvents,
                        context.Update.Status,
                        journalState.ShipType,
                        allowExternalData: context.IsLive
                    )
            )
            .FullAsync(
                async (context, tick) =>
                    tick.RequestShutdown = context.IsLive && await ApplyDesktopTextCommandsAsync(context.JournalEvents)
            )
            .FullAsync(ApplyScreenshotProcessingAsync);

    private void RegisterExplorationProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .Full(
                (context, _) =>
                    JumpInfo.ApplyUpdate(
                        new JumpInfoApplyUpdateRequest(
                            journalState.SystemName,
                            journalState.SystemAddress,
                            journalState.StarPosition,
                            context.Update.NavRoute,
                            context.JournalEvents,
                            context.Update.Status,
                            Route.CreateSnapshot(),
                            context.IsBootstrapRead
                        )
                    )
            )
            .Full(
                (context, _) =>
                    GalaxyMap.ApplyUpdate(
                        journalState.SystemName,
                        journalState.SystemAddress,
                        context.Update.NavRoute,
                        context.JournalEvents,
                        context.Update.Status,
                        context.IsBootstrapRead,
                        journalState.MusicTrack
                    )
            )
            .Full(
                (context, tick) =>
                    ApplyExplorationAndExobiologyJournalEvents(
                        context.JournalEvents,
                        tick.SkipPersistedBootstrapEvents,
                        tick.ScansLostToDeath
                    )
            )
            .FullAsync((_, tick) => PersistExplorationIfChangedAsync(tick.ExplorationBefore));

    private void RegisterSurveyProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .Full(ApplySystemSurveyUpdate)
            .Full((context, _) => UpdateActiveSystemVisit(context.JournalEvents))
            .FullAsync((_, _) => LoadCurrentSystemHistoryAsync())
            .FullAsync(ApplyBoxelSurveyStatsAsync)
            .Full((_, _) => PendingSystemBodyDataLoad = LoadCurrentSystemBodyDataAsync())
            .FullAsync(ApplyLiveFirstFootfallTextCommandsAsync)
            .FullAsync(ApplyFirstFootfallInferenceAsync)
            .Full((_, tick) => tick.ExobiologyChanged = exobiologyState.Version != tick.ExobiologyVersionBefore)
            .FullAsync((context, _) => PersistSystemScanAsync(context.JournalEvents))
            .FullAsync((_, tick) => RefreshSystemSurveyCommanderCodexAsync(forceRefresh: tick.CodexDiscoveryChanged))
            .FullAsync((context, _) => OpenRequestedBiologyCodexEntryAsync(context))
            .FullAsync(ApplySurfaceTrackingAsync)
            .FullAsync((_, tick) => SaveExobiologyIfChangedAsync(tick))
            .Full(UpdateExobiologyDisplayIfObserved);

    private void RegisterShellProjections(JournalProjectionPipeline<JournalTick> pipeline) =>
        pipeline
            .Full((context, _) => ApplyMonitorStatusMessages(context.Update, context.IsManualRefresh))
            // External publication runs after every local reducer and persistence
            // path so an unavailable gateway cannot delay live state projection.
            .FullAsync((context, tick) => ApplyExternalPublicationAsync(context.Update, tick.AllowSharedCargo))
            .FullAsync((_, tick) => RequestShutdownIfNeededAsync(tick.RequestShutdown));

    private async Task AppendCompanionTimelineAsync(JournalMonitorUpdate update)
    {
        if (IsDiagnosticReplay)
        {
            return;
        }

        try
        {
            await companionTimelineStore.AppendAsync(update, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            applicationLogService?.Append("Companion replay history could not be updated: " + exception.Message);
        }
    }

    private void ApplySessionBaseline(JournalProjectionContext context, JournalTick tick)
    {
        tick.PreviousFrontierId = journalState.FrontierId;
        tick.PreviousCommanderName = journalState.CommanderName;
        isAwaitingCommanderIdentity = context.Update.IsAwaitingCommanderIdentity;
        if (context.IsBootstrapRead || context.Update.Status is not null)
        {
            latestStatus = context.Update.Status;
        }
    }

    private void ApplyJournalSessionState(JournalMonitorUpdate update)
    {
        foreach (JournalEventEnvelope journalEvent in update.JournalEvents)
        {
            journalState.Apply(journalEvent);
        }

        UpdateSystemBodyDataGameSessionConfirmation(update);

        if (update.Status is { } status)
        {
            journalState.ReconcileVehicleStatus(status);
        }
    }

    private void ApplyStatusToStatusConsumers(EliteStatus? status)
    {
        if (status is not null)
        {
            exobiologyState.UpdateStatus(status);
            GroundTarget.UpdateStatus(status);
            Colonization.UpdateStatus(status);
        }
    }

    private void ApplyOverlayBehaviorContext()
    {
        OverlayBehavior.UpdateContext(journalState.CurrentSuit, latestStatus?.OnFoot == true);
        OverlayBehavior.UpdateSessionContext(
            latestStatus is not null,
            !string.IsNullOrWhiteSpace(journalState.CommanderName),
            journalState.IsShutdown,
            journalState.IsAtMainMenu || isAwaitingCommanderIdentity,
            journalState.IsAtCarrierManagement
        );
    }

    /// <summary>Synchronizes event-time Raven state before the commander journey is updated.</summary>
    private async Task SynchronizeColonizationLiveProjectsAsync(JournalProjectionContext context, JournalTick tick)
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
        bool isCurrentCargoInventoryAvailable = !awaitFreshCargoSnapshot || update.Cargo is not null;
        await Colonization.SynchronizeLiveProjectsAsync(
            update.JournalEvents,
            allowPublishing: context.IsLive,
            cargoInventory: tick.AllowSharedCargo ? cargoInventoryState : null,
            preferShipCargoDiffForSquadron: isCurrentCargoInventoryAvailable,
            cargoActivity: cargoActivity,
            cancellationToken: context.CancellationToken
        );
    }

    private async Task ApplyJourneyUpdateAsync(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        bool initializedJourney = await Journey.UpdateContextAsync(
            journalState.FrontierId,
            journalState.CommanderName,
            journalState.IsLegacy != true,
            journalState.SystemName,
            journalState.SystemAddress
        );
        if (!initializedJourney)
        {
            await Journey.ApplyJournalEventsAsync(journalEvents);
        }
    }

    private async Task ApplyRouteJournalEventsAsync(JournalProjectionContext context)
    {
        if (context.IsLive)
        {
            await Route.ApplyJournalEventsAsync(context.JournalEvents);
        }
    }

    private async Task ApplyFleetCarrierRouteJournalEventsAsync(JournalProjectionContext context)
    {
        if (context.IsBootstrapRead)
        {
            FleetCarrierRoute.ApplyFleetCarrierJumpEvents(context.JournalEvents);
            return;
        }

        await FleetCarrierRoute.ApplyJournalEventsAsync(context.JournalEvents);
    }

    private async Task ApplyBoxelSearchRouteAsync(NavRouteSnapshot? navRoute)
    {
        if (navRoute is not null)
        {
            await BoxelSearch.UpdateRouteAsync(navRoute);
        }
    }

    private async Task ApplyBoxelSearchJournalEventsAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (!tick.SkipPersistedBootstrapEvents)
        {
            await BoxelSearch.ApplyJournalEventsAsync(context.JournalEvents);
        }
    }

    private void ApplyGuardianCargo(JournalTick tick)
    {
        if (!tick.AllowSharedCargo)
        {
            Guardian.ClearCargo();
        }
        else if (tick.CargoChanged && latestCargo is not null)
        {
            Guardian.UpdateCargo(latestCargo);
        }
    }

    private async Task ApplyColonizationCargoAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (tick.CargoChanged && latestCargo is not null)
        {
            await Colonization.UpdateCargoAsync(latestCargo, publishCurrentShipCargo: context.Update.Cargo is not null);
        }
    }

    private async Task ApplyGuardianStatusAsync(JournalProjectionContext context)
    {
        if (context.Update.Status is not null)
        {
            await Guardian.UpdateStatusAsync(
                context.Update.Status,
                allowGesture: context.IsLive,
                cancellationToken: CancellationToken.None
            );
        }
    }

    private void ApplyStationInfoStatus(EliteStatus? status)
    {
        if (status is not null)
        {
            StationInfo.UpdateStatus(status);
        }
    }

    private async Task ApplyRouteAndBoxelStatusAsync()
    {
        if (latestStatus is null)
        {
            return;
        }

        await Route.UpdateStatusAsync(latestStatus, journalState.MusicTrack);
        await FleetCarrierRoute.UpdateStatusAsync(latestStatus, journalState.MusicTrack);
        await BoxelSearch.UpdateStatusAsync(
            latestStatus,
            allowAutoCopy: !Route.ShouldAutoCopyNextHop && !FleetCarrierRoute.ShouldAutoCopyNextHop,
            nextMusicTrack: journalState.MusicTrack
        );
    }

    private async Task ApplyScreenshotProcessingAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (context.IsBootstrapRead)
        {
            return;
        }

        ScreenshotProcessingResult screenshotResult = await ScreenshotProcessing.ProcessJournalEventsAsync(
            context.JournalEvents,
            journalState.CommanderName,
            tick.GuardianScreenshotContexts,
            latestStatus is { } screenshotStatus
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
        Notifications.ReportScreenshotResult(screenshotResult, ScreenshotProcessing.AddBanner);
    }

    private void ApplySystemSurveyUpdate(JournalProjectionContext context, JournalTick tick)
    {
        JournalMonitorUpdate update = context.Update;
        tick.ExobiologyAfter = exobiologyState.CreateSnapshot();
        tick.ExobiologyChanged = exobiologyState.Version != tick.ExobiologyVersionBefore;
        if (
            update.JournalEvents.Count > 0
            || update.Status is not null
            || tick.ExobiologyChanged
            || context.IsManualRefresh
        )
        {
            SystemSurvey.ApplyUpdate(
                update.JournalEvents,
                update.Status,
                tick.ExobiologyAfter,
                journalState.ActiveSrvType,
                journalState.ParkedSrvType
            );
        }
    }

    private async Task ApplyBoxelSurveyStatsAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (tick.ExobiologyChanged || context.JournalEvents.Count > 0)
        {
            await boxelSurveyStats.IngestSnapshotAsync(
                SystemSurvey.Snapshot,
                cancellationToken: CancellationToken.None
            );
        }

        if (!tick.SkipPersistedBootstrapEvents)
        {
            await boxelSurveyStats.ApplyJournalEventsAsync(context.JournalEvents, CancellationToken.None);
        }
        else
        {
            await boxelSurveyStats.ApplyBootstrapContextAsync(context.JournalEvents, CancellationToken.None);
        }
    }

    private async Task ApplyLiveFirstFootfallTextCommandsAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (context.IsLive && await ApplyFirstFootfallTextCommandsAsync(context.JournalEvents) > 0)
        {
            tick.ExobiologyAfter = exobiologyState.CreateSnapshot();
            SystemSurvey.ApplyUpdate([], null, tick.ExobiologyAfter);
        }
    }

    private async Task ApplyFirstFootfallInferenceAsync(JournalProjectionContext context, JournalTick tick)
    {
        if (await TryInferFirstFootfallAsync(context.Update))
        {
            tick.ExobiologyAfter = exobiologyState.CreateSnapshot();
            SystemSurvey.ApplyUpdate([], null, tick.ExobiologyAfter);
        }
    }

    private async Task OpenRequestedBiologyCodexEntryAsync(JournalProjectionContext context)
    {
        if (
            context.IsLive
            && SystemSurvey.LatestBiologyEntryId is { } entryId
            && context.JournalEvents.Any(IsShowCodexCommand)
        )
        {
            await BiologyCodex.OpenEntryAsync(entryId);
        }
    }

    /// <summary>Restores surface state while restricting mining chat commands to live journal updates.</summary>
    private async Task ApplySurfaceTrackingAsync(JournalProjectionContext context, JournalTick tick)
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
            await Mining.ClearRigsOnShipBoardingAsync(update.JournalEvents, journalState.FrontierId);
        }

        await SurfaceSurvey.ApplyUpdateAsync(
            surfaceSession,
            update.JournalEvents,
            update.Status,
            tick.ExobiologyAfter,
            processJournalMutations: !tick.SkipPersistedBootstrapEvents,
            scansLostToDeath: tick.ScansLostToDeath.ToArray(),
            cancellationToken: CancellationToken.None
        );
        bool isSessionActive = !journalState.IsShutdown && !journalState.IsAtMainMenu;
        await MineMap.ApplyUpdateAsync(
            update.JournalEvents,
            CreateMineMapCommandContext(),
            latestStatus,
            allowCommands: context.IsLive
        );
        await Mining.ApplyUpdateAsync(
            surfaceSession,
            SystemSurvey.Snapshot,
            SystemSurvey.CurrentStatus,
            isSessionActive ? journalState.ActiveSrvType : null,
            new SurfaceMiningMapPresentation(SurfaceSurvey.RadarMarkers, MineMap.ActiveLiveSurvey),
            latestCargo,
            isSessionActive ? journalState.ParkedSrvType : null
        );
        if (context.IsLive && isSessionActive)
        {
            await Mining.ClearRigsFromChatAsync(update.JournalEvents, journalState.FrontierId);
        }
    }

    private async Task SaveExobiologyIfChangedAsync(JournalTick tick)
    {
        if (tick.ExobiologyChanged)
        {
            await SaveExobiologyAsync(tick.ExobiologyAfter);
        }
    }

    private void UpdateExobiologyDisplayIfObserved(JournalProjectionContext context, JournalTick tick)
    {
        if (context.JournalEvents.Count > 0 || context.Update.Status is not null)
        {
            UpdateExobiologyDisplay(tick.ExobiologyAfter);
        }
    }

    /// <summary>Values earlier journal projections hand to later ones during one monitor tick.</summary>
    private sealed class JournalTick
    {
        public string? PreviousFrontierId { get; set; }

        public string? PreviousCommanderName { get; set; }

        public bool AllowSharedCargo { get; set; }

        public bool CargoChanged { get; set; }

        public bool CodexDiscoveryChanged { get; set; }

        /// <summary>Bootstrap replay over an already persisted profile must not reapply its journal mutations.</summary>
        public bool SkipPersistedBootstrapEvents { get; set; }

        public ExplorationSnapshot ExplorationBefore { get; set; } = null!;

        public int ExobiologyVersionBefore { get; set; }

        public BoxelSearchNotificationState BoxelBefore { get; set; } = null!;

        public IReadOnlyDictionary<
            JournalEventEnvelope,
            ScreenshotGuardianContext
        > GuardianScreenshotContexts { get; set; } = null!;

        public bool RequestShutdown { get; set; }

        public HashSet<string> ScansLostToDeath { get; } = new(StringComparer.Ordinal);

        public ExobiologySnapshot ExobiologyAfter { get; set; } = null!;

        public bool ExobiologyChanged { get; set; }
    }
}
