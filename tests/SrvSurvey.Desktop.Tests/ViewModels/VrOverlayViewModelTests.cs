using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class VrOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-view-model-tests-{Guid.NewGuid():N}");

    [Fact]
    public void AdjustmentCanCreateAndReloadCurrentModeOverride()
    {
        var viewModel = CreateViewModel();
        viewModel.SetCurrentRuntimeMode("testbuggy");

        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        viewModel.SelectedMode = "testbuggy";
        viewModel.Scale = 24;
        viewModel.PositionX = -6;
        viewModel.SaveCommand.Execute(null);

        Assert.Contains("testbuggy", viewModel.AvailableModes);
        Assert.Equal(
            24,
            viewModel.GetCalibration("PlotJumpInfo", "testbuggy")!.Scale);
        Assert.Contains("Saved PlotJumpInfo (testbuggy)", viewModel.StatusMessage);
    }

    [Fact]
    public void CancelDiscardsUnsavedPreviewCalibration()
    {
        var viewModel = CreateViewModel();
        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        var savedScale = viewModel.Scale;
        viewModel.Scale = 42;

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsAdjusting);
        Assert.Equal(savedScale, viewModel.Scale);
        Assert.Equal(
            savedScale,
            viewModel.GetCalibration("PlotJumpInfo")!.Scale);
    }

    [Fact]
    public void PreferencesPersistImmediately()
    {
        var viewModel = CreateViewModel();

        viewModel.Enabled = true;
        viewModel.RuntimeProcessName = "vrcompositor";

        Assert.Equal(
            new VrOverlayPreferences(true, "vrcompositor"),
            new VrOverlaySettingsStore(SettingsPath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SettingsPath => Path.Combine(
        temporaryDirectory,
        "ui-settings.json");

    private VrOverlayViewModel CreateViewModel()
    {
        var data = Path.Combine(temporaryDirectory, "data");
        var factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        var factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factory,
            "{\"PlotJumpInfo\":\"center:0, top:8 "
            + "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        return new VrOverlayViewModel(
            new VrOverlaySettingsStore(SettingsPath),
            new VrOverlayCalibrationStore(data, factory));
    }
}
