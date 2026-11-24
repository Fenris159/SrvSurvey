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
        Assert.Equal(22.2, viewModel.RotationPitch, 1);

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

    [Fact]
    public void LinuxOpenXrRoutesWorkOnLinuxAndAreRejectedOnWindows()
    {
        VrOverlayViewModel windowsModel = CreateViewModel(OSPlatform.Windows);
        windowsModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId
        );
        Assert.Equal(VrPlatformProfileCatalog.SteamVrProfileId, windowsModel.SelectedPlatformProfile.Id);

        VrOverlayViewModel linuxModel = CreateViewModel(OSPlatform.Linux);
        linuxModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(
            VrPlatformProfileCatalog.WivrnOpenXrProfileId
        );
        Assert.Equal(VrPlatformProfileCatalog.WivrnOpenXrProfileId, linuxModel.SelectedPlatformProfile.Id);
        Assert.Contains("Linux OpenXR", linuxModel.PlatformSupportMessage);
        Assert.True(linuxModel.BeginAdjustment());
        linuxModel.SelectedOverlayName = "PlotJumpInfo";
        Assert.Equal(8, linuxModel.Scale);

        linuxModel.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(
            VrPlatformProfileCatalog.MonadoOpenXrProfileId
        );
        Assert.Equal(VrPlatformProfileCatalog.MonadoOpenXrProfileId, linuxModel.SelectedPlatformProfile.Id);
        Assert.Contains("Linux OpenXR", linuxModel.PlatformSupportMessage);
    }

    [Fact]
    public void CategorySelectionFiltersTargetsShowsAllCategoryPanelsAndTracksPendingEditsAndDragMode()
    {
        string data = Path.Combine(temporaryDirectory, "data-cat");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-cat");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        string factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factory,
            """
            {
              "PlotJumpInfo": "center:0, top:8 { s: 8, p: <0, 4.5, 11>, r: <22.2, 0, 0>}",
              "PlotBioStatus": "center:0, top:8 { s: 3.5, p: <0, 4.5, 11>, r: <22.2, 0, 0>}",
              "PlotBioSystem": "left:8, bottom:144 { s: 3.2, p: <-6, -0.5, 9.5>, r: <-2.5, 32.3, 0>}"
            }
            """
        );
        var model = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "settings-cat.json")),
            new VrOverlayCalibrationStore(data, factory),
            VrPlatformProfileCatalog.ForPlatform(OSPlatform.Linux, Architecture.X64, 0)
        );
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.WivrnOpenXrProfileId);

        // Drag mode cannot enable before adjustment begins.
        model.IsDragModeEnabled = true;
        Assert.False(model.IsDragModeEnabled);
        Assert.Empty(model.VisibleAdjustmentOverlays);

        Assert.True(model.BeginAdjustment());
        Assert.Equal(VrOverlayViewModel.AllCategoriesLabel, model.SelectedCategory);
        Assert.Contains("Biology & surface", model.AvailableCategories);
        Assert.Contains("Exploration & navigation", model.AvailableCategories);
        Assert.Single(model.VisibleAdjustmentOverlays);

        // Selecting a category filters AvailableOverlays and exposes all panels in that category.
        model.SelectedCategory = "Biology & surface";
        Assert.True(model.IsCategoryPreviewActive);
        Assert.Equal(["PlotBioStatus", "PlotBioSystem"], model.AvailableOverlays);
        Assert.Equal(["PlotBioStatus", "PlotBioSystem"], model.VisibleAdjustmentOverlays);
        Assert.Equal("PlotBioStatus", model.SelectedOverlayName);

        // Edits on multiple panels in the category remain visible while switching between them and save together.
        model.PositionX = 2.5;
        model.SelectedOverlayName = "PlotBioSystem";
        model.PositionY = 1.5;
        Assert.Equal(2.5f, model.GetCalibration("PlotBioStatus")!.Position.X);
        Assert.Equal(1.5f, model.GetCalibration("PlotBioSystem")!.Position.Y);

        model.IsDragModeEnabled = true;
        Assert.True(model.IsDragModeEnabled);
        Assert.Contains("Drag mode active", model.StatusMessage);

        model.SaveCommand.Execute(null);
        Assert.Contains("Saved 2 overlays", model.StatusMessage);

        // Cycle category commands wrap cleanly and Cancel turns off drag mode.
        model.NextCategoryCommand.Execute(null);
        model.PreviousCategoryCommand.Execute(null);
        Assert.Equal("Biology & surface", model.SelectedCategory);

        model.PositionX = -9;
        model.CancelCommand.Execute(null);
        Assert.False(model.IsAdjusting);
        Assert.False(model.IsDragModeEnabled);
        Assert.Equal(-6f, model.GetCalibration("PlotBioSystem")!.Position.X);

        // Partial group save failure refreshes committed overlays and reports which overlays remain unsaved.
        Assert.True(model.BeginAdjustment());
        model.SelectedCategory = "Biology & surface";
        model.SelectedOverlayName = "PlotBioStatus";
        model.PositionX = 4.25;
        model.SelectedOverlayName = "PlotBioSystem";
        model.PositionX = double.NaN;
        model.SaveCommand.Execute(null);
        Assert.Contains("Saved 1 of 2 overlays; unsaved: PlotBioSystem", model.StatusMessage);
        model.CancelCommand.Execute(null);
        Assert.Equal(4.25f, model.GetCalibration("PlotBioStatus")!.Position.X);
        Assert.Equal(-6f, model.GetCalibration("PlotBioSystem")!.Position.X);
    }

    [Fact]
    public void PartialSaveHandlesReloadFailureWithoutThrowingAndResetFailurePreservesEdits()
    {
        string data = Path.Combine(temporaryDirectory, "data-fail");
        string factoryDirectory = Path.Combine(temporaryDirectory, "factory-fail");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(factoryDirectory);
        string factory = Path.Combine(factoryDirectory, "plotters.json");
        File.WriteAllText(
            factory,
            """
            {
              "PlotBioStatus": "center:0, top:8 { s: 3.5, p: <0, 4.5, 11>, r: <22.2, 0, 0>}",
              "PlotBioSystem": "left:8, bottom:144 { s: 3.2, p: <-6, -0.5, 9.5>, r: <-2.5, 32.3, 0>}"
            }
            """
        );
        var model = new VrOverlayViewModel(
            new VrOverlaySettingsStore(Path.Combine(temporaryDirectory, "settings-fail.json")),
            new VrOverlayCalibrationStore(data, factory),
            VrPlatformProfileCatalog.ForPlatform(OSPlatform.Linux, Architecture.X64, 0)
        );
        model.SelectedPlatformProfile = VrPlatformProfileCatalog.Get(VrPlatformProfileCatalog.WivrnOpenXrProfileId);
        Assert.True(model.BeginAdjustment());
        model.SelectedCategory = "Biology & surface";

        // Corrupt an unrelated mode file after load so partial-save reload fails inside the catch block.
        string routeDir = Path.Combine(data, "vr", "wivrn");
        Directory.CreateDirectory(routeDir);
        string corruptModePath = Path.Combine(routeDir, "corrupt.json");
        File.WriteAllText(corruptModePath, "{");

        model.SelectedOverlayName = "PlotBioStatus";
        model.PositionX = 3.25;
        model.SelectedOverlayName = "PlotBioSystem";
        model.PositionX = double.NaN;
        model.SaveCommand.Execute(null);
        Assert.Contains("Saved 1 of 2 overlays; unsaved: PlotBioSystem", model.StatusMessage);

        // Corrupt Default.json so ResetCommand fails on Save; UI values and pending edits must stay intact.
        File.Delete(corruptModePath);
        File.WriteAllText(Path.Combine(routeDir, "Default.json"), "{");
        model.SelectedOverlayName = "PlotBioStatus";
        model.PositionX = 7.5;
        model.ResetCommand.Execute(null);
        Assert.Contains("VR calibration was not saved", model.StatusMessage);
        Assert.Equal(7.5, model.PositionX);
        Assert.Equal(7.5f, model.GetCalibration("PlotBioStatus")!.Position.X);
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
