using System.Diagnostics;
using Avalonia;
using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Supplies optional compositor/game sources and context checks while keeping native defaults automatic.</summary>
public sealed record AdditionalKeyboardInput(
    IGameKeyboardInput? GameKeyboardInput = null,
    IGlobalShortcutInput? PortalInput = null,
    Func<bool>? IsGameRunning = null,
    Func<bool>? SuppressShortcuts = null,
    Func<PixelRect?>? OverlayMonitorBounds = null
);

public sealed class GlobalKeyboardHookService : IAsyncDisposable
{
    private static readonly long PressStateMaxAgeTicks = 2 * Stopwatch.Frequency;
    private static readonly long ModifierStateMaxAgeTicks = 10 * Stopwatch.Frequency;
    private const ulong X11AutoRepeatEventGapMilliseconds = 5;
    private readonly Lock callbackLock = new();
    private readonly Lock lifecycleLock = new();
    private readonly Lock statusLock = new();
    private readonly Func<IGlobalHook> hookFactory;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly Func<long> timestampProvider;
    private readonly OverlayHostKind host;
    private readonly GlobalInputBindingRouter router;
    private readonly Dictionary<KeyCode, KeyPressState> pressedKeys = [];
    private readonly Dictionary<KeyCode, KeyPressState> gamePressedKeys = [];
    private readonly IGameKeyboardInput? gameKeyboardInput;
    private readonly IGlobalShortcutInput? portalInput;
    private readonly Func<bool> isGameRunning;
    private readonly Func<bool> suppressShortcuts;
    private readonly KeyboardInputSelector inputSelector = new();
    private bool nestedGameForeground;
    private readonly CancellationTokenSource gameInputCancellation = new();
    private Task? gameInputTask;
    private GlobalInputSettings settings;
    private IGlobalHook? hook;
    private Task? runTask;
    private Task previousHookStopTask = Task.CompletedTask;
    private Task? disposalTask;
    private long lifecycleVersion;
    private volatile bool disposed;
    private string status;
    private string lastKeyboardInput = "No configured shortcut received yet.";
    private string keyboardFocusStatus = "Waiting for Elite Dangerous.";
    private long lastDiagnosticsRefresh;
    private bool hadGameContext;
    private bool gameWasRunning;
    private int? gameProcessId;
    private string? gameDisplay;
    private long? lastNestedGameInput;
    private int pressResetRequested;

    private readonly record struct KeyPressState(long LastPressTimestamp, ulong? LastReleaseEventTime);

