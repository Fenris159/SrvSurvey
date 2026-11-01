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
