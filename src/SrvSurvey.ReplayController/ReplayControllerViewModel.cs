using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Diagnostics.Replay;

namespace SrvSurvey.ReplayController;

public sealed class ReplayControllerViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly string managedRoot;
    private readonly IDiagnosticInstanceLauncher instanceLauncher;
    private readonly ReplaySessionManager sessionManager;
    private readonly Func<DiagnosticReplaySession, JournalReplayPlayer>
        playerFactory;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly SynchronizationContext? synchronizationContext;
    private readonly AsyncCommand launchCommand;
    private readonly AsyncCommand stopCommand;
    private readonly AsyncCommand restartCommand;
    private readonly AsyncCommand previousCommand;
    private readonly AsyncCommand stepCommand;
    private readonly AsyncCommand playCommand;
    private readonly RelayCommand pauseCommand;
    private DiagnosticReplaySession? session;
    private JournalReplayPlayer? player;
    private IDiagnosticInstance? instance;
    private CancellationTokenSource? instanceMonitorCancellation;
    private Task? instanceMonitorTask;
    private CancellationTokenSource? playbackCancellation;
    private Task? playbackTask;
    private JournalReplayEvent? currentEvent;
    private JournalReplayEvent? selectedEvent;
    private string sourcePath = string.Empty;
    private string srvSurveyExecutablePath;
    private string statusMessage = "Import a journal or .srvreplay package to begin.";
    private double speedMultiplier = 1;
    private bool isBusy;
    private bool isPlaying;

    public ReplayControllerViewModel(
        string managedRoot,
        IDiagnosticInstanceLauncher? instanceLauncher = null,
        ReplaySessionManager? sessionManager = null,
        Func<DiagnosticReplaySession, JournalReplayPlayer>? playerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        this.managedRoot = Path.GetFullPath(managedRoot);
        this.instanceLauncher = instanceLauncher
            ?? new ProcessDiagnosticInstanceLauncher();
        this.sessionManager = sessionManager ?? new ReplaySessionManager();
        this.playerFactory = playerFactory
            ?? (replaySession => new JournalReplayPlayer(replaySession));
        synchronizationContext = SynchronizationContext.Current;
        srvSurveyExecutablePath = ResolveDefaultExecutablePath();
        launchCommand = new AsyncCommand(LaunchAsync, () => CanLaunch);
        stopCommand = new AsyncCommand(
            StopAsync,
            () => IsInstanceRunning && !IsBusy);
        restartCommand = new AsyncCommand(RestartAsync, () => CanControlReplay);
        previousCommand = new AsyncCommand(PreviousAsync, () => CanControlReplay && Position > 0);
        stepCommand = new AsyncCommand(
            StepAsync,
            () => CanControlReplay && !IsComplete && !IsPlaying);
        playCommand = new AsyncCommand(PlayAsync, () => CanControlReplay && !IsComplete && !IsPlaying);
        pauseCommand = new RelayCommand(Pause, () => IsPlaying);
        LaunchCommand = launchCommand;
        StopCommand = stopCommand;
        RestartCommand = restartCommand;
        PreviousCommand = previousCommand;
        StepCommand = stepCommand;
        PlayCommand = playCommand;
        PauseCommand = pauseCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand LaunchCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand RestartCommand { get; }

    public ICommand PreviousCommand { get; }

    public ICommand StepCommand { get; }

    public ICommand PlayCommand { get; }

    public ICommand PauseCommand { get; }

    public IReadOnlyList<double> SpeedOptions { get; } = [0.25, 0.5, 1, 2, 5, 10, 25];

    public IReadOnlyList<JournalReplayEvent> Events => session?.Events ?? [];

    public bool HasSession => session is not null;

    public bool CanLaunch => HasSession
        && !IsBusy
        && !string.IsNullOrWhiteSpace(SrvSurveyExecutablePath)
        && File.Exists(SrvSurveyExecutablePath);

    public bool CanControlReplay => HasSession && IsInstanceRunning && !IsBusy;

    public bool IsInstanceRunning => instance?.IsRunning == true;

    public bool IsComplete => player?.IsComplete ?? false;

    public int Position => player?.Position ?? 0;

    public int TotalEvents => session?.Events.Count ?? 0;

    public string PositionText => $"{Position:N0} / {TotalEvents:N0}";

    public string SourcePath => sourcePath;

    public string SessionDirectory => session?.SessionDirectory ?? string.Empty;

    public string ManifestPath => session?.ManifestPath ?? string.Empty;

    public string PlaybackJournalPath => session?.PlaybackJournalPath ?? string.Empty;

    public string LogsDirectory => session?.LogsDirectory ?? string.Empty;

    public string DataDirectory => session?.DataDirectory ?? string.Empty;

    public string CommanderName => session?.Commander.Name ?? string.Empty;

    public string FrontierId => session?.Commander.FrontierId ?? string.Empty;

    public string SourceVersion => session?.SourceVersion ?? string.Empty;

    public string PrivacyModeText => session?.PrivacyMode.ToString() ?? string.Empty;

    public string ValidationStatus => session is null
        ? "No replay has been validated."
        : "Validated: supported format, bounded JSON, commander bootstrap, "
            + "and checksum verified/recorded.";

    public string FidelityStatus => session is null
        ? string.Empty
        : "Journal-first replay. Status, Cargo, ShipLocker, NavRoute, and "
            + "Market timelines are not present in this format revision. "
            + (session.PresentationSnapshot is null
                ? "No overlay presentation snapshot was included."
                : "Overlay enablement, layout, scale, opacity, and viewport are included.");

    public string TimeRangeText => session is null
        ? string.Empty
        : $"{FormatTimestamp(session.Events.FirstOrDefault()?.Timestamp)} to "
            + FormatTimestamp(session.Events.LastOrDefault()?.Timestamp);

    public string SrvSurveyExecutablePath
    {
        get => srvSurveyExecutablePath;
        set
        {
            if (SetField(ref srvSurveyExecutablePath, value?.Trim() ?? string.Empty))
            {
                RaiseCommandStates();
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public double SpeedMultiplier
    {
        get => speedMultiplier;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                return;
            }

            SetField(ref speedMultiplier, value);
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanLaunch));
                OnPropertyChanged(nameof(CanControlReplay));
                RaiseCommandStates();
            }
        }
    }

    public bool IsPlaying
    {
        get => isPlaying;
        private set
        {
            if (SetField(ref isPlaying, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public JournalReplayEvent? CurrentEvent
    {
        get => currentEvent;
        private set => SetField(ref currentEvent, value);
    }

    public JournalReplayEvent? SelectedEvent
    {
        get => selectedEvent;
        set => SetField(ref selectedEvent, value);
    }

    public async Task<bool> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            await PausePlaybackAsync();
            await StopInstanceCoreAsync(cancellationToken);
            var imported = await sessionManager.ImportAsync(
                path,
                managedRoot,
                cancellationToken);
            DetachPlayer();
            session = imported;
            player = playerFactory(imported);
            player.PositionChanged += OnPlayerPositionChanged;
            sourcePath = Path.GetFullPath(path);
            CurrentEvent = null;
            SelectedEvent = imported.Events.FirstOrDefault();
            StatusMessage = $"Imported {imported.Events.Count:N0} events for "
                + $"Commander {imported.Commander.Name}.";
            RaiseSessionProperties();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            StatusMessage = "Import failed: " + exception.Message;
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<bool> LaunchAsync()
    {
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            await PausePlaybackAsync();
            return await LaunchCoreAsync(
                resetPlayback: true,
                CancellationToken.None);
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task StopAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        try
        {
            await PausePlaybackAsync();
            await StopInstanceCoreAsync(CancellationToken.None);
            StatusMessage = "The diagnostic SrvSurvey instance is stopped.";
            RaiseRuntimeProperties();
        }
        catch (Exception exception) when (IsRecoverableControllerFailure(exception))
        {
            StatusMessage = "Stop failed: " + exception.Message
                + $" Logs retained at {LogsDirectory}.";
            RaiseRuntimeProperties();
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<bool> RestartAsync()
    {
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            if (session is null || player is null)
            {
                return false;
            }

            await PausePlaybackAsync();
            await StopInstanceCoreAsync(CancellationToken.None);
            return await LaunchCoreAsync(
                resetPlayback: true,
                CancellationToken.None);
        }
        catch (Exception exception) when (IsRecoverableControllerFailure(exception))
        {
            StatusMessage = "Restart failed: " + exception.Message
                + $" Logs retained at {LogsDirectory}.";
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<bool> PreviousAsync()
    {
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            if (session is null || player is null || Position <= 0)
            {
                return false;
            }

            var targetPosition = Position - 1;
            await PausePlaybackAsync();
            await StopInstanceCoreAsync(CancellationToken.None);
            if (!await LaunchCoreAsync(
                    resetPlayback: true,
                    CancellationToken.None))
            {
                return false;
            }

            await player.SeekAsync(targetPosition, CancellationToken.None);
            StatusMessage =
                $"Reconstructed replay at event {targetPosition:N0}.";
            return true;
        }
        catch (Exception exception) when (IsRecoverableControllerFailure(exception))
        {
            StatusMessage = "Previous failed: " + exception.Message
                + $" Logs retained at {LogsDirectory}.";
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task<bool> StepAsync()
    {
        if (!TryBeginOperation())
        {
            return false;
        }

        try
        {
            if (session is null
                || instance?.IsRunning != true
                || player is null
                || IsPlaying)
            {
                return false;
            }

            var stepped = await player.StepAsync(CancellationToken.None);
            StatusMessage = stepped
                ? $"Emitted event {Position:N0} of {TotalEvents:N0}."
                : "Replay is complete.";
            return stepped;
        }
        catch (Exception exception) when (IsRecoverableControllerFailure(exception))
        {
            StatusMessage = "Step failed: " + exception.Message
                + $" Logs retained at {LogsDirectory}.";
            return false;
        }
        finally
        {
            EndOperation();
        }
    }

    public async Task PlayAsync()
    {
        if (!TryBeginOperation())
        {
            return;
        }

        Task activePlayback;
        try
        {
            if (session is null
                || instance?.IsRunning != true
                || player is null
                || IsPlaying)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            playbackCancellation = cancellation;
            IsPlaying = true;
            StatusMessage = $"Playing at {SpeedMultiplier:0.##}x.";
            activePlayback = PlayCoreAsync(player, cancellation);
            playbackTask = activePlayback;
        }
        finally
        {
            EndOperation();
        }

        await activePlayback;
        if (ReferenceEquals(playbackTask, activePlayback))
        {
            playbackTask = null;
        }
    }

    public void Pause()
    {
        playbackCancellation?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        await operationGate.WaitAsync(CancellationToken.None);
        IsBusy = true;
        try
        {
            await PausePlaybackAsync();
            await StopInstanceCoreAsync(CancellationToken.None);
            DetachPlayer();
        }
        finally
        {
            IsBusy = false;
            operationGate.Release();
            operationGate.Dispose();
        }
    }

    private async Task<bool> LaunchCoreAsync(
        bool resetPlayback,
        CancellationToken cancellationToken)
    {
        if (session is null || player is null)
        {
            StatusMessage = "Import a replay before launching SrvSurvey.";
            return false;
        }

        if (!File.Exists(SrvSurveyExecutablePath))
        {
            StatusMessage = "Choose a valid SrvSurvey executable.";
            return false;
        }

        try
        {
            await StopInstanceCoreAsync(cancellationToken);
            if (resetPlayback)
            {
                await session.ResetRuntimeAsync(cancellationToken);
                await player.ResetAsync(cancellationToken);
            }

            instance = await instanceLauncher.LaunchAsync(
                SrvSurveyExecutablePath,
                session.ManifestPath,
                cancellationToken);
            StartInstanceMonitor(instance);
            StatusMessage = "SrvSurvey launched in isolated diagnostic replay mode. "
                + "Networking and external effects are disabled.";
            RaiseRuntimeProperties();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            StatusMessage = "Launch failed: " + exception.Message;
            await StopInstanceCoreAsync(CancellationToken.None);
            return false;
        }
    }

    private async Task StopInstanceCoreAsync(CancellationToken cancellationToken)
    {
        if (instance is null)
        {
            return;
        }

        var current = instance;
        instance = null;
        var monitorCancellation = instanceMonitorCancellation;
        var monitor = instanceMonitorTask;
        instanceMonitorCancellation = null;
        instanceMonitorTask = null;
        monitorCancellation?.Cancel();
        try
        {
            await current.StopAsync(cancellationToken);
        }
        finally
        {
            if (monitor is not null)
            {
                try
                {
                    await monitor;
                }
                catch (OperationCanceledException) when (
                    monitorCancellation?.IsCancellationRequested == true)
                {
                    // A controller-owned stop supersedes natural-exit reporting.
                }
            }

            monitorCancellation?.Dispose();
            await current.DisposeAsync();
            RaiseRuntimeProperties();
        }
    }

    private async Task PlayCoreAsync(
        JournalReplayPlayer activePlayer,
        CancellationTokenSource cancellation)
    {
        try
        {
            await activePlayer.PlayAsync(
                () => SpeedMultiplier,
                cancellation.Token);
            if (activePlayer.IsComplete)
            {
                StatusMessage = "Replay is complete.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = $"Paused at event {Position:N0}.";
        }
        catch (Exception exception) when (IsRecoverableControllerFailure(exception))
        {
            StatusMessage = "Replay stopped after an I/O failure: "
                + exception.Message
                + $" Logs retained at {LogsDirectory}.";
        }
        finally
        {
            if (ReferenceEquals(playbackCancellation, cancellation))
            {
                playbackCancellation = null;
            }

            cancellation.Dispose();
            IsPlaying = false;
        }
    }

    private async Task PausePlaybackAsync()
    {
        var activePlayback = playbackTask;
        playbackCancellation?.Cancel();
        if (activePlayback is null)
        {
            return;
        }

        await activePlayback;
        if (ReferenceEquals(playbackTask, activePlayback))
        {
            playbackTask = null;
        }
    }

    private void StartInstanceMonitor(IDiagnosticInstance observedInstance)
    {
        var cancellation = new CancellationTokenSource();
        instanceMonitorCancellation = cancellation;
        instanceMonitorTask = MonitorInstanceExitAsync(
            observedInstance,
            cancellation);
    }

    private async Task MonitorInstanceExitAsync(
        IDiagnosticInstance observedInstance,
        CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            var exitCode = await observedInstance.WaitForExitAsync(
                cancellation.Token);
            var exitTransition = await InvokeOnCapturedContextAsync(() =>
            {
                if (!ReferenceEquals(instance, observedInstance))
                {
                    return (Handled: false, Playback: (Task?)null);
                }

                instance = null;
                playbackCancellation?.Cancel();
                if (ReferenceEquals(instanceMonitorCancellation, cancellation))
                {
                    instanceMonitorCancellation = null;
                    instanceMonitorTask = null;
                }

                RaiseRuntimeProperties();
                return (Handled: true, Playback: playbackTask);
            });
            if (exitTransition.Playback is not null)
            {
                await exitTransition.Playback;
            }

            if (exitTransition.Handled)
            {
                await InvokeOnCapturedContextAsync(() =>
                {
                    if (instance is null)
                    {
                        StatusMessage = exitCode == 0
                            ? "Diagnostic SrvSurvey exited normally with code 0. "
                                + $"Logs retained at {LogsDirectory}."
                            : $"Diagnostic SrvSurvey exited unexpectedly with code {exitCode}. "
                                + $"Logs retained at {LogsDirectory}.";
                    }

                    return true;
                });
                await observedInstance.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A controller-owned stop suppresses natural-exit reporting.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private Task<T> InvokeOnCapturedContextAsync<T>(Func<T> action)
    {
        if (synchronizationContext is null
            || ReferenceEquals(SynchronizationContext.Current, synchronizationContext))
        {
            return Task.FromResult(action());
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        synchronizationContext.Post(
            _ =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            },
            state: null);
        return completion.Task;
    }

    private bool TryBeginOperation()
    {
        if (!operationGate.Wait(0, CancellationToken.None))
        {
            return false;
        }

        IsBusy = true;
        return true;
    }

    private static bool IsRecoverableControllerFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception;

    private void EndOperation()
    {
        IsBusy = false;
        operationGate.Release();
    }

    private void OnPlayerPositionChanged(
        object? sender,
        JournalReplayPositionChangedEventArgs eventArgs)
    {
        CurrentEvent = eventArgs.CurrentEvent;
        if (eventArgs.CurrentEvent is not null)
        {
            SelectedEvent = eventArgs.CurrentEvent;
        }

        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(IsComplete));
        RaiseCommandStates();
    }

    private void RaiseSessionProperties()
    {
        OnPropertyChanged(nameof(Events));
        OnPropertyChanged(nameof(HasSession));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(TotalEvents));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(SessionDirectory));
        OnPropertyChanged(nameof(ManifestPath));
        OnPropertyChanged(nameof(PlaybackJournalPath));
        OnPropertyChanged(nameof(LogsDirectory));
        OnPropertyChanged(nameof(DataDirectory));
        OnPropertyChanged(nameof(CommanderName));
        OnPropertyChanged(nameof(FrontierId));
        OnPropertyChanged(nameof(SourceVersion));
        OnPropertyChanged(nameof(PrivacyModeText));
        OnPropertyChanged(nameof(ValidationStatus));
        OnPropertyChanged(nameof(FidelityStatus));
        OnPropertyChanged(nameof(TimeRangeText));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(IsComplete));
        RaiseCommandStates();
    }

    private void RaiseRuntimeProperties()
    {
        OnPropertyChanged(nameof(IsInstanceRunning));
        OnPropertyChanged(nameof(CanControlReplay));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        launchCommand.RaiseCanExecuteChanged();
        stopCommand.RaiseCanExecuteChanged();
        restartCommand.RaiseCanExecuteChanged();
        previousCommand.RaiseCanExecuteChanged();
        stepCommand.RaiseCanExecuteChanged();
        playCommand.RaiseCanExecuteChanged();
        pauseCommand.RaiseCanExecuteChanged();
    }

    private void DetachPlayer()
    {
        if (player is not null)
        {
            player.PositionChanged -= OnPlayerPositionChanged;
            player = null;
        }
    }

    private static string ResolveDefaultExecutablePath()
    {
        var executableName = OperatingSystem.IsWindows()
            ? "SrvSurvey.Desktop.exe"
            : "SrvSurvey.Desktop";
        return Path.Combine(AppContext.BaseDirectory, executableName);
    }

    private static string FormatTimestamp(DateTimeOffset? timestamp) =>
        timestamp?.ToString(
            "u",
            System.Globalization.CultureInfo.InvariantCulture)
        ?? "unknown time";

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private int isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            Volatile.Read(ref isExecuting) == 0 && canExecute();

        public async void Execute(object? parameter)
        {
            if (!canExecute()
                || Interlocked.CompareExchange(ref isExecuting, 1, 0) != 0)
            {
                return;
            }

            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                Volatile.Write(ref isExecuting, 0);
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RelayCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
