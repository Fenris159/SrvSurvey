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
    private CancellationTokenSource? playbackCancellation;
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
        ReplaySessionManager? sessionManager = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(managedRoot);
        this.managedRoot = Path.GetFullPath(managedRoot);
        this.instanceLauncher = instanceLauncher
            ?? new ProcessDiagnosticInstanceLauncher();
        this.sessionManager = sessionManager ?? new ReplaySessionManager();
        srvSurveyExecutablePath = ResolveDefaultExecutablePath();
        launchCommand = new AsyncCommand(LaunchAsync, () => CanLaunch);
        stopCommand = new AsyncCommand(StopAsync, () => IsInstanceRunning);
        restartCommand = new AsyncCommand(RestartAsync, () => CanControlReplay);
        previousCommand = new AsyncCommand(PreviousAsync, () => CanControlReplay && Position > 0);
        stepCommand = new AsyncCommand(StepAsync, () => CanControlReplay && !IsComplete);
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
            + "Market timelines are not present in this format revision.";

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
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            Pause();
            await StopInstanceCoreAsync(cancellationToken);
            var imported = await sessionManager.ImportAsync(
                path,
                managedRoot,
                cancellationToken);
            DetachPlayer();
            session = imported;
            player = new JournalReplayPlayer(imported);
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
                or ArgumentException)
        {
            StatusMessage = "Import failed: " + exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> LaunchAsync()
    {
        return await LaunchCoreAsync(resetPlayback: true, CancellationToken.None);
    }

    public async Task StopAsync()
    {
        Pause();
        await StopInstanceCoreAsync(CancellationToken.None);
        StatusMessage = "The diagnostic SrvSurvey instance is stopped.";
        RaiseRuntimeProperties();
    }

    public async Task<bool> RestartAsync()
    {
        if (session is null || player is null)
        {
            return false;
        }

        Pause();
        await StopInstanceCoreAsync(CancellationToken.None);
        return await LaunchCoreAsync(resetPlayback: true, CancellationToken.None);
    }

    public async Task<bool> PreviousAsync()
    {
        if (session is null || player is null || Position <= 0)
        {
            return false;
        }

        var targetPosition = Position - 1;
        Pause();
        await StopInstanceCoreAsync(CancellationToken.None);
        if (!await LaunchCoreAsync(resetPlayback: true, CancellationToken.None))
        {
            return false;
        }

        await player.SeekAsync(targetPosition, CancellationToken.None);
        StatusMessage = $"Reconstructed replay at event {targetPosition:N0}.";
        return true;
    }

    public async Task<bool> StepAsync()
    {
        if (!CanControlReplay || player is null)
        {
            return false;
        }

        var stepped = await player.StepAsync(CancellationToken.None);
        StatusMessage = stepped
            ? $"Emitted event {Position:N0} of {TotalEvents:N0}."
            : "Replay is complete.";
        return stepped;
    }

    public async Task PlayAsync()
    {
        if (!CanControlReplay || player is null || IsPlaying)
        {
            return;
        }

        playbackCancellation = new CancellationTokenSource();
        var cancellation = playbackCancellation;
        IsPlaying = true;
        StatusMessage = $"Playing at {SpeedMultiplier:0.##}x.";
        try
        {
            await player.PlayAsync(() => SpeedMultiplier, cancellation.Token);
            if (player.IsComplete)
            {
                StatusMessage = "Replay is complete.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = $"Paused at event {Position:N0}.";
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

    public void Pause()
    {
        playbackCancellation?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        Pause();
        await StopInstanceCoreAsync(CancellationToken.None);
        DetachPlayer();
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

        IsBusy = true;
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
            StatusMessage = "SrvSurvey launched in isolated diagnostic replay mode. "
                + "Networking and external effects are disabled.";
            RaiseRuntimeProperties();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            StatusMessage = "Launch failed: " + exception.Message;
            await StopInstanceCoreAsync(CancellationToken.None);
            return false;
        }
        finally
        {
            IsBusy = false;
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
        await current.StopAsync(cancellationToken);
        await current.DisposeAsync();
        RaiseRuntimeProperties();
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
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            await execute();
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
