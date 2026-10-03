using System.Diagnostics;
using Avalonia;
using SharpHook.Data;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>A batch of buffered game keys and whether their display's press state must be reset.</summary>
public sealed record GameKeyboardEventBatch(
    bool Reset,
    IReadOnlyList<UioHookEvent> Events,
    bool IsGameForeground = false
);

/// <summary>Provides keyboard events from a game display separate from the desktop.</summary>
public interface IGameKeyboardInput : IDisposable
{
    /// <summary>Drains pending events, closing the game connection when keyboard input is disabled.</summary>
    GameKeyboardEventBatch ReadEvents(bool enabled);

    /// <summary>Reports a connected secondary keyboard source.</summary>
    bool IsRunning => false;
    string? Display => null;
    int? ProcessId => null;
}

/// <summary>Follows a verified Gamescope bridge or discovers Elite's nested display without changing the application's DISPLAY.</summary>
internal sealed class GamescopeKeyboardInput : IGameKeyboardInput
{
    private readonly Func<GamescopeGameWindowBridge?> readBridge;
    private readonly Func<string, IX11KeyboardRecord?> createRecord;
    private readonly string? desktopDisplay;
    private readonly Func<EliteKeyboardDisplay?> readDisplay;
    private readonly Func<string, IGameWindowTracker?> createTracker;
    private IGameWindowTracker? nestedTracker;
    private GamescopeGameWindowBridge? bridge;
    private IX11KeyboardRecord? record;
    private long lastBridgeCheck;

    public bool IsRunning => record is { HasFailed: false };
    public string? Display => bridge?.Display;
    public int? ProcessId => bridge?.ProcessId;

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

    /// <summary>Reopens changed game displays and drains their buffered press/release events.</summary>
    public GameKeyboardEventBatch ReadEvents(bool enabled)
    {
        if (!enabled)
        {
            bool reset = record is not null;
            Dispose();
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
    public void Dispose()
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
