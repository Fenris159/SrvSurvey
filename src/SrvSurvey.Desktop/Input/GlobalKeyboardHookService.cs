using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

public sealed class GlobalKeyboardHookService : IAsyncDisposable
{
    private readonly object callbackLock = new();
    private readonly object lifecycleLock = new();
    private readonly object statusLock = new();
    private readonly Func<IGlobalHook> hookFactory;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly OverlayHostKind host;
    private readonly GlobalInputBindingRouter router;
    private GlobalInputSettings settings;
    private IGlobalHook? hook;
    private Task? runTask;
    private Task previousHookStopTask = Task.CompletedTask;
    private Task? disposalTask;
    private long lifecycleVersion;
    private volatile bool disposed;
    private string status;

    public GlobalKeyboardHookService(
        GlobalInputSettings settings,
        OverlayHostKind host,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        Func<IGlobalHook>? hookFactory = null)
    {
        this.settings = settings
            ?? throw new ArgumentNullException(nameof(settings));
        this.host = host;
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive
            ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.hookFactory = hookFactory ?? CreateHook;
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

        var version = Interlocked.Increment(ref lifecycleVersion);
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
        var currentSettings = Volatile.Read(ref settings);
        if (!currentSettings.KeyboardEnabled)
        {
            SetStatus("Global keyboard input is disabled.");
            return;
        }

        if (host is not OverlayHostKind.Windows
            && !OverlayPlatformCapabilities.IsX11Compatible(host))
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
            if (disposed
                || version != lifecycleVersion
                || hook is not null)
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

                    pendingStatus =
                        $"Global keyboard input could not start: {exception.Message}";
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
            runAsyncOnBackgroundThread: true);
    }

    private void OnKeyReleased(
        object? sender,
        KeyboardHookEventArgs eventArgs)
    {
        lock (callbackLock)
        {
            var currentSettings = Volatile.Read(ref settings);
            if (disposed
                || !currentSettings.KeyboardEnabled
                || eventArgs.IsEventSimulated
                || !IsInputContextActive())
            {
                return;
            }

            var chord = KeyboardChordFormatter.Format(
                eventArgs.Data.KeyCode,
                eventArgs.RawEvent.Mask);
            if (chord is not null && router.TryResolve(chord, out var action))
            {
                ActionTriggered?.Invoke(
                    this,
                    new GlobalInputActionTriggeredEventArgs(action, chord));
            }
        }
    }

    private bool IsInputContextActive()
    {
        return isApplicationActive()
            || gameWindowTracker.GetSnapshot().IsForeground;
    }

    private void OnHookEnabled(object? sender, HookEventArgs eventArgs)
    {
        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled)
        {
            SetStatus("Global keyboard input is active.");
        }
    }

    private void OnHookDisabled(object? sender, HookEventArgs eventArgs)
    {
        if (!disposed && Volatile.Read(ref settings).KeyboardEnabled)
        {
            SetStatus("Global keyboard input stopped.");
        }
    }

    private async Task ObserveRunAsync(
        long version,
        IGlobalHook observedHook,
        Task task)
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
            if (StopHook(observedHook, task)
                && !disposed
                && version == Volatile.Read(ref lifecycleVersion)
                && Volatile.Read(ref settings).KeyboardEnabled)
            {
                SetStatus(failure is null
                    ? "Global keyboard input stopped."
                    : $"Global keyboard input stopped: {failure.Message}");
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
            stopCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
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
            stopCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            previousHookStopTask = stopCompletion.Task;
        }

        DisposeHook(expectedHook);
        _ = CompleteHookStopAsync(expectedTask, stopCompletion);
        return true;
    }

    private async Task StartAfterStopAsync(long version, Task stoppedTask)
    {
        await WaitForHookToStopAsync(stoppedTask).ConfigureAwait(false);
        if (!disposed
            && version == Volatile.Read(ref lifecycleVersion)
            && Volatile.Read(ref settings).KeyboardEnabled)
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

    private static async Task CompleteHookStopAsync(
        Task runTask,
        TaskCompletionSource stopCompletion)
    {
        await WaitForHookToStopAsync(runTask).ConfigureAwait(false);
        stopCompletion.TrySetResult();
    }

    private void DisposeHook(IGlobalHook currentHook)
    {
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
            if ((expectedStatus is not null
                    && !string.Equals(
                        this.status,
                        expectedStatus,
                        StringComparison.Ordinal))
                || string.Equals(this.status, status, StringComparison.Ordinal))
            {
                return;
            }

            this.status = status;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishPendingStatus(
        string? pendingStatus,
        string? expectedStatus)
    {
        if (pendingStatus is not null)
        {
            SetStatus(pendingStatus, expectedStatus);
        }
    }
}

public sealed record GlobalInputActionTriggeredEventArgs(
    GlobalInputAction Action,
    string Chord);
