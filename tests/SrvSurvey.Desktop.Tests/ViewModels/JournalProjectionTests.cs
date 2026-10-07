using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class JournalProjectionTests
{
    [Fact]
    public async Task LiveTickProjectsEveryWorkspaceInTheProductionPlanOrder()
    {
        var adapter = new RecordingAdapter();
        var projection = new JournalProjection(adapter);

        await projection.ApplyAsync(Context(LiveChange()));

        Assert.Equal(FullPlan, adapter.Operations);
        Assert.Single(adapter.Ticks);
    }

    [Fact]
    public async Task IdlePollServicesMiningAndRetainedColonizationWorkBeforeHousekeeping()
    {
        var adapter = new RecordingAdapter();
        var projection = new JournalProjection(adapter);

        await projection.ApplyAsync(Context(Unchanged()));

        Assert.Equal(
            [
                JournalProjectionOperation.MiningIdle,
                JournalProjectionOperation.ColonizationIdle,
                JournalProjectionOperation.IdleHousekeeping,
            ],
            adapter.Operations
        );
    }

    [Fact]
    public async Task ManualRefreshWithoutNewEventsProjectsTheFullWorkspaceGraph()
    {
        var adapter = new RecordingAdapter();
        var projection = new JournalProjection(adapter);

        await projection.ApplyAsync(Context(Unchanged(), isManualRefresh: true));

        Assert.Equal(FullPlan, adapter.Operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapRestoresContextWithoutRunningLiveCommandsOrRepeatingPersistedMutations(
        bool loadedProfile
    )
    {
        var adapter = new RecordingAdapter
        {
            Observe = (operation, _, tick) =>
            {
                if (operation == JournalProjectionOperation.CommanderProfile)
                {
                    tick.LoadedExistingProfile = loadedProfile;
                }
            },
        };
        var projection = new JournalProjection(adapter);
        JournalProjectionContext context = Context(BootstrapRead());

        await projection.ApplyAsync(context);

        Assert.False(context.IsLive);
        Assert.Equal(
            FullPlan
                .Where(operation =>
                    !LiveOnly.Contains(operation)
                    && (!loadedProfile || operation != JournalProjectionOperation.BoxelEvents)
                )
                .Select(operation =>
                    operation == JournalProjectionOperation.FleetCarrierEvents
                        ? JournalProjectionOperation.FleetCarrierBootstrapEvents
                        : operation
                ),
            adapter.Operations
        );
        JournalProjectionTick tick = Assert.Single(adapter.Ticks);
        Assert.Equal(loadedProfile, tick.SkipPersistedBootstrapEvents);
        Assert.Contains(JournalProjectionOperation.ExplorationEvents, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.SurfaceTracking, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExternalPublication, adapter.Operations);
        Assert.False(tick.RequestShutdown);
        Assert.False(tick.AllowLiveEffects);
        Assert.Contains(JournalProjectionOperation.FleetCarrierBootstrapEvents, adapter.Operations);
    }

    [Fact]
    public async Task PersistedProfileDoesNotSuppressNewLiveJournalMutations()
    {
        var adapter = new RecordingAdapter
        {
            Observe = (operation, _, tick) =>
            {
                if (operation == JournalProjectionOperation.CommanderProfile)
                {
                    tick.LoadedExistingProfile = true;
                }
                if (operation == JournalProjectionOperation.BoxelEvents)
                {
                    Assert.False(tick.SkipPersistedBootstrapEvents);
                }
            },
        };

        await new JournalProjection(adapter).ApplyAsync(Context(LiveChange()));

        Assert.Equal(FullPlan, adapter.Operations);
        Assert.False(Assert.Single(adapter.Ticks).SkipPersistedBootstrapEvents);
    }

    [Fact]
    public async Task WorkspaceResultsReachDependentOperationsOfTheSameTickOnly()
    {
        int ticks = 0;
        var observedCargo = new List<bool>();
        var observedDeaths = new List<string[]>();
        var observedShutdown = new List<bool>();
        var adapter = new RecordingAdapter
        {
            Observe = (operation, _, tick) =>
            {
                switch (operation)
                {
                    case JournalProjectionOperation.SessionBaseline:
                        ticks++;
                        break;
                    case JournalProjectionOperation.CargoInventory:
                        tick.AllowSharedCargo = ticks == 1;
                        tick.CargoChanged = ticks == 1;
                        break;
                    case JournalProjectionOperation.ColonizationProjects:
                        observedCargo.Add(tick.AllowSharedCargo && tick.CargoChanged);
                        break;
                    case JournalProjectionOperation.DesktopCommands:
                        tick.RequestShutdown = ticks == 1;
                        break;
                    case JournalProjectionOperation.ExplorationEvents:
                        if (ticks == 1)
                        {
                            tick.ScansLostToDeath.Add("biology-1");
                        }
                        break;
                    case JournalProjectionOperation.SurfaceTracking:
                        observedDeaths.Add([.. tick.ScansLostToDeath]);
                        break;
                    case JournalProjectionOperation.Shutdown:
                        observedShutdown.Add(tick.RequestShutdown);
                        break;
                }
            },
        };
        var projection = new JournalProjection(adapter);

        await projection.ApplyAsync(Context(LiveChange()));
        await projection.ApplyAsync(Context(LiveChange()));

        Assert.Equal([true, false], observedCargo);
        Assert.Equal(new string[][] { ["biology-1"], [] }, observedDeaths);
        Assert.Equal([true, false], observedShutdown);
        Assert.Equal(2, adapter.Ticks.Count);
    }

    [Fact]
    public async Task AsynchronousCommanderLoadingFinishesBeforeDependentWorkspacesStart()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new RecordingAdapter
        {
            Delay = operation =>
                operation == JournalProjectionOperation.CommanderProfile ? gate.Task : Task.CompletedTask,
        };
        var projection = new JournalProjection(adapter);

        Task tick = projection.ApplyAsync(Context(LiveChange()));

        Assert.False(tick.IsCompleted);
        Assert.Equal(JournalProjectionOperation.CommanderProfile, adapter.Operations[^1]);
        Assert.DoesNotContain(JournalProjectionOperation.Quests, adapter.Operations);
        gate.SetResult();
        await tick;
        Assert.Equal(FullPlan, adapter.Operations);
    }

    [Theory]
    [InlineData((int)JournalProjectionOperation.CommanderCodex)]
    [InlineData((int)JournalProjectionOperation.SurfaceTracking)]
    [InlineData((int)JournalProjectionOperation.ExternalPublication)]
    public async Task FirstWorkspaceFailureStopsFanOutAndPropagatesTheSameFailure(int failingOperationValue)
    {
        var failingOperation = (JournalProjectionOperation)failingOperationValue;
        var failure = new IOException("workspace unavailable");
        var adapter = new RecordingAdapter
        {
            Delay = operation => operation == failingOperation ? Task.FromException(failure) : Task.CompletedTask,
        };
        var projection = new JournalProjection(adapter);

        IOException observed = await Assert.ThrowsAsync<IOException>(() =>
            projection.ApplyAsync(Context(LiveChange()))
        );

        Assert.Same(failure, observed);
        Assert.Equal(FullPlan.Take(Array.IndexOf(FullPlan, failingOperation) + 1), adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.Shutdown, adapter.Operations);
    }

    [Fact]
    public async Task CancellationBeforeAnIdleTickRunsNoWorkspaces()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var adapter = new RecordingAdapter();

        OperationCanceledException observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JournalProjection(adapter).ApplyAsync(Context(Unchanged(), cancellationToken: cancellation.Token))
        );

        Assert.Equal(cancellation.Token, observed.CancellationToken);
        Assert.Empty(adapter.Operations);
    }

    [Fact]
    public async Task AlreadyConsumedFullTickPersistsEvenWhenSubmittedAfterCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var adapter = new RecordingAdapter();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JournalProjection(adapter).ApplyAsync(Context(LiveChange(), cancellationToken: cancellation.Token))
        );

        Assert.Contains(JournalProjectionOperation.SessionState, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExplorationEvents, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExplorationPersistence, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExobiologyPersistence, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.FirstFootfallInference, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.ExternalPublication, adapter.Operations);
        Assert.All(adapter.Ticks, tick => Assert.Equal(CancellationToken.None, tick.WorkCancellationToken));
    }

    [Fact]
    public async Task RequestCancellationFromAnEarlyAwaitDrainsTheConsumedBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var adapter = new RecordingAdapter
        {
            Delay = operation =>
            {
                if (operation != JournalProjectionOperation.ColonizationProjects)
                {
                    return Task.CompletedTask;
                }
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JournalProjection(adapter).ApplyAsync(Context(LiveChange(), cancellationToken: cancellation.Token))
        );

        Assert.Contains(JournalProjectionOperation.ExplorationEvents, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExplorationPersistence, adapter.Operations);
        Assert.Contains(JournalProjectionOperation.ExobiologyPersistence, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.FirstFootfallInference, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.ExternalPublication, adapter.Operations);
    }

    [Fact]
    public async Task CancellationFromAnotherTokenRemainsAWorkspaceFailure()
    {
        using var otherCancellation = new CancellationTokenSource();
        await otherCancellation.CancelAsync();
        var adapter = new RecordingAdapter
        {
            Delay = operation =>
                operation == JournalProjectionOperation.ColonizationProjects
                    ? Task.FromCanceled(otherCancellation.Token)
                    : Task.CompletedTask,
        };

        OperationCanceledException observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JournalProjection(adapter).ApplyAsync(Context(LiveChange()))
        );

        Assert.Equal(otherCancellation.Token, observed.CancellationToken);
        Assert.Equal(JournalProjectionOperation.ColonizationProjects, adapter.Operations[^1]);
        Assert.DoesNotContain(JournalProjectionOperation.ExplorationPersistence, adapter.Operations);
    }

    [Theory]
    [InlineData((int)JournalProjectionOperation.Timeline)]
    [InlineData((int)JournalProjectionOperation.SessionState)]
    [InlineData((int)JournalProjectionOperation.CommanderCodex)]
    [InlineData((int)JournalProjectionOperation.GuardianEvents)]
    [InlineData((int)JournalProjectionOperation.ExplorationEvents)]
    [InlineData((int)JournalProjectionOperation.ExplorationPersistence)]
    [InlineData((int)JournalProjectionOperation.SystemHistory)]
    public async Task CancellationAnywhereInAConsumedTickDrainsPersistenceAndStopsLaterLiveEffects(int cancelAtValue)
    {
        using var cancellation = new CancellationTokenSource();
        var cancelAt = (JournalProjectionOperation)cancelAtValue;
        bool explorationSaved = false;
        bool exobiologySaved = false;
        var adapter = new RecordingAdapter
        {
            Observe = (operation, _, tick) =>
            {
                if (operation == cancelAt)
                {
                    cancellation.Cancel();
                }
                if (operation == JournalProjectionOperation.ExplorationPersistence)
                {
                    explorationSaved = true;
                }
                if (operation == JournalProjectionOperation.ExobiologyPersistence)
                {
                    exobiologySaved = true;
                }
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new JournalProjection(adapter).ApplyAsync(Context(LiveChange(), cancellationToken: cancellation.Token))
        );

        Assert.True(explorationSaved);
        Assert.True(exobiologySaved);
        Assert.Equal(JournalProjectionOperation.MonitorStatus, adapter.Operations[^1]);
        Assert.DoesNotContain(JournalProjectionOperation.FirstFootfallCommands, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.FirstFootfallInference, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.BiologyCodex, adapter.Operations);
        Assert.DoesNotContain(JournalProjectionOperation.ExternalPublication, adapter.Operations);
        JournalProjectionTick tick = Assert.Single(adapter.Ticks);
        Assert.False(tick.AllowLiveEffects);
        Assert.True(tick.IsCancellationDrain);
        Assert.Equal(CancellationToken.None, tick.WorkCancellationToken);
    }

    [Fact]
    public async Task ProjectionRejectsMissingAdapterOrContext()
    {
        Assert.Throws<ArgumentNullException>(() => new JournalProjection(null!));
        var projection = new JournalProjection(new RecordingAdapter());
        await Assert.ThrowsAsync<ArgumentNullException>(() => projection.ApplyAsync(null!));
    }

    private static JournalProjectionContext Context(
        JournalMonitorUpdate update,
        bool isManualRefresh = false,
        CancellationToken cancellationToken = default
    ) => new(update, isManualRefresh, cancellationToken);

    private static JournalMonitorUpdate Unchanged() =>
        new(null, [], null, null, null, null, [], IsBootstrapRead: false);

    private static JournalMonitorUpdate LiveChange() => Unchanged() with { SessionContextChanged = true };

    private static JournalMonitorUpdate BootstrapRead() => Unchanged() with { IsBootstrapRead = true };

    private static readonly JournalProjectionOperation[] LiveOnly =
    [
        JournalProjectionOperation.RouteEvents,
        JournalProjectionOperation.DesktopCommands,
        JournalProjectionOperation.Screenshots,
        JournalProjectionOperation.FirstFootfallCommands,
        JournalProjectionOperation.FirstFootfallInference,
        JournalProjectionOperation.BiologyCodex,
    ];

    // This is the actual journal contract: every workspace observes all earlier reducers/persistence,
    // and publication/shutdown happen after the local graph has finished.
    private static readonly JournalProjectionOperation[] FullPlan =
    [
        JournalProjectionOperation.Timeline,
        JournalProjectionOperation.SessionBaseline,
        JournalProjectionOperation.JournalInspector,
        JournalProjectionOperation.SessionState,
        JournalProjectionOperation.Firegroups,
        JournalProjectionOperation.BoardedVehicle,
        JournalProjectionOperation.MusicTrack,
        JournalProjectionOperation.CommanderChange,
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
        JournalProjectionOperation.JumpInfo,
        JournalProjectionOperation.GalaxyMap,
        JournalProjectionOperation.ExplorationEvents,
        JournalProjectionOperation.ExplorationPersistence,
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
        JournalProjectionOperation.MonitorStatus,
        JournalProjectionOperation.ExternalPublication,
        JournalProjectionOperation.Shutdown,
    ];

    private sealed class RecordingAdapter : IJournalProjectionAdapter
    {
        public List<JournalProjectionOperation> Operations { get; } = [];
        public HashSet<JournalProjectionTick> Ticks { get; } = [];
        public Action<
            JournalProjectionOperation,
            JournalProjectionContext,
            JournalProjectionTick
        >? Observe { get; init; }
        public Func<JournalProjectionOperation, Task>? Delay { get; init; }

        public async Task ApplyAsync(
            JournalProjectionOperation operation,
            JournalProjectionContext context,
            JournalProjectionTick tick
        )
        {
            Operations.Add(operation);
            Ticks.Add(tick);
            Observe?.Invoke(operation, context, tick);
            if (Delay is not null)
            {
                await Delay(operation);
            }
        }
    }
}
