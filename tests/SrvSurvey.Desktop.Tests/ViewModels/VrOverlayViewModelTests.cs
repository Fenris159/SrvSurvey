using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class VrOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-view-model-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void AdjustmentCanCreateAndReloadCurrentModeOverride()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.SetCurrentRuntimeMode("testbuggy");

        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        viewModel.SelectedMode = "testbuggy";
        viewModel.Scale = 24;
        viewModel.PositionX = -6;
        viewModel.SaveCommand.Execute(null);

        Assert.Contains("testbuggy", viewModel.AvailableModes);
        Assert.Equal(24, viewModel.GetCalibration("PlotJumpInfo", "testbuggy")!.Scale);
        Assert.Contains("Saved PlotJumpInfo (testbuggy)", viewModel.StatusMessage);
    }

    [Fact]
    public void CancelDiscardsUnsavedPreviewCalibration()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        double savedScale = viewModel.Scale;
        viewModel.Scale = 42;

        viewModel.CancelCommand.Execute(null);

        Assert.False(viewModel.IsAdjusting);
        Assert.Equal(savedScale, viewModel.Scale);
        Assert.Equal(savedScale, viewModel.GetCalibration("PlotJumpInfo")!.Scale);
    }

    [Fact]
    public void PreferencesPersistImmediately()
    {
        VrOverlayViewModel viewModel = CreateViewModel();

        viewModel.Enabled = true;
        viewModel.RuntimeProcessName = "vrcompositor";

        Assert.Equal(
            new VrOverlayPreferences(true, "steamvr", "vrcompositor"),
            new VrOverlaySettingsStore(SettingsPath).Load()
        );
    }

    [Fact]
    public void PlatformSelectionPersistsAndExposesPairingSteps()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        VrPlatformProfile meta = viewModel.PlatformProfiles.Single(profile => profile.Id == "meta-via-steamvr");

        viewModel.SelectedPlatformProfile = meta;

        Assert.Equal(meta, viewModel.SelectedPlatformProfile);
        Assert.False(viewModel.IsCustomRuntime);
        Assert.Contains(meta.PairingSteps, step => step.Contains("SteamVR", StringComparison.Ordinal));
        Assert.Equal("meta-via-steamvr", new VrOverlaySettingsStore(SettingsPath).Load().RuntimeProfileId);
    }

    [Fact]
    public void CustomProfileUsesConfiguredProcess()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.SelectedPlatformProfile = viewModel.PlatformProfiles.Single(profile => profile.IsCustom);
        viewModel.RuntimeProcessName = "custom-runtime";

        Assert.True(viewModel.IsCustomRuntime);
        Assert.Equal("custom-runtime", viewModel.RuntimeProcessName);
    }

    [Fact]
    public void DisablingOverlaysReportsOffWithoutRequestingAnotherConnection()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        int connectionChecks = 0;
        viewModel.ConnectionCheckRequested += (_, _) => connectionChecks++;

        viewModel.Enabled = true;
        viewModel.Enabled = false;

        Assert.Equal(1, connectionChecks);
        Assert.Equal(VrOverlayConnectionState.Disabled, viewModel.ConnectionState);
        Assert.Equal("OFF", viewModel.ConnectionStateLabel);
        Assert.Equal("VR overlays are off", viewModel.ConnectionHeadline);
        Assert.Contains("calibration", viewModel.ConnectionDetail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VrOverlayConnectionState.WaitingForRuntime, "WAITING")]
    [InlineData(VrOverlayConnectionState.Connecting, "CONNECTING")]
    [InlineData(VrOverlayConnectionState.Ready, "CONNECTED")]
    [InlineData(VrOverlayConnectionState.Error, "NEEDS ATTENTION")]
    [InlineData((VrOverlayConnectionState)99, "UNKNOWN")]
    public void ConnectionStatesExposeReadableLabels(VrOverlayConnectionState state, string expectedLabel)
    {
        VrOverlayViewModel viewModel = CreateViewModel();

        viewModel.SetConnectionStatus(state, "Test headline", "Test detail");

        Assert.Equal(expectedLabel, viewModel.ConnectionStateLabel);
        Assert.Equal("Test headline", viewModel.ConnectionHeadline);
        Assert.Equal("Test detail", viewModel.ConnectionDetail);
    }

    [Fact]
    public void ChangingRouteWhileEnabledRequestsAConnectionCheck()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.Enabled = true;
        int connectionChecks = 0;
        viewModel.ConnectionCheckRequested += (_, _) => connectionChecks++;

        viewModel.SelectedPlatformProfile = viewModel.PlatformProfiles.Single(profile =>
            profile.Id == VrPlatformProfileCatalog.MetaAlvrProfileId
        );

        Assert.Equal(1, connectionChecks);
        Assert.Equal(VrOverlayConnectionState.WaitingForRuntime, viewModel.ConnectionState);
        Assert.Equal("Connection route changed", viewModel.ConnectionHeadline);
        Assert.Contains("ALVR", viewModel.ConnectionDetail, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SettingsPath => Path.Combine(temporaryDirectory, "ui-settings.json");

    private VrOverlayViewModel CreateViewModel()
    {
        string data = Path.Combine(temporaryDirectory, "data");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        string factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(factory, "{\"PlotJumpInfo\":\"center:0, top:8 " + "{ s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\"}");
        return new VrOverlayViewModel(
            new VrOverlaySettingsStore(SettingsPath),
            new VrOverlayCalibrationStore(data, factory)
        );
    }
}
