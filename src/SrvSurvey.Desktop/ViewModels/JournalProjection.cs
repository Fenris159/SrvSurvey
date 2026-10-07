namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns journal workspace fan-out, ordering, bootstrap/live policy and per-tick handoffs.</summary>
internal sealed class JournalProjection(IJournalProjectionAdapter adapter)
{
    private readonly IJournalProjectionAdapter adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private static readonly JournalProjectionOperation[] IdleOperations =
    [
        JournalProjectionOperation.MiningIdle,
        JournalProjectionOperation.ColonizationIdle,
        JournalProjectionOperation.IdleHousekeeping,
    ];

    private static readonly JournalProjectionOperation[] SessionOperations =
    [
        JournalProjectionOperation.Timeline,
        JournalProjectionOperation.SessionBaseline,
        JournalProjectionOperation.JournalInspector,
        JournalProjectionOperation.SessionState,
        JournalProjectionOperation.Firegroups,
        JournalProjectionOperation.BoardedVehicle,
        JournalProjectionOperation.MusicTrack,
        JournalProjectionOperation.CommanderChange,
    ];

    private static readonly JournalProjectionOperation[] CargoAndStatusOperations =
    [
        JournalProjectionOperation.CargoInventory,
        JournalProjectionOperation.MiningWorkspace,
        JournalProjectionOperation.ShipLocker,
        JournalProjectionOperation.FrontierInventory,
        JournalProjectionOperation.DockToDock,
        JournalProjectionOperation.DesktopBehavior,
        JournalProjectionOperation.StatusConsumers,
        JournalProjectionOperation.GroundTargetEvents,
        JournalProjectionOperation.GreenGasGiant,
        JournalProjectionOperation.FrontierJournal,
        JournalProjectionOperation.OverlayBehavior,
        JournalProjectionOperation.PostProcessorCommander,
    ];

    private static readonly JournalProjectionOperation[] CommanderContextOperations =
    [
        JournalProjectionOperation.CommanderCodex,
        JournalProjectionOperation.ColonizationContext,
        JournalProjectionOperation.SearchContext,
        JournalProjectionOperation.MiningSystem,
        JournalProjectionOperation.MineMapSystem,
        JournalProjectionOperation.NearestSystems,
        JournalProjectionOperation.CodexBingo,
        JournalProjectionOperation.SystemNotes,
        JournalProjectionOperation.BoxelContext,
        JournalProjectionOperation.GuardianContext,
        JournalProjectionOperation.HumanSiteContext,
        JournalProjectionOperation.StationInfoContext,
        JournalProjectionOperation.CommanderProfile,
        JournalProjectionOperation.Quests,
        JournalProjectionOperation.ColonizationCommander,
        JournalProjectionOperation.ColonizationProjects,
        JournalProjectionOperation.Journey,
    ];

    private static readonly JournalProjectionOperation[] NavigationOperations =
    [
        JournalProjectionOperation.RouteContext,
        JournalProjectionOperation.RouteManagerContext,
        JournalProjectionOperation.FleetCarrierRouteContext,
        JournalProjectionOperation.FleetCarrierManagerContext,
        JournalProjectionOperation.RouteAutoCopy,
        JournalProjectionOperation.RouteEvents,
        JournalProjectionOperation.FleetCarrierEvents,
        JournalProjectionOperation.ExplorationBaseline,
        JournalProjectionOperation.BoxelRoute,
        JournalProjectionOperation.SearchNavigation,
        JournalProjectionOperation.BoxelEvents,
        JournalProjectionOperation.JournalNotifications,
        JournalProjectionOperation.Pulse,
        JournalProjectionOperation.BoxelNotifications,
    ];

