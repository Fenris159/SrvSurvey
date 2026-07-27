using SharpHook;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

public sealed class GlobalKeyboardHookService : IDisposable
{
    private readonly Func<IGlobalHook> hookFactory;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly OverlayHostKind host;
    private readonly GlobalInputBindingRouter router;
    private GlobalInputSettings settings;
    private IGlobalHook? hook;
    private bool disposed;

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
        Status = settings.KeyboardEnabled
            ? "Global keyboard input is ready to start."
            : "Global keyboard input is disabled.";
    }

    public event EventHandler<GlobalInputActionTriggeredEventArgs>? ActionTriggered;

    public event EventHandler? StatusChanged;

    public string Status { get; private set; }

    public bool IsRunning => Volatile.Read(ref hook)?.IsRunning == true;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!settings.KeyboardEnabled)
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

        if (Volatile.Read(ref hook) is not null)
        {
            return;
        }

        IGlobalHook? pendingHook = null;
        try
        {
            pendingHook = hookFactory();
            pendingHook.KeyReleased += OnKeyReleased;
            pendingHook.HookEnabled += OnHookEnabled;
            pendingHook.HookDisabled += OnHookDisabled;
            if (Interlocked.CompareExchange(
                    ref hook,
                    pendingHook,
                    comparand: null) is not null)
            {
                DisposeHook(pendingHook);
                return;
            }

            SetStatus("Starting global keyboard input...");
            var task = pendingHook.RunAsync();
            _ = ObserveRunAsync(pendingHook, task);
        }
        catch (Exception exception)
        {
            if (pendingHook is not null)
            {
                StopHook(pendingHook);
            }

            SetStatus($"Global keyboard input could not start: {exception.Message}");
        }
    }

    public void Update(GlobalInputSettings updatedSettings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        settings = updatedSettings
            ?? throw new ArgumentNullException(nameof(updatedSettings));
        router.Update(settings);
        if (settings.KeyboardEnabled)
        {
            Start();
        }
        else
        {
            StopHook();
            SetStatus("Global keyboard input is disabled.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopHook();
        gameWindowTracker.Dispose();
    }

    private static IGlobalHook CreateHook()
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
        if (!settings.KeyboardEnabled
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

    private bool IsInputContextActive()
    {
        return isApplicationActive()
            || gameWindowTracker.GetSnapshot().IsForeground;
    }

    private void OnHookEnabled(object? sender, HookEventArgs eventArgs)
    {
        SetStatus("Global keyboard input is active.");
    }

    private void OnHookDisabled(object? sender, HookEventArgs eventArgs)
    {
        if (!disposed && settings.KeyboardEnabled)
        {
            SetStatus("Global keyboard input stopped.");
        }
    }

    private async Task ObserveRunAsync(
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
            if (StopHook(observedHook)
                && !disposed
                && settings.KeyboardEnabled)
            {
                SetStatus(failure is null
                    ? "Global keyboard input stopped."
                    : $"Global keyboard input stopped: {failure.Message}");
            }
        }
    }

    private void StopHook()
    {
        var currentHook = Interlocked.Exchange(ref hook, null);
        if (currentHook is null)
        {
            return;
        }

        DisposeHook(currentHook);
    }

    private bool StopHook(IGlobalHook expectedHook)
    {
        if (!ReferenceEquals(
                Interlocked.CompareExchange(
                    ref hook,
                    value: null,
                    expectedHook),
                expectedHook))
        {
            return false;
        }

        DisposeHook(expectedHook);
        return true;
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

    private void SetStatus(string status)
    {
        if (string.Equals(Status, status, StringComparison.Ordinal))
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record GlobalInputActionTriggeredEventArgs(
    GlobalInputAction Action,
    string Chord);
