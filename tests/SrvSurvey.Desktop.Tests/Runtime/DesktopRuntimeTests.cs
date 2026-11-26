using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ProfileSync;
using SrvSurvey.Desktop.Runtime;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.Tests.ProfileSync;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Runtime;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DesktopRuntimeTests
{
    /// <summary>Observes the composed runtime before overlay construction and isolates colonization priority from other gates.</summary>
    [AvaloniaFact]
    public async Task StartupAppliesColonizationPriorityBeforeOverlayConstruction()
    {
        using var temp = new ProfileSyncTestRoot();
        var store = new CombatSettingsStore(temp.Paths.UiSettingsPath);
        store.Save(store.Load() with { SuppressForActiveBuildProjects = true });
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        OverlayWindowRegistry registry = OverlayWindowRegistry.Shared;
        registry.SetPriorityFacts(OverlayPriorityFacts.None);
        bool? combatPresented = null;
        bool? combatWithoutColony = null;
        await using var runtime = DesktopRuntime.StartCompositionForTests(
            Application.Current!,
            desktop,
            new DesktopStartup([], new ApplicationLogService(temp.Paths.DataDirectory))
            {
                AppDataPathsOverride = temp.Paths,
            },
            new RecordingDesktopLifetime([]),
            DesktopStartupCheckpoint.MainWindowReady,
            (_, _, checkpoint) =>
            {
                if (checkpoint != DesktopStartupCheckpoint.MainWindowReady)
                {
                    return;
                }
                // Isolate the priority decision from the independent journal-readiness gate.
                registry.SetGlobalSuppression(false, false, false);
                registry.SetVehicleExcludedPlotters([]);
                var combat = new Window();
                var colony = new Window();
                try
                {
                    registry.Register(combat, "PlotMassacre");
                    registry.SetPresentationVisual(combat, new Border());
                    combatWithoutColony = registry.ShouldPresent(combat);
                    registry.Register(colony, "PlotBuildCommodities");
                    registry.SetPresentationVisual(colony, new Border());
                    combatPresented = registry.ShouldPresent(combat);
                }
                finally
                {
                    combat.Close();
                    colony.Close();
                }
            }
        );
        await runtime.RequestShutdownAsync(DesktopShutdownReason.StartupFailure);
        Assert.True(combatWithoutColony);
        Assert.False(combatPresented);
    }

    /// <summary>Sends a resolved shortcut through the real runtime and verifies its saved colonization preference.</summary>
    [AvaloniaFact]
    public async Task ComposedRuntimeDispatchesTheCoveredGroupsShortcutAndPersistsIt()
    {
        using var temp = new ProfileSyncTestRoot();
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        Task<bool>? dispatched = null;
        await using var runtime = DesktopRuntime.StartCompositionForTests(
            Application.Current!,
            desktop,
            new DesktopStartup([], new ApplicationLogService(temp.Paths.DataDirectory))
            {
                AppDataPathsOverride = temp.Paths,
            },
            new RecordingDesktopLifetime([]),
            DesktopStartupCheckpoint.OverlayDependentsReady,
            (instance, window, checkpoint) =>
            {
                if (
                    checkpoint == DesktopStartupCheckpoint.OverlayDependentsReady
                    && window?.DataContext is MainWindowViewModel main
                )
                {
                    main.Colonization.CollapseCoveredCommodityGroups = false;
                    dispatched = instance.ExecuteGlobalInputActionAsync(
                        GlobalInputAction.ToggleCollapseCoveredColonyGroups
                    );
                }
            }
        );
        Assert.NotNull(dispatched);
        Assert.True(await dispatched);
        Assert.True(
            new ColonizationSettingsStore(temp.Paths.UiSettingsPath).LoadOverlayPreferences().CollapseCoveredGroups
        );
        await runtime.RequestShutdownAsync(DesktopShutdownReason.StartupFailure);
    }

    [AvaloniaFact]
    public void UnrelatedShortcutsRemainAvailableToTheirWorkspace()
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        using var controller = new DesktopOverlayController(main, new OverlayWindowRegistry());
        Assert.Null(controller.TryExecute(GlobalInputAction.MapZoomIn));
    }

    [AvaloniaFact]
    public void RetiredControllerCannotOverrideTheActiveSessionsOverlayPreferences()
    {
        using MainWindowViewModel retired = MainWindowViewModelTestBuilder.Create(null, _ => { });
        using MainWindowViewModel active = MainWindowViewModelTestBuilder.Create(null, _ => { });
        retired.OverlayBehavior.UpdateSessionContext(true, true, false, false);
        active.OverlayBehavior.UpdateSessionContext(true, true, false, false);
        retired.Combat.SuppressForActiveBuildProjects = false;
        active.Combat.SuppressForActiveBuildProjects = false;
        var registry = new OverlayWindowRegistry();
        using var oldController = new DesktopOverlayController(retired, registry);
        oldController.Dispose();
        using var controller = new DesktopOverlayController(active, registry);
        var colony = new Window();
        var combat = new Window();
        try
        {
            registry.Register(colony, "PlotBuildCommodities");
            registry.SetPresentationVisual(colony, new Border());
            registry.Register(combat, "PlotMassacre");
            registry.SetPresentationVisual(combat, new Border());
            Assert.True(registry.ShouldPresent(combat));
            retired.Combat.SuppressForActiveBuildProjects = true;
            Assert.True(registry.ShouldPresent(combat));
            retired.OverlayBehavior.UpdateSessionContext(true, true, true, false);
            Assert.True(registry.ShouldPresent(combat));
            active.Combat.SuppressForActiveBuildProjects = true;
            Assert.False(registry.ShouldPresent(combat));
        }
        finally
        {
            colony.Close();
            combat.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("PlotFootCombat")]
    [InlineData("PlotMassacre")]
    public void CombatSuppressionPreferenceAppliesOnlyWhileTheColonyPanelIsPresented(string plotter)
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        main.OverlayBehavior.UpdateSessionContext(true, true, false, false);
        var registry = new OverlayWindowRegistry();
        using var controller = new DesktopOverlayController(main, registry);
        var colony = new Window();
        var combat = new Window();
        try
        {
            main.Combat.SuppressForActiveBuildProjects = true;
            registry.Register(combat, plotter);
            registry.SetPresentationVisual(combat, new Border());
            registry.Register(colony, "PlotBuildCommodities");
            registry.SetVehicleExcludedPlotters(["PlotBuildCommodities"]);
            Assert.True(registry.ShouldPresent(combat));
            registry.SetPresentationVisual(colony, new Border());
            Assert.False(registry.ShouldPresent(colony));
            Assert.True(registry.ShouldPresent(combat));
            registry.SetVehicleExcludedPlotters([]);
            Assert.True(registry.ShouldPresent(colony), registry.GetDecision(colony).Reasons.ToString());
            Assert.False(registry.ShouldPresent(combat));
            registry.SetPresentationVisible(colony, false);
            Assert.True(registry.ShouldPresent(combat));
            registry.SetPresentationVisible(colony, true);
            Assert.False(registry.ShouldPresent(combat));
            main.Combat.SuppressForActiveBuildProjects = false;
            Assert.True(registry.ShouldPresent(combat));
        }
        finally
        {
            colony.Close();
            combat.Close();
            registry.SetPriorityFacts(default);
            registry.SetVehicleExcludedPlotters([]);
        }
    }

    [AvaloniaTheory]
    [InlineData(OdysseySuitType.Dominator)]
    [InlineData(OdysseySuitType.Maverick)]
    public void SuitSuppressionKeepsCombatCoordinatorEligibleWhileManualAndSessionSuppressionStillApply(
        OdysseySuitType suit
    )
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var registry = new OverlayWindowRegistry();
        using var overlays = new HostedOverlayTestHarness(registry);
        using var combat = new CombatOverlayCoordinator(main.Combat, overlays.Session);
        using var controller = new DesktopOverlayController(main, registry, combat);
        try
        {
            main.OverlayBehavior.HideInDominatorSuit = true;
            main.OverlayBehavior.HideInMaverickSuit = true;
            main.OverlayBehavior.UpdateContext(suit, onFoot: true);
            main.OverlayBehavior.UpdateSessionContext(true, true, false, false);
            Assert.True(main.OverlayBehavior.ShouldSuppressForSuit);
            Assert.False(combat.IsSuppressed);
            Assert.False(registry.ShouldHost("PlotJumpInfo"));
            Assert.True(registry.ShouldHost("PlotFootCombat"));

            Assert.True(controller.TryExecute(GlobalInputAction.ToggleAllVisibility));
            Assert.True(combat.IsSuppressed);
            Assert.True(controller.TryExecute(GlobalInputAction.ToggleAllVisibility));
            main.OverlayBehavior.UpdateSessionContext(false, true, false, false);
            Assert.True(combat.IsSuppressed);
        }
        finally
        {
            OverlayWindowRegistry.Shared.SetGlobalSuppression(false, false, false);
        }
    }

    /// <summary>The app always passes resolved paths; doing so must still initialize an automatic commander's private settings.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalStartupWithResolvedPathsStillSelectsCommanderSettings(bool selectBeforeComposition)
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.2026-10-15T010000.01.log"),
            """{"event":"Commander","Name":"First","FID":"F123"}"""
        );
        AppDataPaths paths = temp.Paths;
        new OverlayScaleSettingsStore(paths.SharedUiSettingsPath).Save(new(OverlayScaleCatalog.GetIndex(65)));
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext context = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => paths,
            CancellationToken.None
        );
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var applicationLog = new ApplicationLogService(paths.DataDirectory);
        await using var runtime = DesktopRuntime.StartCompositionForTests(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = selectBeforeComposition ? context.AppDataPaths : paths,
            },
            new RecordingDesktopLifetime([]),
            DesktopStartupCheckpoint.OverlayInfrastructureReady
        );
        await runtime.RequestShutdownAsync(DesktopShutdownReason.StartupFailure);

        AppDataPaths commander = paths with { SettingsFrontierId = "F123" };
        Assert.True(File.Exists(Path.Combine(commander.OverlaySettingsDirectory, ".profile-initialized")));
        Assert.Equal(
            OverlayScaleCatalog.GetIndex(65),
            new OverlayScaleSettingsStore(commander.UiSettingsPath).Load().Index
        );
    }

    /// <summary>Both colonization actions show a hidden available panel on their first press and keep checkbox state synchronized.</summary>
    [AvaloniaTheory]
    [InlineData(GlobalInputAction.ToggleColonizationCommoditiesVisibility)]
    [InlineData(GlobalInputAction.ShowColonyShopping)]
    public void ColonizationShortcutsToggleActualVisibilityAndPersistThroughDocking(GlobalInputAction action)
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        main.OverlayBehavior.UpdateSessionContext(true, true, false, false);
        var window = new MainWindow(main);
        using var unavailableController = new DesktopOverlayController(main, OverlayWindowRegistry.Shared);
        try
        {
            Assert.False(unavailableController.TryExecute(action));
            ColonizationCommodityOverlayViewModel overlay = main.Colonization.CommodityOverlay;
            Assert.Same(overlay, main.OverlayInteraction.ColonizationOverlay);
            var plan = new ColonizationCommodityPlan
            {
                Title = "Test build",
                ProjectNames = ["Test build"],
                FleetCarriers = [],
                Rows =
                [
                    new ColonizationCommodityPlanRow
                    {
                        Commodity = "steel",
                        DisplayName = "Steel",
                        Category = "Metals",
                        Needed = 100,
                    },
                ],
            };
            var shipStatus = new EliteStatus { Flags = StatusFlags.InMainShip };
            var journal = new JournalSessionState();
            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"event":"LoadGame","Ship":"python"}""",
                    out JournalEventEnvelope? loadGame,
                    out _
                )
            );
            journal.Apply(loadGame!);
            main.OverlayExceptions.UpdateBoardedVehicle(journal, shipStatus);
            overlay.Apply(plan, shipStatus);
            using var host = new HostedOverlayTestHarness(OverlayWindowRegistry.Shared);
            using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
            using var controller = new DesktopOverlayController(
                main,
                OverlayWindowRegistry.Shared,
                colony: coordinator
            );
            OverlayPanelVisibilityEntryViewModel panel = main.OverlayPanelVisibility.Panels.Single(item =>
                item.PlotterName == "PlotBuildCommodities"
            );
            Assert.True(panel.IsEnabled);
            Assert.False(coordinator.IsVisible);

            Assert.True(controller.TryExecute(action));
            Assert.True(coordinator.IsVisible);
            overlay.Apply(
                plan,
                new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
                updatedHasMarketSinceDocking: true
            );
            overlay.Apply(plan, new EliteStatus { Flags = StatusFlags.InMainShip });
            Assert.True(coordinator.IsVisible);

            Assert.True(controller.TryExecute(action));
            Assert.False(panel.IsEnabled);
            Assert.False(coordinator.IsVisible);
            Assert.True(controller.TryExecute(action));
            Assert.True(panel.IsEnabled);
            Assert.True(coordinator.IsVisible);
        }
        finally
        {
            main.OverlayPanelVisibility.EnsureVisible("PlotBuildCommodities");
            window.Close();
        }
    }

    /// <summary>The shortcut updates live groups, the bound checkbox preference, and persisted settings on each press.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapseCoveredGroupsShortcutUpdatesAndPersistsDisplayStyle(bool hideCollapsedGroups)
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null);
        var window = new MainWindow(main);
        OverlayWindowRegistry.Shared.SetGlobalSuppression(false, false, false);
        OverlayWindowRegistry.Shared.SetVehicleExcludedPlotters([]);
        using var controller = new DesktopOverlayController(main, new OverlayWindowRegistry());
        Assert.True(OverlayWindowRegistry.Shared.ShouldHost("PlotJumpInfo"));
        ColonizationViewModel colonization = main.Colonization;
        colonization.CollapseCoveredCommodityGroups = false;
        colonization.HideCollapsedCommodityGroups = hideCollapsedGroups;
        colonization.CommodityOverlay.Apply(
            new ColonizationCommodityPlan
            {
                Title = "Test build",
                ProjectNames = ["Test build"],
                FleetCarriers = [new ColonizationFleetCarrier { MarketId = 1, Name = "ABC-123" }],
                Rows =
                [
                    new ColonizationCommodityPlanRow
                    {
                        Commodity = "steel",
                        DisplayName = "Steel",
                        Category = "Metals",
                        Needed = 100,
                        OnFleetCarriers = 100,
                    },
                ],
            },
            new EliteStatus { Flags = StatusFlags.Docked }
        );
        var store = new ColonizationSettingsStore(main.AppDataPaths.UiSettingsPath);
        try
        {
            Assert.False(Assert.Single(colonization.CommodityOverlay.Groups).IsCollapsed);
            InputBindingViewModel binding = main.InputSettings.Bindings.Single(item =>
                item.Definition.Action == GlobalInputAction.ToggleCollapseCoveredColonyGroups
            );
            Assert.Equal("ALT CTRL SHIFT X", binding.Chord);

            Assert.True(controller.TryExecute(binding.Definition.Action));

            Assert.True(colonization.CollapseCoveredCommodityGroups);
            Assert.True(store.LoadOverlayPreferences().CollapseCoveredGroups);
            Assert.Equal(hideCollapsedGroups, colonization.HideCollapsedCommodityGroups);
            Assert.Equal(hideCollapsedGroups, store.LoadOverlayPreferences().HideCollapsedGroups);
            if (hideCollapsedGroups)
            {
                Assert.Empty(colonization.CommodityOverlay.Groups);
            }
            else
            {
                Assert.True(Assert.Single(colonization.CommodityOverlay.Groups).IsCollapsed);
            }

            Assert.True(controller.TryExecute(binding.Definition.Action));

            Assert.False(colonization.CollapseCoveredCommodityGroups);
            Assert.False(store.LoadOverlayPreferences().CollapseCoveredGroups);
            Assert.Equal(hideCollapsedGroups, colonization.HideCollapsedCommodityGroups);
            Assert.Equal(hideCollapsedGroups, store.LoadOverlayPreferences().HideCollapsedGroups);
            Assert.False(Assert.Single(colonization.CommodityOverlay.Groups).IsCollapsed);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task ConcurrentShutdownRequestsShareOneOrderedStop()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        Task first = runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
        Task second = runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);

        Assert.Same(first, second);
        phases.AllowProducerStop.SetResult();
        await first;

        Assert.Equal(
            [
                "quiesce:MainWindowClose",
                "stop-producers",
                "dispose-dependents",
                "dispose-view-model",
                "dispose-infrastructure",
                "shutdown:0",
            ],
            events
        );
    }

    [Fact]
    public async Task ReentrantShutdownRequestJoinsTheStopAlreadyInProgress()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        Task? reentrantRequest = null;
        phases.OnQuiesce = _ => reentrantRequest = runtime.RequestShutdownAsync(DesktopShutdownReason.UpdateHandoff);

        Task first = runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);

        Assert.Same(first, reentrantRequest);
        phases.AllowProducerStop.SetResult();
        await first;
        Assert.Equal(1, events.Count(item => item.StartsWith("quiesce:", StringComparison.Ordinal)));
        Assert.Equal(1, events.Count(item => item == "shutdown:0"));
    }

    [Fact]
    public async Task DisposingRuntimeUsesDedicatedShutdownReason()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        await runtime.DisposeAsync();

        Assert.Equal("quiesce:RuntimeDisposed", events[0]);
        Assert.Equal("shutdown:0", events[^1]);
    }

    [Fact]
    public async Task FailureReportingCannotPreventRemainingShutdownPhases()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events)
        {
            ThrowWhenQuiescing = true,
            ThrowWhenStoppingProducers = true,
            ThrowWhenReportingFailure = true,
        };
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        await runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);

        Assert.Equal(
            [
                "quiesce:MainWindowClose",
                "report:Quiesce failed.",
                "stop-producers",
                "report:Producer stop failed.",
                "dispose-dependents",
                "dispose-view-model",
                "dispose-infrastructure",
                "shutdown:0",
            ],
            events
        );
    }

    [AvaloniaFact]
    public async Task AttachingASecondMainWindowIsRejected()
    {
        List<string> events = [];
        var phases = new RecordingDesktopRuntimePhases(events);
        var runtime = DesktopRuntime.CreateForTests(new RecordingDesktopLifetime(events), phases);
        var first = new Window();
        runtime.AttachMainWindow(first);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            runtime.AttachMainWindow(new Window())
        );

        Assert.Contains("already has a main window", exception.Message, StringComparison.Ordinal);
        phases.AllowProducerStop.SetResult();
        await runtime.DisposeAsync();
    }

    [Fact]
    public async Task StartupFailureIsPreservedWhileAcquiredRuntimeIsRolledBack()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        var expected = new InvalidOperationException("Startup failed.");
        var acquiredResource = new RecordingDisposable(() => events.Add("dispose-startup-resource"));
        phases.OnDisposeViewModel = acquiredResource.Dispose;

        await using var runtime = DesktopRuntime.StartForTests(
            lifetime,
            phases,
            () =>
            {
                events.Add("acquire-startup-resource");
                throw expected;
            }
        );
        await runtime.RequestShutdownAsync(DesktopShutdownReason.StartupFailure);

        Assert.Same(expected, phases.StartupFailure);
        Assert.Equal(
            [
                "acquire-startup-resource",
                "startup-failure:Startup failed.",
                "quiesce:StartupFailure",
                "stop-producers",
                "dispose-dependents",
                "dispose-view-model",
                "dispose-startup-resource",
                "dispose-infrastructure",
                "shutdown:1",
            ],
            events
        );
        Assert.True(acquiredResource.IsDisposed);
    }

    [AvaloniaTheory]
    [InlineData((int)DesktopStartupCheckpoint.OverlayInfrastructureReady)]
    [InlineData((int)DesktopStartupCheckpoint.MainViewModelDependenciesReady)]
    [InlineData((int)DesktopStartupCheckpoint.MainWindowReady)]
    [InlineData((int)DesktopStartupCheckpoint.OverlayDependentsReady)]
    [InlineData((int)DesktopStartupCheckpoint.ProducersReady)]
    public async Task ProductionStartupCheckpointRollsBackAcquiredResources(int checkpointValue)
    {
        var checkpoint = (DesktopStartupCheckpoint)checkpointValue;
        Application application =
            Application.Current ?? throw new InvalidOperationException("The Headless application is unavailable.");
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-runtime-startup-{Guid.NewGuid():N}");
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        try
        {
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []
            );
            var applicationLog = new ApplicationLogService(paths.DataDirectory);
            await using var runtime = DesktopRuntime.StartCompositionForTests(
                application,
                desktop,
                new DesktopStartup([], applicationLog) { AppDataPathsOverride = paths },
                lifetime,
                checkpoint
            );
            await runtime.RequestShutdownAsync(DesktopShutdownReason.StartupFailure);

            Assert.Equal(["shutdown:1"], events);
            Assert.Contains($"Startup failed at {checkpoint}.", applicationLog.Text, StringComparison.Ordinal);
            Assert.Contains("Application exit", applicationLog.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Desktop runtime shutdown failed", applicationLog.Text, StringComparison.Ordinal);
            Assert.Null(desktop.MainWindow);
            Assert.Empty(OverlayWindowRegistry.Shared.Snapshot());
            // Vehicle exceptions can still hide an unknown vessel; runtime suppression must be cleared.
            Assert.True(OverlayWindowRegistry.Shared.ShouldHost("PlotBuildCommodities"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }
    }

    /// <summary>Hides immediately while cleanup runs and rejects repeated close attempts until orderly shutdown.</summary>
    [AvaloniaFact]
    public async Task MainWindowCloseHidesWhileRuntimeCleanupFinishes()
    {
        List<string> events = [];
        var window = new Window();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        var lifetime = new RecordingDesktopLifetime(events, window.Close);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        runtime.AttachMainWindow(window);
        window.Show();

        try
        {
            window.Close();
            Task stopping = runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            Assert.False(window.IsVisible);
            Assert.False(window.IsEnabled);
            Assert.False(closed);
            Assert.False(stopping.IsCompleted);
            window.Close();
            Assert.False(closed);
            Assert.Same(stopping, runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose));
            Assert.DoesNotContain("shutdown:0", events);
        }
        finally
        {
            phases.AllowProducerStop.SetResult();
            await runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
        }
        Assert.True(closed);
        Assert.Equal(1, events.Count(item => item == "shutdown:0"));
    }

    /// <summary>Retains the process and local snapshot until a held Drive upload completes, even after another close request.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedCloseCannotInterruptShutdownBackup(bool uploadFails)
    {
        using var root = new ProfileSyncTestRoot();
        root.Store.SavePreferences(new(Automatic: true));
        root.Write("config", "cross-platform-ui.json", "{\"Theme\":\"current\"}");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cloud = new FakeProfileDrive
        {
            BeforeUpload = async token =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                if (uploadFails)
                {
                    throw new HttpRequestException("offline");
                }
            },
        };
        using var service = new ProfileSyncService(root.Store, cloud);
        List<string> events = [];
        var window = new Window();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        var lifetime = new RecordingDesktopLifetime(events, window.Close);
        var phases = new RecordingDesktopRuntimePhases(events) { DisposeViewModel = () => service.ShutdownAsync() };
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        runtime.AttachMainWindow(window);
        window.Show();
        try
        {
            window.Close();
            Task stopping = runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root.Store.StateDirectory, "backups")));
            Assert.False(closed);
            Assert.False(stopping.IsCompleted);
            window.Close();
            Assert.False(closed);
            Assert.False(window.IsVisible);
            Assert.DoesNotContain("shutdown:0", events);
        }
        finally
        {
            release.TrySetResult();
            await runtime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
        }
        Assert.True(closed);
        Assert.Equal(1, events.Count(item => item == "shutdown:0"));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root.Store.StateDirectory, "backups")));
        if (uploadFails)
        {
            Assert.Empty(cloud.Uploaded);
            Assert.Contains("Local data was retained", service.Status);
        }
        else
        {
            Assert.Single(cloud.Uploaded);
            Assert.Contains("Google Drive", service.Status);
        }
    }

    [Fact]
    public async Task OperatingSystemShutdownIsNotCancelled()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        bool cancel = runtime.RequestMainWindowClose(WindowCloseReason.OSShutdown);

        Assert.False(cancel);
        Assert.Equal(["quiesce:OperatingSystemShutdown", "stop-producers"], events);
        phases.AllowProducerStop.SetResult();
        await runtime.RequestShutdownAsync(DesktopShutdownReason.OperatingSystemShutdown);
    }

    [Theory]
    [InlineData((int)DesktopShutdownReason.JournalCommand)]
    [InlineData((int)DesktopShutdownReason.RemoteInstanceRequest)]
    public async Task InternalExitReasonEntersTheSharedStopPipeline(int reasonValue)
    {
        var reason = (DesktopShutdownReason)reasonValue;
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        await runtime.RequestShutdownAsync(reason);

        Assert.Equal($"quiesce:{reason}", events[0]);
        Assert.Equal("shutdown:0", events[^1]);
    }

    [AvaloniaFact]
    public async Task LinuxTerminationIsPostedToTheUiDispatcher()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        var entered = new TaskCompletionSource<DesktopShutdownReason>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        bool ranOnUiThread = false;
        phases.OnQuiesce = reason =>
        {
            ranOnUiThread = Dispatcher.UIThread.CheckAccess();
            entered.SetResult(reason);
        };
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);

        await Task.Run(() => runtime.PostShutdownOnUiThread(DesktopShutdownReason.LinuxTermination));
        DesktopShutdownReason actualReason = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(DesktopShutdownReason.LinuxTermination, actualReason);
        Assert.True(ranOnUiThread);
        phases.AllowProducerStop.SetResult();
        await runtime.RequestShutdownAsync(DesktopShutdownReason.LinuxTermination);
    }

    [AvaloniaFact]
    public async Task RestartLaunchFailureLeavesTheRuntimeRunning()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        var expected = new InvalidOperationException("Launch failed.");

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RestartAsync(() => throw expected)
        );

        Assert.Same(expected, actual);
        Assert.Empty(events);
        phases.AllowProducerStop.SetResult();
    }

    [AvaloniaFact]
    public async Task SuccessfulRestartHidesTheRetiringWindowBeforeSlowCleanup()
    {
        List<string> events = [];
        var window = new Window();
        var lifetime = new RecordingDesktopLifetime(events, window.Close);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        runtime.AttachMainWindow(window);
        window.Show();

        Task restarting = runtime.RestartAsync(() => events.Add("launch"));

        Assert.False(window.IsVisible);
        Assert.Equal(["launch", "quiesce:Restart", "stop-producers"], events);
        phases.AllowProducerStop.SetResult();
        await restarting;
    }

    [AvaloniaFact]
    public async Task UpdateHandoffHidesTheRetiringWindowBeforeSlowCleanup()
    {
        List<string> events = [];
        var window = new Window();
        var lifetime = new RecordingDesktopLifetime(events, window.Close);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(lifetime, phases);
        runtime.AttachMainWindow(window);
        window.Show();

        Task stopping = runtime.RequestShutdownAsync(DesktopShutdownReason.UpdateHandoff);

        Assert.False(window.IsVisible);
        Assert.Equal(["quiesce:UpdateHandoff", "stop-producers"], events);
        phases.AllowProducerStop.SetResult();
        await stopping;
    }

    /// <summary>Automatic mode switches commanders in-process while loading each commander's isolated settings and keeping ProfileSync alive.</summary>
    [AvaloniaFact]
    public async Task AutomaticCommanderSwitchReloadsRuntimeWithIsolatedCommanderSettings()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Fileheader","part":1,"Odyssey":true}
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        AppDataPaths firstPaths = temp.Paths with { SettingsFrontierId = "F13228986" };
        AppDataPaths secondPaths = temp.Paths with { SettingsFrontierId = "F14004033" };
        CommanderSettingsProfile.Prepare(firstPaths);
        CommanderSettingsProfile.Prepare(secondPaths);
        new OverlayScaleSettingsStore(firstPaths.UiSettingsPath).Save(new(OverlayScaleCatalog.GetIndex(65)));
        new OverlayScaleSettingsStore(secondPaths.UiSettingsPath).Save(new(OverlayScaleCatalog.BaselineIndex));
        new LegacyOverlayLayoutStore(firstPaths.OverlaySettingsDirectory).Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Left, 120, LegacyVerticalAnchor.Top, 80, null),
            }
        );
        new LegacyOverlayLayoutStore(secondPaths.OverlaySettingsDirectory).Save(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                ["PlotJumpInfo"] = new(LegacyHorizontalAnchor.Left, 450, LegacyVerticalAnchor.Top, 80, null),
            }
        );

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        var cloud = new FakeProfileDrive();
        var profileSync = new ProfileSyncService(temp.Store, cloud);
        DesktopRuntime? activeRuntime = null;
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var switchedBackToFirst = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string[] arguments = ["--journal-directory", journals];

        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                ProfileSyncService = profileSync,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F14004033",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedToSecond.TrySetResult(replacement);
                    }
                    else if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F13228986",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedBackToFirst.TrySetResult(replacement);
                    }
                },
            },
            lifetime
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");
            Assert.Equal("F13228986", activeRuntime.MainViewModel!.AppDataPaths.SettingsFrontierId);
            Assert.True(activeRuntime.MainViewModel.AllowAutomaticCommanderSwitch);
            Assert.Equal(65, activeRuntime.MainViewModel.OverlayScale.ScalePercent);
            Assert.Equal(
                120,
                activeRuntime
                    .MainViewModel.OverlayLayout.Overlays.Single(overlay => overlay.Name == "PlotJumpInfo")
                    .HorizontalOffset
            );

            // Simulate closing Commander A and reconnecting as Commander B without restarting SrvSurvey.
            await File.AppendAllTextAsync(firstJournal, "{\"event\":\"Shutdown\"}\n");
            File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 7, 33, 50, DateTimeKind.Utc));
            string secondJournal = Path.Combine(journals, "Journal.2026-11-23T073400.01.log");
            await File.WriteAllTextAsync(
                secondJournal,
                """
                {"event":"Fileheader","part":1,"Odyssey":true}
                {"event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
                {"event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}

                """
            );
            File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 34, 15, DateTimeKind.Utc));

            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");

            Assert.Empty(lifetimeEvents);
            Assert.Equal("F14004033", secondRuntime.MainViewModel!.AppDataPaths.SettingsFrontierId);
            Assert.Equal("Second Cmdr", secondRuntime.MainViewModel.CommanderName);
            using (var peerClaims = new SrvSurvey.Desktop.Platform.CommanderClaimRegistry(temp.Paths.CacheDirectory))
            {
                Assert.False(peerClaims.IsClaimedByOtherInstance("F13228986"));
                Assert.True(peerClaims.IsClaimedByOtherInstance("F14004033"));
                Assert.False(peerClaims.Claim("F14004033"));
            }
            Assert.Equal(0, secondRuntime.MainViewModel.OverlayScale.ScalePercent);
            Assert.Equal(
                450,
                secondRuntime
                    .MainViewModel.OverlayLayout.Overlays.Single(overlay => overlay.Name == "PlotJumpInfo")
                    .HorizontalOffset
            );
            Assert.Empty(cloud.Uploaded);
            Assert.Contains("Automatic commander switch", applicationLog.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Application exit", applicationLog.Text, StringComparison.Ordinal);

            // Hide to tray, then switch back to Commander A; replacement must stay hidden and still load Commander A.
            secondRuntime.MainWindow!.ShowInTaskbar = false;
            secondRuntime.MainWindow.Hide();
            string thirdJournal = Path.Combine(journals, "Journal.2026-11-23T090000.01.log");
            await File.WriteAllTextAsync(
                thirdJournal,
                """
                {"event":"Fileheader","part":1,"Odyssey":true}
                {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
                {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

                """
            );
            File.SetLastWriteTimeUtc(thirdJournal, new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc));

            DesktopRuntime restoredFirst = await WaitForTaskAsync(switchedBackToFirst.Task);
            await WaitUntilAsync(() => restoredFirst.MainViewModel?.FrontierId == "F13228986");

            Assert.False(restoredFirst.MainWindow!.IsVisible);
            Assert.False(restoredFirst.MainWindow.ShowInTaskbar);
            Assert.Equal(65, restoredFirst.MainViewModel!.OverlayScale.ScalePercent);
            Assert.Equal(
                120,
                restoredFirst
                    .MainViewModel.OverlayLayout.Overlays.Single(overlay => overlay.Name == "PlotJumpInfo")
                    .HorizontalOffset
            );
            Assert.Equal(
                OverlayScaleCatalog.GetIndex(65),
                new OverlayScaleSettingsStore(firstPaths.UiSettingsPath).Load().Index
            );
            Assert.Equal(
                OverlayScaleCatalog.BaselineIndex,
                new OverlayScaleSettingsStore(secondPaths.UiSettingsPath).Load().Index
            );
        }
        finally
        {
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }

        Assert.Equal(["shutdown:0"], lifetimeEvents);
    }

    /// <summary>An explicitly pinned Preferred commander stays on its assigned commander when another commander connects.</summary>
    [AvaloniaFact]
    public async Task PinnedPreferredCommanderDoesNotAutoSwitch()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));
        new CommanderPreferenceSettingsStore(temp.Paths.SharedUiSettingsPath).Save(new("First Cmdr", "F13228986"));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        bool replaced = false;
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        await using var runtime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = _ => replaced = true,
            },
            new RecordingDesktopLifetime([])
        );

        await WaitUntilAsync(() => runtime.MainViewModel?.FrontierId == "F13228986");
        Assert.False(runtime.MainViewModel!.AllowAutomaticCommanderSwitch);

        string secondJournal = Path.Combine(journals, "Journal.2026-11-23T073400.01.log");
        await File.WriteAllTextAsync(
            secondJournal,
            """
            {"event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
            {"event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}

            """
        );
        File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 34, 15, DateTimeKind.Utc));
        await runtime.MainViewModel.RefreshAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.False(replaced);
        Assert.Equal("F13228986", runtime.MainViewModel.FrontierId);
    }

    /// <summary>An exit request arriving during an automatic commander switch aborts replacement startup and shuts down cleanly.</summary>
    [AvaloniaFact]
    public async Task ShutdownDuringAutomaticCommanderSwitchAbortsReplacementAndExitsCleanly()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        var cloud = new FakeProfileDrive();
        var profileSync = new ProfileSyncService(temp.Store, cloud);
        bool replaced = false;
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        var runtime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                ProfileSyncService = profileSync,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = _ => replaced = true,
            },
            lifetime
        );

        await WaitUntilAsync(() => runtime.MainViewModel?.FrontierId == "F13228986");

        Task switchTask = runtime.SwitchCommanderAsync("F14004033");
        Task shutdownTask = runtime.RequestShutdownAsync(DesktopShutdownReason.LinuxTermination, exitCode: 0);
        await WaitUntilAsync(() => switchTask.IsCompleted && shutdownTask.IsCompleted);
        await Task.WhenAll(switchTask, shutdownTask);

        Assert.False(replaced);
        Assert.Equal(["shutdown:0"], lifetimeEvents);
        Assert.True(cloud.Disposed);
        Assert.Contains("Application exit (LinuxTermination)", applicationLog.Text, StringComparison.Ordinal);
    }

    /// <summary>Switching from a minimized window into a commander with MinimizeToTray keeps the replacement hidden in the tray.</summary>
    [AvaloniaFact]
    public async Task MinimizedAutomaticCommanderSwitchWithMinimizeToTrayStaysHidden()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        AppDataPaths firstPaths = temp.Paths with { SettingsFrontierId = "F13228986" };
        AppDataPaths secondPaths = temp.Paths with { SettingsFrontierId = "F14004033" };
        CommanderSettingsProfile.Prepare(firstPaths);
        CommanderSettingsProfile.Prepare(secondPaths);
        new DesktopBehaviorSettingsStore(secondPaths.UiSettingsPath).Save(
            new DesktopBehaviorPreferences(
                FocusGameOnStart: false,
                FocusGameOnMinimize: false,
                FocusGameAfterFsdJump: false,
                MinimizeToTray: true
            )
        );

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        DesktopRuntime? activeRuntime = null;
        var switched = new TaskCompletionSource<DesktopRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    switched.TrySetResult(replacement);
                },
            },
            new RecordingDesktopLifetime([])
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");
            activeRuntime.MainWindow!.WindowState = WindowState.Minimized;
            Assert.True(activeRuntime.MainWindow.HasTrayIcon);

            await activeRuntime.SwitchCommanderAsync("F14004033");
            DesktopRuntime secondRuntime = await WaitForTaskAsync(switched.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.AppDataPaths.SettingsFrontierId == "F14004033");

            Assert.False(secondRuntime.MainWindow!.IsVisible);
            Assert.False(secondRuntime.MainWindow.ShowInTaskbar);
        }
        finally
        {
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }
    }

    [Fact]
    public void ReplacementWindowRequiresTrayIconBeforeHidingToTrayOnCommanderSwitch()
    {
        var hiddenToTray = new CommanderSwitchWindowState(WasHiddenToTray: true, WasMinimized: false, WasActive: false);
        var minimized = new CommanderSwitchWindowState(WasHiddenToTray: false, WasMinimized: true, WasActive: false);

        Assert.False(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: false,
                hiddenToTray,
                minimizeToTray: false,
                hasTrayIcon: false
            )
        );
        Assert.True(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: false,
                hiddenToTray,
                minimizeToTray: false,
                hasTrayIcon: true
            )
        );
        Assert.False(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: true,
                hiddenToTray,
                minimizeToTray: true,
                hasTrayIcon: true
            )
        );
        Assert.False(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: false,
                minimized,
                minimizeToTray: true,
                hasTrayIcon: false
            )
        );
        Assert.True(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: false,
                minimized,
                minimizeToTray: true,
                hasTrayIcon: true
            )
        );
        Assert.False(
            DesktopRuntime.ShouldHideReplacementWindowToTray(
                needsStartupUpdateConfirmation: false,
                minimized,
                minimizeToTray: false,
                hasTrayIcon: true
            )
        );
    }

    /// <summary>On Linux, SIGTERM registration stays live across commander switch handoff and late shutdown calls forward to the replacement.</summary>
    [AvaloniaFact]
    public async Task LinuxTerminationRegistrationSpansCommanderSwitchAndForwardsLateShutdown()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        var switched = new TaskCompletionSource<DesktopRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        var outgoing = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement => switched.TrySetResult(replacement),
            },
            lifetime
        );

        await WaitUntilAsync(() => outgoing.MainViewModel?.FrontierId == "F13228986");
        if (OperatingSystem.IsLinux())
        {
            Assert.True(outgoing.HasLinuxTerminationRegistration);
        }

        Task switchTask = outgoing.SwitchCommanderAsync("F14004033");
        if (OperatingSystem.IsLinux())
        {
            // During asynchronous commander-switch teardown, the outgoing SIGTERM handler remains active.
            Assert.True(outgoing.HasLinuxTerminationRegistration);
        }

        DesktopRuntime replacement = await WaitForTaskAsync(switched.Task);
        await WaitUntilAsync(() => switchTask.IsCompleted);
        await switchTask;

        Assert.False(outgoing.HasLinuxTerminationRegistration);
        if (OperatingSystem.IsLinux())
        {
            Assert.True(replacement.HasLinuxTerminationRegistration);
        }

        // A late shutdown posted against the outgoing runtime forwards to the replacement runtime.
        Task forwardedShutdown = outgoing.RequestShutdownAsync(DesktopShutdownReason.LinuxTermination, exitCode: 0);
        await WaitUntilAsync(() => forwardedShutdown.IsCompleted);
        await forwardedShutdown;

        Assert.Equal(["shutdown:0"], lifetimeEvents);
        Assert.False(replacement.HasLinuxTerminationRegistration);
    }

    /// <summary>Automatic commander handoff applies progress written while deferred instead of skipping it as bootstrap, and carries checked update state across.</summary>
    [AvaloniaFact]
    public async Task AutomaticCommanderSwitchPreservesProgressWrittenBeforeHandoffAndRetainsUpdateStatus()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"event":"Fileheader","part":1,"Odyssey":true}
            {"event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        // Pre-create Commander B's saved profile so LoadedExistingProfile is true on switch.
        AppDataPaths firstPaths = temp.Paths with
        {
            SettingsFrontierId = "F13228986",
        };
        AppDataPaths secondPaths = temp.Paths with { SettingsFrontierId = "F14004033" };
        CommanderSettingsProfile.Prepare(firstPaths);
        CommanderSettingsProfile.Prepare(secondPaths);
        var profileStore = new CommanderProfileStore(temp.Paths.DataDirectory);
        await profileStore.SaveExobiologyAsync(
            "F14004033",
            "Second Cmdr",
            isOdyssey: true,
            SrvSurvey.Core.Exobiology.ExobiologySnapshot.Empty
        );

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        DesktopRuntime? activeRuntime = null;
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );
        var initialReleaseState = new ReleaseUpdateSessionState(
            HasCompletedCheck: true,
            UseDevelopmentReleases: true,
            LatestVersion: "2.2.0.0",
            StatusMessage: "Update 2.2.0.0 is available.",
            IsUpdateAvailable: true,
            ReleaseUri: new Uri("https://example.invalid/releases/v2.2.0.0"),
            ReleasePackage: null,
            ReleaseVersion: SrvSurvey.Core.Updates.ReleaseVersion.Parse("2.2.0.0"),
            ReleaseNotes: "Test release notes",
            DismissedReleaseVersion: null,
            PreviousInstallationOutcomeMessage: null
        );
        var initialReferenceState = new ReferenceDataUpdateSessionState(
            HasCompletedRefresh: true,
            StatusMessage: "Published reference data is current.",
            UpdatedCatalogs: "Catalogs updated this session: None needed; already current.",
            BackupDirectory: "Backup created this session: Not needed; no catalogs were replaced.",
            IsRestartRequired: false
        );

        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                ReleaseUpdateSessionState = initialReleaseState,
                ReferenceDataUpdateSessionState = initialReferenceState,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F14004033",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedToSecond.TrySetResult(replacement);
                    }
                },
            },
            lifetime
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");
            Assert.True(activeRuntime.MainViewModel!.ReleaseUpdates.IsUpdateAvailable);
            Assert.True(activeRuntime.MainViewModel.VrOverlay.BeginAdjustment());

            // Commander B logs in and records an organic scan while Commander A is calibrating VR.
            DateTimeOffset liveStart = DateTimeOffset.UtcNow;
            string secondJournal = Path.Combine(journals, $"Journal.{liveStart:yyyy-MM-ddTHHmmss}.01.log");
            await File.WriteAllTextAsync(
                secondJournal,
                $$"""
                {"timestamp":"{{liveStart:yyyy-MM-ddTHH:mm:ssZ}}","event":"Fileheader","part":1,"Odyssey":true}
                {"timestamp":"{{liveStart.AddSeconds(
                    1
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
                {"timestamp":"{{liveStart.AddSeconds(
                    2
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}
                {"timestamp":"{{liveStart.AddSeconds(
                    3
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"Location","StarSystem":"Sol","SystemAddress":10477373803,"Body":"Sol 3","BodyID":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    4
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Log","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    5
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Sample","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    6
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Analyse","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}

                """
            );
            File.SetLastWriteTimeUtc(secondJournal, liveStart.AddSeconds(15).UtcDateTime);

            // Close VR calibration -> automatic switch runs and applies Commander B's organic scan live.
            activeRuntime.MainViewModel.VrOverlay.CancelCommand.Execute(null);
            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");

            // The organic scan recorded before the handoff must be applied and persisted, not skipped as bootstrap.
            CommanderProfileLoadResult reloaded = await profileStore.LoadAsync("F14004033", isOdyssey: true);
            Assert.NotNull(reloaded.Data);
            Assert.NotEmpty(reloaded.Data.Exobiology.ScannedBioEntryIds);

            // Release and reference-data update status must survive the commander switch intact.
            Assert.True(secondRuntime.MainViewModel!.ReleaseUpdates.IsUpdateAvailable);
            Assert.Equal("2.2.0.0", secondRuntime.MainViewModel.ReleaseUpdates.LatestVersion);
            Assert.Equal(
                "Published reference data is current.",
                secondRuntime.MainViewModel.ReferenceDataUpdates.StatusMessage
            );
        }
        finally
        {
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }
    }

    /// <summary>Multi-client conflict suppression does not mark unsaved progress as persisted, unbound handoff avoids duplicate replay, and conflicting explicit launches are rejected.</summary>
    [AvaloniaFact]
    public async Task CommanderSwitchConflictDoesNotSkipUnsavedProgressUnboundHandoffDeduplicatesAndConflictingLaunchFails()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        var profileStore = new CommanderProfileStore(temp.Paths.DataDirectory);
        await profileStore.SaveExobiologyAsync(
            "F14004033",
            "Second Cmdr",
            isOdyssey: true,
            SrvSurvey.Core.Exobiology.ExobiologySnapshot.Empty
        );

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        var switcher = new MutableSwitchWindowCount { WindowCount = 1 };
        DesktopRuntime? activeRuntime = null;
        var switchedToFirst = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );

        // 1. Unbound startup (no journals yet) projects the first commander's FSDJump once, then hands off its confirmed checkpoint without doubling jump count.
        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                GameWindowSwitcherFactory = () => switcher,
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F13228986",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedToFirst.TrySetResult(replacement);
                    }
                    else if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F14004033",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedToSecond.TrySetResult(replacement);
                    }
                },
            },
            lifetime
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel is not null);
            string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
            await File.WriteAllTextAsync(
                firstJournal,
                """
                {"timestamp":"2026-11-23T06:01:57Z","event":"Fileheader","part":1,"Odyssey":true}
                {"timestamp":"2026-11-23T06:01:58Z","event":"Commander","Name":"First Cmdr","FID":"F13228986"}
                {"timestamp":"2026-11-23T06:01:59Z","event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}
                {"timestamp":"2026-11-23T06:01:59Z","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Sol","SystemAddress":10477373803}
                {"timestamp":"2026-11-23T06:02:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"JumpDist":12.5}

                """
            );
            File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 2, 0, DateTimeKind.Utc));

            DesktopRuntime firstBound = await WaitForTaskAsync(switchedToFirst.Task);
            await WaitUntilAsync(() => firstBound.MainViewModel?.FrontierId == "F13228986");
            CommanderProfileLoadResult firstProfile = await profileStore.LoadAsync("F13228986", isOdyssey: true);
            Assert.NotNull(firstProfile.Data);
            Assert.Equal(1, firstProfile.Data.Exploration.JumpCount);
            Assert.Equal(12.5, firstProfile.Data.Exploration.DistanceTravelled, 4);

            // 2. Launching a second explicit instance for F13228986 while F13228986 is owned must fail startup before opening a writable workspace.
            List<string> conflictEvents = [];
            var conflictLifetime = new RecordingDesktopLifetime(conflictEvents);
            await using var conflicting = DesktopRuntime.Start(
                Application.Current!,
                new ClassicDesktopStyleApplicationLifetime(),
                new DesktopStartup(["--frontier-id", "F13228986", "--journal-directory", journals], applicationLog)
                {
                    AppDataPathsOverride = temp.Paths with { SettingsFrontierId = "F13228986" },
                    GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                },
                conflictLifetime
            );
            await WaitUntilAsync(() => conflictEvents.Count > 0);
            Assert.Equal(["shutdown:1"], conflictEvents);
            Assert.Null(conflicting.MainViewModel);

            // 2b. Even if initial startup context had no commander yet and discovery finds F13228986 during runtime composition, startup must still fail before opening F13228986's workspace.
            List<string> lateDiscoveryConflictEvents = [];
            var lateDiscoveryLifetime = new RecordingDesktopLifetime(lateDiscoveryConflictEvents);
            await using var lateConflicting = DesktopRuntime.Start(
                Application.Current!,
                new ClassicDesktopStyleApplicationLifetime(),
                new DesktopStartup(["--journal-directory", journals], applicationLog)
                {
                    AppDataPathsOverride = temp.Paths,
                    GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                },
                lateDiscoveryLifetime
            );
            await WaitUntilAsync(() => lateDiscoveryConflictEvents.Count > 0);
            Assert.Equal(["shutdown:1"], lateDiscoveryConflictEvents);
            Assert.Null(lateConflicting.MainViewModel);

            // 3. While two Elite clients are open (only A has SrvSurvey), B completes a ScanOrganic; once A exits and B writes fresh activity, B's ScanOrganic must NOT be skipped.
            switcher.WindowCount = 2;
            DateTimeOffset liveStart = DateTimeOffset.UtcNow;
            string secondJournal = Path.Combine(journals, $"Journal.{liveStart:yyyy-MM-ddTHHmmss}.01.log");
            await File.WriteAllTextAsync(
                secondJournal,
                $$"""
                {"timestamp":"{{liveStart:yyyy-MM-ddTHH:mm:ssZ}}","event":"Fileheader","part":1,"Odyssey":true}
                {"timestamp":"{{liveStart.AddSeconds(
                    1
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
                {"timestamp":"{{liveStart.AddSeconds(
                    2
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}
                {"timestamp":"{{liveStart.AddSeconds(
                    3
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"Location","StarSystem":"Sol","SystemAddress":10477373803,"Body":"Sol 3","BodyID":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    4
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Log","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    5
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Sample","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}
                {"timestamp":"{{liveStart.AddSeconds(
                    6
                ):yyyy-MM-ddTHH:mm:ssZ}}","event":"ScanOrganic","ScanType":"Analyse","Genus":"$Codex_Ent_Aleoids_Genus_Name;","Species":"$Codex_Ent_Aleoids_01_Name;","Variant":"$Codex_Ent_Aleoids_01_A_Name;","SystemAddress":10477373803,"Body":3}

                """
            );
            File.SetLastWriteTimeUtc(secondJournal, liveStart.AddSeconds(10).UtcDateTime);
            await firstBound.MainViewModel!.RefreshAsync();
            Assert.False(switchedToSecond.Task.IsCompleted);

            switcher.WindowCount = 1;
            await File.AppendAllTextAsync(
                secondJournal,
                $"{{\"timestamp\":\"{liveStart.AddSeconds(15):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}}\n"
            );
            File.SetLastWriteTimeUtc(secondJournal, liveStart.AddSeconds(15).UtcDateTime);

            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");
            CommanderProfileLoadResult reloadedSecond = await profileStore.LoadAsync("F14004033", isOdyssey: true);
            Assert.NotNull(reloadedSecond.Data);
            Assert.NotEmpty(reloadedSecond.Data.Exobiology.ScannedBioEntryIds);
        }
        finally
        {
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }
    }

    /// <summary>First switch to a pre-existing journal does not replay historical jumps or commands, pending reference refresh stays serialized after prior completion, and ordinary restarts retain --instance-frontier-id.</summary>
    [AvaloniaFact]
    public async Task FirstSwitchSkipsHistoricalJournalPrefixPendingRefreshStaysBusyAndRetainedRestartBeatsPreference()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        var profileStore = new CommanderProfileStore(temp.Paths.DataDirectory);

        // Pre-populate Commander B's saved totals and a pre-existing journal from an earlier process (including an old .exit command).
        await profileStore.SaveExplorationAsync(
            "F14004033",
            "Second Cmdr",
            isOdyssey: true,
            new SrvSurvey.Core.Exploration.ExplorationSnapshot(
                EstimatedRewards: 0,
                DistanceTravelled: 40.0,
                JumpCount: 2,
                ScanCount: 0,
                DetailedSurfaceScanCount: 0,
                LandedBodyCount: 0
            )
        );
        string secondJournal = Path.Combine(journals, "Journal.2026-11-23T050000.01.log");
        await File.WriteAllTextAsync(
            secondJournal,
            """
            {"timestamp":"2026-11-23T05:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"2026-11-23T05:00:01Z","event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
            {"timestamp":"2026-11-23T05:00:02Z","event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}
            {"timestamp":"2026-11-23T05:00:03Z","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Sol","SystemAddress":10477373803}
            {"timestamp":"2026-11-23T05:00:04Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"JumpDist":20.0}
            {"timestamp":"2026-11-23T05:00:05Z","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Achenar","SystemAddress":10477373804}
            {"timestamp":"2026-11-23T05:00:06Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":10477373804,"JumpDist":20.0}
            {"timestamp":"2026-11-23T05:00:07Z","event":"SendText","To":"local","Message":".exit"}

            """
        );
        File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 5, 0, 7, DateTimeKind.Utc));

        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060157.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"timestamp":"2026-11-23T06:01:57Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"2026-11-23T06:01:58Z","event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"timestamp":"2026-11-23T06:01:59Z","event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 1, 57, DateTimeKind.Utc));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        List<string> lifetimeEvents = [];
        var lifetime = new RecordingDesktopLifetime(lifetimeEvents);
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        DesktopRuntime? activeRuntime = null;
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var pendingRefreshGate = new TaskCompletionSource<ReferenceDataUpdateSessionState>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var priorCompletedReferenceState = new ReferenceDataUpdateSessionState(
            HasCompletedRefresh: true,
            StatusMessage: "Published reference data is current.",
            UpdatedCatalogs: "Catalogs updated this session: None needed; already current.",
            BackupDirectory: "Backup created this session: Not needed; no catalogs were replaced.",
            IsRestartRequired: false,
            HasAttemptedRefresh: true
        );
        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );

        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                ReferenceDataUpdateSessionState = priorCompletedReferenceState,
                PendingReferenceDataRefresh = pendingRefreshGate.Task,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    if (
                        string.Equals(
                            replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                            "F14004033",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        switchedToSecond.TrySetResult(replacement);
                    }
                },
            },
            lifetime
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");
            Assert.True(activeRuntime.MainViewModel!.ReferenceDataUpdates.IsRefreshing);
            Assert.False(activeRuntime.MainViewModel.ReferenceDataUpdates.RefreshCommand.CanExecute(null));

            // Append ONE genuinely new jump to Commander B's pre-existing journal -> triggers first switch to B.
            await File.AppendAllTextAsync(
                secondJournal,
                """
                {"timestamp":"2026-11-23T07:00:00Z","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Alioth","SystemAddress":10477373805}
                {"timestamp":"2026-11-23T07:00:01Z","event":"FSDJump","StarSystem":"Alioth","SystemAddress":10477373805,"JumpDist":12.5}

                """
            );
            File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 0, 1, DateTimeKind.Utc));

            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");

            // Pending refresh carried across the switch keeps the replacement Refresh button disabled until completion, then surfaces restart-required.
            Assert.True(secondRuntime.MainViewModel!.ReferenceDataUpdates.IsRefreshing);
            Assert.False(secondRuntime.MainViewModel.ReferenceDataUpdates.RefreshCommand.CanExecute(null));
            pendingRefreshGate.SetResult(
                new ReferenceDataUpdateSessionState(
                    HasCompletedRefresh: true,
                    StatusMessage: "Activated 1 verified reference update(s). Restart SrvSurvey to use them.",
                    UpdatedCatalogs: "Catalogs updated this session: codex.json",
                    BackupDirectory: "Backup created this session: /tmp/backup",
                    IsRestartRequired: true,
                    HasAttemptedRefresh: true
                )
            );
            await WaitUntilAsync(() => secondRuntime.MainViewModel.ReferenceDataUpdates.IsRestartRequired);
            Assert.False(secondRuntime.MainViewModel.ReferenceDataUpdates.IsRefreshing);

            // Historical prefix (2 jumps + .exit) ran as bootstrap; only the 1 new jump was applied live.
            Assert.Empty(lifetimeEvents);
            CommanderProfileLoadResult reloadedSecond = await profileStore.LoadAsync("F14004033", isOdyssey: true);
            Assert.NotNull(reloadedSecond.Data);
            Assert.Equal(3, reloadedSecond.Data.Exploration.JumpCount);
            Assert.Equal(52.5, reloadedSecond.Data.Exploration.DistanceTravelled, 4);
        }
        finally
        {
            pendingRefreshGate.TrySetCanceled();
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }

        // Ordinary restart with --instance-frontier-id B must retain B even if another instance changed the shared preference to A and owns A's claim.
        new CommanderPreferenceSettingsStore(temp.Paths.SharedUiSettingsPath).Save(new("First Cmdr", "F13228986"));
        using SrvSurvey.Desktop.Platform.CommanderClaimLease? commanderAClaim =
            SrvSurvey.Desktop.Platform.CommanderClaimRegistry.TryAcquire(temp.Paths.CacheDirectory, "F13228986");
        Assert.NotNull(commanderAClaim);

        string[] restartArgs = StartupOptions.ForRestart(arguments, "F14004033");
        DesktopStartupContext restartContext = await DesktopStartupContext.ResolveAsync(
            restartArgs,
            () => temp.Paths,
            CancellationToken.None
        );
        Assert.Equal("F14004033", restartContext.AppDataPaths.SettingsFrontierId);

        var restartPackage = new SrvSurvey.Core.Updates.CrossPlatformReleasePackage(
            "linux-x64",
            "SrvSurvey.tar.gz",
            "tar.gz",
            1024,
            new string('b', 64),
            new Uri("https://example.invalid/SrvSurvey.tar.gz")
        );
        var restartReleaseState = new ReleaseUpdateSessionState(
            HasCompletedCheck: true,
            UseDevelopmentReleases: true,
            LatestVersion: "2.2.0.0",
            StatusMessage: "Update available.",
            IsUpdateAvailable: true,
            ReleaseUri: new Uri("https://example.invalid/releases"),
            ReleasePackage: restartPackage,
            ReleaseVersion: SrvSurvey.Core.Updates.ReleaseVersion.Parse("2.2.0.0"),
            ReleaseNotes: "Notes",
            DismissedReleaseVersion: null,
            PreviousInstallationOutcomeMessage: null,
            HasAttemptedCheck: true
        );
        List<string> restartLifetimeEvents = [];
        await using var restarted = DesktopRuntime.Start(
            Application.Current!,
            new ClassicDesktopStyleApplicationLifetime(),
            new DesktopStartup(restartArgs, applicationLog)
            {
                AppDataPathsOverride = restartContext.AppDataPaths,
                ReleaseUpdateSessionState = restartReleaseState,
                ReferenceDataUpdateSessionState = priorCompletedReferenceState,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
            },
            new RecordingDesktopLifetime(restartLifetimeEvents)
        );
        await WaitUntilAsync(() => restarted.MainViewModel?.FrontierId == "F14004033");
        Assert.Empty(restartLifetimeEvents);
        Assert.Equal("F14004033", restarted.MainViewModel!.TargetFrontierId);
        Assert.Equal("F14004033", restarted.MainViewModel.AppDataPaths.SettingsFrontierId);
        Assert.False(restarted.MainViewModel.AllowAutomaticCommanderSwitch);

        // Direct SwitchCommanderAsync must also refuse to tear down while a release installation is in flight.
        var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInstall = new TaskCompletionSource<SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        restarted.MainViewModel.ReleaseUpdates.ConfigureInstallationWorkflow(
            new GatedInstallWorkflow(installStarted, releaseInstall.Task)
        );
        restarted.MainViewModel.ReleaseUpdates.InstallConfirmed = true;
        Task installTask = restarted.MainViewModel.ReleaseUpdates.InstallAsync();
        await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(restarted.MainViewModel.ReleaseUpdates.IsInstalling);
        await restarted.SwitchCommanderAsync("F13228986");
        Assert.Equal("F14004033", restarted.MainViewModel.AppDataPaths.SettingsFrontierId);
        releaseInstall.SetResult(
            new SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult(
                SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowStatus.Cancelled,
                SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowStage.Downloading,
                SrvSurvey.Desktop.Platform.ReleaseInstallationCleanupStatus.Succeeded
            )
        );
        await installTask;
    }

    /// <summary>An unconsumed peer claim preserves live progress on switch, while a confirmed persisted checkpoint splits bootstrap prefix from live tail.</summary>
    [AvaloniaFact]
    public async Task PeerClaimWithoutConfirmedCheckpointPreservesLiveProgressWhileConfirmedCheckpointSplitsPrefix()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        var profileStore = new CommanderProfileStore(temp.Paths.DataDirectory);

        await profileStore.SaveExplorationAsync(
            "F14004033",
            "Second Cmdr",
            isOdyssey: true,
            new SrvSurvey.Core.Exploration.ExplorationSnapshot(
                EstimatedRewards: 0,
                DistanceTravelled: 40.0,
                JumpCount: 2,
                ScanCount: 0,
                DetailedSurfaceScanCount: 0,
                LandedBodyCount: 0
            )
        );

        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060000.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"timestamp":"2026-11-23T06:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"2026-11-23T06:00:01Z","event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"timestamp":"2026-11-23T06:00:02Z","event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 0, 2, DateTimeKind.Utc));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        DesktopRuntime? activeRuntime = null;
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var switchedBackToFirst = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var switchedToSecondAgain = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int secondSwitchCount = 0;

        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );

        activeRuntime = DesktopRuntime.Start(
            Application.Current!,
            desktop,
            new DesktopStartup(arguments, applicationLog)
            {
                AppDataPathsOverride = startupContext.AppDataPaths,
                GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                OnRuntimeReplaced = replacement =>
                {
                    activeRuntime = replacement;
                    string? fid = replacement.MainViewModel?.AppDataPaths.SettingsFrontierId;
                    if (string.Equals(fid, "F14004033", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++secondSwitchCount == 1)
                        {
                            switchedToSecond.TrySetResult(replacement);
                        }
                        else
                        {
                            switchedToSecondAgain.TrySetResult(replacement);
                        }
                    }
                    else if (string.Equals(fid, "F13228986", StringComparison.OrdinalIgnoreCase))
                    {
                        switchedBackToFirst.TrySetResult(replacement);
                    }
                },
            },
            new RecordingDesktopLifetime([])
        );

        try
        {
            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");

            // 1. A peer holds F14004033's claim without consuming secondJournal (e.g., different folder or early exit).
            DateTimeOffset liveStart = DateTimeOffset.UtcNow;
            string secondJournal = Path.Combine(journals, $"Journal.{liveStart:yyyy-MM-ddTHHmmss}.01.log");
            using (
                SrvSurvey.Desktop.Platform.CommanderClaimLease? unconsumedPeer =
                    SrvSurvey.Desktop.Platform.CommanderClaimRegistry.TryAcquire(temp.Paths.CacheDirectory, "F14004033")
            )
            {
                Assert.NotNull(unconsumedPeer);
                await File.WriteAllTextAsync(
                    secondJournal,
                    $$"""
                    {"timestamp":"{{liveStart:yyyy-MM-ddTHH:mm:ssZ}}","event":"Fileheader","part":1,"Odyssey":true}
                    {"timestamp":"{{liveStart.AddSeconds(
                        1
                    ):yyyy-MM-ddTHH:mm:ssZ}}","event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
                    {"timestamp":"{{liveStart.AddSeconds(
                        2
                    ):yyyy-MM-ddTHH:mm:ssZ}}","event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}
                    {"timestamp":"{{liveStart.AddSeconds(
                        3
                    ):yyyy-MM-ddTHH:mm:ssZ}}","event":"StartJump","JumpType":"Hyperspace","StarSystem":"Sol","SystemAddress":10477373803}
                    {"timestamp":"{{liveStart.AddSeconds(
                        4
                    ):yyyy-MM-ddTHH:mm:ssZ}}","event":"FSDJump","StarSystem":"Sol","SystemAddress":10477373803,"JumpDist":15.0}

                    """
                );
                File.SetLastWriteTimeUtc(secondJournal, liveStart.AddSeconds(4).UtcDateTime);
                await activeRuntime!.MainViewModel!.RefreshAsync();
                Assert.False(switchedToSecond.Task.IsCompleted);
            }

            // After the peer releases its claim and a fresh event arrives, the full unconsumed jump must apply live.
            await File.AppendAllTextAsync(
                secondJournal,
                $"{{\"timestamp\":\"{liveStart.AddSeconds(10):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}}\n"
            );
            File.SetLastWriteTimeUtc(secondJournal, liveStart.AddSeconds(10).UtcDateTime);

            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");
            CommanderProfileLoadResult afterFirstSwitch = await profileStore.LoadAsync("F14004033", isOdyssey: true);
            Assert.NotNull(afterFirstSwitch.Data);
            Assert.Equal(3, afterFirstSwitch.Data.Exploration.JumpCount);
            Assert.Equal(55.0, afterFirstSwitch.Data.Exploration.DistanceTravelled, 4);

            // 2. Switch back to Commander A, then simulate a peer confirming persistence of a prefix of thirdJournal before exiting.
            await File.AppendAllTextAsync(
                firstJournal,
                $"{{\"timestamp\":\"{liveStart.AddMinutes(5):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}}\n"
            );
            File.SetLastWriteTimeUtc(firstJournal, liveStart.AddMinutes(5).UtcDateTime);
            DesktopRuntime firstAgain = await WaitForTaskAsync(switchedBackToFirst.Task);
            await WaitUntilAsync(() => firstAgain.MainViewModel?.FrontierId == "F13228986");

            DateTimeOffset thirdStart = liveStart.AddMinutes(10);
            string thirdJournal = Path.Combine(journals, $"Journal.{thirdStart:yyyy-MM-ddTHHmmss}.01.log");
            string persistedPrefix =
                $"{{\"timestamp\":\"{thirdStart:yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"Fileheader\",\"part\":1,\"Odyssey\":true}}\n"
                + $"{{\"timestamp\":\"{thirdStart.AddSeconds(1):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"Commander\",\"Name\":\"Second Cmdr\",\"FID\":\"F14004033\"}}\n"
                + $"{{\"timestamp\":\"{thirdStart.AddSeconds(2):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"LoadGame\",\"Commander\":\"Second Cmdr\",\"FID\":\"F14004033\"}}\n"
                + $"{{\"timestamp\":\"{thirdStart.AddSeconds(3):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\",\"StarSystem\":\"Achenar\",\"SystemAddress\":10477373804}}\n"
                + $"{{\"timestamp\":\"{thirdStart.AddSeconds(4):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"FSDJump\",\"StarSystem\":\"Achenar\",\"SystemAddress\":10477373804,\"JumpDist\":20.0}}\n";
            string unpersistedTail =
                $"{{\"timestamp\":\"{thirdStart.AddMinutes(1).AddSeconds(3):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"StartJump\",\"JumpType\":\"Hyperspace\",\"StarSystem\":\"Alioth\",\"SystemAddress\":10477373805}}\n"
                + $"{{\"timestamp\":\"{thirdStart.AddMinutes(1).AddSeconds(4):yyyy-MM-ddTHH:mm:ssZ}\",\"event\":\"FSDJump\",\"StarSystem\":\"Alioth\",\"SystemAddress\":10477373805,\"JumpDist\":10.0}}\n";
            long persistedPrefixBytes = System.Text.Encoding.UTF8.GetByteCount(persistedPrefix);
            Assert.True(
                SrvSurvey.Desktop.Platform.CommanderJournalCheckpointStore.TrySave(
                    temp.Paths.CacheDirectory,
                    "F14004033",
                    new SrvSurvey.Core.Journal.JournalCheckpoint(thirdJournal, persistedPrefixBytes)
                )
            );
            await File.WriteAllTextAsync(thirdJournal, persistedPrefix + unpersistedTail);
            File.SetLastWriteTimeUtc(thirdJournal, thirdStart.AddMinutes(1).AddSeconds(4).UtcDateTime);

            long thirdJournalLength = new FileInfo(thirdJournal).Length;
            DesktopRuntime secondAgain = await WaitForTaskAsync(switchedToSecondAgain.Task);
            await WaitUntilAsync(() =>
                secondAgain.MainViewModel?.FrontierId == "F14004033"
                && secondAgain.MainViewModel.CurrentJournalCheckpoint?.ByteOffset == thirdJournalLength
            );
            CommanderProfileLoadResult afterConfirmedSwitch = await profileStore.LoadAsync(
                "F14004033",
                isOdyssey: true
            );
            Assert.NotNull(afterConfirmedSwitch.Data);
            // Only the 10.0 ly unpersisted tail is added; the 20.0 ly confirmed prefix runs as bootstrap.
            Assert.Equal(4, afterConfirmedSwitch.Data.Exploration.JumpCount);
            Assert.Equal(65.0, afterConfirmedSwitch.Data.Exploration.DistanceTravelled, 4);
        }
        finally
        {
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }
    }

    /// <summary>Declining a queued automatic switch releases the reserved claim and resumes polling, and pending startup update confirmation defers automatic switching.</summary>
    [AvaloniaFact]
    public async Task DeclinedAutomaticSwitchResumesPollingAndPendingUpdateConfirmationDefersSwitch()
    {
        using var temp = new ProfileSyncTestRoot();
        string journals = temp.Path("data", "journals");
        Directory.CreateDirectory(journals);
        string firstJournal = Path.Combine(journals, "Journal.2026-11-23T060000.01.log");
        await File.WriteAllTextAsync(
            firstJournal,
            """
            {"timestamp":"2026-11-23T06:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
            {"timestamp":"2026-11-23T06:00:01Z","event":"Commander","Name":"First Cmdr","FID":"F13228986"}
            {"timestamp":"2026-11-23T06:00:02Z","event":"LoadGame","Commander":"First Cmdr","FID":"F13228986"}

            """
        );
        File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 6, 0, 2, DateTimeKind.Utc));

        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var applicationLog = new ApplicationLogService(temp.Paths.DataDirectory);
        DesktopRuntime? activeRuntime = null;
        var switchedToSecond = new TaskCompletionSource<DesktopRuntime>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        var package = new SrvSurvey.Core.Updates.CrossPlatformReleasePackage(
            "linux-x64",
            "SrvSurvey.tar.gz",
            "tar.gz",
            1024,
            new string('c', 64),
            new Uri("https://example.invalid/SrvSurvey.tar.gz")
        );
        var releaseState = new ReleaseUpdateSessionState(
            HasCompletedCheck: true,
            UseDevelopmentReleases: true,
            LatestVersion: "2.2.0.0",
            StatusMessage: "Update available.",
            IsUpdateAvailable: true,
            ReleaseUri: new Uri("https://example.invalid/releases"),
            ReleasePackage: package,
            ReleaseVersion: SrvSurvey.Core.Updates.ReleaseVersion.Parse("2.2.0.0"),
            ReleaseNotes: "Notes",
            DismissedReleaseVersion: null,
            PreviousInstallationOutcomeMessage: null,
            HasAttemptedCheck: true
        );
        var referenceState = new ReferenceDataUpdateSessionState(
            HasCompletedRefresh: true,
            StatusMessage: "Published reference data is current.",
            UpdatedCatalogs: "None",
            BackupDirectory: "None",
            IsRestartRequired: false,
            HasAttemptedRefresh: true
        );

        string[] arguments = ["--journal-directory", journals];
        DesktopStartupContext startupContext = await DesktopStartupContext.ResolveAsync(
            arguments,
            () => temp.Paths,
            CancellationToken.None
        );

        try
        {
            activeRuntime = DesktopRuntime.Start(
                Application.Current!,
                desktop,
                new DesktopStartup(arguments, applicationLog)
                {
                    AppDataPathsOverride = startupContext.AppDataPaths,
                    ReleaseUpdateSessionState = releaseState,
                    ReferenceDataUpdateSessionState = referenceState,
                    GameWindowSwitcherFactory = () => new FixedGameWindowSwitcher(1),
                    OnRuntimeReplaced = replacement =>
                    {
                        activeRuntime = replacement;
                        if (
                            string.Equals(
                                replacement.MainViewModel?.AppDataPaths.SettingsFrontierId,
                                "F14004033",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            switchedToSecond.TrySetResult(replacement);
                        }
                    },
                },
                new RecordingDesktopLifetime([])
            );

            await WaitUntilAsync(() => activeRuntime?.MainViewModel?.FrontierId == "F13228986");

            // Hold a pending startup update confirmation so automatic switching defers until confirmation clears.
            SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.SetPendingConfirmation(
                Path.Combine(temp.Paths.DataDirectory, "pending-plan.json")
            );
            string secondJournal = Path.Combine(journals, "Journal.2026-11-23T070000.01.log");
            await File.WriteAllTextAsync(
                secondJournal,
                """
                {"timestamp":"2026-11-23T07:00:00Z","event":"Fileheader","part":1,"Odyssey":true}
                {"timestamp":"2026-11-23T07:00:01Z","event":"Commander","Name":"Second Cmdr","FID":"F14004033"}
                {"timestamp":"2026-11-23T07:00:02Z","event":"LoadGame","Commander":"Second Cmdr","FID":"F14004033"}

                """
            );
            File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 0, 2, DateTimeKind.Utc));

            // Pending update confirmation keeps automatic switching deferred.
            await activeRuntime!.MainViewModel!.RefreshAsync();
            Assert.False(activeRuntime.MainViewModel.CanProceedWithAutomaticCommanderSwitch());
            Assert.False(switchedToSecond.Task.IsCompleted);

            // Clear pending confirmation, then simulate an install starting in the gap between switch request and execution.
            SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.SetPendingConfirmation(null);
            var installStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseInstall = new TaskCompletionSource<SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            activeRuntime.MainViewModel.ReleaseUpdates.ConfigureInstallationWorkflow(
                new GatedInstallWorkflow(installStarted, releaseInstall.Task)
            );
            activeRuntime.MainViewModel.ReleaseUpdates.InstallConfirmed = true;
            Task? installTask = null;
            activeRuntime.MainViewModel.AutomaticCommanderSwitchRequested += _ =>
            {
                if (installTask is null)
                {
                    installTask = activeRuntime.MainViewModel.ReleaseUpdates.InstallAsync();
                }
            };

            // Trigger switch request -> handler starts InstallAsync synchronously before the posted SwitchCommanderAsync runs.
            await activeRuntime.MainViewModel.RefreshAsync();
            await installStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(activeRuntime.MainViewModel.ReleaseUpdates.IsInstalling);

            // Process queued UI work so SwitchCommanderAsync declines, releases F14004033's reservation, and resumes polling.
            await WaitUntilAsync(() =>
            {
                using SrvSurvey.Desktop.Platform.CommanderClaimLease? probe =
                    SrvSurvey.Desktop.Platform.CommanderClaimRegistry.TryAcquire(
                        temp.Paths.CacheDirectory,
                        "F14004033"
                    );
                return probe is not null;
            });
            Assert.False(switchedToSecond.Task.IsCompleted);

            // Verify live monitoring (MonitorAsync) stayed alive for F13228986 without manual RefreshAsync even while install is active.
            await File.AppendAllTextAsync(
                firstJournal,
                "{\"timestamp\":\"2026-11-23T07:00:05Z\",\"event\":\"Location\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803}\n"
            );
            File.SetLastWriteTimeUtc(firstJournal, new DateTime(2026, 11, 23, 7, 0, 5, DateTimeKind.Utc));
            await WaitUntilAsync(() => activeRuntime.MainViewModel.OverviewSystemName == "Sol");

            // Canceling the install allows the automatic commander switch to proceed cleanly.
            releaseInstall.SetResult(
                new SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult(
                    SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowStatus.Cancelled,
                    SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowStage.Downloading,
                    SrvSurvey.Desktop.Platform.ReleaseInstallationCleanupStatus.Succeeded
                )
            );
            await installTask!;

            await File.AppendAllTextAsync(
                secondJournal,
                "{\"timestamp\":\"2026-11-23T07:00:10Z\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}\n"
            );
            File.SetLastWriteTimeUtc(secondJournal, new DateTime(2026, 11, 23, 7, 0, 10, DateTimeKind.Utc));

            DesktopRuntime secondRuntime = await WaitForTaskAsync(switchedToSecond.Task);
            await WaitUntilAsync(() => secondRuntime.MainViewModel?.FrontierId == "F14004033");

            // Even if a commander switch runs while startup update confirmation is still pending, the replacement window runs confirmation on its first frame.
            SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.SetPendingConfirmation(
                Path.Combine(temp.Paths.DataDirectory, "replacement-plan.json")
            );
            Assert.True(SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.HasPendingStartupUpdateAction);
            await secondRuntime.SwitchCommanderAsync("F13228986");
            await WaitUntilAsync(() =>
                !SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.HasPendingStartupUpdateAction
            );
        }
        finally
        {
            SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.SetPendingConfirmation(null);
            SrvSurvey.Desktop.Platform.ApplicationUpdateBootstrap.SetPendingOutcome(null);
            if (activeRuntime is not null)
            {
                await activeRuntime.RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
            }
        }
    }

    private sealed class GatedInstallWorkflow(
        TaskCompletionSource started,
        Task<SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult> resultTask
    ) : SrvSurvey.Desktop.Platform.IReleaseInstallationWorkflow
    {
        public SrvSurvey.Desktop.Platform.ReleaseInstallationCapability Capability { get; } =
            new(SrvSurvey.Desktop.Platform.ReleaseInstallationCapabilityStatus.Supported);

        public SrvSurvey.Desktop.Platform.ReleaseInstallationCapability RefreshCapability() => Capability;

        public Task<SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowResult> ExecuteAsync(
            SrvSurvey.Desktop.Platform.ReleaseInstallationRequest request,
            IProgress<SrvSurvey.Desktop.Platform.ReleaseInstallationWorkflowProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            started.TrySetResult();
            return resultTask;
        }
    }

    private sealed class MutableSwitchWindowCount : IGameWindowSwitcher
    {
        public int WindowCount { get; set; } = 1;

        public int GetAvailableWindowCount() => WindowCount;

        public bool TryActivateCurrent() => WindowCount > 0;

        public bool TryActivateNext() => WindowCount > 1;

        public void Dispose() { }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for desktop runtime state.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
    }

    private static async Task<T> WaitForTaskAsync<T>(Task<T> task)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Timed out waiting for commander switch.");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        return await task;
    }

    private sealed class FixedGameWindowSwitcher(int windowCount) : IGameWindowSwitcher
    {
        public int GetAvailableWindowCount() => windowCount;

        public bool TryActivateCurrent() => windowCount > 0;

        public bool TryActivateNext() => windowCount > 1;

        public void Dispose() { }
    }

    private sealed class RecordingDesktopRuntimePhases : IDesktopRuntimePhases
    {
        private readonly List<string> events;

        public RecordingDesktopRuntimePhases(List<string> events)
        {
            this.events = events;
        }

        public TaskCompletionSource AllowProducerStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowWhenStoppingProducers { get; init; }

        public bool ThrowWhenQuiescing { get; init; }

        public bool ThrowWhenReportingFailure { get; init; }

        public Exception? StartupFailure { get; private set; }

        public Action<DesktopShutdownReason>? OnQuiesce { get; set; }

        public Action? OnDisposeViewModel { get; set; }

        /// <summary>Supplies asynchronous cleanup work to hold shutdown until a backup completes.</summary>
        public Func<Task>? DisposeViewModel { get; init; }

        public void Quiesce(DesktopShutdownReason reason)
        {
            events.Add($"quiesce:{reason}");
            OnQuiesce?.Invoke(reason);
            if (ThrowWhenQuiescing)
            {
                throw new InvalidOperationException("Quiesce failed.");
            }
        }

        public async Task StopProducersAsync()
        {
            events.Add("stop-producers");
            await AllowProducerStop.Task;
            if (ThrowWhenStoppingProducers)
            {
                throw new InvalidOperationException("Producer stop failed.");
            }
        }

        public Task DisposeDependentsAsync()
        {
            events.Add("dispose-dependents");
            return Task.CompletedTask;
        }

        /// <summary>Records view-model disposal and awaits any injected background backup work.</summary>
        public Task DisposeViewModelAsync()
        {
            events.Add("dispose-view-model");
            OnDisposeViewModel?.Invoke();
            return DisposeViewModel?.Invoke() ?? Task.CompletedTask;
        }

        public Task DisposeInfrastructureAsync()
        {
            events.Add("dispose-infrastructure");
            return Task.CompletedTask;
        }

        public void ReportShutdownFailure(Exception exception)
        {
            events.Add("report:" + exception.Message);
            if (ThrowWhenReportingFailure)
            {
                throw new InvalidOperationException("Failure reporting failed.");
            }
        }

        public void ReportStartupFailure(Exception exception)
        {
            StartupFailure = exception;
            events.Add("startup-failure:" + exception.Message);
        }
    }

    private sealed class RecordingDesktopLifetime : IDesktopRuntimeLifetime
    {
        private readonly List<string> events;
        private readonly Action? shutdown;

        public RecordingDesktopLifetime(List<string> events, Action? shutdown = null)
        {
            this.events = events;
            this.shutdown = shutdown;
        }

        public void Shutdown(int exitCode = 0)
        {
            events.Add($"shutdown:{exitCode}");
            shutdown?.Invoke();
        }
    }

    private sealed class RecordingDisposable(Action dispose) : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            dispose();
        }
    }
}
