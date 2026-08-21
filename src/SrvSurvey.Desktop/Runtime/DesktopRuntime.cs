namespace SrvSurvey.Desktop.Runtime;

internal enum DesktopShutdownReason
{
    MainWindowClose,
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
}

internal sealed class DesktopRuntime : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly IDesktopRuntimeLifetime lifetime;
    private readonly IDesktopRuntimePhases phases;
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

    internal static DesktopRuntime CreateForTests(
        IDesktopRuntimeLifetime lifetime,
        IDesktopRuntimePhases phases)
    {
        return new DesktopRuntime(lifetime, phases);
    }

    internal Task RequestShutdownAsync(
        DesktopShutdownReason reason,
        int exitCode = 0)
    {
        lock (sync)
        {
            shutdownTask ??= StopAsync(reason, exitCode);
            return shutdownTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
    }

    private async Task StopAsync(
        DesktopShutdownReason reason,
        int exitCode)
    {
        TryRun(() => phases.Quiesce(reason));
        await TryRunAsync(phases.StopProducersAsync);
        await TryRunAsync(phases.DisposeDependentsAsync);
        await TryRunAsync(phases.DisposeViewModelAsync);
        await TryRunAsync(phases.DisposeInfrastructureAsync);
        TryRun(() => lifetime.Shutdown(exitCode));
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
}