    /// <summary>Listens for configured shortcuts through desktop, discovered game displays, and optional Wayland portals.</summary>
    public GlobalKeyboardHookService(
        GlobalInputSettings settings,
        OverlayHostKind host,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        Func<IGlobalHook>? hookFactory = null,
        Func<long>? timestampProvider = null,
        AdditionalKeyboardInput? additionalInput = null
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.host = host;
        this.gameWindowTracker = gameWindowTracker ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.hookFactory = hookFactory ?? CreateHook;
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        this.gameKeyboardInput =
            additionalInput?.GameKeyboardInput
            ?? (
                hookFactory is null
                && OperatingSystem.IsLinux()
                && host is OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland or OverlayHostKind.LinuxWayland
                    ? new GamescopeKeyboardInput(
                        GamescopeGameWindowBridge.TryReadCurrent,
                        readDisplay: () =>
                            EliteKeyboardDisplayDiscovery.ReadCurrent(additionalInput?.OverlayMonitorBounds?.Invoke()),
                        preferredMonitorBounds: additionalInput?.OverlayMonitorBounds
                    )
                    : null
            );
        isGameRunning = additionalInput?.IsGameRunning ?? EliteKeyboardDisplayDiscovery.IsGameRunning;
        suppressShortcuts = additionalInput?.SuppressShortcuts ?? (static () => false);
        this.portalInput =
            additionalInput?.PortalInput
            ?? (
                hookFactory is null
                && OperatingSystem.IsLinux()
                && host is OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland or OverlayHostKind.LinuxWayland
                    ? new GlobalShortcutsPortalInput()
                    : null
            );
        if (this.portalInput is not null)
        {
            this.portalInput.ActionTriggered += OnPortalAction;
            this.portalInput.StatusChanged += OnPortalStatus;
        }
        router = new GlobalInputBindingRouter(settings);
        inputSelector.SetMode(settings.KeyboardSource);
        status = settings.KeyboardEnabled
            ? "Global keyboard input is ready to start."
            : "Global keyboard input is disabled.";
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;

    public event EventHandler? StatusChanged;

    /// <summary>Completes when silent portal discovery or restoration has finished, before startup game focus.</summary>
    public Task StartupReady => portalInput?.StartupReady ?? Task.CompletedTask;

    /// <summary>Reports provider health and selection without exposing arbitrary keyboard input.</summary>
    public KeyboardInputDiagnostics Diagnostics
    {
        get
        {
            lock (callbackLock)
            {
                return new KeyboardInputDiagnostics(
                    inputSelector.SelectedMode,
                    hook?.IsRunning == true,
                    gameKeyboardInput?.IsRunning == true,
                    portalInput?.IsRunning == true && portalInput.CanHandleAllBindings,
                    Volatile.Read(ref lastKeyboardInput),
                    keyboardFocusStatus,
                    gameKeyboardInput?.Display is { } display
                        ? $"Game display {display}."
                        : "No separate game display is connected."
                )
                {
                    CanOpenDesktopShortcutSettings = portalInput?.CanOpenSettings == true,
                    DesktopShortcutSettingsStatus =
                        portalInput?.SettingsStatus ?? "Desktop shortcut settings are unavailable.",
                };
            }
        }
    }

    /// <summary>Opens compositor shortcut configuration only when explicitly requested from input settings.</summary>
    public Task OpenDesktopShortcutSettingsAsync() => portalInput?.OpenSettingsAsync() ?? Task.CompletedTask;

    /// <summary>Clears source and press-state detection while retaining bindings and desktop permissions.</summary>
    public void ResetDetection()
    {
        inputSelector.Reset();
        Interlocked.Exchange(ref pressResetRequested, 1);
        Volatile.Write(ref lastKeyboardInput, "Input detection reset. Use a shortcut with Elite Dangerous focused.");
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears held-key state on its owning callback path without blocking settings on an in-flight event.</summary>
    private void ApplyRequestedPressReset()
    {
        if (Interlocked.Exchange(ref pressResetRequested, 0) != 0)
        {
            pressedKeys.Clear();
            gamePressedKeys.Clear();
            lastNestedGameInput = null;
        }
    }

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

    public bool IsRunning
    {
        get
        {
            lock (lifecycleLock)
            {
                return hook?.IsRunning == true
                    || portalInput?.IsRunning == true
                    || gameKeyboardInput?.IsRunning == true;
            }
        }
    }

    /// <summary>Starts the desktop hook and the optional nested-display listener.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (lifecycleLock)
        {
            gameInputTask ??= ReadGameKeyboardAsync();
        }
        portalInput?.Update(Volatile.Read(ref settings));
        Start(Volatile.Read(ref lifecycleVersion));
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
        portalInput?.Update(updatedSettings);

        long version = Interlocked.Increment(ref lifecycleVersion);
        if (updatedSettings.KeyboardEnabled)
        {
            Start(version);
        }
        else
        {
            _ = StopHook();
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

    /// <summary>Starts a raw listener only on platforms that provide desktop keyboard hooks.</summary>
    private void Start(long version)
    {
        GlobalInputSettings currentSettings = Volatile.Read(ref settings);
        if (!currentSettings.KeyboardEnabled)
        {
            SetStatus("Global keyboard input is disabled.");
            return;
        }

        if (host is not OverlayHostKind.Windows && !OverlayPlatformCapabilities.IsX11Compatible(host))
        {
            SetStatus(
                host == OverlayHostKind.LinuxWayland
                    ? "Wayland keyboard input is waiting for Global Shortcuts portal support."
                    : "Global keyboard input is unavailable on this platform."
            );
            return;
        }

        Task? stoppedTask = null;
        IGlobalHook? startedHook = null;
        Task? startedTask = null;
        string? pendingStatus = null;
        string? statusBeforeStart = null;
        lock (lifecycleLock)
        {
            if (disposed || version != lifecycleVersion || hook is not null)
            {
                return;
            }

            if (!previousHookStopTask.IsCompleted)
            {
                stoppedTask = previousHookStopTask;
            }
            else
            {
                IGlobalHook? pendingHook = null;
                try
                {
                    pendingHook = hookFactory();
                    pendingHook.KeyPressed += OnKeyPressed;
                    pendingHook.KeyReleased += OnKeyReleased;
                    pendingHook.HookEnabled += OnHookEnabled;
                    pendingHook.HookDisabled += OnHookDisabled;
                    hook = pendingHook;

                    statusBeforeStart = Status;
                    pendingStatus = "Starting global keyboard input...";
                    startedTask = pendingHook.RunAsync();
                    runTask = startedTask;
                    startedHook = pendingHook;
                }
                catch (Exception exception)
                {
                    hook = null;
                    runTask = null;
                    if (pendingHook is not null)
                    {
                        DisposeHook(pendingHook);
                    }

                    pendingStatus = $"Global keyboard input could not start: {exception.Message}";
                    statusBeforeStart = null;
                }
            }
        }

        PublishPendingStatus(pendingStatus, statusBeforeStart);

        if (stoppedTask is not null)
        {
            _ = StartAfterStopAsync(version, stoppedTask);
        }
        else if (startedHook is not null && startedTask is not null)
        {
            _ = ObserveRunAsync(version, startedHook, startedTask);
        }
    }

    /// <summary>Stops both input sources before disposing their shared game tracker.</summary>
    private async Task DisposeCoreAsync()
    {
        disposed = true;
        Interlocked.Increment(ref lifecycleVersion);
        await gameInputCancellation.CancelAsync().ConfigureAwait(false);
        if (gameInputTask is not null)
        {
            await gameInputTask.ConfigureAwait(false);
        }
        gameKeyboardInput?.Dispose();
        if (portalInput is not null)
        {
            await portalInput.DisposeAsync().ConfigureAwait(false);
            portalInput.ActionTriggered -= OnPortalAction;
            portalInput.StatusChanged -= OnPortalStatus;
        }
        gameInputCancellation.Dispose();
        await WaitForHookToStopAsync(StopHook()).ConfigureAwait(false);
        lock (callbackLock)
        {
            gameWindowTracker.Dispose();
        }
    }

    private static EventLoopGlobalHook CreateHook()
    {
        return new EventLoopGlobalHook(
            GlobalHookType.Keyboard,
            globalHookProvider: null,
            runAsyncOnBackgroundThread: true
        );
    }

    /// <summary>Drains buffered game-display events without blocking the desktop dispatcher.</summary>
    private async Task ReadGameKeyboardAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        try
        {
            while (await timer.WaitForNextTickAsync(gameInputCancellation.Token).ConfigureAwait(false))
            {
                GameKeyboardEventBatch batch =
                    gameKeyboardInput?.ReadEvents(Volatile.Read(ref settings).KeyboardEnabled)
                    ?? new GameKeyboardEventBatch(false, []);
                lock (callbackLock)
                {
                    ApplyRequestedPressReset();
                    if (batch.Reset)
                    {
                        gamePressedKeys.Clear();
                        inputSelector.Reset(KeyboardInputSource.NestedDisplay);
                    }
                    nestedGameForeground = batch.IsGameForeground;
                    foreach (UioHookEvent input in batch.Events)
                    {
                        var args = new KeyboardHookEventArgs(input);
                        if (input.Type == EventType.KeyPressed)
                        {
                            OnKeyPressed(gameKeyboardInput, args);
                        }
                        else
                        {
                            OnKeyReleased(gameKeyboardInput, args);
                        }
                    }
                    long now = Stopwatch.GetTimestamp();
                    if (
                        lastDiagnosticsRefresh == 0
                        || Stopwatch.GetElapsedTime(lastDiagnosticsRefresh, now) >= TimeSpan.FromSeconds(1)
                    )
                    {
                        lastDiagnosticsRefresh = now;
                        RefreshGameContext();
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (gameInputCancellation.IsCancellationRequested)
        {
            // The nested listener stops before its native connections are disposed.
        }
    }

    /// <summary>Relearns after game identity changes and distinguishes confirmed focus from an untrackable Wayland surface.</summary>
    private void RefreshGameContext()
    {
        GameWindowSnapshot snapshot = gameWindowTracker.GetSnapshot();
        bool running = snapshot.IsAvailable || gameKeyboardInput?.ProcessId is not null || isGameRunning();
        int? processId = snapshot.ProcessId ?? gameKeyboardInput?.ProcessId;
        string? display = gameKeyboardInput?.Display;
        if (hadGameContext && (gameWasRunning != running || gameProcessId != processId || gameDisplay != display))
        {
            inputSelector.Reset();
            pressedKeys.Clear();
            gamePressedKeys.Clear();
            lastNestedGameInput = null;
        }
        hadGameContext = true;
        gameWasRunning = running;
        gameProcessId = processId;
        gameDisplay = display;
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
        bool gameFocused
    )
    {
        string sourceName = source switch
        {
            KeyboardInputSource.Desktop => "Desktop keyboard",
            KeyboardInputSource.NestedDisplay => "Game display",
            _ => "Wayland portal",
        };
        lastKeyboardInput = accepted
            ? $"Last shortcut: {GlobalInputActionCatalog.Get(action).DisplayName} from {sourceName}."
            : $"Last shortcut: {GlobalInputActionCatalog.Get(action).DisplayName} from {sourceName}; duplicate or unselected source ignored.";
        keyboardFocusStatus = (gameFocused, isApplicationActive()) switch
        {
            (true, _) => "Elite Dangerous focus confirmed.",
            (_, true) => "SrvSurvey has focus; automatic detection is not learned here.",
            _ => "Elite Dangerous focus is unconfirmed; automatic detection was not learned.",
        };
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Routes a press with independent repeat and modifier state for each display.</summary>
    private void OnKeyPressed(object? sender, KeyboardHookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            ApplyRequestedPressReset();
            GlobalInputSettings currentSettings = Volatile.Read(ref settings);
            if (disposed || !currentSettings.KeyboardEnabled || eventArgs.IsEventSimulated)
            {
                return;
            }

            KeyCode keyCode = eventArgs.Data.KeyCode;
            bool usesX11Events =
                ReferenceEquals(sender, gameKeyboardInput) || OverlayPlatformCapabilities.IsX11Compatible(host);
            Dictionary<KeyCode, KeyPressState> keyStates = ReferenceEquals(sender, gameKeyboardInput)
                ? gamePressedKeys
                : pressedKeys;
            bool applicationActive = isApplicationActive();
            bool gameFocused =
                !applicationActive
                && (
                    gameWindowTracker.GetSnapshot().IsForeground
                    || (ReferenceEquals(sender, gameKeyboardInput) && nestedGameForeground)
                );
            if (!IsModifierKey(keyCode) && !applicationActive && !gameFocused)
            {
                return;
            }

            long timestamp = timestampProvider();
            if (keyStates.TryGetValue(keyCode, out KeyPressState previousPress))
            {
                if (
                    usesX11Events
                    && previousPress.LastReleaseEventTime is ulong releaseEventTime
                    && eventArgs.RawEvent.Time >= releaseEventTime
                    && eventArgs.RawEvent.Time - releaseEventTime <= X11AutoRepeatEventGapMilliseconds
                )
                {
                    // X11 reports a held key's repeat as a release and press
                    // with the same native event time.
                    keyStates[keyCode] = new KeyPressState(timestamp, null);
                    return;
                }

                if (
                    previousPress.LastReleaseEventTime is null
                    && timestamp >= previousPress.LastPressTimestamp
                    && timestamp - previousPress.LastPressTimestamp < PressStateMaxAgeTicks
                )
                {
                    keyStates[keyCode] = new KeyPressState(timestamp, null);
                    return;
                }
            }

            keyStates[keyCode] = new KeyPressState(timestamp, null);

            EventMask mask = eventArgs.RawEvent.Mask;
            if (usesX11Events)
            {
                // XRecord can omit held modifiers from a non-modifier key's mask.
                mask |= GetObservedModifierMask(keyStates, timestamp);
            }

            string? chord = KeyboardChordFormatter.Format(keyCode, mask);
            if (chord is not null && router.TryResolve(chord, out GlobalInputAction action))
            {
                KeyboardInputSource source = ReferenceEquals(sender, gameKeyboardInput)
                    ? KeyboardInputSource.NestedDisplay
                    : KeyboardInputSource.Desktop;
                if (source == KeyboardInputSource.NestedDisplay && gameFocused)
                {
                    lastNestedGameInput = timestamp;
                }
                bool accepted = inputSelector.TryAccept(source, action, chord, timestamp, learn: gameFocused);
                ReportKeyboardInput(source, action, accepted, gameFocused);
                if (accepted)
                {
                    ActionTriggered?.Invoke(this, new GlobalInputActionTriggeredEventArgs(action, chord));
                }
            }
        }
    }

    /// <summary>Releases a key on its originating display without clearing another display's state.</summary>
    private void OnKeyReleased(object? sender, KeyboardHookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            KeyCode keyCode = eventArgs.Data.KeyCode;
            bool usesX11Events =
                ReferenceEquals(sender, gameKeyboardInput) || OverlayPlatformCapabilities.IsX11Compatible(host);
            Dictionary<KeyCode, KeyPressState> keyStates = ReferenceEquals(sender, gameKeyboardInput)
                ? gamePressedKeys
                : pressedKeys;
            if (usesX11Events && keyStates.TryGetValue(keyCode, out KeyPressState previousPress))
            {
                keyStates[keyCode] = previousPress with { LastReleaseEventTime = eventArgs.RawEvent.Time };
            }
            else
            {
                keyStates.Remove(keyCode);
            }
        }
    }

    /// <summary>Accepts compositor-approved actions when game focus is known, or Elite is running on an untrackable Wayland surface.</summary>
    private void OnPortalAction(object? sender, GlobalInputActionTriggeredEventArgs args)
    {
        lock (callbackLock)
        {
            ApplyRequestedPressReset();
            if (disposed || !Volatile.Read(ref settings).KeyboardEnabled || suppressShortcuts())
            {
                return;
            }
            GameWindowSnapshot snapshot = gameWindowTracker.GetSnapshot();
            bool applicationActive = isApplicationActive();
            long now = timestampProvider();
            bool recentGameInput =
                lastNestedGameInput is long received
                && now >= received
                && Stopwatch.GetElapsedTime(received, now) < TimeSpan.FromMilliseconds(200);
            bool gameFocused =
                !applicationActive && (snapshot.IsForeground || (nestedGameForeground && recentGameInput));
            if (!(applicationActive || gameFocused || (!snapshot.IsAvailable && isGameRunning())))
            {
                return;
            }
            bool accepted = inputSelector.TryAccept(
                KeyboardInputSource.Portal,
                args.Action,
                args.Chord,
                now,
                learn: gameFocused,
                canLearn: portalInput?.CanHandleAllBindings == true
            );
            ReportKeyboardInput(KeyboardInputSource.Portal, args.Action, accepted, gameFocused);
            if (accepted)
            {
                ActionTriggered?.Invoke(this, args);
            }
        }
    }

    /// <summary>Releases disconnected portal selections and reports settings changes independently of raw listener activation.</summary>
    private void OnPortalStatus(string message, bool available)
    {
        if (!available || portalInput?.CanHandleAllBindings == false)
        {
            inputSelector.Reset(KeyboardInputSource.Portal);
        }
        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled && message.Length > 0)
        {
            SetStatus(message);
        }
        else
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reconstructs held modifiers when XRecord omits them from the event mask.</summary>
    private static EventMask GetObservedModifierMask(Dictionary<KeyCode, KeyPressState> keyStates, long timestamp)
    {
        EventMask mask = EventMask.None;
        if (IsModifierHeld(keyStates, KeyCode.VcLeftAlt, timestamp))
        {
            mask |= EventMask.LeftAlt;
        }

        if (IsModifierHeld(keyStates, KeyCode.VcRightAlt, timestamp))
        {
            mask |= EventMask.RightAlt;
        }

        if (IsModifierHeld(keyStates, KeyCode.VcLeftControl, timestamp))
        {
            mask |= EventMask.LeftCtrl;
        }

        if (IsModifierHeld(keyStates, KeyCode.VcRightControl, timestamp))
        {
            mask |= EventMask.RightCtrl;
        }

        if (IsModifierHeld(keyStates, KeyCode.VcLeftShift, timestamp))
        {
            mask |= EventMask.LeftShift;
        }

        if (IsModifierHeld(keyStates, KeyCode.VcRightShift, timestamp))
        {
            mask |= EventMask.RightShift;
        }

        return mask;
    }

    /// <summary>Checks modifier state within one display's bounded observation window.</summary>
    private static bool IsModifierHeld(Dictionary<KeyCode, KeyPressState> keyStates, KeyCode keyCode, long timestamp)
    {
        return keyStates.TryGetValue(keyCode, out KeyPressState state)
            && state.LastReleaseEventTime is null
            && timestamp >= state.LastPressTimestamp
            && timestamp - state.LastPressTimestamp < ModifierStateMaxAgeTicks;
    }

    private static bool IsModifierKey(KeyCode keyCode)
    {
        return keyCode
            is KeyCode.VcLeftAlt
                or KeyCode.VcRightAlt
                or KeyCode.VcLeftControl
                or KeyCode.VcRightControl
                or KeyCode.VcLeftShift
                or KeyCode.VcRightShift;
    }

    private void OnHookEnabled(object? sender, HookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            pressedKeys.Clear();
        }

        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled)
        {
            SetStatus("Global keyboard input is active.");
        }
    }

    /// <summary>Releases desktop source selections when the raw listener stops.</summary>
    private void OnHookDisabled(object? sender, HookEventArgs eventArgs)
    {
        inputSelector.Reset(KeyboardInputSource.Desktop);
        lock (callbackLock)
        {
            pressedKeys.Clear();
        }

        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled)
        {
            SetStatus("Global keyboard input stopped.");
        }
    }

    private async Task ObserveRunAsync(long version, IGlobalHook observedHook, Task task)
    {
        Exception? failure = null;
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (
                StopHook(observedHook, task)
                && !disposed
                && version == Volatile.Read(ref lifecycleVersion)
                && Volatile.Read(ref settings).KeyboardEnabled
            )
            {
                SetStatus(
                    failure is null
                        ? "Global keyboard input stopped."
                        : $"Global keyboard input stopped: {failure.Message}"
                );
            }
        }
    }

    private Task StopHook()
    {
        IGlobalHook currentHook;
        Task currentTask;
        TaskCompletionSource stopCompletion;
        lock (lifecycleLock)
        {
            if (hook is null)
            {
                return previousHookStopTask;
            }

            currentHook = hook;
            currentTask = runTask ?? Task.CompletedTask;
            hook = null;
            runTask = null;
            stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previousHookStopTask = stopCompletion.Task;
        }

        DisposeHook(currentHook);
        _ = CompleteHookStopAsync(currentTask, stopCompletion);
        return stopCompletion.Task;
    }

    private bool StopHook(IGlobalHook expectedHook, Task expectedTask)
    {
        TaskCompletionSource stopCompletion;
        lock (lifecycleLock)
        {
            if (!ReferenceEquals(hook, expectedHook))
            {
                return false;
            }

            hook = null;
            runTask = null;
            stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            previousHookStopTask = stopCompletion.Task;
        }

        DisposeHook(expectedHook);
        _ = CompleteHookStopAsync(expectedTask, stopCompletion);
        return true;
    }

    private async Task StartAfterStopAsync(long version, Task stoppedTask)
    {
        await WaitForHookToStopAsync(stoppedTask).ConfigureAwait(false);
        if (!disposed && version == Volatile.Read(ref lifecycleVersion) && Volatile.Read(ref settings).KeyboardEnabled)
        {
            Start(version);
        }
    }

    private static async Task WaitForHookToStopAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ObserveRunAsync reports hook failures through the runtime status.
        }
    }

    private static async Task CompleteHookStopAsync(Task runTask, TaskCompletionSource stopCompletion)
    {
        await WaitForHookToStopAsync(runTask).ConfigureAwait(false);
        stopCompletion.TrySetResult();
    }

    private void DisposeHook(IGlobalHook currentHook)
    {
        currentHook.KeyPressed -= OnKeyPressed;
        currentHook.KeyReleased -= OnKeyReleased;
        currentHook.HookEnabled -= OnHookEnabled;
        currentHook.HookDisabled -= OnHookDisabled;
        try
        {
            currentHook.Dispose();
        }
        catch (Exception)
        {
            // Shutdown must continue even if the native hook has already ended.
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

    private void PublishPendingStatus(string? pendingStatus, string? expectedStatus)
    {
        if (pendingStatus is not null)
        {
            SetStatus(pendingStatus, expectedStatus);
        }
    }
}

public sealed class GlobalInputActionTriggeredEventArgs(GlobalInputAction action, string chord) : EventArgs
{
    public GlobalInputAction Action { get; } = action;

    public string Chord { get; } = chord;
}
