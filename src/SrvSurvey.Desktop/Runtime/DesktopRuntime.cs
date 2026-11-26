using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Runtime;

internal sealed record CommanderSwitchWindowState(bool WasHiddenToTray, bool WasMinimized, bool WasActive);

internal sealed record DesktopStartup(string[] Arguments, ApplicationLogService? ApplicationLog)
{
    internal AppDataPaths? AppDataPathsOverride { get; init; }

    internal DiagnosticReplayContext? DiagnosticReplay { get; init; }

    internal bool BringMainWindowToFront { get; init; }

    internal bool IsCommanderSwitch { get; init; }

    internal CommanderSwitchWindowState? CommanderSwitchWindowState { get; init; }

    internal Action<DesktopStartupCheckpoint>? Checkpoint { get; init; }

    internal Action<DesktopRuntime>? OnRuntimeReplaced { get; init; }

    internal Func<SrvSurvey.Desktop.Platform.Overlay.IGameWindowSwitcher>? GameWindowSwitcherFactory { get; init; }

    internal SrvSurvey.Desktop.ProfileSync.ProfileSyncService? ProfileSyncService { get; init; }

    internal SrvSurvey.Desktop.Platform.CommanderClaimLease? CommanderClaimLease { get; init; }

    internal IReadOnlyDictionary<string, JournalCheckpoint>? CommanderJournalCheckpoints { get; init; }

    internal JournalBaselineSnapshot? CommanderJournalBaseline { get; init; }

    internal JournalSwitchResume? CommanderSwitchJournalResume { get; init; }

    internal ReleaseUpdateSessionState? ReleaseUpdateSessionState { get; init; }

    internal Task<ReleaseUpdateSessionState>? PendingReleaseUpdateCheck { get; init; }

    internal ReferenceDataUpdateSessionState? ReferenceDataUpdateSessionState { get; init; }

    internal Task<ReferenceDataUpdateSessionState>? PendingReferenceDataRefresh { get; init; }
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
    CommanderSwitch,
    UpdateHandoff,
    RemoteInstanceRequest,
    LinuxTermination,
    OperatingSystemShutdown,
    RuntimeDisposed,
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
    private readonly Lock sync = new();
    private readonly IDesktopRuntimeLifetime lifetime;
    private readonly IDesktopRuntimePhases phases;
    private Window? attachedMainWindow;
    private Task? shutdownTask;
    private DesktopShutdownReason? activeShutdownReason;
    private (DesktopShutdownReason Reason, int ExitCode)? pendingShutdown;
    private DesktopRuntime? replacementRuntime;

    internal bool HasLinuxTerminationRegistration => linuxTerminationRegistration is not null;

    internal MainWindowViewModel? MainViewModel => mainViewModel;

    internal MainWindow? MainWindow => mainWindow;

