using System.Diagnostics;
using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GamescopeKeyboardInputTests
{
    /// <summary>Discovers and tracks an unbridged Elite display without listening twice on the desktop server.</summary>
    [Fact]
    public async Task DiscoversDisplayWithoutBridgeAndCarriesGameFocus()
    {
        var recorder = new FakeRecord();
        var tracker = new TestGameWindowTracker { Focused = true };
        EliteKeyboardDisplay? display = new(123, ":2");
        await using var input = new GamescopeKeyboardInput(
            () => null,
            _ => recorder,
            ":0",
            () => display,
            _ => tracker
        );
        recorder.Events = [new UioHookEvent { Type = EventType.KeyPressed }];
        GameKeyboardEventBatch batch = input.ReadEvents(true);
        Assert.True(batch.IsGameForeground);
        Assert.Single(batch.Events);
        Assert.Equal(new GameDisplayConnection(":2", 123), input.State.GameDisplay);
        Assert.True(input.ReadEvents(false).Reset);
        Assert.True(tracker.IsDisposed);
        display = new(123, ":0.0");
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Equal(1, recorder.Disposals);
        Assert.Null(input.State.GameDisplay);
    }

    /// <summary>Prefers a verified bridge and uses process discovery only when the bridge is missing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BridgeAndProcessDiscoveryWorkTogether(bool hasBridge)
    {
        var recorder = new FakeRecord();
        int discoveries = 0;
        string? openedDisplay = null;
        await using var input = new GamescopeKeyboardInput(
            () => hasBridge ? new GamescopeGameWindowBridge(123, ":2", default) : null,
            display =>
            {
                openedDisplay = display;
                return recorder;
            },
            ":0",
            () =>
            {
                discoveries++;
                return new EliteKeyboardDisplay(456, ":3");
            },
            _ => new TestGameWindowTracker(),
            () => new Avalonia.PixelRect(1920, 0, 2560, 1440)
        );
        input.ReadEvents(true);
        Assert.Equal(hasBridge ? ":2" : ":3", openedDisplay);
        Assert.Equal(hasBridge ? 0 : 1, discoveries);
        Assert.Equal(hasBridge ? 123 : 456, input.State.GameDisplay?.ProcessId);
    }

    /// <summary>Checks routing, server failure, disabling, and reconnection without desktop duplication.</summary>
    [Fact]
    public async Task FollowsBridgeAndClearsStaleEventsAcrossRestarts()
    {
        GamescopeGameWindowBridge? bridge = new(1, ":nested", default);
        var opened = new List<FakeRecord>();
        await using var input = new GamescopeKeyboardInput(
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
        input.ReadEvents(false);

        Assert.True(input.ReadEvents(true).Reset);
        Assert.Equal(2, opened.Count);
        Assert.True(input.ReadEvents(false).Reset);
        Assert.Equal(1, opened[1].Disposals);
        Assert.False(input.ReadEvents(false).Reset);
        bridge = new(2, ":desktop", default);
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Equal(2, opened.Count);
        input.ReadEvents(false);
        bridge = null;
        Assert.Empty(input.ReadEvents(true).Events);
    }

    /// <summary>Checks that an unavailable extension or permission leaves the desktop hook usable.</summary>
    [Fact]
    public async Task MissingNativeRecorderIsAnEmptyBatch()
    {
        await using var input = new GamescopeKeyboardInput(() => new(1, ":nested", default), _ => null, ":desktop");
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.Empty(input.ReadEvents(true).Events);
        Assert.False(input.State.IsRunning);
    }

    /// <summary>Polling reports nested chords with the display's own focus, repeat, and modifier state.</summary>
    [Fact]
    public async Task PollingReportsNestedChordsWithItsOwnFocusRepeatAndModifiers()
    {
        var recorder = new FakeRecord();
        var tracker = new TestGameWindowTracker { Focused = true };
        await using var input = new GamescopeKeyboardInput(
            () => null,
            _ => recorder,
            ":0",
            () => new EliteKeyboardDisplay(123, ":2"),
            _ => tracker
        );
        var sink = new RecordingKeyboardSink();
        input.Attach(sink);
        recorder.Enqueue(KeyEvent(KeyCode.VcRightAlt, true, 100), KeyEvent(KeyCode.VcO, true, 110));
        input.Start(GlobalInputSettings.Default with { KeyboardEnabled = true });

        await WaitForAsync(() => sink.Activations.Count == 1);
        KeyboardActivation activation = Assert.Single(sink.Activations);
        Assert.Equal(
            new KeyboardActivation(KeyboardInputSource.NestedDisplay, "ALT O", null, activation.Focus),
            activation
        );
        Assert.Contains(KeyboardFocusEvidence.GameDisplayForeground, sink.Evidence);
        Assert.True(input.State is { IsRunning: true, IsGameForeground: true });
        Assert.Contains(KeyboardInputSource.NestedDisplay, sink.Releases);

        recorder.Enqueue(KeyEvent(KeyCode.VcO, false, 120), KeyEvent(KeyCode.VcO, true, 121));
        recorder.Enqueue(KeyEvent(KeyCode.VcO, false, 200), KeyEvent(KeyCode.VcO, true, 300));
        await WaitForAsync(() => sink.Activations.Count == 2);
        Assert.Equal(["ALT O", "ALT O"], sink.Chords);

        input.ResetDetection();
        recorder.Enqueue(KeyEvent(KeyCode.VcO, true, 400));
        await WaitForAsync(() => sink.Activations.Count == 3);
        Assert.Equal("O", sink.Chords.Last());

        input.Update(GlobalInputSettings.Default);
        await WaitForAsync(() => recorder.Disposals == 1);
        Assert.False(input.State.IsRunning);
    }

    /// <summary>A transient discovery, native-read, focus, or activation failure never kills shortcut polling.</summary>
    [Theory]
    [InlineData("bridge")]
    [InlineData("record")]
    [InlineData("tracker")]
    [InlineData("focus")]
    [InlineData("activation")]
    [InlineData("cancellation")]
    public async Task PollingRecoversAfterTransientFailures(string failure)
    {
        int attempts = 0;
        void FailOnce()
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                if (failure == "cancellation")
                {
                    throw new OperationCanceledException("Unrelated operation canceled");
                }
                throw new InvalidOperationException("Transient poll failure");
            }
        }
        var recorder = new FakeRecord();
        var tracker = new TestGameWindowTracker { Focused = true };
        var sink = new RecordingKeyboardSink();
        if (failure is "record" or "cancellation")
        {
            recorder.BeforeRead = FailOnce;
        }
        if (failure == "tracker")
        {
            tracker.BeforeSnapshot = FailOnce;
        }
        if (failure == "focus")
        {
            sink.BeforeFocus = FailOnce;
        }
        if (failure == "activation")
        {
            sink.BeforeActivation = FailOnce;
        }
        await using var input = new GamescopeKeyboardInput(
            () =>
            {
                if (failure == "bridge")
                {
                    FailOnce();
                }
                return null;
            },
            _ => recorder,
            ":0",
            () => new EliteKeyboardDisplay(123, ":2"),
            _ => tracker
        );
        input.Attach(sink);
        recorder.Enqueue(KeyEvent(KeyCode.VcO, true, 100));
        recorder.Enqueue(KeyEvent(KeyCode.VcO, true, 200));
        input.Start(GlobalInputSettings.Default with { KeyboardEnabled = true });
        await WaitForAsync(() => !sink.Activations.IsEmpty);
        Assert.True(Volatile.Read(ref attempts) >= 2);
        Assert.Equal("O", Assert.Single(sink.Activations).Chord);
        Assert.True(input.State.IsGameForeground);
    }

    /// <summary>Checks nested keys use independent modifier state and honor disable/re-enable and context gating.</summary>
    [Theory]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    [InlineData(OverlayHostKind.LinuxWayland)]
    public async Task NestedKeysRemainIndependentFromDesktopModifiers(OverlayHostKind host)
    {
        using var desktop = new TestGlobalHook(TestThreadingMode.Simple);
        var opened = new List<FakeRecord>();
        var nested = new GamescopeKeyboardInput(
            () => new GamescopeGameWindowBridge(1, ":nested", default),
            _ =>
            {
                var record = new FakeRecord();
                lock (opened)
                {
                    opened.Add(record);
                }
                return record;
            },
            ":desktop"
        );
        var tracker = new TestGameWindowTracker
        {
            Snapshot = GameWindowSnapshot.Unavailable with { IsForeground = true },
        };
        GlobalInputSettings settings = GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string> { [GlobalInputAction.ToggleOverlayInteraction] = "O" },
        };
        await using var service = new GlobalKeyboardHookService(
            settings,
            [nested, new DesktopKeyboardHookSource(host, () => desktop)],
            tracker,
            () => false
        );
        int actions = 0;
        service.ActionTriggered += (_, _) => Interlocked.Increment(ref actions);
        service.Start();
        await WaitForAsync(() => Opened(opened, 1));
        desktop.SimulateKeyPress(KeyCode.VcLeftAlt);
        opened[0].Enqueue(KeyEvent(KeyCode.VcO, true, 100));
        await WaitForAsync(() => Volatile.Read(ref actions) == 1);
        opened[0].Enqueue(KeyEvent(KeyCode.VcO, true, 110), KeyEvent(KeyCode.VcO, false, 120));
        opened[0].Enqueue(KeyEvent(KeyCode.VcO, true, 200), KeyEvent(KeyCode.VcO, false, 210));
        await WaitForAsync(() => Volatile.Read(ref actions) == 2);
        tracker.Snapshot = GameWindowSnapshot.Unavailable;
        opened[0].Enqueue(KeyEvent(KeyCode.VcO, true, 300));
        await WaitForAsync(() => opened[0].IsDrained);
        await Task.Delay(60);
        Assert.Equal(2, Volatile.Read(ref actions));

        service.Update(settings with { KeyboardEnabled = false });
        await WaitForAsync(() => opened[0].Disposals == 1);
        tracker.Snapshot = GameWindowSnapshot.Unavailable with { IsForeground = true };
        service.Update(settings);
        await WaitForAsync(() => Opened(opened, 2));
        opened[1].Enqueue(KeyEvent(KeyCode.VcO, true, 400));
        await WaitForAsync(() => Volatile.Read(ref actions) == 3);
        await service.DisposeAsync();
        Assert.Equal(1, opened[1].Disposals);
    }

    /// <summary>Creates a native-like keyboard event for the nested input source.</summary>
    private static UioHookEvent KeyEvent(KeyCode key, bool pressed, ulong time) =>
        new()
        {
            Type = pressed ? EventType.KeyPressed : EventType.KeyReleased,
            Time = time,
            Keyboard = new KeyboardEventData { KeyCode = key },
        };

    private static bool Opened(List<FakeRecord> opened, int count)
    {
        lock (opened)
        {
            return opened.Count >= count;
        }
    }

    /// <summary>Waits for the 20 ms polling loop's deterministic observable result.</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(3), "The nested keyboard source never reported.");
            await Task.Delay(10);
        }
    }

    /// <summary>Tracks the lifetime and output of a simulated nested recorder.</summary>
    private sealed class FakeRecord : IX11KeyboardRecord
    {
        private readonly Queue<UioHookEvent[]> pending = new();

        public Action? BeforeRead { get; set; }
        public bool HasFailed { get; set; }
        public UioHookEvent[] Events { get; set; } = [];
        public int Disposals { get; private set; }

        public bool IsDrained
        {
            get
            {
                lock (pending)
                {
                    return pending.Count == 0;
                }
            }
        }

        /// <summary>Queues one batch for a later poll.</summary>
        public void Enqueue(params UioHookEvent[] events)
        {
            lock (pending)
            {
                pending.Enqueue(events);
            }
        }

        /// <summary>Returns the assigned events, or one queued batch per poll.</summary>
        public IReadOnlyList<UioHookEvent> ReadEvents()
        {
            BeforeRead?.Invoke();
            UioHookEvent[] result = Events;
            Events = [];
            lock (pending)
            {
                return result.Length > 0 || !pending.TryDequeue(out UioHookEvent[]? next) ? result : next;
            }
        }

        /// <summary>Records connection closure.</summary>
        public void Dispose() => Disposals++;
    }
}
