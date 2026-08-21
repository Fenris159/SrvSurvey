using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Tests.Runtime;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DesktopRuntimeTests
{
    [Fact]
    public async Task ConcurrentShutdownRequestsShareOneOrderedStop()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);

        var first = runtime.RequestShutdownAsync(
            DesktopShutdownReason.MainWindowClose);
        var second = runtime.RequestShutdownAsync(
            DesktopShutdownReason.MainWindowClose);

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
            events);
    }

    [Fact]
    public async Task ReentrantShutdownRequestJoinsTheStopAlreadyInProgress()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);
        Task? reentrantRequest = null;
        phases.OnQuiesce = _ =>
            reentrantRequest = runtime.RequestShutdownAsync(
                DesktopShutdownReason.UpdateHandoff);

        var first = runtime.RequestShutdownAsync(
            DesktopShutdownReason.MainWindowClose);

        Assert.Same(first, reentrantRequest);
        phases.AllowProducerStop.SetResult();
        await first;
        Assert.Equal(1, events.Count(item => item.StartsWith(
            "quiesce:",
            StringComparison.Ordinal)));
        Assert.Equal(1, events.Count(item => item == "shutdown:0"));
    }

    [Fact]
    public async Task FailureReportingCannotPreventRemainingShutdownPhases()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events)
        {
            ThrowWhenStoppingProducers = true,
            ThrowWhenReportingFailure = true,
        };
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);

        await runtime.RequestShutdownAsync(
            DesktopShutdownReason.MainWindowClose);

        Assert.Equal(
            [
                "quiesce:MainWindowClose",
                "stop-producers",
                "report:Producer stop failed.",
                "dispose-dependents",
                "dispose-view-model",
                "dispose-infrastructure",
                "shutdown:0",
            ],
            events);
    }

    [Fact]
    public async Task StartupFailureIsPreservedWhileAcquiredRuntimeIsRolledBack()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        var expected = new InvalidOperationException("Startup failed.");
        var acquiredResource = new RecordingDisposable(
            () => events.Add("dispose-startup-resource"));
        phases.OnDisposeViewModel = acquiredResource.Dispose;

        await using var runtime = DesktopRuntime.StartForTests(
            lifetime,
            phases,
            () =>
            {
                events.Add("acquire-startup-resource");
                throw expected;
            });
        await runtime.RequestShutdownAsync(
            DesktopShutdownReason.StartupFailure);

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
            events);
        Assert.True(acquiredResource.IsDisposed);
    }

    [AvaloniaFact]
    public async Task MainWindowCloseWaitsForRuntimeCleanup()
    {
        List<string> events = [];
        var window = new Window();
        var lifetime = new RecordingDesktopLifetime(events, window.Close);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);
        runtime.AttachMainWindow(window);
        window.Show();

        window.Close();

        Assert.True(window.IsVisible);
        Assert.False(window.IsEnabled);
        phases.AllowProducerStop.SetResult();
        await runtime.RequestShutdownAsync(
            DesktopShutdownReason.MainWindowClose);
        Assert.False(window.IsVisible);
    }

    [Fact]
    public async Task OperatingSystemShutdownIsNotCancelled()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);

        var cancel = runtime.RequestMainWindowClose(
            WindowCloseReason.OSShutdown);

        Assert.False(cancel);
        Assert.Equal(
            [
                "quiesce:OperatingSystemShutdown",
                "stop-producers",
            ],
            events);
        phases.AllowProducerStop.SetResult();
        await runtime.RequestShutdownAsync(
            DesktopShutdownReason.OperatingSystemShutdown);
    }

    [Theory]
    [InlineData((int)DesktopShutdownReason.JournalCommand)]
    [InlineData((int)DesktopShutdownReason.RemoteInstanceRequest)]
    [InlineData((int)DesktopShutdownReason.LinuxTermination)]
    public async Task InternalExitReasonEntersTheSharedStopPipeline(
        int reasonValue)
    {
        var reason = (DesktopShutdownReason)reasonValue;
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        phases.AllowProducerStop.SetResult();
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);

        await runtime.RequestShutdownAsync(reason);

        Assert.Equal($"quiesce:{reason}", events[0]);
        Assert.Equal("shutdown:0", events[^1]);
    }

    [AvaloniaFact]
    public async Task RestartLaunchFailureLeavesTheRuntimeRunning()
    {
        List<string> events = [];
        var lifetime = new RecordingDesktopLifetime(events);
        var phases = new RecordingDesktopRuntimePhases(events);
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);
        var expected = new InvalidOperationException("Launch failed.");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.RestartAsync(() => throw expected));

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
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);
        runtime.AttachMainWindow(window);
        window.Show();

        var restarting = runtime.RestartAsync(() => events.Add("launch"));

        Assert.False(window.IsVisible);
        Assert.Equal(
            [
                "launch",
                "quiesce:Restart",
                "stop-producers",
            ],
            events);
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
        await using var runtime = DesktopRuntime.CreateForTests(
            lifetime,
            phases);
        runtime.AttachMainWindow(window);
        window.Show();

        var stopping = runtime.RequestShutdownAsync(
            DesktopShutdownReason.UpdateHandoff);

        Assert.False(window.IsVisible);
        Assert.Equal(
            [
                "quiesce:UpdateHandoff",
                "stop-producers",
            ],
            events);
        phases.AllowProducerStop.SetResult();
        await stopping;
    }

    private sealed class RecordingDesktopRuntimePhases
        : IDesktopRuntimePhases
    {
        private readonly List<string> events;

        public RecordingDesktopRuntimePhases(List<string> events)
        {
            this.events = events;
        }

        public TaskCompletionSource AllowProducerStop { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowWhenStoppingProducers { get; init; }

        public bool ThrowWhenReportingFailure { get; init; }

        public Exception? StartupFailure { get; private set; }

        public Action<DesktopShutdownReason>? OnQuiesce { get; set; }

        public Action? OnDisposeViewModel { get; set; }

        public void Quiesce(DesktopShutdownReason reason)
        {
            events.Add($"quiesce:{reason}");
            OnQuiesce?.Invoke(reason);
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
            return Task.CompletedTask;
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
                throw new InvalidOperationException(
                    "Failure reporting failed.");
            }
        }

        public void ReportStartupFailure(Exception exception)
        {
            StartupFailure = exception;
            events.Add("startup-failure:" + exception.Message);
        }
    }

    private sealed class RecordingDesktopLifetime
        : IDesktopRuntimeLifetime
    {
        private readonly List<string> events;
        private readonly Action? shutdown;

        public RecordingDesktopLifetime(
            List<string> events,
            Action? shutdown = null)
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
