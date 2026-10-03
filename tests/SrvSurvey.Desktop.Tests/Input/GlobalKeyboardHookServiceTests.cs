using System.Collections.Concurrent;
using System.Diagnostics;
using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalKeyboardHookServiceTests
{
    public static TheoryData<string, EventMask, KeyCode> ShortcutVariants =>
        new()
        {
            { "O", EventMask.None, KeyCode.VcO },
            { "SHIFT O", EventMask.LeftShift, KeyCode.VcO },
            { "ALT O", EventMask.LeftAlt, KeyCode.VcO },
            { "CTRL O", EventMask.LeftCtrl, KeyCode.VcO },
            { "ALT SHIFT O", EventMask.LeftAlt | EventMask.LeftShift, KeyCode.VcO },
            { "ALT CTRL O", EventMask.LeftAlt | EventMask.LeftCtrl, KeyCode.VcO },
            { "CTRL SHIFT O", EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.VcO },
            { "ALT CTRL SHIFT O", EventMask.LeftAlt | EventMask.LeftCtrl | EventMask.LeftShift, KeyCode.VcO },
            { "X", EventMask.None, KeyCode.VcX },
            { "F2", EventMask.None, KeyCode.VcF2 },
            { "D1", EventMask.None, KeyCode.Vc1 },
        };

    public static TheoryData<string, KeyCode[]> ObservedModifierVariants =>
        new()
        {
            { "SHIFT O", [KeyCode.VcRightShift] },
            { "ALT O", [KeyCode.VcRightAlt] },
            { "CTRL O", [KeyCode.VcRightControl] },
            { "ALT SHIFT O", [KeyCode.VcRightAlt, KeyCode.VcRightShift] },
            { "ALT CTRL O", [KeyCode.VcRightAlt, KeyCode.VcRightControl] },
            { "CTRL SHIFT O", [KeyCode.VcRightControl, KeyCode.VcRightShift] },
            { "ALT CTRL SHIFT O", [KeyCode.VcRightAlt, KeyCode.VcRightControl, KeyCode.VcRightShift] },
        };

    /// <summary>Checks nested keys use independent modifier state and honor disable/re-enable and context gating.</summary>
    [Theory]
    [InlineData(OverlayHostKind.LinuxXWayland)]
    [InlineData(OverlayHostKind.LinuxWayland)]
    public async Task NestedKeysRemainIndependentFromDesktopModifiers(OverlayHostKind host)
    {
        using var desktop = new TestGlobalHook(TestThreadingMode.Simple);
        var game = new QueueGameInput();
        bool foreground = true;
        GlobalInputSettings settings = GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = new Dictionary<GlobalInputAction, string> { [GlobalInputAction.ToggleOverlayInteraction] = "O" },
        };
        await using var service = new GlobalKeyboardHookService(
            settings,
            host,
            new CallbackGameTracker(() => foreground),
            () => false,
            () => desktop,
            additionalInput: new(GameKeyboardInput: game)
        );
        int actions = 0;
        service.ActionTriggered += (_, _) => Interlocked.Increment(ref actions);
        service.Start();
        desktop.SimulateKeyPress(KeyCode.VcLeftAlt);
        game.Queue.Enqueue(new(true, [KeyEvent(KeyCode.VcO, true, 100)]));
        await WaitForCountAsync(() => Volatile.Read(ref actions), 1);
        game.Queue.Enqueue(new(false, [KeyEvent(KeyCode.VcO, true, 110), KeyEvent(KeyCode.VcO, false, 120)]));
        await Task.Delay(80);
        Assert.Equal(1, Volatile.Read(ref actions));
        game.Queue.Enqueue(new(false, [KeyEvent(KeyCode.VcO, true, 200), KeyEvent(KeyCode.VcO, false, 210)]));
        await WaitForCountAsync(() => Volatile.Read(ref actions), 2);
        foreground = false;
        game.Queue.Enqueue(new(false, [KeyEvent(KeyCode.VcO, true, 300)]));
        await Task.Delay(80);
        Assert.Equal(2, Volatile.Read(ref actions));
        service.Update(settings with { KeyboardEnabled = false });
        await WaitForCountAsync(() => Volatile.Read(ref game.DisabledReads), 1);
        foreground = true;
        service.Update(settings);
        game.Queue.Enqueue(new(true, [KeyEvent(KeyCode.VcO, true, 400)]));
        await WaitForCountAsync(() => Volatile.Read(ref actions), 3);
        await service.DisposeAsync();
        Assert.True(game.Disposed);
    }

    /// <summary>Creates a native-like keyboard event for the secondary input source.</summary>
    private static UioHookEvent KeyEvent(KeyCode key, bool pressed, ulong time) =>
        new()
        {
            Type = pressed ? EventType.KeyPressed : EventType.KeyReleased,
            Time = time,
            Keyboard = new KeyboardEventData { KeyCode = key },
        };

    /// <summary>Waits for an asynchronous source's deterministic observable result.</summary>
    private static async Task WaitForCountAsync(Func<int> count, int expected)
    {
        for (int attempt = 0; attempt < 100 && count() < expected; attempt++)
        {
            await Task.Delay(20);
        }
        Assert.True(count() >= expected);
    }

    /// <summary>Feeds a background input source without invoking native APIs.</summary>
    private sealed class QueueGameInput : IGameKeyboardInput
    {
        public readonly ConcurrentQueue<GameKeyboardEventBatch> Queue = new();
        public int DisabledReads;
        public bool Disposed { get; private set; }

        /// <summary>Returns one queued batch when enabled and clears it when disabled.</summary>
        public GameKeyboardEventBatch ReadEvents(bool enabled)
        {
            if (!enabled)
            {
                Interlocked.Increment(ref DisabledReads);
                Queue.Clear();
                return new(true, []);
            }
            return Queue.TryDequeue(out GameKeyboardEventBatch? batch) ? batch : new(false, []);
        }

        /// <summary>Records listener disposal.</summary>
        public void Dispose() => Disposed = true;
    }

    /// <summary>Supplies a changeable game foreground state.</summary>
    private sealed class CallbackGameTracker(Func<bool> foreground) : IGameWindowTracker
    {
        /// <summary>Returns only the foreground flag needed by shortcut gating.</summary>
        public GameWindowSnapshot GetSnapshot() => GameWindowSnapshot.Unavailable with { IsForeground = foreground() };

        /// <summary>Releases the stateless tracker.</summary>
        public void Dispose() { }
    }

    [Fact]
    public async Task DispatchesConfiguredChordWhileApplicationIsActive()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple) { EventMask = _ => EventMask.LeftAlt };
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook
        );
        GlobalInputActionTriggeredEventArgs? triggered = null;
        service.ActionTriggered += (_, eventArgs) => triggered = eventArgs;

        service.Start();
        testHook.SimulateKeyPress(KeyCode.VcX);

        Assert.Equal("Global keyboard input is active.", service.Status);
        Assert.NotNull(triggered);
        Assert.Equal(GlobalInputAction.ToggleAllVisibility, triggered.Action);
        Assert.Equal("ALT X", triggered.Chord);
    }

    [Fact]
    public async Task DispatchesChordOnKeyPressEvenWhenModifiersAreReleasedBeforeLetter()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt | EventMask.LeftShift,
        };
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook
        );
        var triggered = new List<GlobalInputAction>();
        service.ActionTriggered += (_, eventArgs) => triggered.Add(eventArgs.Action);

        service.Start();
        testHook.SimulateKeyPress(KeyCode.VcO);
        testHook.SimulateKeyPress(KeyCode.VcO);
        testHook.EventMask = _ => EventMask.None;
        testHook.SimulateKeyRelease(KeyCode.VcO);

        Assert.Equal([GlobalInputAction.ToggleOverlayInteraction], triggered);

        testHook.EventMask = _ => EventMask.LeftAlt | EventMask.LeftShift;
        testHook.SimulateKeyPress(KeyCode.VcO);

        Assert.Equal(
            [GlobalInputAction.ToggleOverlayInteraction, GlobalInputAction.ToggleOverlayInteraction],
            triggered
        );
    }

    [Fact]
    public async Task DispatchesFirstChordAfterIdleWhenPriorKeyReleaseWasMissed()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt | EventMask.LeftShift,
        };
        long timestamp = 0;
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook,
            timestampProvider: () => timestamp
        );
        int triggered = 0;
        service.ActionTriggered += (_, eventArgs) =>
        {
            if (eventArgs.Action == GlobalInputAction.ToggleOverlayInteraction)
            {
                triggered++;
            }
        };

        service.Start();
        testHook.SimulateKeyPress(KeyCode.VcO);
        testHook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal(1, triggered);

        timestamp += Stopwatch.Frequency * 3;
        testHook.SimulateKeyPress(KeyCode.VcO);

        Assert.Equal(2, triggered);
    }

    [Theory]
    [MemberData(nameof(ShortcutVariants))]
    public async Task X11AutoRepeatDoesNotRetriggerConfiguredChord(string chord, EventMask mask, KeyCode keyCode)
    {
        long eventTime = 1_000_000;
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventDateTime = _ => DateTimeOffset.UnixEpoch.AddMilliseconds(eventTime),
            EventMask = _ => mask,
        };
        long timestamp = 0;
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
                Bindings = GlobalInputSettings.Default.Bindings.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Key == GlobalInputAction.ToggleOverlayInteraction ? chord : string.Empty
                ),
            },
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook,
            timestampProvider: () => timestamp
        );
        int triggered = 0;
        service.ActionTriggered += (_, eventArgs) =>
        {
            if (eventArgs.Action == GlobalInputAction.ToggleOverlayInteraction)
            {
                triggered++;
            }
        };

        service.Start();
        testHook.SimulateKeyPress(keyCode);
        timestamp += Stopwatch.Frequency / 2;
        eventTime += 500;
        testHook.SimulateKeyRelease(keyCode);
        timestamp += Stopwatch.Frequency / 1000;
        eventTime += 1;
        testHook.SimulateKeyPress(keyCode);
        Assert.Equal(1, triggered);

        eventTime += 30;
        testHook.SimulateKeyRelease(keyCode);
        timestamp += Stopwatch.Frequency / 10;
        eventTime += 100;
        testHook.SimulateKeyPress(keyCode);
        Assert.Equal(2, triggered);
    }

    [Fact]
    public async Task X11AutoRepeatDoesNotRetriggerAnyConfigurableKeyboardAction()
    {
        long eventTime = 1_000_000;
        long timestamp = 0;
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventDateTime = _ => DateTimeOffset.UnixEpoch.AddMilliseconds(eventTime),
        };
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook,
            timestampProvider: () => timestamp
        );
        var triggered = new List<GlobalInputAction>();
        service.ActionTriggered += (_, eventArgs) => triggered.Add(eventArgs.Action);
        service.Start();

        foreach (GlobalInputActionDefinition definition in GlobalInputActionCatalog.All)
        {
            var bindings = GlobalInputActionCatalog.All.ToDictionary(item => item.Action, _ => string.Empty);
            bindings[definition.Action] = "O";
            service.Update(GlobalInputSettings.Default with { KeyboardEnabled = true, Bindings = bindings });

            int before = triggered.Count;
            testHook.SimulateKeyPress(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 2;
            eventTime += 500;
            testHook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 1000;
            eventTime += 1;
            testHook.SimulateKeyPress(KeyCode.VcO);
            Assert.True(triggered.Count == before + 1, $"Repeat retriggered {definition.Action}.");
            Assert.Equal(definition.Action, triggered[^1]);

            eventTime += 30;
            testHook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 10;
            eventTime += 100;
            testHook.SimulateKeyPress(KeyCode.VcO);
            Assert.True(triggered.Count == before + 2, $"Second tap missed {definition.Action}.");
            Assert.Equal(definition.Action, triggered[^1]);
            eventTime += 30;
            testHook.SimulateKeyRelease(KeyCode.VcO);
            timestamp += Stopwatch.Frequency / 10;
            eventTime += 100;
        }
    }

    [Theory]
    [MemberData(nameof(ObservedModifierVariants))]
    public async Task X11ChordUsesObservedModifiersWhenLetterEventOmitsItsModifierMask(
        string chord,
        KeyCode[] modifiers
    )
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple);
        long timestamp = 0;
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
                Bindings = GlobalInputSettings.Default.Bindings.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Key == GlobalInputAction.ToggleOverlayInteraction ? chord : string.Empty
                ),
            },
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook,
            timestampProvider: () => timestamp
        );
        var triggered = new List<GlobalInputAction>();
        service.ActionTriggered += (_, eventArgs) => triggered.Add(eventArgs.Action);
        service.Start();

        foreach (KeyCode modifier in modifiers)
        {
            testHook.SimulateKeyPress(modifier);
            timestamp += Stopwatch.Frequency;
        }

        testHook.EventMask = _ => EventMask.NumLock;
        testHook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal([GlobalInputAction.ToggleOverlayInteraction], triggered);

        testHook.SimulateKeyRelease(KeyCode.VcO);
        foreach (KeyCode modifier in modifiers)
        {
            testHook.SimulateKeyRelease(modifier);
        }

        timestamp += Stopwatch.Frequency / 10;
        testHook.SimulateKeyPress(KeyCode.VcO);
        Assert.Equal([GlobalInputAction.ToggleOverlayInteraction], triggered);
    }

    [Fact]
    public async Task X11MissedModifierReleaseExpiresBeforeLaterPlainKey()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple);
        long timestamp = 0;
        await using var service = new GlobalKeyboardHookService(
            GlobalInputSettings.Default with
            {
                KeyboardEnabled = true,
            },
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook,
            timestampProvider: () => timestamp
        );
        var triggered = new List<GlobalInputAction>();
        service.ActionTriggered += (_, eventArgs) => triggered.Add(eventArgs.Action);
        service.Start();

        testHook.SimulateKeyPress(KeyCode.VcRightAlt);
        testHook.SimulateKeyPress(KeyCode.VcRightShift);
        timestamp += Stopwatch.Frequency * 11;
        testHook.SimulateKeyPress(KeyCode.VcO);

        Assert.Empty(triggered);
    }

    [Fact]
    public async Task IgnoresChordOutsideApplicationAndGameContext()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple) { EventMask = _ => EventMask.LeftAlt };
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => false,
            hookFactory: () => testHook
        );
        int triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        testHook.SimulateKeyPress(KeyCode.VcX);

        Assert.Equal(0, triggerCount);
    }

    /// <summary>Checks native Wayland uses optional portal support rather than starting an X11 desktop hook.</summary>
    [Fact]
    public async Task DoesNotCreateHookOnUnsupportedHost()
    {
        int factoryCalls = 0;
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () =>
            {
                factoryCalls++;
                return new TestGlobalHook();
            }
        );

        service.Start();

        Assert.Equal(0, factoryCalls);
        Assert.Equal("Wayland keyboard input is waiting for Global Shortcuts portal support.", service.Status);
    }

    [Fact]
    public async Task StartsHookThroughXWaylandCompatibility()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple);
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxXWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook
        );

        service.Start();

        Assert.True(service.IsRunning);
        Assert.Equal("Global keyboard input is active.", service.Status);
    }

    [Fact]
    public async Task ReportsHookStartupFailure()
    {
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => throw new InvalidOperationException("test failure")
        );

        service.Start();

        Assert.False(service.IsRunning);
        Assert.Equal("Global keyboard input could not start: test failure", service.Status);
    }

    /// <summary>Blocks the key callback specifically, allowing background focus diagnostics to run before the event.</summary>
    [Fact]
    public async Task DisposalWaitsForInFlightEventBeforeDisposingTracker()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.EventLoop) { EventMask = _ => EventMask.LeftAlt };
        var tracker = new BlockingGameWindowTracker();
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            tracker,
            isApplicationActive: () =>
            {
                tracker.EnableBlocking();
                return false;
            },
            hookFactory: () => testHook
        );
        service.Start();

        testHook.SimulateKeyPress(KeyCode.VcX);
        await tracker.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        Task disposal = service.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        Assert.False(tracker.IsDisposed);

        tracker.AllowSnapshot();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(tracker.IsDisposed);
    }

    /// <summary>Prevents a replacement hook from starting until the original key callback and event loop finish.</summary>
    [Fact]
    public async Task RestartWaitsForPreviousEventLoopToStop()
    {
        using var firstHook = new TestGlobalHook(TestThreadingMode.EventLoop) { EventMask = _ => EventMask.LeftAlt };
        using var secondHook = new TestGlobalHook(TestThreadingMode.Simple);
        var tracker = new BlockingGameWindowTracker();
        var secondHookCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int factoryCalls = 0;
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            tracker,
            isApplicationActive: () =>
            {
                tracker.EnableBlocking();
                return false;
            },
            hookFactory: () =>
            {
                if (Interlocked.Increment(ref factoryCalls) == 1)
                {
                    return firstHook;
                }

                secondHookCreated.TrySetResult();
                return secondHook;
            }
        );

        service.Start();
        firstHook.SimulateKeyPress(KeyCode.VcX);
        await tracker.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        service.Update(EnabledSettings() with { KeyboardEnabled = false });
        service.Update(EnabledSettings());

        Assert.Equal(1, Volatile.Read(ref factoryCalls));

        tracker.AllowSnapshot();
        await secondHookCreated.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref factoryCalls));
    }

    private static GlobalInputSettings EnabledSettings()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "ALT X";
        return GlobalInputSettings.Default with { KeyboardEnabled = true, Bindings = bindings };
    }

    private sealed class StubGameWindowTracker : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return GameWindowSnapshot.Unavailable;
        }

        public void Dispose() { }
    }

    private sealed class BlockingGameWindowTracker : IGameWindowTracker
    {
        private readonly ManualResetEventSlim allowSnapshot = new();
        private bool blockSnapshots;
        private readonly TaskCompletionSource snapshotEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;

        public Task SnapshotEntered => snapshotEntered.Task;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        /// <summary>Arms blocking from the key callback while its service lock excludes background diagnostics.</summary>
        public void EnableBlocking() => Volatile.Write(ref blockSnapshots, true);

        /// <summary>Blocks an armed event snapshot, leaving earlier background focus refreshes nonblocking.</summary>
        public GameWindowSnapshot GetSnapshot()
        {
            if (!Volatile.Read(ref blockSnapshots))
            {
                return GameWindowSnapshot.Unavailable;
            }
            snapshotEntered.TrySetResult();
            if (!allowSnapshot.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The test did not release the blocked tracker snapshot.");
            }

            return GameWindowSnapshot.Unavailable;
        }

        public void AllowSnapshot()
        {
            allowSnapshot.Set();
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref disposed, 1);
            allowSnapshot.Dispose();
        }
    }
}
