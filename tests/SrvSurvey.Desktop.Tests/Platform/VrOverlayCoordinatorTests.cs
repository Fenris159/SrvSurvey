using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Infrastructure;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class VrOverlayCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-coordinator-tests-{Guid.NewGuid():N}"
    );

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void VrCapturePreservesPanelColorAndAppliesHostOpacityOnce(bool combinedPresentation)
    {
        var panel = new Border { Background = new SolidColorBrush(Color.FromRgb(220, 50, 20)) };
        var window = new Window
        {
            Width = 40,
            Height = 20,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyBackgroundFallback = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = panel,
            Opacity = 0.5,
        };
        var registry = new OverlayWindowRegistry();
        registry.Register(window, "PlotJumpInfo");
        try
        {
            window.Show();
            if (combinedPresentation)
            {
                registry.SetPresentationVisual(window, panel);
            }

            VrOverlayViewModel viewModel = CreateViewModel();
            viewModel.Enabled = true;
            var runtime = new StubOpenVrRuntime();
            using var coordinator = new VrOverlayCoordinator(viewModel, registry, runtime, _ => true);

            VrOverlayFrame frame = Assert.IsType<VrOverlayFrame>(runtime.LastFrame);
            Assert.Equal(2, (double)frame.Width / frame.Height);
            int center = ((frame.Height / 2 * frame.Width) + frame.Width / 2) * 4;
            Assert.InRange(frame.RgbaBytes[center], 218, 222);
            Assert.InRange(frame.RgbaBytes[center + 1], 48, 52);
            Assert.InRange(frame.RgbaBytes[center + 2], 18, 22);
            Assert.InRange(frame.RgbaBytes[center + 3] / 255f * runtime.LastAlpha, 0.49f, 0.51f);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void ResetOrientationUsesActiveRuntimeAndReportsResult()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime, _ => true);

        bool reset = coordinator.ResetOrientation();

        Assert.True(reset);
        Assert.Equal(1, runtime.ResetCount);
        Assert.Contains("Captured", viewModel.StatusMessage);
    }

    [Fact]
    public void ResetOrientationFailsClosedWhileVrIsDisabled()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime, _ => true);

        bool reset = coordinator.ResetOrientation();

        Assert.False(reset);
        Assert.Equal(0, runtime.ResetCount);
        Assert.Contains("Enable VR", viewModel.StatusMessage);
    }

    [Fact]
    public void SteamVrProfileUsesOpenVrCapabilityProbeInsteadOfProcessNames()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var inspectedProcesses = new List<string>();
        var runtime = new StubOpenVrRuntime();

        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            processName =>
            {
                inspectedProcesses.Add(processName);
                return false;
            }
        );

        Assert.Empty(inspectedProcesses);
        Assert.Equal(1, runtime.ProbeCount);
        Assert.True(runtime.IsInitialized);
        Assert.Equal(VrOverlayConnectionState.Ready, viewModel.ConnectionState);
        Assert.Contains("Connected through", viewModel.ConnectionHeadline);
    }

    [Fact]
    public void MissingRuntimeReportsActionableWaitingState()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime
        {
            ProbeResult = VrRuntimeProbe.RuntimeUnavailable("SteamVR is not installed."),
        };

        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime, _ => false);

        Assert.False(runtime.IsInitialized);
        Assert.Equal(VrOverlayConnectionState.WaitingForRuntime, viewModel.ConnectionState);
        Assert.Contains("not installed", viewModel.ConnectionDetail);
    }

    [Fact]
    public void ConnectionRequestsSynchronizeOnceWhileDisablingStillShutsDown()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime, _ => true);

        Assert.Equal(1, runtime.ShutdownCount);

        viewModel.Enabled = true;
        Assert.Equal(1, runtime.ProbeCount);

        viewModel.SelectedPlatformProfile = viewModel.PlatformProfiles.Single(profile =>
            profile.Id == VrPlatformProfileCatalog.MetaAlvrProfileId
        );
        Assert.Equal(2, runtime.ProbeCount);

        viewModel.RuntimeProcessName = "custom-runtime";
        Assert.Equal(3, runtime.ProbeCount);

        viewModel.Scale = 12;
        Assert.Equal(4, runtime.ProbeCount);

        viewModel.Enabled = false;
        Assert.Equal(3, runtime.ShutdownCount);
    }

    [Fact]
    public void VrInteractionTogglesOpenVrInputAndReturnsToClickThroughWhenDisabled()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime, _ => true);

        Assert.True(coordinator.ToggleInteraction());
        Assert.True(coordinator.IsInteractionEnabled);
        Assert.True(runtime.InteractionEnabled);
        Assert.Contains("interaction is enabled", viewModel.StatusMessage);

        Assert.True(coordinator.ToggleInteraction());
        Assert.False(coordinator.IsInteractionEnabled);
        Assert.False(runtime.InteractionEnabled);
        Assert.Equal(2, runtime.InteractionChangeCount);
        Assert.Contains("click-through", viewModel.StatusMessage);

        Assert.True(coordinator.ToggleInteraction());
        viewModel.Enabled = false;

        Assert.False(coordinator.IsInteractionEnabled);
        Assert.False(coordinator.ToggleInteraction());
        Assert.Contains("Enable and connect", viewModel.StatusMessage);
    }

    [AvaloniaFact]
    public void NativeResetEventCancelsCapturedUiBeforeLookingUpAnyPanel()
    {
        var button = new Button { Content = "Activate" };
        var window = new Window
        {
            Width = 240,
            Height = 120,
            Content = button,
        };
        var registry = new OverlayWindowRegistry();
        registry.Register(window, "PlotJumpInfo");
        try
        {
            window.Show();
            registry.SetPresentationVisual(window, button);
            VrOverlayViewModel viewModel = CreateViewModel();
            viewModel.Enabled = true;
            var runtime = new StubOpenVrRuntime();
            using var coordinator = new VrOverlayCoordinator(viewModel, registry, runtime, _ => true);
            Assert.True(coordinator.ToggleInteraction());
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
            );
            coordinator.PollInteractionEvents();
            Assert.True(button.IsPressed);
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0)
            );
            coordinator.PollInteractionEvents();
            Assert.False(button.IsPressed);
        }
        finally
        {
            window.Close();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private VrOverlayViewModel CreateViewModel()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            "{\"PlotJumpInfo\":\"center:0, top:8 " + "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\"}"
        );
        return new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath)
        );
    }

    private sealed class StubOpenVrRuntime : IVrOverlayRuntime
    {
        public bool IsInitialized { get; private set; }

        public int ResetCount { get; private set; }

        public int ProbeCount { get; private set; }

        public int ShutdownCount { get; private set; }

        public int InteractionChangeCount { get; private set; }

        public bool InteractionEnabled { get; private set; }

        public VrOverlayFrame? LastFrame { get; private set; }

        public float LastAlpha { get; private set; }

        public Queue<VrOverlayPointerEvent> PointerEvents { get; } = new();

        public VrRuntimeProbe ProbeResult { get; init; } = VrRuntimeProbe.Ready();

        public VrRuntimeProbe Probe()
        {
            ProbeCount++;
            return ProbeResult;
        }

        public VrRuntimeResult Initialize()
        {
            IsInitialized = true;
            return VrRuntimeResult.Success("OpenVR is active.");
        }

        public VrRuntimeResult PublishOverlay(
            string plotterName,
            VrOverlayFrame frame,
            VrOverlayCalibration calibration,
            float alpha
        )
        {
            LastFrame = frame;
            LastAlpha = alpha;
            return VrRuntimeResult.Success("Published.");
        }

        public void RemoveOverlay(string plotterName) { }

        public VrRuntimeResult SetInteractionEnabled(bool enabled)
        {
            InteractionChangeCount++;
            InteractionEnabled = enabled;
            return VrRuntimeResult.Success(
                enabled ? "VR overlay controller interaction is enabled." : "VR overlays are click-through again."
            );
        }

        public IReadOnlyList<VrOverlayPointerEvent> PollPointerEvents()
        {
            var events = new List<VrOverlayPointerEvent>();
            while (PointerEvents.TryDequeue(out VrOverlayPointerEvent? pointerEvent))
            {
                events.Add(pointerEvent);
            }

            return events;
        }

        public VrRuntimeResult ResetOrientation()
        {
            ResetCount++;
            return VrRuntimeResult.Success("Captured the current headset yaw as the overlay origin.");
        }

        public void Shutdown()
        {
            ShutdownCount++;
            IsInitialized = false;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
