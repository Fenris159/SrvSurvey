using SharpHook.Data;
using SharpHook.Testing;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalKeyboardHookServiceTests
{
    [Fact]
    public async Task DispatchesConfiguredChordWhileApplicationIsActive()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () => testHook);
        GlobalInputActionTriggeredEventArgs? triggered = null;
        service.ActionTriggered += (_, eventArgs) => triggered = eventArgs;

        service.Start();
        testHook.SimulateKeyRelease(KeyCode.VcX);

        Assert.Equal("Global keyboard input is active.", service.Status);
        Assert.NotNull(triggered);
        Assert.Equal(
            GlobalInputAction.ToggleAllVisibility,
            triggered.Action);
        Assert.Equal("ALT X", triggered.Chord);
    }

    [Fact]
    public async Task IgnoresChordOutsideApplicationAndGameContext()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.Simple)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => false,
            hookFactory: () => testHook);
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        testHook.SimulateKeyRelease(KeyCode.VcX);

        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public async Task DoesNotCreateHookOnUnsupportedHost()
    {
        var factoryCalls = 0;
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxWayland,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            hookFactory: () =>
            {
                factoryCalls++;
                return new TestGlobalHook();
            });

        service.Start();

        Assert.Equal(0, factoryCalls);
        Assert.Equal(
            "Global keyboard input is unavailable on this platform.",
            service.Status);
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
            hookFactory: () => testHook);

        service.Start();

        Assert.True(service.IsRunning);
        Assert.Equal("Global keyboard input is active.", service.Status);
    }

    [Fact]
    public async Task DisposalWaitsForInFlightEventBeforeDisposingTracker()
    {
        using var testHook = new TestGlobalHook(TestThreadingMode.EventLoop)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        var tracker = new BlockingGameWindowTracker();
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            tracker,
            isApplicationActive: () => false,
            hookFactory: () => testHook);
        service.Start();

        testHook.SimulateKeyRelease(KeyCode.VcX);
        await tracker.SnapshotEntered.WaitAsync(TimeSpan.FromSeconds(2));
        var disposal = service.DisposeAsync().AsTask();

        Assert.False(disposal.IsCompleted);
        Assert.False(tracker.IsDisposed);

        tracker.AllowSnapshot();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(tracker.IsDisposed);
    }

    [Fact]
    public async Task RestartWaitsForPreviousEventLoopToStop()
    {
        using var firstHook = new TestGlobalHook(TestThreadingMode.EventLoop)
        {
            EventMask = _ => EventMask.LeftAlt,
        };
        using var secondHook = new TestGlobalHook(TestThreadingMode.Simple);
        var tracker = new BlockingGameWindowTracker();
        var secondHookCreated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        await using var service = new GlobalKeyboardHookService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            tracker,
            isApplicationActive: () => false,
            hookFactory: () =>
            {
                if (Interlocked.Increment(ref factoryCalls) == 1)
                {
                    return firstHook;
                }

                secondHookCreated.TrySetResult();
                return secondHook;
            });

        service.Start();
        firstHook.SimulateKeyRelease(KeyCode.VcX);
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
        return GlobalInputSettings.Default with
        {
            KeyboardEnabled = true,
            Bindings = bindings,
        };
    }

    private sealed class StubGameWindowTracker : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return GameWindowSnapshot.Unavailable;
        }

        public void Dispose()
        {
        }
    }

    private sealed class BlockingGameWindowTracker : IGameWindowTracker
    {
        private readonly ManualResetEventSlim allowSnapshot = new();
        private readonly TaskCompletionSource snapshotEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int disposed;

        public Task SnapshotEntered => snapshotEntered.Task;

        public bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public GameWindowSnapshot GetSnapshot()
        {
            snapshotEntered.TrySetResult();
            if (!allowSnapshot.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "The test did not release the blocked tracker snapshot.");
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
