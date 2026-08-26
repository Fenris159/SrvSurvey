using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

public sealed class GlobalControllerInputService : IAsyncDisposable
{
    private readonly object lifecycleLock = new();
    private readonly object trackerLock = new();
    private readonly object statusLock = new();
    private readonly IControllerInputBackend backend;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly OverlayHostKind host;
    private readonly GlobalInputBindingRouter router;
    private readonly ControllerChordTracker tracker = new();
    private GlobalInputSettings settings;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "The run observer disposes the captured source after the controller loop exits.")]
    private CancellationTokenSource? runCancellation;
    private Task? runTask;
    private Task? disposalTask;
    private long runVersion;
    private volatile bool disposed;
    private string status;

    public GlobalControllerInputService(
        GlobalInputSettings settings,
        OverlayHostKind host,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        IControllerInputBackend? backend = null)
    {
        this.settings = settings
            ?? throw new ArgumentNullException(nameof(settings));
        this.host = host;
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive
            ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.backend = backend ?? new SdlControllerInputBackend();
        router = new GlobalInputBindingRouter(settings);
        status = GetInactiveStatus(settings)
            ?? "Controller input is ready to start.";
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
                return runCancellation is { IsCancellationRequested: false };
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var currentSettings = Volatile.Read(ref settings);
        var inactiveStatus = GetInactiveStatus(currentSettings);
        if (inactiveStatus is not null)
        {
            SetStatus(inactiveStatus);
            return;
        }

        SetStatus("Starting controller input...");
        CancellationTokenSource cancellation;
        Task task;
        long version;
        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (runCancellation is not null)
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            runCancellation = cancellation;
            version = Interlocked.Increment(ref runVersion);
            task = backend.RunAsync(
                currentSettings.ControllerDeviceId!,
                change => OnInputChanged(version, change),
                update => OnBackendStatusChanged(version, update),
                cancellation.Token);
            runTask = task;
        }

        _ = ObserveRunAsync(version, cancellation, task);
    }

    public void Update(GlobalInputSettings updatedSettings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(updatedSettings);
        var previous = Volatile.Read(ref settings);
        Volatile.Write(ref settings, updatedSettings);
        router.Update(updatedSettings);

        var mustRestart = previous.ControllerEnabled
                != updatedSettings.ControllerEnabled
            || !string.Equals(
                previous.ControllerDeviceId,
                updatedSettings.ControllerDeviceId,
                StringComparison.Ordinal);
        if (mustRestart)
        {
            var stoppedTask = StopRun();
            _ = RestartAfterStopAsync(stoppedTask);
            if (!updatedSettings.ControllerEnabled)
            {
                SetStatus("Controller input is disabled.");
            }

            return;
        }

        if (updatedSettings.ControllerEnabled)
        {
            Start();
        }
        else
        {
            SetStatus("Controller input is disabled.");
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

    private async Task DisposeCoreAsync()
    {
        disposed = true;
        await WaitForRunToStopAsync(StopRun()).ConfigureAwait(false);
        gameWindowTracker.Dispose();
    }

    private bool IsPlatformSupported()
    {
        return host is OverlayHostKind.Windows
            or OverlayHostKind.LinuxX11
            or OverlayHostKind.LinuxXWayland
            or OverlayHostKind.LinuxWayland;
    }

    private string? GetInactiveStatus(GlobalInputSettings currentSettings)
    {
        if (!currentSettings.ControllerEnabled)
        {
            return "Controller input is disabled.";
        }

        if (!IsPlatformSupported())
        {
            return "Controller input is unavailable on this platform.";
        }

        return string.IsNullOrWhiteSpace(currentSettings.ControllerDeviceId)
            ? "Select a controller before enabling controller input."
            : null;
    }

    private void OnInputChanged(long version, ControllerInputChange change)
    {
        if (version != Volatile.Read(ref runVersion))
        {
            return;
        }

        if (ShortcutCaptureSession.TryCapture(change))
        {
            lock (trackerLock)
            {
                tracker.Clear();
            }

            return;
        }

        string? chord;
        lock (trackerLock)
        {
            chord = tracker.UpdateToken(change.Token, change.IsPressed);
        }

        var currentSettings = Volatile.Read(ref settings);
        if (chord is null
            || !currentSettings.ControllerEnabled
            || !IsInputContextActive()
            || !router.TryResolve(chord, out var action))
        {
            return;
        }

        ActionTriggered?.Invoke(
            this,
            new GlobalInputActionTriggeredEventArgs(action, chord));
    }

    private bool IsInputContextActive()
    {
        return isApplicationActive()
            || gameWindowTracker.GetSnapshot().IsForeground;
    }

    private void OnBackendStatusChanged(
        long version,
        ControllerBackendStatus update)
    {
        if (version != Volatile.Read(ref runVersion))
        {
            return;
        }

        if (!update.IsConnected)
        {
            lock (trackerLock)
            {
                tracker.Clear();
            }
        }

        SetStatus(update.Message);
    }

    private async Task ObserveRunAsync(
        long version,
        CancellationTokenSource cancellation,
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
            var isCurrent = false;
            lock (lifecycleLock)
            {
                if (version == runVersion
                    && ReferenceEquals(runCancellation, cancellation))
                {
                    runCancellation = null;
                    runTask = null;
                    isCurrent = true;
                }
            }

            cancellation.Dispose();
            if (isCurrent
                && !disposed
                && Volatile.Read(ref settings).ControllerEnabled)
            {
                SetStatus(failure is null
                    ? "Controller input stopped."
                    : $"Controller input stopped: {failure.Message}");
            }
        }
    }

    private Task? StopRun()
    {
        CancellationTokenSource? cancellation;
        Task? task;
        lock (lifecycleLock)
        {
            Interlocked.Increment(ref runVersion);
            cancellation = runCancellation;
            runCancellation = null;
            task = runTask;
            runTask = null;
        }

        cancellation?.Cancel();
        lock (trackerLock)
        {
            tracker.Clear();
        }

        return task;
    }

    private async Task RestartAfterStopAsync(Task? stoppedTask)
    {
        await WaitForRunToStopAsync(stoppedTask).ConfigureAwait(false);
        lock (lifecycleLock)
        {
            if (!disposed && Volatile.Read(ref settings).ControllerEnabled)
            {
                Start();
            }
        }
    }

    private static async Task WaitForRunToStopAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ObserveRunAsync reports backend failures through the runtime status.
        }
    }

    private void SetStatus(string value)
    {
        lock (statusLock)
        {
            if (string.Equals(status, value, StringComparison.Ordinal))
            {
                return;
            }

            status = value;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