    private DesktopRuntime(IDesktopRuntimeLifetime lifetime, IDesktopRuntimePhases phases)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        this.phases = phases ?? throw new ArgumentNullException(nameof(phases));
    }

    private DesktopRuntime(IDesktopRuntimeLifetime lifetime)
    {
        this.lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        phases = new ProductionDesktopRuntimePhases(this);
    }

    private DesktopRuntime(IClassicDesktopStyleApplicationLifetime lifetime)
        : this(new AvaloniaDesktopRuntimeLifetime(lifetime)) { }

    internal static DesktopRuntime Start(
        Application application,
        IClassicDesktopStyleApplicationLifetime lifetime,
        DesktopStartup startup
    ) => Start(application, lifetime, startup, new AvaloniaDesktopRuntimeLifetime(lifetime));

    internal static DesktopRuntime Start(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        DesktopStartup startup,
        IDesktopRuntimeLifetime lifetime
    )
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(lifetime);

        var runtime = new DesktopRuntime(lifetime);
        runtime.applicationLogService = startup.ApplicationLog;
        try
        {
            runtime.InitializeDesktopApplication(application, desktop, startup);
        }
        catch (Exception exception)
        {
            runtime.BeginStartupFailure(exception);
        }

        return runtime;
    }

    internal static DesktopRuntime CreateForTests(IDesktopRuntimeLifetime lifetime, IDesktopRuntimePhases phases)
    {
        return new DesktopRuntime(lifetime, phases);
    }

    internal static DesktopRuntime StartForTests(
        IDesktopRuntimeLifetime lifetime,
        IDesktopRuntimePhases phases,
        Action initialize
    )
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

    /// <summary>Observes real composition at resource checkpoints and injects a failure to exercise startup rollback.</summary>
    internal static DesktopRuntime StartCompositionForTests(
        Application application,
        IClassicDesktopStyleApplicationLifetime desktop,
        DesktopStartup startup,
        IDesktopRuntimeLifetime lifetime,
        DesktopStartupCheckpoint failureCheckpoint,
        Action<DesktopRuntime, MainWindow?, DesktopStartupCheckpoint>? observe = null
    )
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
                        observe?.Invoke(runtime, runtime.mainWindow, checkpoint);
                        if (checkpoint == failureCheckpoint)
                        {
                            throw new InvalidOperationException($"Startup failed at {checkpoint}.");
                        }
                    },
                }
            );
        }
        catch (Exception exception)
        {
            runtime.BeginStartupFailure(exception);
        }

        return runtime;
    }

    internal Task RequestShutdownAsync(DesktopShutdownReason reason, int exitCode = 0)
    {
        DesktopRuntime? forwardTo = null;
        TaskCompletionSource? completion = null;
        lock (sync)
        {
            if (shutdownTask is not null)
            {
                if (activeShutdownReason == DesktopShutdownReason.CommanderSwitch)
                {
                    if (replacementRuntime is not null)
                    {
                        forwardTo = replacementRuntime;
                    }
                    else
                    {
                        pendingShutdown ??= (reason, exitCode);
                    }
                }

                if (forwardTo is null)
                {
                    return shutdownTask;
                }
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdownTask = completion.Task;
                activeShutdownReason = reason;
            }
        }

        if (forwardTo is not null)
        {
            return forwardTo.RequestShutdownAsync(reason, exitCode);
        }

        _ = CompleteStopAsync(completion!, reason, exitCode);
        return completion!.Task;
    }

    /// <summary>Replaces the active commander-scoped runtime in-process when Automatic mode detects a new commander.</summary>
    internal Task SwitchCommanderAsync(string nextFrontierId, bool isAutomatic = false)
    {
        string? normalized = SrvSurvey.Desktop.Configuration.CommanderSettingsProfile.NormalizeFrontierId(
            nextFrontierId
        );
        TaskCompletionSource? completion = null;
        lock (sync)
        {
            if (shutdownTask is not null)
            {
                return shutdownTask;
            }

            if (
                normalized is not null
                && hostApplication is not null
                && hostDesktop is not null
                && mainViewModel?.ReleaseUpdates.IsInstalling != true
                && (!isAutomatic || mainViewModel?.CanProceedWithAutomaticCommanderSwitch() != false)
            )
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdownTask = completion.Task;
                activeShutdownReason = DesktopShutdownReason.CommanderSwitch;
            }
        }

        if (completion is null)
        {
            mainViewModel?.CancelAutomaticCommanderSwitch();
            return Task.CompletedTask;
        }

        _ = CompleteCommanderSwitchAsync(completion, normalized!);
        return completion.Task;
    }

    internal void AttachMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (attachedMainWindow is not null)
        {
            throw new InvalidOperationException("The desktop runtime already has a main window.");
        }

        attachedMainWindow = window;
        window.Closing += HandleMainWindowClosing;
    }

    internal bool RequestMainWindowClose(WindowCloseReason closeReason)
    {
        if (closeReason == WindowCloseReason.OSShutdown)
        {
            _ = RequestShutdownAsync(DesktopShutdownReason.OperatingSystemShutdown);
            return false;
        }

        _ = RequestShutdownAsync(DesktopShutdownReason.MainWindowClose);
        return true;
    }

    internal async Task RestartAsync(Action startReplacement)
    {
        ArgumentNullException.ThrowIfNull(startReplacement);
        startReplacement();
        await RequestShutdownOnUiThreadAsync(DesktopShutdownReason.Restart, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await RequestShutdownAsync(DesktopShutdownReason.RuntimeDisposed);
    }

    /// <summary>Finishes dependent services and background backups before releasing the close guard and exiting.</summary>
    private async Task StopAsync(DesktopShutdownReason reason, int exitCode)
    {
        _ = await TeardownOutgoingRuntimeAsync(reason, preserveForSwitch: false, nextFrontierId: null);
        TryRun(() => lifetime.Shutdown(exitCode));
    }

    private async Task<OutgoingSwitchResources> TeardownOutgoingRuntimeAsync(
        DesktopShutdownReason reason,
        bool preserveForSwitch,
        string? nextFrontierId,
        string? outgoingFrontierId = null
    )
    {
        TryRun(() => QuiesceMainWindow(reason));
        TryRun(() => phases.Quiesce(reason));
        await TryRunAsync(phases.StopProducersAsync);
        await TryRunAsync(phases.DisposeDependentsAsync);
        SrvSurvey.Desktop.ProfileSync.ProfileSyncService? preservedProfileSync = preserveForSwitch
            ? mainViewModel?.DetachProfileSyncService()
            : null;
        SrvSurvey.Desktop.Platform.CommanderClaimLease? preservedCommanderClaim = preserveForSwitch
            ? mainViewModel?.DetachReservedCommanderClaim(nextFrontierId)
            : null;
        ReleaseUpdateSessionState? releaseUpdateState = preserveForSwitch
            ? mainViewModel?.ReleaseUpdates.CaptureSessionState()
            : null;
        Task<ReleaseUpdateSessionState>? pendingReleaseCheck = preserveForSwitch
            ? mainViewModel?.ReleaseUpdates.CapturePendingCheckAsync()
            : null;
        ReferenceDataUpdateSessionState? referenceDataUpdateState = preserveForSwitch
            ? mainViewModel?.ReferenceDataUpdates.CaptureSessionState()
            : null;
        Task<ReferenceDataUpdateSessionState>? pendingReferenceRefresh = preserveForSwitch
            ? mainViewModel?.ReferenceDataUpdates.CapturePendingRefreshAsync()
            : null;
        string? confirmedOutgoingFrontierId = mainViewModel?.ProjectedFrontierId ?? outgoingFrontierId;
        Dictionary<string, JournalCheckpoint>? checkpoints =
            preserveForSwitch && nextFrontierId is not null
                ? CaptureCommanderJournalCheckpoints(confirmedOutgoingFrontierId)
                : null;
        JournalBaselineSnapshot? journalBaseline = preserveForSwitch
            ? mainViewModel?.CurrentJournalBaseline ?? commanderJournalBaseline
            : null;
        await TryRunAsync(phases.DisposeViewModelAsync);
        await TryRunAsync(phases.DisposeInfrastructureAsync);
        TryRun(ReleaseMainWindowCloseGuard);
        return new OutgoingSwitchResources(
            preservedProfileSync,
            preservedCommanderClaim,
            checkpoints,
            journalBaseline,
            releaseUpdateState,
            pendingReleaseCheck,
            referenceDataUpdateState,
            pendingReferenceRefresh
        );
    }

    private sealed record OutgoingSwitchResources(
        SrvSurvey.Desktop.ProfileSync.ProfileSyncService? ProfileSync,
        SrvSurvey.Desktop.Platform.CommanderClaimLease? CommanderClaim,
        Dictionary<string, JournalCheckpoint>? CommanderJournalCheckpoints,
        JournalBaselineSnapshot? CommanderJournalBaseline,
        ReleaseUpdateSessionState? ReleaseUpdateSessionState,
        Task<ReleaseUpdateSessionState>? PendingReleaseUpdateCheck,
        ReferenceDataUpdateSessionState? ReferenceDataUpdateSessionState,
        Task<ReferenceDataUpdateSessionState>? PendingReferenceDataRefresh
    );

    private async Task CompleteStopAsync(TaskCompletionSource completion, DesktopShutdownReason reason, int exitCode)
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

    private async Task CompleteCommanderSwitchAsync(TaskCompletionSource completion, string nextFrontierId)
    {
        try
        {
            await SwitchCommanderCoreAsync(nextFrontierId);
            completion.SetResult();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    /// <summary>Tears down the outgoing commander's workspaces and composes a fresh runtime for the active commander.</summary>
    private async Task SwitchCommanderCoreAsync(string nextFrontierId)
    {
        Application application = hostApplication!;
        IClassicDesktopStyleApplicationLifetime desktop = hostDesktop!;
        Window? oldWindow = attachedMainWindow;
        CommanderSwitchWindowState? windowState = CaptureWindowStateForCommanderSwitch();
        AppDataPaths basePaths = mainViewModel?.AppDataPaths ?? startupAppDataPaths ?? AppDataPaths.ResolveCurrent();
        AppDataPaths nextPaths = basePaths with { SettingsFrontierId = nextFrontierId };
        string? outgoingFrontierId = basePaths.SettingsFrontierId ?? mainViewModel?.TargetFrontierId;
        string[] nextArguments = StartupOptions.WithoutInstanceFrontierId(originalStartupArguments);
        ApplicationLogService? log = applicationLogService;
        Action<DesktopRuntime>? replacedCallback = onRuntimeReplaced;
        Func<SrvSurvey.Desktop.Platform.Overlay.IGameWindowSwitcher>? switcherFactory = gameWindowSwitcherFactory;

        try
        {
            OutgoingSwitchResources handoff = await TeardownOutgoingRuntimeAsync(
                DesktopShutdownReason.CommanderSwitch,
                preserveForSwitch: true,
                nextFrontierId: nextFrontierId,
                outgoingFrontierId: outgoingFrontierId
            );
            TryRun(() => oldWindow?.Close());

            (DesktopShutdownReason Reason, int ExitCode)? shutdownRequest;
            lock (sync)
            {
                shutdownRequest = pendingShutdown;
            }

            if (shutdownRequest is { } pending)
            {
                if (handoff.CommanderClaim is not null)
                {
                    TryRun(handoff.CommanderClaim.Dispose);
                }

                if (handoff.ProfileSync is not null)
                {
                    await TryRunAsync(() => handoff.ProfileSync.ShutdownAsync(CancellationToken.None));
                    TryRun(handoff.ProfileSync.Dispose);
                }

                log?.Append($"Application exit ({pending.Reason}).");
                TryRun(() => lifetime.Shutdown(pending.ExitCode));
                return;
            }

            JournalCheckpoint? nextCheckpoint = handoff.CommanderJournalCheckpoints?.GetValueOrDefault(nextFrontierId);
            IReadOnlyDictionary<string, long> confirmedOffsets =
                SrvSurvey.Desktop.Platform.CommanderJournalCheckpointStore.TryLoad(
                    nextPaths.CacheDirectory,
                    nextFrontierId
                );
            Program.UpdateStartupContextPaths(nextPaths);
            log?.Append($"Automatic commander switch: reloading desktop runtime for {nextFrontierId}.");
            DesktopRuntime replacement = Start(
                application,
                desktop,
                new DesktopStartup(nextArguments, log)
                {
                    AppDataPathsOverride = nextPaths,
                    ProfileSyncService = handoff.ProfileSync,
                    CommanderClaimLease = handoff.CommanderClaim,
                    CommanderJournalCheckpoints = handoff.CommanderJournalCheckpoints,
                    CommanderJournalBaseline = handoff.CommanderJournalBaseline,
                    CommanderSwitchJournalResume = new JournalSwitchResume(
                        nextCheckpoint,
                        handoff.CommanderJournalBaseline,
                        confirmedOffsets.Count > 0 ? confirmedOffsets : null
                    ),
                    ReleaseUpdateSessionState = handoff.ReleaseUpdateSessionState,
                    PendingReleaseUpdateCheck = handoff.PendingReleaseUpdateCheck,
                    ReferenceDataUpdateSessionState = handoff.ReferenceDataUpdateSessionState,
                    PendingReferenceDataRefresh = handoff.PendingReferenceDataRefresh,
                    IsCommanderSwitch = true,
                    CommanderSwitchWindowState = windowState,
                    OnRuntimeReplaced = replacedCallback,
                    GameWindowSwitcherFactory = switcherFactory,
                },
                lifetime
            );
            (DesktopShutdownReason Reason, int ExitCode)? lateShutdown;
            lock (sync)
            {
                replacementRuntime = replacement;
                lateShutdown = pendingShutdown;
            }

            if (lateShutdown is { } late)
            {
                _ = replacement.RequestShutdownAsync(late.Reason, late.ExitCode);
            }

            replacedCallback?.Invoke(replacement);
        }
        finally
        {
            DisposeResource(ref linuxTerminationRegistration);
        }
    }

    private Dictionary<string, JournalCheckpoint> CaptureCommanderJournalCheckpoints(string? outgoingFrontierId)
    {
        var checkpoints = new Dictionary<string, JournalCheckpoint>(
            commanderJournalCheckpoints,
            StringComparer.OrdinalIgnoreCase
        );
        string? normalizedOutgoing = SrvSurvey.Desktop.Configuration.CommanderSettingsProfile.NormalizeFrontierId(
            outgoingFrontierId
        );
        if (normalizedOutgoing is not null && mainViewModel?.CurrentJournalCheckpoint is { } outgoingCheckpoint)
        {
            checkpoints[normalizedOutgoing] = outgoingCheckpoint;
        }

        return checkpoints;
    }

    private CommanderSwitchWindowState? CaptureWindowStateForCommanderSwitch() =>
        attachedMainWindow is { } window
            ? new CommanderSwitchWindowState(
                WasHiddenToTray: !window.IsVisible && !window.ShowInTaskbar,
                WasMinimized: window.WindowState == WindowState.Minimized,
                WasActive: window.IsActive
            )
            : null;

    private void HandleMainWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        eventArgs.Cancel = RequestMainWindowClose(eventArgs.CloseReason);
    }

    /// <summary>Dismisses the main window immediately while retaining its close guard during background cleanup.</summary>
    private void QuiesceMainWindow(DesktopShutdownReason reason)
    {
        Window? window = attachedMainWindow;
        if (window is null)
        {
            return;
        }

        if (reason == DesktopShutdownReason.OperatingSystemShutdown)
        {
            return;
        }

        window.IsEnabled = false;
        window.Hide();
    }

    /// <summary>Allows the final lifetime shutdown to close the hidden window after all pending work has finished.</summary>
    private void ReleaseMainWindowCloseGuard()
    {
        Window? window = attachedMainWindow;
        attachedMainWindow = null;
        window?.Closing -= HandleMainWindowClosing;
    }

    private void BeginStartupFailure(Exception exception)
    {
        TryReportStartupFailure(exception);
        _ = RequestShutdownAsync(DesktopShutdownReason.StartupFailure, exitCode: 1);
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

    private sealed class AvaloniaDesktopRuntimeLifetime(IClassicDesktopStyleApplicationLifetime lifetime)
        : IDesktopRuntimeLifetime
    {
        public void Shutdown(int exitCode = 0)
        {
            lifetime.Shutdown(exitCode);
        }
    }

    private sealed class ProductionDesktopRuntimePhases(DesktopRuntime runtime) : IDesktopRuntimePhases
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
