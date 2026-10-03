using SharpHook.Data;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GamescopeKeyboardInputTests
{
    /// <summary>Discovers and tracks an unbridged Elite display without listening twice on the desktop server.</summary>
    [Fact]
    public void DiscoversDisplayWithoutBridgeAndCarriesGameFocus()
    {
        var recorder = new FakeRecord();
        var tracker = new NestedTracker();
        EliteKeyboardDisplay? display = new(123, ":2");
        using var input = new GamescopeKeyboardInput(() => null, _ => recorder, ":0", () => display, _ => tracker);
        recorder.Events = [new UioHookEvent { Type = EventType.KeyPressed }];
        GameKeyboardEventBatch batch = input.ReadEvents(true);
        Assert.True(batch.IsGameForeground);
        Assert.Single(batch.Events);
        input.Dispose();
        Assert.True(tracker.Disposed);
        display = new(123, ":0.0");
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Equal(1, recorder.Disposals);
    }

    /// <summary>Provides a foreground game on the discovered server.</summary>
    private sealed class NestedTracker : IGameWindowTracker
    {
        public bool Disposed { get; private set; }

        /// <summary>Returns the foreground window.</summary>
        public GameWindowSnapshot GetSnapshot() => new(1, 123, new Avalonia.PixelRect(0, 0, 100, 100), true, true);

        /// <summary>Records that the discovered tracker was closed.</summary>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Checks routing, server failure, disabling, and reconnection without desktop duplication.</summary>
    [Fact]
    public void FollowsBridgeAndClearsStaleEventsAcrossRestarts()
    {
        GamescopeGameWindowBridge? bridge = new(1, ":nested", default);
        var opened = new List<FakeRecord>();
        using var input = new GamescopeKeyboardInput(
            () => bridge,
            _ =>
            {
                var record = new FakeRecord();
                opened.Add(record);
                return record;
            },
            ":desktop"
        );

        Assert.True(input.ReadEvents(true).Reset);
        opened[0].Events = [new UioHookEvent { Type = EventType.KeyPressed }];
        Assert.Single(input.ReadEvents(true).Events);
        Assert.False(input.ReadEvents(true).Reset);
        opened[0].HasFailed = true;
        GameKeyboardEventBatch failed = input.ReadEvents(true);
        Assert.True(failed.Reset);
        Assert.Empty(failed.Events);
        Assert.Equal(1, opened[0].Disposals);
        input.Dispose();

        Assert.True(input.ReadEvents(true).Reset);
        Assert.Equal(2, opened.Count);
        Assert.True(input.ReadEvents(false).Reset);
        Assert.Equal(1, opened[1].Disposals);
        Assert.False(input.ReadEvents(false).Reset);
        bridge = new(2, ":desktop", default);
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Equal(2, opened.Count);
        input.Dispose();
        bridge = null;
        Assert.Empty(input.ReadEvents(true).Events);
    }

    /// <summary>Checks that an unavailable extension or permission leaves the desktop hook usable.</summary>
    [Fact]
    public void MissingNativeRecorderIsAnEmptyBatch()
    {
        using var input = new GamescopeKeyboardInput(() => new(1, ":nested", default), _ => null, ":desktop");
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Empty(input.ReadEvents(true).Events);
    }

    /// <summary>Tracks the lifetime and output of a simulated nested recorder.</summary>
    private sealed class FakeRecord : IX11KeyboardRecord
    {
        public bool HasFailed { get; set; }
        public UioHookEvent[] Events { get; set; } = [];
        public int Disposals { get; private set; }

        /// <summary>Returns one pending event batch.</summary>
        public IReadOnlyList<UioHookEvent> ReadEvents()
        {
            UioHookEvent[] result = Events;
            Events = [];
            return result;
        }

        /// <summary>Records connection closure.</summary>
        public void Dispose() => Disposals++;
    }
}
