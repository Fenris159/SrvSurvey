using Avalonia;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class GlobalControllerInputServiceTests
{
    [Fact]
    public void DispatchesConfiguredChordOnFirstRelease()
    {
        var backend = new StubControllerInputBackend();
        using var service = new GlobalControllerInputService(
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
    public void IgnoresChordOutsideApplicationAndGameContext()
    {
        var backend = new StubControllerInputBackend();
        using var service = new GlobalControllerInputService(
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
    public void RoutesChordWhileEliteIsForeground()
    {
        var backend = new StubControllerInputBackend();
        using var service = new GlobalControllerInputService(
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
    public void DisconnectClearsPartiallyPressedChord()
    {
        var backend = new StubControllerInputBackend();
        using var service = new GlobalControllerInputService(
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
    public void ChangingSelectedDeviceRestartsBackend()
    {
        var backend = new StubControllerInputBackend();
        using var service = new GlobalControllerInputService(
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

        Assert.Equal(
            ["controller-1", "controller-2"],
            backend.StartedDeviceIds);
    }

    [Fact]
    public void DoesNotStartWithoutSupportedPlatformOrSelection()
    {
        var backend = new StubControllerInputBackend();
        using var unsupported = new GlobalControllerInputService(
            EnabledSettings(),
            OverlayHostKind.Other,
            new StubGameWindowTracker(),
            isApplicationActive: () => true,
            backend);
        using var unselected = new GlobalControllerInputService(
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

        public List<string> StartedDeviceIds { get; } = [];

        public Task RunAsync(
            string deviceId,
            Action<ControllerInputChange> inputChanged,
            Action<ControllerBackendStatus> statusChanged,
            CancellationToken cancellationToken)
        {
            StartedDeviceIds.Add(deviceId);
            onInputChanged = inputChanged;
            onStatusChanged = statusChanged;
            statusChanged(new ControllerBackendStatus(
                IsConnected: true,
                "Controller connected for testing."));
            return Task.Delay(Timeout.Infinite, cancellationToken);
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
}
