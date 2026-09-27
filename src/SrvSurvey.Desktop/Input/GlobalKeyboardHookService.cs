using System.Diagnostics;
using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

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
    private GlobalInputSettings settings;
    private IGlobalHook? hook;
    private Task? runTask;
    private Task previousHookStopTask = Task.CompletedTask;
    private Task? disposalTask;
    private long lifecycleVersion;
    private volatile bool disposed;
    private string status;

    private readonly record struct KeyPressState(long LastPressTimestamp, ulong? LastReleaseEventTime);

    public GlobalKeyboardHookService(
        GlobalInputSettings settings,
        OverlayHostKind host,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        Func<IGlobalHook>? hookFactory = null,
        Func<long>? timestampProvider = null
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.host = host;
        this.gameWindowTracker = gameWindowTracker ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.hookFactory = hookFactory ?? CreateHook;
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        router = new GlobalInputBindingRouter(settings);
        status = settings.KeyboardEnabled
            ? "Global keyboard input is ready to start."
            : "Global keyboard input is disabled.";
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;

    public event EventHandler? StatusChanged;

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
                return hook?.IsRunning == true;
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Start(Volatile.Read(ref lifecycleVersion));
    }

    public void Update(GlobalInputSettings updatedSettings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(updatedSettings);
        Volatile.Write(ref settings, updatedSettings);
        router.Update(updatedSettings);

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
            SetStatus("Global keyboard input is unavailable on this platform.");
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

    private async Task DisposeCoreAsync()
    {
        disposed = true;
        Interlocked.Increment(ref lifecycleVersion);
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

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            GlobalInputSettings currentSettings = Volatile.Read(ref settings);
            if (disposed || !currentSettings.KeyboardEnabled || eventArgs.IsEventSimulated)
            {
                return;
            }

            KeyCode keyCode = eventArgs.Data.KeyCode;
            if (!IsModifierKey(keyCode) && !IsInputContextActive())
            {
                return;
            }

            long timestamp = timestampProvider();
            if (pressedKeys.TryGetValue(keyCode, out KeyPressState previousPress))
            {
                if (
                    OverlayPlatformCapabilities.IsX11Compatible(host)
                    && previousPress.LastReleaseEventTime is ulong releaseEventTime
                    && eventArgs.RawEvent.Time >= releaseEventTime
                    && eventArgs.RawEvent.Time - releaseEventTime <= X11AutoRepeatEventGapMilliseconds
                )
                {
                    // X11 reports a held key's repeat as a release and press
                    // with the same native event time.
                    pressedKeys[keyCode] = new KeyPressState(timestamp, null);
                    return;
                }

                if (
                    previousPress.LastReleaseEventTime is null
                    && timestamp >= previousPress.LastPressTimestamp
                    && timestamp - previousPress.LastPressTimestamp < PressStateMaxAgeTicks
                )
                {
                    pressedKeys[keyCode] = new KeyPressState(timestamp, null);
                    return;
                }
            }

            pressedKeys[keyCode] = new KeyPressState(timestamp, null);

            EventMask mask = eventArgs.RawEvent.Mask;
            if (OverlayPlatformCapabilities.IsX11Compatible(host))
            {
                // XRecord can omit held modifiers from a non-modifier key's mask.
                mask |= GetObservedModifierMask(timestamp);
            }

            string? chord = KeyboardChordFormatter.Format(keyCode, mask);
            if (chord is not null && router.TryResolve(chord, out GlobalInputAction action))
            {
                ActionTriggered?.Invoke(this, new GlobalInputActionTriggeredEventArgs(action, chord));
            }
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            KeyCode keyCode = eventArgs.Data.KeyCode;
            if (
                OverlayPlatformCapabilities.IsX11Compatible(host)
                && pressedKeys.TryGetValue(keyCode, out KeyPressState previousPress)
            )
            {
                pressedKeys[keyCode] = previousPress with { LastReleaseEventTime = eventArgs.RawEvent.Time };
            }
            else
            {
                pressedKeys.Remove(keyCode);
            }
        }
    }

    private bool IsInputContextActive()
    {
        return isApplicationActive() || gameWindowTracker.GetSnapshot().IsForeground;
    }

    private EventMask GetObservedModifierMask(long timestamp)
    {
        EventMask mask = EventMask.None;
        if (IsModifierHeld(KeyCode.VcLeftAlt, timestamp))
        {
            mask |= EventMask.LeftAlt;
        }

        if (IsModifierHeld(KeyCode.VcRightAlt, timestamp))
        {
            mask |= EventMask.RightAlt;
        }

        if (IsModifierHeld(KeyCode.VcLeftControl, timestamp))
        {
            mask |= EventMask.LeftCtrl;
        }

        if (IsModifierHeld(KeyCode.VcRightControl, timestamp))
        {
            mask |= EventMask.RightCtrl;
        }

        if (IsModifierHeld(KeyCode.VcLeftShift, timestamp))
        {
            mask |= EventMask.LeftShift;
        }

        if (IsModifierHeld(KeyCode.VcRightShift, timestamp))
        {
            mask |= EventMask.RightShift;
        }

        return mask;
    }

    private bool IsModifierHeld(KeyCode keyCode, long timestamp)
    {
        return pressedKeys.TryGetValue(keyCode, out KeyPressState state)
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

    private void OnHookDisabled(object? sender, HookEventArgs eventArgs)
    {
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
