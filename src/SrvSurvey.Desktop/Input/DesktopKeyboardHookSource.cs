using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Listens to the desktop keyboard through SharpHook without overlapping native event loops across restarts.</summary>
internal sealed class DesktopKeyboardHookSource : IKeyboardActivationSource
{
    private readonly Lock lifecycleLock = new();
    private readonly OverlayHostKind host;
    private readonly Func<IGlobalHook> hookFactory;
    private readonly KeyPressTracker keys;
    private IKeyboardActivationSink? sink;
    private IGlobalHook? hook;
    private Task? runTask;
    private Task previousHookStopTask = Task.CompletedTask;
    private long lifecycleVersion;
    private volatile bool enabled;
    private volatile bool disposed;

    /// <summary>Creates a listener that uses X11 repeat repair on X11-compatible hosts.</summary>
    public DesktopKeyboardHookSource(OverlayHostKind host, Func<IGlobalHook>? hookFactory = null)
    {
        this.host = host;
        this.hookFactory = hookFactory ?? CreateHook;
        keys = new KeyPressTracker(KeyboardInputSource.Desktop, OverlayPlatformCapabilities.IsX11Compatible(host));
    }

    public KeyboardInputSource Kind => KeyboardInputSource.Desktop;

    public KeyboardSourceState State => new(Volatile.Read(ref hook)?.IsRunning == true);

    public void Attach(IKeyboardActivationSink sink) => this.sink = sink;

    public void Start(GlobalInputSettings settings)
    {
        enabled = settings.KeyboardEnabled;
        StartHook(Volatile.Read(ref lifecycleVersion));
    }

    /// <summary>Restarts or stops the hook, discarding status from a superseded run.</summary>
    public void Update(GlobalInputSettings settings)
    {
        enabled = settings.KeyboardEnabled;
        long version = Interlocked.Increment(ref lifecycleVersion);
        if (settings.KeyboardEnabled)
        {
            StartHook(version);
        }
        else
        {
            _ = StopHook();
        }
    }

    public void ResetDetection() => keys.Clear();

    /// <summary>Waits for the native event loop to stop before shared dependencies are disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Interlocked.Increment(ref lifecycleVersion);
        await WaitForHookToStopAsync(StopHook()).ConfigureAwait(false);
    }

    /// <summary>Starts a raw listener only on platforms that provide desktop keyboard hooks.</summary>
    private void StartHook(long version)
    {
        if (!enabled || sink is not { } target)
        {
            return;
        }

        string? unavailableStatus = GetUnavailableStatus();
        if (unavailableStatus is not null)
        {
            target.ReportStatus(unavailableStatus);
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
                    Volatile.Write(ref hook, pendingHook);

                    statusBeforeStart = target.Status;
                    pendingStatus = "Starting global keyboard input...";
                    startedTask = pendingHook.RunAsync();
                    runTask = startedTask;
                    startedHook = pendingHook;
                }
                catch (Exception exception)
                {
                    Volatile.Write(ref hook, null);
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

        if (pendingStatus is not null)
        {
            target.ReportStatus(pendingStatus, statusBeforeStart);
        }

        if (stoppedTask is not null)
        {
            _ = StartAfterStopAsync(version, stoppedTask);
        }
        else if (startedHook is not null && startedTask is not null)
        {
            _ = ObserveRunAsync(version, startedHook, startedTask);
        }
    }

    /// <summary>Returns the reason a desktop hook cannot start, leaving other keyboard sources available.</summary>
    private string? GetUnavailableStatus()
    {
        if (host is OverlayHostKind.Windows || OverlayPlatformCapabilities.IsX11Compatible(host))
        {
            return null;
        }

        return host == OverlayHostKind.LinuxWayland
            ? "Wayland keyboard input is waiting for Global Shortcuts portal support."
            : "Global keyboard input is unavailable on this platform.";
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
        if (disposed || !enabled || eventArgs.IsEventSimulated || sink is not { } target)
        {
            return;
        }

        keys.Press(
            target,
            eventArgs.Data.KeyCode,
            eventArgs.RawEvent.Mask,
            eventArgs.RawEvent.Time,
            KeyboardFocusEvidence.DesktopWindow
        );
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs eventArgs) =>
        keys.Release(eventArgs.Data.KeyCode, eventArgs.RawEvent.Time);

    private void OnHookEnabled(object? sender, HookEventArgs eventArgs)
    {
        keys.Clear();
        if (!disposed && enabled)
        {
            sink?.ReportStatus("Global keyboard input is active.");
        }
    }

    /// <summary>Releases desktop source selections when the raw listener stops.</summary>
    private void OnHookDisabled(object? sender, HookEventArgs eventArgs)
    {
        sink?.ReleaseSelection(KeyboardInputSource.Desktop);
        keys.Clear();
        if (!disposed && enabled)
        {
            sink?.ReportStatus("Global keyboard input stopped.");
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
            if (StopHook(observedHook, task) && !disposed && version == Volatile.Read(ref lifecycleVersion) && enabled)
            {
                sink?.ReportStatus(
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
            Volatile.Write(ref hook, null);
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

            Volatile.Write(ref hook, null);
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
        if (!disposed && version == Volatile.Read(ref lifecycleVersion) && enabled)
        {
            StartHook(version);
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
}
