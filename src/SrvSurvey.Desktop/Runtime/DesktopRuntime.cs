using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Runtime;

internal sealed record DesktopStartup(
    string[] Arguments,
    ApplicationLogService? ApplicationLog)
{
    internal AppDataPaths? AppDataPathsOverride { get; init; }

    internal Action<DesktopStartupCheckpoint>? Checkpoint { get; init; }
}

internal enum DesktopStartupCheckpoint
{
    OverlayInfrastructureReady,
    MainViewModelDependenciesReady,
    MainWindowReady,
    OverlayDependentsReady,
    ProducersReady,
}

internal enum DesktopShutdownReason
{
    MainWindowClose,
    StartupFailure,
    JournalCommand,
    Restart,
    UpdateHandoff,
    RemoteInstanceRequest,
    LinuxTermination,
    OperatingSystemShutdown,
}

internal interface IDesktopRuntimeLifetime
{
    void Shutdown(int exitCode = 0);
}

internal interface IDesktopRuntimePhases
{
    void Quiesce(DesktopShutdownReason reason);

    Task StopProducersAsync();

    Task DisposeDependentsAsync();

    Task DisposeViewModelAsync();

    Task DisposeInfrastructureAsync();

    void ReportShutdownFailure(Exception exception);

    void ReportStartupFailure(Exception exception);
}

