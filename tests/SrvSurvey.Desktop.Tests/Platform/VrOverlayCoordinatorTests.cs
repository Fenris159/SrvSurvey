using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class VrOverlayCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-coordinator-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ResetOrientationUsesActiveRuntimeAndReportsResult()
    {
        var viewModel = CreateViewModel();
        viewModel.Enabled = true;
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            _ => true);

        var reset = coordinator.ResetOrientation();

        Assert.True(reset);
        Assert.Equal(1, runtime.ResetCount);
        Assert.Contains("Captured", viewModel.StatusMessage);
    }

    [Fact]
    public void ResetOrientationFailsClosedWhileVrIsDisabled()
    {
        var viewModel = CreateViewModel();
        var runtime = new StubOpenVrRuntime();
        using var coordinator = new VrOverlayCoordinator(
            viewModel,
            new OverlayWindowRegistry(),
            runtime,
            _ => true);

        var reset = coordinator.ResetOrientation();

        Assert.False(reset);
        Assert.Equal(0, runtime.ResetCount);
        Assert.Contains("Enable OpenVR", viewModel.StatusMessage);
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
        var dataDirectory = Path.Combine(temporaryDirectory, "data");
        var factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(factoryDirectory);
        var factoryPath = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factoryPath,
            "{\"PlotJumpInfo\":\"center:0, top:8 "
                + "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        return new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            new VrOverlayCalibrationStore(dataDirectory, factoryPath));
    }

    private sealed class StubOpenVrRuntime : IOpenVrRuntime
    {
        public bool IsInitialized { get; private set; }

        public int ResetCount { get; private set; }

        public VrRuntimeResult Initialize()
        {
            IsInitialized = true;
            return VrRuntimeResult.Success("OpenVR is active.");
        }

        public VrRuntimeResult PublishOverlay(
            string plotterName,
            VrOverlayFrame frame,
            VrOverlayCalibration calibration,
            float alpha)
        {
            return VrRuntimeResult.Success("Published.");
        }

        public void RemoveOverlay(string plotterName)
        {
        }

        public VrRuntimeResult ResetOrientation()
        {
            ResetCount++;
            return VrRuntimeResult.Success(
                "Captured the current headset yaw as the overlay origin.");
        }

        public void Shutdown()
        {
            IsInitialized = false;
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
