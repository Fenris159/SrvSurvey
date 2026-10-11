using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
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
    /// <summary>Both colonization actions show a hidden available panel on their first press and keep checkbox state synchronized.</summary>
    [AvaloniaTheory]
    [InlineData(GlobalInputAction.ToggleColonizationCommoditiesVisibility)]
    [InlineData(GlobalInputAction.ShowColonyShopping)]
    public async Task ColonizationShortcutsToggleActualVisibilityAndPersistThroughDocking(GlobalInputAction action)
    {
        List<string> events = [];
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(new RecordingDesktopLifetime(events), phases);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(main);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(DesktopRuntime).GetField("mainViewModel", flags)!.SetValue(runtime, main);
        typeof(DesktopRuntime).GetField("mainWindow", flags)!.SetValue(runtime, window);
        MethodInfo execute = typeof(DesktopRuntime).GetMethod("ExecuteGlobalInputActionAsync", flags)!;
        try
        {
            Assert.False(await (Task<bool>)execute.Invoke(runtime, [action])!);
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
            overlay.Apply(plan, new EliteStatus { Flags = StatusFlags.InMainShip });
            using var host = new HostedOverlayTestHarness(OverlayWindowRegistry.Shared);
            using var coordinator = new ColonizationCommodityOverlayCoordinator(overlay, host.Session);
            typeof(DesktopRuntime)
                .GetField("colonizationCommodityOverlayCoordinator", flags)!
                .SetValue(runtime, coordinator);
            OverlayPanelVisibilityEntryViewModel panel = main.OverlayPanelVisibility.Panels.Single(item =>
                item.PlotterName == "PlotBuildCommodities"
            );
            Assert.True(panel.IsEnabled);
            Assert.False(coordinator.IsVisible);

            Assert.True(await (Task<bool>)execute.Invoke(runtime, [action])!);
            Assert.True(coordinator.IsVisible);
            overlay.Apply(
                plan,
                new EliteStatus { Flags = StatusFlags.Docked, GuiFocus = GuiFocus.StationServices },
                updatedHasMarketSinceDocking: true
            );
            overlay.Apply(plan, new EliteStatus { Flags = StatusFlags.InMainShip });
            Assert.True(coordinator.IsVisible);

            Assert.True(await (Task<bool>)execute.Invoke(runtime, [action])!);
            Assert.False(panel.IsEnabled);
            Assert.False(coordinator.IsVisible);
            Assert.True(await (Task<bool>)execute.Invoke(runtime, [action])!);
            Assert.True(panel.IsEnabled);
            Assert.True(coordinator.IsVisible);
        }
        finally
        {
            main.OverlayPanelVisibility.EnsureVisible("PlotBuildCommodities");
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
            Assert.True(OverlayWindowRegistry.Shared.ShouldPresent("PlotBuildCommodities"));
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
