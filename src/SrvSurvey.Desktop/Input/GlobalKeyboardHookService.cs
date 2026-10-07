using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Supplies optional context checks and overlay geometry for the platform keyboard sources.</summary>
public sealed record AdditionalKeyboardInput(
    Func<bool>? IsGameRunning = null,
    Func<bool>? SuppressShortcuts = null,
    Func<PixelRect?>? OverlayMonitorBounds = null
);

/// <summary>Routes activations from every keyboard source through one focus, source-selection, and dispatch policy.</summary>
public sealed class GlobalKeyboardHookService : IAsyncDisposable, IKeyboardActivationSink
{
    private static readonly TimeSpan DiagnosticsRefreshInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GameDisplayCorroborationWindow = TimeSpan.FromMilliseconds(200);
    private readonly Lock callbackLock = new();
    private readonly Lock lifecycleLock = new();
    private readonly Lock statusLock = new();
    private readonly IReadOnlyList<IKeyboardActivationSource> sources;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly Func<long> timestampProvider;
    private readonly Func<bool> isGameRunning;
    private readonly Func<bool> suppressShortcuts;
    private readonly GlobalInputBindingRouter router;
    private readonly KeyboardInputSelector inputSelector = new();
    private readonly CancellationTokenSource refreshCancellation = new();
    private Task? refreshTask;
    private GlobalInputSettings settings;
    private Task? disposalTask;
    private volatile bool disposed;
    private string status;
    private string lastKeyboardInput = "No configured shortcut received yet.";
    private string keyboardFocusStatus = "Waiting for Elite Dangerous.";
    private bool hadGameContext;
    private bool gameWasRunning;
    private int? gameProcessId;
    private string? gameDisplay;
    private long? lastGameDisplayInput;
    private int resetRequested;

    /// <summary>Listens for configured shortcuts through desktop, discovered game displays, and optional Wayland portals.</summary>
    public GlobalKeyboardHookService(
        GlobalInputSettings settings,
        OverlayHostKind host,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        AdditionalKeyboardInput? additionalInput = null
    )
        : this(
            settings,
            KeyboardInputHost.CreateSources(host, additionalInput?.OverlayMonitorBounds),
            gameWindowTracker,
            isApplicationActive,
            additionalInput
        ) { }

