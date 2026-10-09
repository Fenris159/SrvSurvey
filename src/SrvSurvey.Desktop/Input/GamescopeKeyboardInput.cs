using System.Diagnostics;
using Avalonia;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>A batch of buffered game keys and whether their display's press state must be reset.</summary>
internal sealed record GameKeyboardEventBatch(
    bool Reset,
    IReadOnlyList<UioHookEvent> Events,
    bool IsGameForeground = false
);

/// <summary>Follows a verified Gamescope bridge or discovers Elite's nested display without changing the application's DISPLAY.</summary>
internal sealed class GamescopeKeyboardInput : IKeyboardActivationSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);
    private readonly Func<GamescopeGameWindowBridge?> readBridge;
    private readonly Func<string, IX11KeyboardRecord?> createRecord;
    private readonly string? desktopDisplay;
    private readonly Func<EliteKeyboardDisplay?> readDisplay;
    private readonly Func<string, IGameWindowTracker?> createTracker;
    private readonly KeyPressTracker keys = new(KeyboardInputSource.NestedDisplay, usesX11Events: true);
    private readonly CancellationTokenSource pollCancellation = new();
    private IGameWindowTracker? nestedTracker;
    private GamescopeGameWindowBridge? bridge;
    private IX11KeyboardRecord? record;
    private long lastBridgeCheck;
    private IKeyboardActivationSink? sink;
    private Task? pollTask;
    private volatile bool enabled;
    private volatile bool gameForeground;

    /// <summary>Creates bridge/process discovery, focus tracking, and native event sources.</summary>
    public GamescopeKeyboardInput(
        Func<GamescopeGameWindowBridge?>? readBridge = null,
        Func<string, IX11KeyboardRecord?>? createRecord = null,
        string? desktopDisplay = null,
        Func<EliteKeyboardDisplay?>? readDisplay = null,
        Func<string, IGameWindowTracker?>? createTracker = null,
        Func<PixelRect?>? preferredMonitorBounds = null
    )
    {
        this.readBridge = readBridge ?? (static () => null);
        this.createRecord = createRecord ?? X11KeyboardRecord.TryCreate;
        this.desktopDisplay = desktopDisplay ?? Environment.GetEnvironmentVariable("DISPLAY");
        this.readDisplay =
            readDisplay
            ?? (
                readBridge is null
                    ? () => EliteKeyboardDisplayDiscovery.ReadCurrent(preferredMonitorBounds?.Invoke())
                    : static () => null
            );
        this.createTracker = createTracker ?? X11GameWindowTracker.TryCreate;
    }

    public KeyboardInputSource Kind => KeyboardInputSource.NestedDisplay;

    public KeyboardSourceState State =>
        new(record is { HasFailed: false })
        {
            IsGameForeground = gameForeground,
            GameDisplay = bridge is { } current ? new GameDisplayConnection(current.Display, current.ProcessId) : null,
        };

    public void Attach(IKeyboardActivationSink sink) => this.sink = sink;

    /// <summary>Starts draining buffered game-display events without blocking the desktop dispatcher.</summary>
    public void Start(GlobalInputSettings settings)
    {
        enabled = settings.KeyboardEnabled;
        pollTask ??= PollAsync();
    }

    /// <summary>Closes the game connection on the next poll when keyboard input is disabled.</summary>
    public void Update(GlobalInputSettings settings) => enabled = settings.KeyboardEnabled;

    public void ResetDetection() => keys.Clear();

    /// <summary>Stops polling before the native connections are closed.</summary>
    public async ValueTask DisposeAsync()
    {
        await pollCancellation.CancelAsync().ConfigureAwait(false);
        if (pollTask is not null)
        {
            await pollTask.ConfigureAwait(false);
        }
        CloseDisplay();
        pollCancellation.Dispose();
    }

    /// <summary>Reopens changed game displays and drains their buffered press/release events.</summary>
    internal GameKeyboardEventBatch ReadEvents(bool keyboardEnabled)
    {
        if (!keyboardEnabled)
        {
            bool reset = record is not null;
            CloseDisplay();
            return new GameKeyboardEventBatch(reset, []);
        }

        bool changed = RefreshBridge();
        IReadOnlyList<UioHookEvent> events = record?.ReadEvents() ?? [];
        if (record?.HasFailed == true)
        {
            DisposeRecord();
            return new GameKeyboardEventBatch(true, []);
        }
        return new GameKeyboardEventBatch(changed, events, nestedTracker?.GetSnapshot().IsForeground == true);
    }

    private async Task PollAsync()
    {
        using var timer = new PeriodicTimer(PollInterval);
        bool failureReported = false;
        try
        {
            while (await timer.WaitForNextTickAsync(pollCancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    Deliver(ReadEvents(enabled));
                    failureReported = false;
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException || !pollCancellation.IsCancellationRequested)
                {
                    keys.Clear();
                    gameForeground = false;
                    try
                    {
                        sink?.ReleaseSelection(KeyboardInputSource.NestedDisplay);
                    }
                    catch (Exception)
                    {
                        // A failing observer must not prevent the next poll from recovering.
                    }
                    if (!failureReported)
                    {
                        failureReported = true;
                        Trace.TraceWarning("Game keyboard polling failed: {0}", exception.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (pollCancellation.IsCancellationRequested)
        {
            // The nested listener stops before its native connections are disposed.
        }
    }

    /// <summary>Reports one batch with this display's own repeat, modifier, and foreground state.</summary>
    private void Deliver(GameKeyboardEventBatch batch)
    {
        if (sink is not { } target)
        {
            return;
        }

        if (batch.Reset)
        {
            keys.Clear();
            target.ReleaseSelection(KeyboardInputSource.NestedDisplay);
        }
        gameForeground = batch.IsGameForeground;
        KeyboardFocusEvidence evidence = batch.IsGameForeground
            ? KeyboardFocusEvidence.GameDisplayForeground
            : KeyboardFocusEvidence.DesktopWindow;
        foreach (UioHookEvent input in batch.Events)
        {
            if (input.Type != EventType.KeyPressed)
            {
                keys.Release(input.Keyboard.KeyCode, input.Time);
            }
            else if (enabled)
            {
                keys.Press(target, input.Keyboard.KeyCode, input.Mask, input.Time, evidence);
            }
        }
    }

    /// <summary>Checks game metadata once per second and opens only a distinct local game display.</summary>
    private bool RefreshBridge()
    {
        long now = Stopwatch.GetTimestamp();
        if (lastBridgeCheck != 0 && Stopwatch.GetElapsedTime(lastBridgeCheck, now) < TimeSpan.FromSeconds(1))
        {
            return false;
        }
        lastBridgeCheck = now;
        GamescopeGameWindowBridge? current = readBridge();
        bool discovered = current is null;
        if (current is null && readDisplay() is EliteKeyboardDisplay gameDisplay)
        {
            current = new GamescopeGameWindowBridge(gameDisplay.ProcessId, gameDisplay.Display, default);
        }
        if (current is not null && NormalizeDisplay(current.Display) == NormalizeDisplay(desktopDisplay))
        {
            current = null; // The desktop hook already listens on this server.
        }
        if (record is not null && bridge?.ProcessId == current?.ProcessId && bridge?.Display == current?.Display)
        {
            return false;
        }
        DisposeRecord();
        bridge = current;
        if (current is null)
        {
            return true;
        }
        record = createRecord(current.Display);
        if (record is not null && discovered)
        {
            nestedTracker = createTracker(current.Display);
        }
        if (record is not null)
        {
            Trace.TraceInformation("Global keyboard input: listening on Elite display {0}.", current.Display);
        }
        return true;
    }

    /// <summary>Recognizes the default screen suffix as the same X server.</summary>
    private static string? NormalizeDisplay(string? display) =>
        display?.EndsWith(".0", StringComparison.Ordinal) == true ? display[..^2] : display;

    /// <summary>Closes transient connections while allowing a later enable or game restart.</summary>
    private void CloseDisplay()
    {
        DisposeRecord();
        bridge = null;
        lastBridgeCheck = 0;
    }

    /// <summary>Releases the current recorder exactly once.</summary>
    private void DisposeRecord()
    {
        record?.Dispose();
        record = null;
        nestedTracker?.Dispose();
        nestedTracker = null;
    }
}

/// <summary>Owns a nonblocking XRecord stream on one transient X server.</summary>
internal interface IX11KeyboardRecord : IDisposable
{
    /// <summary>Reports a disconnected native stream.</summary>
    bool HasFailed { get; }

    /// <summary>Drains recorded key events, including keys pressed and released between reads.</summary>
    IReadOnlyList<UioHookEvent> ReadEvents();
}