internal sealed partial class DesktopRuntime : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IDesktopRuntimeLifetime lifetime;
    private readonly IDesktopRuntimePhases phases;
    private Window? attachedMainWindow;
    private Task? shutdownTask;

    private DesktopRuntime(
        IDesktopRuntimeLifetime lifetime,
        IDesktopRuntimePhases phases)
    {
        this.lifetime = lifetime
            ?? throw new ArgumentNullException(nameof(lifetime));
        this.phases = phases
            ?? throw new ArgumentNullException(nameof(phases));
    }

    private DesktopRuntime(IDesktopRuntimeLifetime lifetime)
    {
        this.lifetime = lifetime
            ?? throw new ArgumentNullException(nameof(lifetime));
        phases = new ProductionDesktopRuntimePhases(this);
    }

    private DesktopRuntime(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(new AvaloniaDesktopRuntimeLifetime(lifetime))
    {
    }

    internal static DesktopRuntime Start(
        Application application,
        IClassicDesktopStyleApplicationLifetime lifetime,
        DesktopStartup startup)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(startup);

        var runtime = new DesktopRuntime(lifetime);
        runtime.applicationLogService = startup.ApplicationLog;
        try
        {
            runtime.InitializeDesktopApplication(application, lifetime, startup);
        }
        catch (Exception exception)
        {
            runtime.BeginStartupFailure(exception);
        }

        return runtime;
    }

    internal static DesktopRuntime CreateForTests(
        IDesktopRuntimeLifetime lifetime,
        IDesktopRuntimePhases phases)
    {
        return new DesktopRuntime(lifetime, phases);
    }

    internal static DesktopRuntime StartForTests(
        IDesktopRuntimeLifetime lifetime,
        IDesktopRuntimePhases phases,
        Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        var runtime = new DesktopRuntime(lifetime, phases);
        try
        {
            initialize();
        }
        catch (Exception exception)
        {
            runtime.BeginStartupFailure(exception);
        }

        return runtime;
    }

    internal static DesktopRuntime StartCompositionForTests(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        DesktopStartup startup,
        IDesktopRuntimeLifetime lifetime,
        DesktopStartupCheckpoint failureCheckpoint)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(startup);
        var runtime = new DesktopRuntime(lifetime);
        runtime.applicationLogService = startup.ApplicationLog;
        try
        {
            runtime.InitializeDesktopApplication(
                application,
                desktop,
                startup with
                {
                    Checkpoint = checkpoint =>
                    {
                        if (checkpoint == failureCheckpoint)
                        {
                            throw new InvalidOperationException(
                                $"Startup failed at {checkpoint}.");
                        }
                    },
                });
        }
        catch (Exception exception)
        {
            runtime.BeginStartupFailure(exception);
        }

        return runtime;
    }

    internal Task RequestShutdownAsync(
        DesktopShutdownReason reason,
        int exitCode = 0)
    {
        TaskCompletionSource completion;
        lock (sync)
        {
            if (shutdownTask is not null)
            {
                return shutdownTask;
            }

            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = completion.Task;
        }

        _ = CompleteStopAsync(completion, reason, exitCode);
        return completion.Task;
    }

    internal void AttachMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (attachedMainWindow is not null)
        {
            throw new InvalidOperationException(
                "The desktop runtime already has a main window.");
        }

        attachedMainWindow = window;
        window.Closing += HandleMainWindowClosing;
    }

    internal bool RequestMainWindowClose(WindowCloseReason closeReason)
    {
        if (closeReason == WindowCloseReason.OSShutdown)
        {
            _ = RequestShutdownAsync(
                DesktopShutdownReason.OperatingSystemShutdown);
            return false;
        }

        _ = RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
        return true;
    }

    internal async Task RestartAsync(Action startReplacement)
    {
        ArgumentNullException.ThrowIfNull(startReplacement);
        startReplacement();
        await RequestShutdownOnUiThreadAsync(DesktopShutdownReason.Restart);
    }

    public async ValueTask DisposeAsync()
    {
        await RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
    }

    private async Task StopAsync(
        DesktopShutdownReason reason,
        int exitCode)
    {
        TryRun(() => QuiesceMainWindow(reason));
        TryRun(() => phases.Quiesce(reason));
        await TryRunAsync(phases.StopProducersAsync);
        await TryRunAsync(phases.DisposeDependentsAsync);
        await TryRunAsync(phases.DisposeViewModelAsync);
        await TryRunAsync(phases.DisposeInfrastructureAsync);
        TryRun(() => lifetime.Shutdown(exitCode));
    }

    private async Task CompleteStopAsync(
        TaskCompletionSource completion,
        DesktopShutdownReason reason,
        int exitCode)
    {
        try
        {
            await StopAsync(reason, exitCode);
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private void HandleMainWindowClosing(
        object? sender,
        WindowClosingEventArgs eventArgs)
    {
        eventArgs.Cancel = RequestMainWindowClose(eventArgs.CloseReason);
    }

    private void QuiesceMainWindow(DesktopShutdownReason reason)
    {
        var window = attachedMainWindow;
        attachedMainWindow = null;
        if (window is null)
        {
            return;
        }

        window.Closing -= HandleMainWindowClosing;
        if (reason == DesktopShutdownReason.OperatingSystemShutdown)
        {
            return;
        }

        window.IsEnabled = false;
        if (reason is DesktopShutdownReason.Restart
            or DesktopShutdownReason.UpdateHandoff)
        {
            window.Hide();
        }
    }

    private void BeginStartupFailure(Exception exception)
    {
        TryReportStartupFailure(exception);
        _ = RequestShutdownAsync(
            DesktopShutdownReason.StartupFailure,
            exitCode: 1);
    }

    private void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            TryReportShutdownFailure(exception);
        }
    }

    private async Task TryRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            TryReportShutdownFailure(exception);
        }
    }

    private void TryReportShutdownFailure(Exception exception)
    {
        try
        {
            phases.ReportShutdownFailure(exception);
        }
        catch
        {
            // Shutdown continues even when its diagnostic adapter is unavailable.
        }
    }

    private void TryReportStartupFailure(Exception exception)
    {
        try
        {
            phases.ReportStartupFailure(exception);
        }
        catch
        {
            // Startup rollback still runs when diagnostics are unavailable.
        }
    }

    private sealed class AvaloniaDesktopRuntimeLifetime(
        IClassicDesktopStyleApplicationLifetime lifetime)
        : IDesktopRuntimeLifetime
    {
        public void Shutdown(int exitCode = 0)
        {
            lifetime.Shutdown(exitCode);
        }
    }

    private sealed class ProductionDesktopRuntimePhases(DesktopRuntime runtime)
        : IDesktopRuntimePhases
    {
        public void Quiesce(DesktopShutdownReason reason)
        {
            runtime.QuiesceDesktopRuntime(reason);
        }

        public Task StopProducersAsync()
        {
            return runtime.StopDesktopProducersAsync();
        }

        public Task DisposeDependentsAsync()
        {
            return runtime.DisposeDesktopDependentsAsync();
        }

        public Task DisposeViewModelAsync()
        {
            return runtime.DisposeMainViewModelAsync();
        }

        public Task DisposeInfrastructureAsync()
        {
            return runtime.DisposeDesktopInfrastructureAsync();
        }

        public void ReportShutdownFailure(Exception exception)
        {
            runtime.TryAppendShutdownFailure(exception);
        }

        public void ReportStartupFailure(Exception exception)
        {
            runtime.TryAppendStartupFailure(exception);
        }
    }
}
