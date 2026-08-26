using Avalonia;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GlobalControllerInputServiceTests
{
    [Fact]
    public async Task DispatchesConfiguredChordOnFirstRelease()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);
        GlobalInputActionTriggeredEventArgs? triggered = null;
        service.ActionTriggered += (_, eventArgs) => triggered = eventArgs;

        service.Start();
        backend.Emit("B1", isPressed: true);
        backend.Emit("B2", isPressed: true);
        backend.Emit("B1", isPressed: false);
        backend.Emit("B2", isPressed: false);

        Assert.NotNull(triggered);
        Assert.Equal(
            GlobalInputAction.ToggleAllVisibility,
            triggered.Action);
        Assert.Equal("B1 B2", triggered.Chord);
    }

    [Fact]
    public async Task IgnoresChordOutsideApplicationAndGameContext()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => false,
            backend);
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        backend.Emit("B1", isPressed: true);
        backend.Emit("B2", isPressed: true);
        backend.Emit("B1", isPressed: false);

        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public async Task RoutesChordWhileEliteIsForeground()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(isForeground: true),
            isApplicationActive: () => false,
            backend);
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        backend.Emit("B1", isPressed: true);
        backend.Emit("B2", isPressed: true);
        backend.Emit("B1", isPressed: false);

        Assert.Equal(1, triggerCount);
    }

    [Fact]
    public async Task SendsInputToActiveShortcutCaptureInsteadOfActionRouter()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(isForeground: true),
            isApplicationActive: () => false,
            backend);
        var owner = new object();
        List<ControllerInputChange> captured = [];
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;
        ShortcutCaptureSession.Begin(owner, captured.Add);
        try
        {
            service.Start();
            backend.Emit("B1", isPressed: true);
            backend.Emit("B2", isPressed: true);
            backend.Emit("B1", isPressed: false);

            Assert.Equal(3, captured.Count);
            Assert.Equal(0, triggerCount);
        }
        finally
        {
            ShortcutCaptureSession.End(owner);
        }
    }

    [Fact]
    public async Task CaptureClearsAChordStartedBeforeCaptureBegan()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(isForeground: true),
            isApplicationActive: () => false,
            backend);
        var owner = new object();
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;
        service.Start();
        backend.Emit("B1", isPressed: true);

        ShortcutCaptureSession.Begin(owner, _ => { });
        try
        {
            backend.Emit("B1", isPressed: false);
        }
        finally
        {
            ShortcutCaptureSession.End(owner);
        }

        backend.Emit("B2", isPressed: true);
        backend.Emit("B2", isPressed: false);

        Assert.Equal(0, triggerCount);
    }

    [Fact]
    public async Task DisconnectClearsPartiallyPressedChord()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);
        var triggerCount = 0;
        service.ActionTriggered += (_, _) => triggerCount++;

        service.Start();
        backend.Emit("B1", isPressed: true);
        backend.ReportDisconnected();
        backend.Emit("B1", isPressed: false);

        Assert.Equal(0, triggerCount);
        Assert.Equal(
            "Controller disconnected for testing.",
            service.Status);
    }

    [Fact]
    public async Task ChangingSelectedDeviceRestartsBackend()
    {
        var backend = new StubControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);

        service.Start();
        service.Update(EnabledSettings() with
        {
            ControllerDeviceId = "controller-2",
        });
        await backend.WaitForStartCountAsync(2);

        Assert.Equal(
            ["controller-1", "controller-2"],
            backend.StartedDeviceIds);
    }

    [Fact]
    public async Task DoesNotStartWithoutSupportedPlatformOrSelection()
    {
        var backend = new StubControllerInputBackend();
        await using var unsupported = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Other,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);
        await using var unselected = new GlobalControllerInputService(
            EnabledSettings() with { ControllerDeviceId = null },
            OverlayHostKind.Windows,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);

        unsupported.Start();
        unselected.Start();

        Assert.Empty(backend.StartedDeviceIds);
        Assert.Equal(
            "Controller input is unavailable on this platform.",
            unsupported.Status);
        Assert.Equal(
            "Select a controller before enabling controller input.",
            unselected.Status);
    }

    [Fact]
    public async Task RestartWaitsForThePreviousBackendToStop()
    {
        var backend = new BlockingStopControllerInputBackend();
        await using var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);

        service.Start();
        service.Update(EnabledSettings() with
        {
            ControllerDeviceId = "controller-2",
        });

        await backend.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["controller-1"], backend.StartedDeviceIds);

        backend.AllowStop();
        await backend.SecondRunStarted.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(
            ["controller-1", "controller-2"],
            backend.StartedDeviceIds);
    }

    [Fact]
    public async Task DisposalWaitsForTheBackendToStop()
    {
        var backend = new BlockingStopControllerInputBackend();
        var tracker = new CountingGameWindowTracker();
        var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            tracker,
            isApplicationActive: () => true,
            backend);

        service.Start();
        var firstDisposal = service.DisposeAsync().AsTask();
        var secondDisposal = service.DisposeAsync().AsTask();

        await backend.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Same(firstDisposal, secondDisposal);
        Assert.False(firstDisposal.IsCompleted);
        Assert.Equal(0, tracker.DisposeCount);

        backend.AllowStop();
        await Task.WhenAll(firstDisposal, secondDisposal)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, tracker.DisposeCount);
    }

    [Fact]
    public async Task DisposalPreventsAQueuedRestart()
    {
        var backend = new BlockingStopControllerInputBackend();
        var service = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.LinuxX11,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);

        service.Start();
        service.Update(EnabledSettings() with
        {
            ControllerDeviceId = "controller-2",
        });
        await backend.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(2));

        var disposal = service.DisposeAsync().AsTask();
        backend.AllowStop();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["controller-1"], backend.StartedDeviceIds);
    }

    private static GlobalInputSettings EnabledSettings()
    {
        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        bindings[GlobalInputAction.ToggleAllVisibility] = "B1 B2";
        return GlobalInputSettings.Default with
        {
            ControllerEnabled = true,
            ControllerDeviceId = "controller-1",
            Bindings = bindings,
        };
    }

    private sealed class StubControllerInputBackend
        : IControllerInputBackend
    {
        private Action<ControllerInputChange>? onInputChanged;
        private Action<ControllerBackendStatus>? onStatusChanged;
        private TaskCompletionSource startChanged = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> StartedDeviceIds { get; } = [];

        public Task RunAsync(
            string deviceId,
            Action<ControllerInputChange> inputChanged,
            Action<ControllerBackendStatus> statusChanged,
            CancellationToken cancellationToken)
        {
            lock (StartedDeviceIds)
            {
                StartedDeviceIds.Add(deviceId);
                startChanged.TrySetResult();
                startChanged = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            onInputChanged = inputChanged;
            onStatusChanged = statusChanged;
            statusChanged(new ControllerBackendStatus(
                IsConnected: true,
                "Controller connected for testing."));
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public async Task WaitForStartCountAsync(int count)
        {
            while (true)
            {
                Task changed;
                lock (StartedDeviceIds)
                {
                    if (StartedDeviceIds.Count >= count)
                    {
                        return;
                    }

                    changed = startChanged.Task;
                }

                await changed.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        public void Emit(string token, bool isPressed)
        {
            onInputChanged?.Invoke(new ControllerInputChange(
                token,
                isPressed));
        }

        public void ReportDisconnected()
        {
            onStatusChanged?.Invoke(new ControllerBackendStatus(
                IsConnected: false,
                "Controller disconnected for testing."));
        }
    }

    private sealed class BlockingStopControllerInputBackend
        : IControllerInputBackend
    {
        private readonly object startedDeviceIdsLock = new();
        private readonly List<string> startedDeviceIds = [];
        private readonly TaskCompletionSource cancellationObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource allowStop = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource secondRunStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int runCount;

        public IReadOnlyList<string> StartedDeviceIds
        {
            get
            {
                lock (startedDeviceIdsLock)
                {
                    return [.. startedDeviceIds];
                }
            }
        }

        public Task CancellationObserved => cancellationObserved.Task;

        public Task SecondRunStarted => secondRunStarted.Task;

        public async Task RunAsync(
            string deviceId,
            Action<ControllerInputChange> onInputChanged,
            Action<ControllerBackendStatus> onStatusChanged,
            CancellationToken cancellationToken)
        {
            lock (startedDeviceIdsLock)
            {
                startedDeviceIds.Add(deviceId);
            }
            var currentRun = Interlocked.Increment(ref runCount);
            if (currentRun > 1)
            {
                secondRunStarted.TrySetResult();
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                if (currentRun == 1)
                {
                    cancellationObserved.TrySetResult();
                    await allowStop.Task;
                }
            }
        }

        public void AllowStop()
        {
            allowStop.TrySetResult();
        }
    }

    private sealed class StubGameWindowTracker(bool isForeground = false)
        : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return isForeground
                ? new GameWindowSnapshot(
                    NativeHandle: (nint)1,
                    ProcessId: 1,
                    ClientBounds: new PixelRect(0, 0, 1, 1),
                    IsVisible: true,
                    IsForeground: true)
                : GameWindowSnapshot.Unavailable;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CountingGameWindowTracker : IGameWindowTracker
    {
        private int disposeCount;

        public int DisposeCount => Volatile.Read(ref disposeCount);

        public GameWindowSnapshot GetSnapshot()
        {
            return GameWindowSnapshot.Unavailable;
        }

        public void Dispose()
        {
            Interlocked.Increment(ref disposeCount);
        }
    }
}
