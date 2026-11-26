using System.Text.Json;
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

    [AvaloniaFact]
    public void CalibrationPublishesSampleAndHeadsetControlsWithoutMakingADesktopPanelVisible()
    {
        VrOverlayViewModel model = CreateViewModel();
        model.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(model, registry, runtime);
        Assert.True(model.BeginAdjustment());
        Assert.Collection(
            runtime.Frames.Keys.Order(),
            name => Assert.Equal("PlotAdjustVR", name),
            name => Assert.Equal("PlotJumpInfo", name)
        );
        Assert.Empty(registry.Snapshot());
        VrOverlayFrame sample = runtime.Frames["PlotJumpInfo"];
        Assert.Contains(sample.RgbaBytes.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        Assert.Equal(1f, runtime.LastAlpha);
        model.PlaceInFrontCommand.Execute(null);
        Assert.Equal(10, model.PositionZ);
        Assert.Equal(0, model.PositionY);
        Assert.Equal(8, model.Scale);
        Assert.Equal(0, runtime.ResetCount);
        model.CancelCommand.Execute(null);
        Assert.Empty(runtime.Frames);
        Assert.Equal(20, model.GetCalibration("PlotJumpInfo")!.Scale);
    }

    [AvaloniaTheory]
    [InlineData(15, 16, 16, false)]
    [InlineData(16, 16, 16, true)]
    [InlineData(16, int.MaxValue, 18, false)]
    public void CalibrationReservesRuntimeCapacityForItsControlsAndSample(
        int liveCount,
        int capacity,
        int expected,
        bool selectedIsLive
    )
    {
        string[] liveNames = OverlayLayoutCatalog
            .Supported.Select(definition => definition.Name)
            .Where(name => name != "PlotBioStatus" && (selectedIsLive || name != "PlotJumpInfo"))
            .OrderByDescending(name => name == "PlotJumpInfo")
            .Take(liveCount)
            .ToArray();
        var placements = liveNames
            .Append("PlotBioStatus")
            .Append("PlotJumpInfo")
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, _ => "left:0, top:0 { s: 5, p: <0, 0, 10>, r: <0, 0, 0>}");
        string data = Path.Combine(temporaryDirectory, "data");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "plotters.json"), JsonSerializer.Serialize(placements));
        VrOverlayViewModel model = CreateViewModel();
        model.SetAvailableOverlays(["PlotJumpInfo"]);
        model.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var windows = new List<Window>();
        try
        {
            foreach (string name in liveNames.Prepend("PlotBioStatus"))
            {
                var window = new Window
                {
                    Width = 20,
                    Height = 20,
                    WindowDecorations = WindowDecorations.None,
                    Content = new Border { Background = Brushes.Orange },
                };
                windows.Add(window);
                registry.Register(window, name);
                if (name != "PlotBioStatus")
                {
                    window.Show();
                }
            }
            var runtime = new StubOpenVrRuntime { MaximumOverlays = capacity };
            using var coordinator = new VrOverlayCoordinator(model, registry, runtime);
            Assert.Equal(liveCount, runtime.Frames.Count);
            Assert.True(model.BeginAdjustment());
            Assert.Equal(expected, runtime.Frames.Count);
            Assert.Contains("PlotAdjustVR", runtime.Frames.Keys);
            Assert.Contains("PlotJumpInfo", runtime.Frames.Keys);
            Assert.DoesNotContain("PlotBioStatus", runtime.Frames.Keys);
            model.CancelCommand.Execute(null);
            Assert.Equal(liveCount, runtime.Frames.Count);
            Assert.DoesNotContain("PlotAdjustVR", runtime.Frames.Keys);
        }
        finally
        {
            foreach (Window window in windows)
            {
                window.Close();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaceInFrontFailsWithoutChangingCalibrationWhenHeadTrackingIsUnavailable(bool enabled)
    {
        VrOverlayViewModel model = CreateViewModel();
        model.Enabled = enabled;
        model.BeginAdjustment();
        using var coordinator = new VrOverlayCoordinator(
            model,
            new OverlayWindowRegistry(),
            new StubOpenVrRuntime { PoseAvailable = false }
        );
        model.PlaceInFrontCommand.Execute(null);
        Assert.Equal(3, model.PositionZ);
        Assert.Contains("tracked head pose", model.StatusMessage);
    }

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
    public void ChangingEdvrInputSourceDisablesExistingInteractionUntilTheUserTogglesAgain()
    {
        VrOverlayViewModel model = CreateViewModel();
        model.Enabled = true;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(model, new OverlayWindowRegistry(), runtime);
        Assert.True(coordinator.ToggleInteraction());
        model.EdvrControllerInput = true;
        Assert.False(coordinator.IsInteractionEnabled);
        Assert.False(runtime.InteractionEnabled);
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

    [AvaloniaFact]
    public void CalibrationControlsHoldPoseWhilePointerIsCapturedAndRecenterAfterRelease()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime();
        using var router = new VrOverlayInputRouter();
        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            _ => true,
            inputRouter: router
        );

        Assert.True(viewModel.BeginAdjustment());
        Assert.True(coordinator.ToggleInteraction());
        Assert.False(router.HasCapture);

        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
        );
        coordinator.PollInteractionEvents();
        Assert.True(router.HasCapture);
        int headPoseCallsWhileCaptured = runtime.HeadPoseCalls;
        viewModel.Scale = 12;
        Assert.Equal(headPoseCallsWhileCaptured, runtime.HeadPoseCalls);

        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonUp, 20, 20)
        );
        coordinator.PollInteractionEvents();
        Assert.False(router.HasCapture);
        viewModel.Scale = 13;
        Assert.True(runtime.HeadPoseCalls > headPoseCallsWhileCaptured);
    }

    [AvaloniaFact]
    public void CategoryPreviewPublishesAllCategoryPanelsAndDragModeMovesAndAdjustsDepth()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data-drag");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-drag");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            """
            {
              "PlotBioStatus": "center:0, top:8 { s: 4, p: <0, 4, 10>, r: <0, 0, 0>}",
              "PlotBioSystem": "left:8, bottom:144 { s: 4, p: <-6, 0, 10>, r: <0, 0, 0>}"
            }
            """
        );
        var viewModel = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "drag-settings.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath)
        );
        viewModel.Enabled = true;

        long now = 1000;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            _ => true,
            clock: () => now
        );

        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedCategory = "Biology & surface";
        Assert.Contains("PlotAdjustVR", runtime.Frames.Keys);
        Assert.Contains("PlotBioStatus", runtime.Frames.Keys);
        Assert.Contains("PlotBioSystem", runtime.Frames.Keys);
        Assert.Equal("PlotBioStatus", viewModel.SelectedOverlayName);

        Assert.True(coordinator.ToggleInteraction());
        viewModel.IsDragModeEnabled = true;

        // Grabbing PlotBioSystem selects it and drags it right/up + pushes depth with wheel.
        double bioWidth = OverlayLayoutCatalog.GetRequired("PlotBioSystem").PreviewSize.Width;
        double bioHeight = runtime.Frames["PlotBioSystem"].PointerHeight;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.LeftButtonDown,
                bioWidth * 0.5,
                bioHeight * 0.5
            )
        );
        coordinator.PollInteractionEvents();
        Assert.Equal("PlotBioSystem", viewModel.SelectedOverlayName);

        double startX = viewModel.PositionX;
        double startY = viewModel.PositionY;
        double startZ = viewModel.PositionZ;
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.Move,
                bioWidth * 0.75,
                bioHeight * 0.25
            )
        );
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.Wheel,
                bioWidth * 0.75,
                bioHeight * 0.25,
                WheelY: 1
            )
        );
        coordinator.PollInteractionEvents();
        Assert.True(viewModel.PositionX > startX);
        Assert.True(viewModel.PositionY > startY);
        Assert.True(viewModel.PositionZ > startZ);

        // Intermediate move within <32 ms is throttled, while releasing applies the final drop position without throttling.
        double afterDragX = viewModel.PositionX;
        now += 10;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotBioSystem", VrOverlayPointerEventKind.Move, bioWidth, bioHeight * 0.25)
        );
        coordinator.PollInteractionEvents();
        Assert.Equal(afterDragX, viewModel.PositionX, 4);

        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.LeftButtonUp,
                bioWidth,
                bioHeight * 0.25
            )
        );
        coordinator.PollInteractionEvents();
        double afterReleaseX = viewModel.PositionX;
        Assert.True(afterReleaseX > afterDragX);

        // Move after release does not keep dragging; same-coordinate release after an applied move does not double-apply.
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotBioSystem", VrOverlayPointerEventKind.Move, bioWidth, bioHeight * 0.25)
        );
        coordinator.PollInteractionEvents();
        Assert.Equal(afterReleaseX, viewModel.PositionX, 4);

        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.LeftButtonDown,
                bioWidth * 0.5,
                bioHeight * 0.5
            )
        );
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotBioSystem", VrOverlayPointerEventKind.Move, bioWidth * 0.75, bioHeight * 0.5)
        );
        coordinator.PollInteractionEvents();
        double singleMoveX = viewModel.PositionX;
        Assert.True(singleMoveX > afterReleaseX);
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.LeftButtonUp,
                bioWidth * 0.75,
                bioHeight * 0.5
            )
        );
        coordinator.PollInteractionEvents();
        Assert.Equal(singleMoveX, viewModel.PositionX, 4);

        // Changing selection or toggling interaction off clears an active drag so later moves cannot edit another panel.
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioSystem",
                VrOverlayPointerEventKind.LeftButtonDown,
                bioWidth * 0.5,
                bioHeight * 0.5
            )
        );
        coordinator.PollInteractionEvents();
        viewModel.SelectedOverlayName = "PlotBioStatus";
        double statusStartX = viewModel.PositionX;
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotBioSystem", VrOverlayPointerEventKind.Move, bioWidth, bioHeight * 0.5)
        );
        coordinator.PollInteractionEvents();
        Assert.Equal(statusStartX, viewModel.PositionX, 4);

        // Disabling interaction during a drag clears drag state so head-pose tracking resumes.
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotBioStatus",
                VrOverlayPointerEventKind.LeftButtonDown,
                bioWidth * 0.5,
                bioHeight * 0.5
            )
        );
        coordinator.PollInteractionEvents();
        Assert.True(coordinator.ToggleInteraction());
        Assert.False(coordinator.IsInteractionEnabled);

        // Re-enable interaction, then simulate runtime self-shutdown on SessionLost: coordinator clears interaction state.
        Assert.True(coordinator.ToggleInteraction());
        Assert.True(coordinator.IsInteractionEnabled);
        runtime.SimulateSessionLostOnNextProbe = true;
        viewModel.RefreshConnectionCommand.Execute(null);
        Assert.False(coordinator.IsInteractionEnabled);
    }

    [AvaloniaFact]
    public void LowLayerCapacityPreservesControlsAndSelectedPreviewDuringCategoryCalibration()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data-cap");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-cap");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            """
            {
              "PlotBioStatus": "center:0, top:8 { s: 3.5, p: <0, 4.5, 11>, r: <0, 0, 0>}",
              "PlotBioSystem": "left:8, bottom:144 { s: 3.2, p: <-6, -0.5, 9.5>, r: <0, 0, 0>}"
            }
            """
        );
        var viewModel = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "ui-settings-cap.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath)
        );
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime { MaximumOverlays = 2 };
        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime);

        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedCategory = "Biology & surface";
        viewModel.SelectedOverlayName = "PlotBioSystem";

        Assert.Equal(2, runtime.Frames.Count);
        Assert.True(runtime.Frames.ContainsKey(VrOverlayCalibrationSession.ControlsPlotterName));
        Assert.True(runtime.Frames.ContainsKey("PlotBioSystem"));
        Assert.False(runtime.Frames.ContainsKey("PlotBioStatus"));
    }

    [AvaloniaFact]
    public void VdxrCalibrationHonorsNativeLayerBudgetAndShowsOneSlotWarningWhenInteractionTakesCursorSlot()
    {
        string mailboxPath = Path.Combine(temporaryDirectory, "vdxr-budget", "frames.bin");
        long now = 100_000;
        using var runtime = new OpenXrOverlayRuntime(() => new OpenXrFrameBridge(mailboxPath, () => now), () => true);
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;

        using var coordinator = new VrOverlayCoordinator(viewModel, new OverlayWindowRegistry(), runtime);
        using (
            var file = new FileStream(
                mailboxPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            )
        )
        using (
            var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
                file,
                null,
                OpenXrFramePacket.Capacity,
                System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true
            )
        )
        using (System.IO.MemoryMappedFiles.MemoryMappedViewAccessor view = mapping.CreateViewAccessor())
        {
            // Ready state with 2 available overlay layers above Elite's projection layer (encoded as 3).
            view.Write(40, (uint)OpenXrLayerState.Ready);
            view.Write(44, 3u);
            view.Write(32, now);
        }

        using var inputFile = new FileStream(
            mailboxPath + OpenXrInputBridge.FileSuffix,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete
        );
        using var inputMapping = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
            inputFile,
            null,
            4096,
            System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true
        );
        using System.IO.MemoryMappedFiles.MemoryMappedViewAccessor inputView = inputMapping.CreateViewAccessor();
        inputView.Write(56, inputView.ReadUInt64(24));
        inputView.Write(40, 1u);
        inputView.Write(32, now);

        viewModel.RefreshConnectionCommand.Execute(null);
        Assert.True(runtime.IsInitialized);
        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        Assert.Equal(2, runtime.MaximumOverlays);
        Assert.Contains("2 live overlays", viewModel.ConnectionDetail, StringComparison.Ordinal);

        // Enabling controller interaction takes one layer for the cursor, leaving 1 slot -> only PlotAdjustVR stays live + warning shown.
        Assert.True(coordinator.ToggleInteraction());
        Assert.Equal(1, runtime.MaximumOverlays);
        Assert.Contains("1 live overlays", viewModel.ConnectionDetail, StringComparison.Ordinal);
        Assert.Contains("only fits the calibration controls", viewModel.StatusMessage, StringComparison.Ordinal);

        // When only 1 overlay layer is free, disabling and attempting to re-enable controller interaction must fail instead of hiding PlotAdjustVR.
        Assert.True(coordinator.ToggleInteraction());
        using (
            var file = new FileStream(
                mailboxPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            )
        )
        using (
            var mapping = System.IO.MemoryMappedFiles.MemoryMappedFile.CreateFromFile(
                file,
                null,
                OpenXrFramePacket.Capacity,
                System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: true
            )
        )
        using (System.IO.MemoryMappedFiles.MemoryMappedViewAccessor view = mapping.CreateViewAccessor())
        {
            // 1 available overlay layer above Elite (encoded as 2).
            view.Write(44, 2u);
        }

        Assert.False(coordinator.ToggleInteraction());
        Assert.False(coordinator.IsInteractionEnabled);
        Assert.Equal(1, runtime.MaximumOverlays);
        Assert.Contains("two free composition layers", viewModel.StatusMessage, StringComparison.Ordinal);
        viewModel.RefreshConnectionCommand.Execute(null);
        Assert.Contains("1 live overlays", viewModel.ConnectionDetail, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ReplacingLivePlotterWithCalibrationPreviewFlushesQueuedPointerInput()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var liveWindow = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Orange },
        };
        var runtime = new StubOpenVrRuntime();
        try
        {
            registry.Register(liveWindow, "PlotJumpInfo");
            liveWindow.Show();
            using var coordinator = new VrOverlayCoordinator(viewModel, registry, runtime);
            Assert.True(runtime.Frames.ContainsKey("PlotJumpInfo"));
            Assert.True(coordinator.ToggleInteraction());

            // Queue a click against the live PlotJumpInfo, then enter calibration which replaces it with a preview.
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 50, 50)
            );
            int beforeChanges = runtime.InteractionChangeCount;
            Assert.True(viewModel.BeginAdjustment());
            Assert.Empty(runtime.PointerEvents);
            Assert.True(runtime.InteractionChangeCount > beforeChanges);
        }
        finally
        {
            liveWindow.Close();
        }
    }

    [AvaloniaFact]
    public void SteadyLiveOverlaysDoNotResetVrPointerInteractionAcrossSynchronizeTicks()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data-steady");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-steady");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            """
            {
              "PlotBioStatus": "center:0, top:8 { s: 4, p: <0, 4, 10>, r: <0, 0, 0>}",
              "PlotJumpInfo": "center:0, top:8 { s: 4, p: <0, 2, 10>, r: <0, 0, 0>}"
            }
            """
        );
        var viewModel = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "steady-settings.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath)
        );
        viewModel.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var liveWindow = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Orange },
        };
        var runtime = new StubOpenVrRuntime();
        using var router = new VrOverlayInputRouter();
        try
        {
            registry.Register(liveWindow, "PlotBioStatus");
            liveWindow.Show();
            using var coordinator = new VrOverlayCoordinator(
                viewModel,
                registry,
                runtime,
                _ => true,
                inputRouter: router
            );
            Assert.True(viewModel.BeginAdjustment());
            viewModel.SelectedOverlayName = "PlotJumpInfo";
            Assert.True(coordinator.ToggleInteraction());

            // Press and hold on PlotAdjustVR while a steady live overlay (PlotBioStatus) is also published.
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
            );
            coordinator.PollInteractionEvents();
            Assert.True(router.HasCapture);

            int interactionCallsBefore = runtime.InteractionChangeCount;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonUp, 20, 20)
            );

            // Repeated Synchronize ticks allocate fresh RegisteredOverlayWindow snapshots for liveWindow,
            // but must NOT treat the unchanged surface as replaced or flush queued pointer events.
            viewModel.RefreshConnectionCommand.Execute(null);
            viewModel.RefreshConnectionCommand.Execute(null);

            Assert.Equal(interactionCallsBefore, runtime.InteractionChangeCount);
            Assert.True(router.HasCapture);
            Assert.Single(runtime.PointerEvents);

            coordinator.PollInteractionEvents();
            Assert.False(router.HasCapture);
        }
        finally
        {
            liveWindow.Close();
        }
    }

    [AvaloniaFact]
    public void ReplacingUnrelatedOverlayPreservesActiveVrCaptureDragAndQueuedRelease()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data-unrelated");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-unrelated");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            """
            {
              "PlotBioStatus": "center:0, top:8 { s: 4, p: <0, 4, 10>, r: <0, 0, 0>}",
              "PlotJumpInfo": "center:0, top:8 { s: 4, p: <0, 2, 10>, r: <0, 0, 0>}"
            }
            """
        );
        var viewModel = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "unrelated-settings.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath)
        );
        viewModel.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var bioWindow1 = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Orange },
        };
        var bioWindow2 = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Green },
        };
        var bioWindow3 = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Blue },
        };
        var runtime = new StubOpenVrRuntime();
        using var router = new VrOverlayInputRouter();
        long tickMs = 1000;
        try
        {
            registry.Register(bioWindow1, "PlotBioStatus");
            bioWindow1.Show();
            using var coordinator = new VrOverlayCoordinator(
                viewModel,
                registry,
                runtime,
                _ => true,
                inputRouter: router,
                clock: () => tickMs
            );
            Assert.True(viewModel.BeginAdjustment());
            viewModel.SelectedOverlayName = "PlotJumpInfo";
            Assert.True(coordinator.ToggleInteraction());

            // 0. A queued click on unchanged PlotAdjustVR BEFORE capture starts (including after a leading Reset) must not be swallowed when unrelated PlotBioStatus is replaced.
            int preCaptureInteractionChanges = runtime.InteractionChangeCount;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotBioStatus", VrOverlayPointerEventKind.Move, 10, 10)
            );
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent(string.Empty, VrOverlayPointerEventKind.Reset, 0, 0)
            );
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.Move, 20, 20)
            );
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
            );
            var bioWindow0 = new Window
            {
                Width = 200,
                Height = 100,
                WindowDecorations = WindowDecorations.None,
                Content = new Border { Background = Brushes.Yellow },
            };
            try
            {
                registry.Register(bioWindow0, "PlotBioStatus");
                bioWindow0.Show();
                viewModel.RefreshConnectionCommand.Execute(null);
                bioWindow1.Close();
                Assert.Equal(preCaptureInteractionChanges, runtime.InteractionChangeCount);
                coordinator.PollInteractionEvents();
                Assert.True(router.HasCapture);
                Assert.Equal("PlotAdjustVR", router.CapturedPlotterName);
                runtime.PointerEvents.Enqueue(
                    new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonUp, 20, 20)
                );
                coordinator.PollInteractionEvents();
                Assert.False(router.HasCapture);
            }
            finally
            {
                bioWindow0.Close();
            }
            registry.Register(bioWindow2, "PlotBioStatus");
            bioWindow2.Show();
            viewModel.RefreshConnectionCommand.Execute(null);

            // 1. Active UI capture on PlotAdjustVR survives replacing unrelated PlotBioStatus, including its queued release.
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonDown, 20, 20)
            );
            coordinator.PollInteractionEvents();
            Assert.True(router.HasCapture);
            Assert.Equal("PlotAdjustVR", router.CapturedPlotterName);

            int interactionCallsBefore = runtime.InteractionChangeCount;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotBioStatus", VrOverlayPointerEventKind.Move, 10, 10)
            );
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotAdjustVR", VrOverlayPointerEventKind.LeftButtonUp, 20, 20)
            );

            registry.Register(bioWindow3, "PlotBioStatus");
            bioWindow3.Show();
            viewModel.RefreshConnectionCommand.Execute(null);
            bioWindow2.Close();

            Assert.Equal(interactionCallsBefore, runtime.InteractionChangeCount);
            Assert.True(router.HasCapture);
            Assert.Equal("PlotAdjustVR", router.CapturedPlotterName);

            coordinator.PollInteractionEvents();
            Assert.False(router.HasCapture);
            Assert.Null(router.CapturedPlotterName);

            // 2. Active drag on PlotJumpInfo preview survives replacing unrelated PlotBioStatus and applies queued release movement.
            viewModel.IsDragModeEnabled = true;
            double startX = viewModel.PositionX;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 100, 50)
            );
            coordinator.PollInteractionEvents();

            tickMs += 100;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonUp, 200, 50)
            );
            var bioWindow4 = new Window
            {
                Width = 200,
                Height = 100,
                WindowDecorations = WindowDecorations.None,
                Content = new Border { Background = Brushes.Purple },
            };
            try
            {
                registry.Register(bioWindow4, "PlotBioStatus");
                bioWindow4.Show();
                viewModel.RefreshConnectionCommand.Execute(null);
                bioWindow3.Close();

                coordinator.PollInteractionEvents();
                Assert.True(Math.Abs(viewModel.PositionX - startX) > 0.1);
            }
            finally
            {
                bioWindow4.Close();
            }

            // 3. Releasing a drag after the 32ms throttle window with the same coordinate (as SteamVR emits) must NOT apply the movement a second time.
            double dragStartX = viewModel.PositionX;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 100, 50)
            );
            coordinator.PollInteractionEvents();
            tickMs += 100;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, 180, 50)
            );
            coordinator.PollInteractionEvents();
            double afterMoveX = viewModel.PositionX;
            Assert.True(Math.Abs(afterMoveX - dragStartX) > 0.1);

            tickMs += 100;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonUp, 180, 50)
            );
            coordinator.PollInteractionEvents();
            Assert.Equal(afterMoveX, viewModel.PositionX, 6);

            // 4. Subsequent moves (and throttled final drop) that hit the same UV coordinate against the panel's NEW pose MUST still apply.
            double continuedStartX = viewModel.PositionX;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonDown, 100, 50)
            );
            coordinator.PollInteractionEvents();
            tickMs += 100;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, 150, 50)
            );
            coordinator.PollInteractionEvents();
            double afterFirstStepX = viewModel.PositionX;
            Assert.True(Math.Abs(afterFirstStepX - continuedStartX) > 0.1);

            // Next poll after the panel moved to its updated pose: hitting 150 again is another +25% drag step.
            tickMs += 100;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, 150, 50)
            );
            coordinator.PollInteractionEvents();
            double afterSecondStepX = viewModel.PositionX;
            Assert.True(Math.Abs(afterSecondStepX - afterFirstStepX) > 0.1);

            // Throttled move at 150 followed immediately by release at 150 must apply the final drop step.
            tickMs += 10;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, 150, 50)
            );
            coordinator.PollInteractionEvents();
            Assert.Equal(afterSecondStepX, viewModel.PositionX, 6);

            tickMs += 5;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.LeftButtonUp, 150, 50)
            );
            coordinator.PollInteractionEvents();
            Assert.True(Math.Abs(viewModel.PositionX - afterSecondStepX) > 0.1);
        }
        finally
        {
            bioWindow1.Close();
            bioWindow2.Close();
            bioWindow3.Close();
        }
    }

    [AvaloniaFact]
    public void CoordinatorKeepsWiVRnSessionClosedUntilEliteStartsAndReleasesItOnGameExit()
    {
        bool gameRunning = false;
        var backend = new StubLinuxOpenXrBackend();
        var inspector = new LinuxVrRuntimeInspector(
            isLinux: () => true,
            getEnvironmentVariable: _ => null,
            readAllText: _ => null,
            fileExists: _ => false,
            isProcessRunning: name => name == "wivrn-server"
        );
        using var runtime = new LinuxOpenXrOverlayRuntime(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId,
            backend,
            inspector,
            () => gameRunning
        );
        VrOverlayViewModel model = CreateViewModel();
        model.Enabled = true;
        var registry = new OverlayWindowRegistry();

        using var coordinator = new VrOverlayCoordinator(model, registry, runtime);
        Assert.Equal(VrOverlayConnectionState.WaitingForRuntime, model.ConnectionState);
        Assert.Contains("Waiting for Elite Dangerous", model.ConnectionDetail, StringComparison.Ordinal);
        Assert.Equal(0, backend.CreateCalls);
        Assert.False(runtime.IsInitialized);

        // Starting Elite attaches overlays cleanly and allows controller interaction.
        gameRunning = true;
        model.RefreshConnectionCommand.Execute(null);
        Assert.Equal(VrOverlayConnectionState.Ready, model.ConnectionState);
        Assert.Equal(1, backend.CreateCalls);
        Assert.True(runtime.IsInitialized);
        Assert.True(coordinator.ToggleInteraction());
        Assert.True(coordinator.IsInteractionEnabled);

        // Exiting Elite immediately tears down the OpenXR session and resets interaction so WiVRn returns to lobby.
        gameRunning = false;
        model.RefreshConnectionCommand.Execute(null);
        Assert.Equal(VrOverlayConnectionState.WaitingForRuntime, model.ConnectionState);
        Assert.Equal(1, backend.DestroyCalls);
        Assert.False(runtime.IsInitialized);
        Assert.False(coordinator.IsInteractionEnabled);

        // Restarting Elite reconnects cleanly without the user toggling overlays off and back on.
        gameRunning = true;
        model.RefreshConnectionCommand.Execute(null);
        Assert.Equal(VrOverlayConnectionState.Ready, model.ConnectionState);
        Assert.Equal(2, backend.CreateCalls);
        Assert.True(runtime.IsInitialized);
    }

    [AvaloniaFact]
    public void PlaceInFrontAndDragBatchPropertyUpdatesIntoSingleSynchronizePass()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        long now = 1000;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            _ => true,
            clock: () => now
        );

        Assert.True(viewModel.BeginAdjustment());
        Assert.True(coordinator.ToggleInteraction());

        int beforePlace = runtime.CompleteFrameCount;
        viewModel.PlaceInFrontCommand.Execute(null);
        Assert.Equal(beforePlace + 1, runtime.CompleteFrameCount);

        viewModel.IsDragModeEnabled = true;
        double width = OverlayLayoutCatalog.GetRequired("PlotJumpInfo").PreviewSize.Width;
        double height = runtime.Frames["PlotJumpInfo"].PointerHeight;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent(
                "PlotJumpInfo",
                VrOverlayPointerEventKind.LeftButtonDown,
                width * 0.5,
                height * 0.5
            )
        );
        coordinator.PollInteractionEvents();

        int beforeDrag = runtime.CompleteFrameCount;
        now += 50;
        runtime.PointerEvents.Enqueue(
            new VrOverlayPointerEvent("PlotJumpInfo", VrOverlayPointerEventKind.Move, width * 0.75, height * 0.25)
        );
        coordinator.PollInteractionEvents();
        Assert.Equal(beforeDrag + 1, runtime.CompleteFrameCount);
    }

    [AvaloniaFact]
    public void SingleLayerCapacityReportsCalibrationStatusAndFailedInteractionReenableResetsLocally()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var registry = new OverlayWindowRegistry();
        var liveWindow = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Orange },
        };
        var runtime = new StubOpenVrRuntime { MaximumOverlays = 1 };
        try
        {
            registry.Register(liveWindow, "PlotJumpInfo");
            liveWindow.Show();
            using var coordinator = new VrOverlayCoordinator(viewModel, registry, runtime);
            Assert.True(viewModel.BeginAdjustment());
            Assert.Single(runtime.Frames);
            Assert.True(runtime.Frames.ContainsKey(VrOverlayCalibrationSession.ControlsPlotterName));
            Assert.Equal(VrOverlayConnectionState.Ready, viewModel.ConnectionState);
            Assert.Contains("layer limit", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

            // Reset to normal capacity, enable interaction, then fail SetInteractionEnabled during surface replacement.
            viewModel.CancelCommand.Execute(null);
            runtime.MaximumOverlays = 16;
            viewModel.RefreshConnectionCommand.Execute(null);
            Assert.True(coordinator.ToggleInteraction());
            Assert.True(coordinator.IsInteractionEnabled);

            runtime.FailNextSetInteractionTrue = true;
            Assert.True(viewModel.BeginAdjustment());
            Assert.False(coordinator.IsInteractionEnabled);
            Assert.Contains("Interaction rejected", viewModel.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            liveWindow.Close();
        }
    }

    [AvaloniaFact]
    public void CategoryPreviewResolvesUnselectedPanelsAgainstSelectedModeNotRuntimeMode()
    {
        string dataDirectory = Path.Combine(temporaryDirectory, "data-mode");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-mode");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        string factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            """
            {
              "PlotJumpInfo": "center:0, top:8 { s: 4, p: <0, 1, 10>, r: <0, 0, 0>}",
              "PlotBioStatus": "center:0, top:8 { s: 4, p: <0, 4, 10>, r: <0, 0, 0>}",
              "PlotBioSystem": "left:8, bottom:144 { s: 4, p: <-6, 0, 10>, r: <0, 0, 0>}"
            }
            """
        );
        var calibrationStore = new VrOverlayCalibrationStore(dataDirectory, factoryPath);
        calibrationStore.Save(
            "PlotBioSystem",
            new VrOverlayCalibration(4, new System.Numerics.Vector3(-2, 3, 9), System.Numerics.Vector3.Zero),
            mode: "Ship"
        );
        calibrationStore.Save(
            "PlotBioSystem",
            new VrOverlayCalibration(4, new System.Numerics.Vector3(5, -2, 8), System.Numerics.Vector3.Zero),
            mode: "testbuggy"
        );
        calibrationStore.Save(
            "PlotJumpInfo",
            new VrOverlayCalibration(4, new System.Numerics.Vector3(9, 2, 11), System.Numerics.Vector3.Zero),
            mode: "Ship"
        );

        var viewModel = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "mode-settings.json")),
            calibrationStore
        );
        viewModel.Enabled = true;

        var registry = new OverlayWindowRegistry();
        var liveWindow = new Window
        {
            Width = 200,
            Height = 100,
            WindowDecorations = WindowDecorations.None,
            Content = new Border { Background = Brushes.Orange },
        };
        var runtime = new StubOpenVrRuntime();
        long now = 1000;
        try
        {
            registry.Register(liveWindow, "PlotJumpInfo");
            liveWindow.Show();
            using var coordinator = new VrOverlayCoordinator(
                viewModel,
                registry,
                runtime,
                _ => true,
                modeProvider: () => "Ship",
                clock: () => now
            );

            Assert.True(viewModel.BeginAdjustment());
            viewModel.SelectedCategory = "Biology & surface";
            viewModel.SelectedMode = "testbuggy";
            Assert.Equal("PlotBioStatus", viewModel.SelectedOverlayName);

            // Unselected category sample resolves against SelectedMode ("testbuggy"), while live panel uses runtimeMode ("Ship").
            Assert.Equal(5f, runtime.Calibrations["PlotBioSystem"].Position.X);
            Assert.Equal(-2f, runtime.Calibrations["PlotBioSystem"].Position.Y);
            Assert.Equal(9f, runtime.Calibrations["PlotJumpInfo"].Position.X);

            // Dragging the unselected sibling starts cleanly from its testbuggy placement without jumping to Ship.
            Assert.True(coordinator.ToggleInteraction());
            viewModel.IsDragModeEnabled = true;
            double width = OverlayLayoutCatalog.GetRequired("PlotBioSystem").PreviewSize.Width;
            double height = runtime.Frames["PlotBioSystem"].PointerHeight;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent(
                    "PlotBioSystem",
                    VrOverlayPointerEventKind.LeftButtonDown,
                    width * 0.5,
                    height * 0.5
                )
            );
            coordinator.PollInteractionEvents();
            Assert.Equal("PlotBioSystem", viewModel.SelectedOverlayName);
            Assert.Equal(5, viewModel.PositionX);

            now += 50;
            runtime.PointerEvents.Enqueue(
                new VrOverlayPointerEvent("PlotBioSystem", VrOverlayPointerEventKind.Move, width * 0.75, height * 0.5)
            );
            coordinator.PollInteractionEvents();
            Assert.True(viewModel.PositionX > 5);
        }
        finally
        {
            liveWindow.Close();
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

        public int MaximumOverlays { get; set; } = int.MaxValue;

        public int ResetCount { get; private set; }

        public int ProbeCount { get; private set; }

        public int ShutdownCount { get; private set; }

        public int CompleteFrameCount { get; private set; }

        public int InteractionChangeCount { get; private set; }

        public bool InteractionEnabled { get; private set; }

        public bool FailNextSetInteractionTrue { get; set; }

        public VrOverlayFrame? LastFrame { get; private set; }

        public float LastAlpha { get; private set; }

        public Queue<VrOverlayPointerEvent> PointerEvents { get; } = new();

        public Dictionary<string, VrOverlayFrame> Frames { get; } = [];

        public Dictionary<string, VrOverlayCalibration> Calibrations { get; } = [];

        public bool PoseAvailable { get; init; } = true;

        public int HeadPoseCalls { get; private set; }

        public bool TryGetHeadPose(out VrOverlayHeadPose? pose)
        {
            HeadPoseCalls++;
            pose = PoseAvailable ? new VrOverlayHeadPose(System.Numerics.Vector3.Zero, 0) : null;
            return PoseAvailable;
        }

        public VrRuntimeProbe ProbeResult { get; init; } = VrRuntimeProbe.Ready();

        public bool SimulateSessionLostOnNextProbe { get; set; }

        public VrRuntimeProbe Probe()
        {
            ProbeCount++;
            if (SimulateSessionLostOnNextProbe)
            {
                SimulateSessionLostOnNextProbe = false;
                IsInitialized = false;
                return VrRuntimeProbe.HeadsetUnavailable("Session lost.");
            }

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
            Frames[plotterName] = frame;
            Calibrations[plotterName] = calibration;
            return VrRuntimeResult.Success("Published.");
        }

        public VrRuntimeResult CompleteFrame()
        {
            CompleteFrameCount++;
            return VrRuntimeResult.Success("Completed.");
        }

        public void RemoveOverlay(string plotterName) => Frames.Remove(plotterName);

        public VrRuntimeResult SetInteractionEnabled(bool enabled)
        {
            InteractionChangeCount++;
            if (enabled && FailNextSetInteractionTrue)
            {
                FailNextSetInteractionTrue = false;
                InteractionEnabled = false;
                return VrRuntimeResult.Failure("Interaction rejected.");
            }

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

    private sealed class StubLinuxOpenXrBackend : ILinuxOpenXrOverlayBackend
    {
        public bool IsAvailable => true;
        public int CreateCalls { get; private set; }
        public int DestroyCalls { get; private set; }

        public LinuxOpenXrProbeOutcome Probe(string expectedRuntime) => new(LinuxOpenXrNativeStatus.Ok, 0, "Ready");

        public LinuxOpenXrCreateOutcome Create(string expectedRuntime)
        {
            CreateCalls++;
            return new(new nint(CreateCalls), LinuxOpenXrNativeStatus.Ok, 0, "Created");
        }

        public bool Publish(nint session, byte[] packet) => true;

        public LinuxOpenXrSessionStatus GetStatus(nint session) => new(LinuxOpenXrNativeStatus.Ok, 0, 0, 16, "Active");

        public bool TryGetHeadPose(nint session, uint expectedOriginRevision, out VrOverlayHeadPose? pose)
        {
            pose = new VrOverlayHeadPose(System.Numerics.Vector3.Zero, 0);
            return true;
        }

        public LinuxOpenXrInteractionOutcome SetInteractionEnabled(nint session, bool enabled) =>
            new(true, 0, enabled ? "Interaction on." : "Interaction off.");

        public IReadOnlyList<OpenXrPointerEvent> PollPointerEvents(nint session) => [];

        public void Destroy(nint session) => DestroyCalls++;
    }
}