    private static readonly JournalProjectionOperation[] ActivityOperations =
    [
        JournalProjectionOperation.GuardianEvents,
        JournalProjectionOperation.GuardianCargo,
        JournalProjectionOperation.ColonizationCargo,
        JournalProjectionOperation.ColonizationMarket,
        JournalProjectionOperation.BuildProjects,
        JournalProjectionOperation.Combat,
        JournalProjectionOperation.GuardianStatus,
        JournalProjectionOperation.StationInfoStatus,
        JournalProjectionOperation.NavigationStatus,
        JournalProjectionOperation.HumanSite,
        JournalProjectionOperation.DesktopCommands,
        JournalProjectionOperation.Screenshots,
    ];

    private static readonly JournalProjectionOperation[] ExplorationOperations =
    [
        JournalProjectionOperation.JumpInfo,
        JournalProjectionOperation.GalaxyMap,
        JournalProjectionOperation.ExplorationEvents,
        JournalProjectionOperation.ExplorationPersistence,
    ];

    private static readonly JournalProjectionOperation[] SurveyOperations =
    [
        JournalProjectionOperation.SystemSurvey,
        JournalProjectionOperation.SystemVisit,
        JournalProjectionOperation.SystemHistory,
        JournalProjectionOperation.BoxelStats,
        JournalProjectionOperation.BodyData,
        JournalProjectionOperation.FirstFootfallCommands,
        JournalProjectionOperation.FirstFootfallInference,
        JournalProjectionOperation.ExobiologyVersion,
        JournalProjectionOperation.SystemScan,
        JournalProjectionOperation.SurveyCodex,
        JournalProjectionOperation.BiologyCodex,
        JournalProjectionOperation.SurfaceTracking,
        JournalProjectionOperation.ExobiologyPersistence,
        JournalProjectionOperation.ExobiologyDisplay,
    ];

    private static readonly JournalProjectionOperation[] ShellOperations =
    [
        JournalProjectionOperation.MonitorStatus,
        JournalProjectionOperation.ExternalPublication,
        JournalProjectionOperation.Shutdown,
    ];

    private static readonly JournalProjectionOperation[][] FullStages =
    [
        SessionOperations,
        CargoAndStatusOperations,
        CommanderContextOperations,
        NavigationOperations,
        ActivityOperations,
        ExplorationOperations,
        SurveyOperations,
        ShellOperations,
    ];

    internal async Task ApplyAsync(JournalProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tick = new JournalProjectionTick(context);
        if (context.IsIdle)
        {
            await ApplyStageAsync(IdleOperations, context, tick);
            context.CancellationToken.ThrowIfCancellationRequested();
            return;
        }

        foreach (JournalProjectionOperation[] stage in FullStages)
        {
            await ApplyStageAsync(stage, context, tick);
        }
        context.CancellationToken.ThrowIfCancellationRequested();
    }

    private async Task ApplyStageAsync(
        JournalProjectionOperation[] operations,
        JournalProjectionContext context,
        JournalProjectionTick tick
    )
    {
        foreach (JournalProjectionOperation operation in operations)
        {
            ObserveCancellation(context, tick);
            if (!ShouldApply(operation, tick))
            {
                continue;
            }
            JournalProjectionOperation selected =
                operation == JournalProjectionOperation.FleetCarrierEvents && !tick.AllowLiveEffects
                    ? JournalProjectionOperation.FleetCarrierBootstrapEvents
                    : operation;
            try
            {
                await adapter.ApplyAsync(selected, context, tick);
            }
            catch (OperationCanceledException)
                when (!context.IsIdle && context.CancellationToken.IsCancellationRequested)
            {
                // A canceled optional await must not discard the monitor batch before its
                // exploration/exobiology reducers and required persistence have observed it.
                ObserveCancellation(context, tick);
            }
            if (operation == JournalProjectionOperation.CommanderProfile)
            {
                tick.SkipPersistedBootstrapEvents = context.IsBootstrapRead && tick.LoadedExistingProfile;
            }
        }
    }

