using System.Runtime.InteropServices;
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
    public void ExperimentalEdvrInputIsExplicitPersistedAndOnlyOfferedForTheVdxrRoute()
    {
        VrOverlayViewModel model = CreateViewModel();
        Assert.False(model.EdvrControllerInput);
        Assert.False(model.IsVdxr);
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.True(model.IsVdxr);
        model.EdvrControllerInput = true;
        Assert.True(new VrOverlaySettingsStore(SettingsPath).Load().EdvrControllerInput);
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.SteamVrProfileId);
        Assert.False(model.IsVdxr);
        Assert.True(CreateViewModel().EdvrControllerInput);
        model.EdvrControllerInput = false;
        Assert.False(new VrOverlaySettingsStore(SettingsPath).Load().EdvrControllerInput);
    }

    [Fact]
    public void HeadsetSelectorsCyclePanelsAndModesAndDiscardUnsavedSelectionChanges()
    {
        VrOverlayViewModel model = CreateViewModel();
        bool placeEnabled = false;
        model.PlaceInFrontCommand.CanExecuteChanged += (_, _) =>
            placeEnabled = model.PlaceInFrontCommand.CanExecute(null);
        Assert.False(model.PlaceInFrontCommand.CanExecute(null));
        model.SetCurrentRuntimeMode("testbuggy");
        Assert.True(model.BeginAdjustment());
        Assert.True(model.PlaceInFrontCommand.CanExecute(null));
        Assert.True(placeEnabled);
        Assert.Equal("PlotBioStatus", model.SelectedOverlayName);
        model.PreviousOverlayCommand.Execute(null);
        Assert.Equal("PlotJumpInfo", model.SelectedOverlayName);
        model.Scale = 42;
        model.NextOverlayCommand.Execute(null);
        Assert.Equal("PlotBioStatus", model.SelectedOverlayName);
        model.NextOverlayCommand.Execute(null);
        Assert.Equal("PlotJumpInfo", model.SelectedOverlayName);
        Assert.Equal(20, model.Scale);
        model.PreviousModeCommand.Execute(null);
        Assert.Equal("testbuggy", model.SelectedMode);
        model.NextModeCommand.Execute(null);
        Assert.Equal("Default", model.SelectedMode);
    }

    [Fact]
    public void VdxrResetUsesSeatedFactoryPlacementAndVehicleResetUsesTheSavedRouteDefault()
    {
        VrOverlayViewModel model = CreateViewModel();
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        model.BeginAdjustment();
        model.SelectedOverlayName = "PlotJumpInfo";
        model.Scale = 42;
        model.ResetCommand.Execute(null);
        Assert.Equal(8, model.Scale);
        Assert.Equal(4.5, model.PositionY);
        model.SetCurrentRuntimeMode("testbuggy");
        model.SelectedMode = "testbuggy";
        model.PositionY = 35;
        model.ResetCommand.Execute(null);
        model.CancelCommand.Execute(null);
        Assert.Equal(4.5f, model.GetCalibration("PlotJumpInfo", "testbuggy")!.Position.Y);
    }

    [Theory]
    [InlineData("{\"PlotJumpInfo\":42}")]
    [InlineData("{\"PlotJumpInfo\":")]
    public void InvalidRouteCalibrationKeepsThePreviousRouteAndReportsTheLoadFailure(string json)
    {
        VrOverlayViewModel model = CreateViewModel();
        string directory = Path.Combine(temporaryDirectory, "data", "vr", "vdxr");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Default.json"), json);
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.Equal(VrPlatformProfileCatalog.SteamVrProfileId, model.SelectedPlatformProfile.Id);
        Assert.Contains("could not be loaded", model.StatusMessage);
    }

    [Fact]
    public void ChangingToARouteWithoutTheSelectedPanelCannotPublishOrSaveItsOldPlacement()
    {
        VrOverlayViewModel model = CreateViewModel();
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "data", "plotters.json"),
            """{"PlotJumpInfo":"center:0, top:8","PlotBioStatus":"left:0, top:0 { s: 5, p: <0, 0, 10>, r: <0, 0, 0>}"}"""
        );
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.True(model.BeginAdjustment());
        model.SelectedOverlayName = "PlotJumpInfo";
        model.Scale = 42;
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.SteamVrProfileId);
        Assert.Equal(["PlotBioStatus"], model.AvailableOverlays);
        Assert.Equal("PlotBioStatus", model.SelectedOverlayName);
        Assert.Null(model.GetCalibration("PlotJumpInfo"));
        Assert.Equal(5, model.Scale);
        model.SaveCommand.Execute(null);
        Assert.Null(
            new VrOverlayCalibrationStore(Path.Combine(temporaryDirectory, "data")).Load().Resolve("PlotJumpInfo", null)
        );
    }

    [Fact]
    public void VdxrUsesSeatedDefaultsAndKeepsSavedPlacementSeparateFromSteamVr()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.True(viewModel.BeginAdjustment());
        viewModel.SelectedOverlayName = "PlotJumpInfo";
        Assert.Equal(8, viewModel.Scale);
        Assert.Equal(4.5, viewModel.PositionY);
        Assert.Equal(11, viewModel.PositionZ);
        Assert.Equal(-20, viewModel.RotationPitch);

        viewModel.PositionY = 1;
        viewModel.SaveCommand.Execute(null);
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.SteamVrProfileId);
        Assert.Equal(20, viewModel.Scale);
        Assert.Equal(2, viewModel.PositionY);
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.Equal(1, viewModel.PositionY);
        Assert.Equal(8, viewModel.Scale);
    }

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
    public void VdxrProfilePersistsAndExplainsTheRequiredOpenXrLayer()
    {
        VrOverlayViewModel viewModel = CreateViewModel();
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.Contains("OpenXR layer", viewModel.PlatformSupportMessage);
        Assert.Contains("Direct3D 11", viewModel.PlatformSupportMessage);
        Assert.Contains(
            viewModel.SelectedPlatformProfile.PairingSteps,
            step => step.Contains("Runtime: VDXR", StringComparison.Ordinal)
        );
        Assert.Equal(
            VrPlatformProfileCatalog.MetaVdxrProfileId,
            new VrOverlaySettingsStore(SettingsPath).Load().RuntimeProfileId
        );
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

    [Fact]
    public void LinuxRejectsUnsupportedRoutesEvenWhenCallerUsesTheUnrestrictedCatalog()
    {
        VrOverlayViewModel viewModel = CreateViewModel(OSPlatform.Linux);
        int connectionChecks = 0;
        viewModel.ConnectionCheckRequested += (_, _) => connectionChecks++;
        VrPlatformProfile before = viewModel.SelectedPlatformProfile;
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaVdxrProfileId);
        Assert.Equal(before, viewModel.SelectedPlatformProfile);
        Assert.Equal(0, connectionChecks);
        Assert.Equal(
            VrPlatformProfileCatalog.SteamVrProfileId,
            new VrOverlaySettingsStore(SettingsPath).Load().RuntimeProfileId
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedUnavailableRouteStaysVisibleWithOverlaysOffUntilAnAvailableRouteIsChosen(bool enabled)
    {
        var store = new VrOverlaySettingsStore(SettingsPath);
        store.Save(new VrOverlayPreferences(enabled, VrPlatformProfileCatalog.MetaVdxrProfileId, "vrserver"));
        VrOverlayViewModel viewModel = CreateViewModel(OSPlatform.Linux);
        Assert.Equal(VrPlatformProfileCatalog.MetaVdxrProfileId, viewModel.SelectedPlatformProfile.Id);
        Assert.False(viewModel.Enabled);
        Assert.False(viewModel.CanEnableOverlays);
        Assert.Contains("Windows only", viewModel.PlatformSupportMessage);
        Assert.Contains("unavailable", viewModel.ConnectionHeadline);
        Assert.Contains("Windows only", viewModel.StatusMessage);
        Assert.False(store.Load().Enabled);
        int checks = 0;
        viewModel.ConnectionCheckRequested += (_, _) => checks++;
        viewModel.Enabled = true;
        Assert.False(viewModel.Enabled);
        Assert.Equal(0, checks);
        viewModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.MetaAlvrProfileId);
        Assert.True(viewModel.CanEnableOverlays);
        Assert.False(viewModel.Enabled);
        Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, store.Load().RuntimeProfileId);
        viewModel.Enabled = true;
        Assert.True(viewModel.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private string SettingsPath => Path.Combine(temporaryDirectory, "ui-settings.json");

    private VrOverlayViewModel CreateViewModel(OSPlatform? platform = null)
    {
        string data = Path.Combine(temporaryDirectory, "data");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        string factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factory,
            "{\"PlotJumpInfo\":\"center:0, top:8 { s: 20, p: <1, 2, 3>, r: <4, 5, 6>}\",\"PlotBioStatus\":\"left:0, top:0 { s: 5, p: <0, 0, 10>, r: <0, 0, 0>}\"}"
        );
        return new VrOverlayViewModel(
            new VrOverlaySettingsStore(SettingsPath),
            new VrOverlayCalibrationStore(data, factory),
            VrPlatformProfileCatalog.ForPlatform(platform ?? OSPlatform.Windows, Architecture.X64, 22000)
        );
    }
}