    /// <summary>Routes the given sources, sampling one clock for focus, repeat detection, and duplicate merging.</summary>
    internal GlobalKeyboardHookService(
        GlobalInputSettings settings,
        IReadOnlyList<IKeyboardActivationSource> sources,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        AdditionalKeyboardInput? additionalInput = null,
        Func<long>? timestampProvider = null
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
        this.gameWindowTracker = gameWindowTracker ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        isGameRunning = additionalInput?.IsGameRunning ?? EliteKeyboardDisplayDiscovery.IsGameRunning;
        suppressShortcuts = additionalInput?.SuppressShortcuts ?? (static () => false);
        router = new GlobalInputBindingRouter(settings);
        inputSelector.SetMode(settings.KeyboardSource);
        status = settings.KeyboardEnabled
            ? "Global keyboard input is ready to start."
            : "Global keyboard input is disabled.";
        foreach (IKeyboardActivationSource source in sources)
        {
            source.Attach(this);
        }
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;

    public event EventHandler? StatusChanged;

    /// <summary>Completes when every source's silent discovery or restoration has finished, before startup game focus.</summary>
    public Task StartupReady => Task.WhenAll(sources.Select(source => source.StartupReady));

    /// <summary>Reports provider health and selection without exposing arbitrary keyboard input.</summary>
    public KeyboardInputDiagnostics Diagnostics
    {
        get
        {
            lock (callbackLock)
            {
                (KeyboardInputSource Kind, KeyboardSourceState State)[] states =
                [
                    .. sources.Select(source => (source.Kind, source.State)),
                ];
                GameDisplayConnection? display = FindGameDisplay(states.Select(entry => entry.State));
                return new KeyboardInputDiagnostics(
                    inputSelector.SelectedMode,
                    IsAvailable(states, KeyboardInputSource.Desktop),
                    IsAvailable(states, KeyboardInputSource.NestedDisplay),
                    IsAvailable(states, KeyboardInputSource.Portal),
                    Volatile.Read(ref lastKeyboardInput),
                    keyboardFocusStatus,
                    display is not null ? $"Game display {display.Display}." : "No separate game display is connected."
                )
                {
                    DesktopShortcutSettings = states
                        .Select(entry => entry.State.DesktopShortcutSettings)
                        .FirstOrDefault(offered => offered is not null),
                };
            }
        }
    }

    /// <summary>Opens desktop shortcut configuration only when explicitly requested from input settings.</summary>
    public Task OpenDesktopShortcutSettingsAsync() =>
        sources
            .FirstOrDefault(source => source.State.DesktopShortcutSettings is not null)
            ?.OpenDesktopShortcutSettingsAsync()
        ?? Task.CompletedTask;

    public string Status
    {
        get
        {
            lock (statusLock)
            {
                return status;
            }
        }
    }

    public bool IsRunning => sources.Any(source => source.State.IsRunning);

    /// <summary>Clears source and press-state detection while retaining bindings and desktop permissions.</summary>
    public void ResetDetection()
    {
        inputSelector.Reset();
        Interlocked.Exchange(ref resetRequested, 1);
        foreach (IKeyboardActivationSource source in sources)
        {
            source.ResetDetection();
        }
        Volatile.Write(ref lastKeyboardInput, "Input detection reset. Use a shortcut with Elite Dangerous focused.");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Starts every keyboard source and the periodic focus diagnostics.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (lifecycleLock)
        {
            refreshTask ??= Task.Run(RefreshDiagnosticsAsync, CancellationToken.None);
        }
        GlobalInputSettings current = Volatile.Read(ref settings);
        foreach (IKeyboardActivationSource source in sources)
        {
            source.Start(current);
        }
        if (!current.KeyboardEnabled)
        {
            SetStatus("Global keyboard input is disabled.");
        }
    }

    /// <summary>Applies bindings to every source and releases learned selections when configuration changes.</summary>
    public void Update(GlobalInputSettings updatedSettings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(updatedSettings);
        GlobalInputSettings previous = Interlocked.Exchange(ref settings, updatedSettings);
        router.Update(updatedSettings);
        if (
            previous.KeyboardSource != updatedSettings.KeyboardSource
            || previous.KeyboardEnabled != updatedSettings.KeyboardEnabled
            || previous.Bindings.Count != updatedSettings.Bindings.Count
            || previous.Bindings.Any(pair =>
                !string.Equals(
                    updatedSettings.Bindings.GetValueOrDefault(pair.Key),
                    pair.Value,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            inputSelector.SetMode(updatedSettings.KeyboardSource);
            ResetDetection();
        }

        foreach (IKeyboardActivationSource source in sources)
        {
            source.Update(updatedSettings);
        }
        if (!updatedSettings.KeyboardEnabled)
        {
            SetStatus("Global keyboard input is disabled.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleLock)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    string IKeyboardActivationSink.Status => Status;

    /// <summary>Samples focus once per input under the callback lock that also guards tracker disposal.</summary>
    KeyboardFocus IKeyboardActivationSink.SampleFocus(KeyboardFocusEvidence evidence)
    {
        lock (callbackLock)
        {
            if (disposed)
            {
                return new KeyboardFocus(timestampProvider(), false, false, false);
            }
            if (Interlocked.Exchange(ref resetRequested, 0) != 0)
            {
                lastGameDisplayInput = null;
            }
            return evidence == KeyboardFocusEvidence.Untracked ? SampleUntrackedFocus() : SampleTrackedFocus(evidence);
        }
    }

    /// <summary>Accepts a configured action, learning the shared source only from confirmed in-game input.</summary>
    void IKeyboardActivationSink.Activate(KeyboardActivation activation)
    {
        lock (callbackLock)
        {
            KeyboardFocus focus = activation.Focus;
            if (
                disposed
                || !Volatile.Read(ref settings).KeyboardEnabled
                || !focus.AllowsShortcuts
                || !TryResolve(activation, out GlobalInputAction action)
            )
            {
                return;
            }

            if (activation.Source == KeyboardInputSource.NestedDisplay && focus.GameFocused)
            {
                lastGameDisplayInput = focus.Timestamp;
            }
            bool accepted = inputSelector.TryAccept(
                activation.Source,
                action,
                activation.Chord,
                focus.Timestamp,
                learn: focus.GameFocused,
                canLearn: sources.FirstOrDefault(source => source.Kind == activation.Source)?.State.CanServeAllBindings
                    ?? true
            );
            ReportKeyboardInput(activation.Source, action, accepted, focus);
            if (accepted)
            {
                ActionTriggered?.Invoke(this, new GlobalInputActionTriggeredEventArgs(action, activation.Chord));
            }
        }
    }

    /// <summary>Shows a source's listener status only while keyboard input is enabled.</summary>
    void IKeyboardActivationSink.ReportStatus(string message, string? expectedStatus)
    {
        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled && message.Length > 0)
        {
            SetStatus(message, expectedStatus);
        }
        else
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void IKeyboardActivationSink.ReleaseSelection(KeyboardInputSource source) => inputSelector.Reset(source);

    /// <summary>Confirms raw-key focus from the desktop window or the key's own game display.</summary>
    private KeyboardFocus SampleTrackedFocus(KeyboardFocusEvidence evidence)
    {
        bool applicationActive = isApplicationActive();
        bool gameFocused =
            !applicationActive
            && (
                gameWindowTracker.GetSnapshot().IsForeground || evidence == KeyboardFocusEvidence.GameDisplayForeground
            );
        return new KeyboardFocus(timestampProvider(), applicationActive, gameFocused, applicationActive || gameFocused);
    }

    /// <summary>Accepts compositor shortcuts when game focus is known, or Elite is running on an untrackable Wayland surface.</summary>
    private KeyboardFocus SampleUntrackedFocus()
    {
        // Compositor shortcuts still fire while SrvSurvey text entry has focus.
        if (suppressShortcuts())
        {
            return new KeyboardFocus(timestampProvider(), false, false, false);
        }

        GameWindowSnapshot snapshot = gameWindowTracker.GetSnapshot();
        bool applicationActive = isApplicationActive();
        long now = timestampProvider();
        bool recentGameInput =
            lastGameDisplayInput is long received
            && now >= received
            && Stopwatch.GetElapsedTime(received, now) < GameDisplayCorroborationWindow;
        bool gameFocused =
            !applicationActive
            && (snapshot.IsForeground || (recentGameInput && sources.Any(source => source.State.IsGameForeground)));
        bool allowed = applicationActive || gameFocused || (!snapshot.IsAvailable && isGameRunning());
        return new KeyboardFocus(now, applicationActive, gameFocused, allowed);
    }

    private bool TryResolve(KeyboardActivation activation, out GlobalInputAction action)
    {
        if (activation.Action is GlobalInputAction resolved)
        {
            action = resolved;
            return true;
        }
        return router.TryResolve(activation.Chord, out action);
    }

    private static bool IsAvailable(
        (KeyboardInputSource Kind, KeyboardSourceState State)[] states,
        KeyboardInputSource kind
    ) => states.Any(entry => entry.Kind == kind && entry.State is { IsRunning: true, CanServeAllBindings: true });

    private static GameDisplayConnection? FindGameDisplay(IEnumerable<KeyboardSourceState> states) =>
        states.Select(state => state.GameDisplay).FirstOrDefault(display => display is not null);

    /// <summary>Refreshes focus diagnostics once per second without blocking the desktop dispatcher.</summary>
    private async Task RefreshDiagnosticsAsync()
    {
        using var timer = new PeriodicTimer(DiagnosticsRefreshInterval);
        try
        {
            do
            {
                lock (callbackLock)
                {
                    RefreshGameContext();
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                }
            } while (await timer.WaitForNextTickAsync(refreshCancellation.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
            // Diagnostics stop before sources and the shared tracker are disposed.
        }
    }

    /// <summary>Relearns after game identity changes and distinguishes confirmed focus from an untrackable Wayland surface.</summary>
    private void RefreshGameContext()
    {
        GameWindowSnapshot snapshot = gameWindowTracker.GetSnapshot();
        GameDisplayConnection? display = FindGameDisplay(sources.Select(source => source.State));
        bool running = snapshot.IsAvailable || display is not null || isGameRunning();
        int? processId = snapshot.ProcessId ?? display?.ProcessId;
        string? displayName = display?.Display;
        if (hadGameContext && (gameWasRunning != running || gameProcessId != processId || gameDisplay != displayName))
        {
            inputSelector.Reset();
            foreach (IKeyboardActivationSource source in sources)
            {
                source.ResetDetection();
            }
            lastGameDisplayInput = null;
        }
        hadGameContext = true;
        gameWasRunning = running;
        gameProcessId = processId;
        gameDisplay = displayName;
        keyboardFocusStatus = (isApplicationActive(), snapshot.IsForeground, running, snapshot.IsAvailable) switch
        {
            (true, _, _, _) => "SrvSurvey has focus; automatic detection is not learned here.",
            (_, true, _, _) => "Elite Dangerous focus confirmed.",
            (_, _, false, _) => "Elite Dangerous is not running.",
            (_, _, _, false) => "Elite Dangerous is running; desktop focus cannot be confirmed.",
            _ => "Elite Dangerous is not focused.",
        };
    }

    /// <summary>Explains the last configured shortcut and publishes changes to source detection.</summary>
    private void ReportKeyboardInput(
        KeyboardInputSource source,
        GlobalInputAction action,
        bool accepted,
        KeyboardFocus focus
    )
    {
        string sourceName = KeyboardInputDiagnostics.GetLabel(source);
        lastKeyboardInput = accepted
            ? $"Last shortcut: {GlobalInputActionCatalog.Get(action).DisplayName} from {sourceName}."
            : $"Last shortcut: {GlobalInputActionCatalog.Get(action).DisplayName} from {sourceName}; duplicate or unselected source ignored.";
        keyboardFocusStatus = (focus.GameFocused, focus.ApplicationActive) switch
        {
            (true, _) => "Elite Dangerous focus confirmed.",
            (_, true) => "SrvSurvey has focus; automatic detection is not learned here.",
            _ => "Elite Dangerous focus is unconfirmed; automatic detection was not learned.",
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops every source before disposing their shared game tracker.</summary>
    private async Task DisposeCoreAsync()
    {
        disposed = true;
        await refreshCancellation.CancelAsync().ConfigureAwait(false);
        Exception? disposalFailure = null;
        try
        {
            if (refreshTask is not null)
            {
                await refreshTask.ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            disposalFailure = exception;
        }
        foreach (IKeyboardActivationSource source in sources)
        {
            try
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                disposalFailure ??= exception;
            }
        }
        refreshCancellation.Dispose();
        try
        {
            lock (callbackLock)
            {
                gameWindowTracker.Dispose();
            }
        }
        catch (Exception exception)
        {
            disposalFailure ??= exception;
        }
        if (disposalFailure is not null)
        {
            ExceptionDispatchInfo.Capture(disposalFailure).Throw();
        }
    }

    private void SetStatus(string status, string? expectedStatus = null)
    {
        lock (statusLock)
        {
            if (
                (expectedStatus is not null && !string.Equals(this.status, expectedStatus, StringComparison.Ordinal))
                || string.Equals(this.status, status, StringComparison.Ordinal)
            )
            {
                return;
            }

            this.status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class GlobalInputActionTriggeredEventArgs(GlobalInputAction action, string chord) : EventArgs
{
    public GlobalInputAction Action { get; } = action;

    public string Chord { get; } = chord;
}