    private static void ObserveCancellation(JournalProjectionContext context, JournalProjectionTick tick)
    {
        if (!context.CancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (context.IsIdle)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
        }
        // The monitor has consumed the events. Persist their local progress before returning
        // cancellation, while suppressing additional live effects during the remaining projection.
        tick.AllowLiveEffects = false;
        tick.IsCancellationDrain = true;
        tick.WorkCancellationToken = CancellationToken.None;
    }

    private static bool ShouldApply(JournalProjectionOperation operation, JournalProjectionTick tick) =>
        operation switch
        {
            JournalProjectionOperation.RouteEvents
            or JournalProjectionOperation.DesktopCommands
            or JournalProjectionOperation.Screenshots
            or JournalProjectionOperation.FirstFootfallCommands
            or JournalProjectionOperation.FirstFootfallInference
            or JournalProjectionOperation.BiologyCodex => tick.AllowLiveEffects,
            JournalProjectionOperation.RouteAutoCopy
            or JournalProjectionOperation.ColonizationMarket
            or JournalProjectionOperation.BodyData
            or JournalProjectionOperation.SurveyCodex
            or JournalProjectionOperation.ExternalPublication
            or JournalProjectionOperation.Shutdown => !tick.IsCancellationDrain,
            JournalProjectionOperation.BoxelEvents => !tick.SkipPersistedBootstrapEvents,
            _ => true,
        };
}

/// <summary>Internal seam: production translates operations; tests record the actual module-owned plan.</summary>
internal interface IJournalProjectionAdapter
{
    Task ApplyAsync(JournalProjectionOperation operation, JournalProjectionContext context, JournalProjectionTick tick);
}

internal enum JournalProjectionOperation
{
    MiningIdle,
    ColonizationIdle,
    IdleHousekeeping,
    Timeline,
    SessionBaseline,
    JournalInspector,
    SessionState,
    Firegroups,
    BoardedVehicle,
    MusicTrack,
    CommanderChange,
    CargoInventory,
    MiningWorkspace,
    ShipLocker,
    FrontierInventory,
    DockToDock,
    DesktopBehavior,
    StatusConsumers,
    GroundTargetEvents,
    GreenGasGiant,
    FrontierJournal,
    OverlayBehavior,
    PostProcessorCommander,
    CommanderCodex,
    ColonizationContext,
    SearchContext,
    MiningSystem,
    MineMapSystem,
    NearestSystems,
    CodexBingo,
    SystemNotes,
    BoxelContext,
    GuardianContext,
    HumanSiteContext,
    StationInfoContext,
    CommanderProfile,
    Quests,
    ColonizationCommander,
    ColonizationProjects,
    Journey,
    RouteContext,
    RouteManagerContext,
    FleetCarrierRouteContext,
    FleetCarrierManagerContext,
    RouteAutoCopy,
    RouteEvents,
    FleetCarrierEvents,
    FleetCarrierBootstrapEvents,
    ExplorationBaseline,
    BoxelRoute,
    SearchNavigation,
    BoxelEvents,
    JournalNotifications,
    Pulse,
    BoxelNotifications,
    GuardianEvents,
    GuardianCargo,
    ColonizationCargo,
    ColonizationMarket,
    BuildProjects,
    Combat,
    GuardianStatus,
    StationInfoStatus,
    NavigationStatus,
    HumanSite,
    DesktopCommands,
    Screenshots,
    JumpInfo,
    GalaxyMap,
    ExplorationEvents,
    ExplorationPersistence,
    SystemSurvey,
    SystemVisit,
    SystemHistory,
    BoxelStats,
    BodyData,
    FirstFootfallCommands,
    FirstFootfallInference,
    ExobiologyVersion,
    SystemScan,
    SurveyCodex,
    BiologyCodex,
    SurfaceTracking,
    ExobiologyPersistence,
    ExobiologyDisplay,
    MonitorStatus,
    ExternalPublication,
    Shutdown,
}
