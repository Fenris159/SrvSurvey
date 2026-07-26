using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GlobalInputSettingsViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-input-view-model-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SavesKeyboardToggleAndNormalizedBinding()
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        var store = new GlobalInputSettingsStore(path);
        var viewModel = new GlobalInputSettingsViewModel(
            store,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
            new StubControllerDeviceProvider());
        var changed = 0;
        viewModel.SettingsChanged += (_, _) => changed++;

        viewModel.KeyboardEnabled = true;
        viewModel.Bindings[0].Chord = "ctrl alt x";

        var loaded = store.Load();
        Assert.True(loaded.KeyboardEnabled);
        Assert.Equal(
            "ALT CTRL X",
            loaded.Bindings[GlobalInputAction.ToggleAllVisibility]);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void InvalidBindingDoesNotReplaceActiveSetting()
    {
        var viewModel = Create(OverlayHostKind.Windows);
        var binding = viewModel.Bindings[0];
        var original = viewModel.CurrentSettings.Bindings[
            GlobalInputAction.ToggleAllVisibility];
        var changed = 0;
        viewModel.SettingsChanged += (_, _) => changed++;

        binding.Chord = "CTRL A B";

        Assert.True(binding.HasValidationError);
        Assert.Equal(
            original,
            viewModel.CurrentSettings.Bindings[
                GlobalInputAction.ToggleAllVisibility]);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void UnsupportedHostCannotEnableKeyboardHook()
    {
        var viewModel = Create(OverlayHostKind.LinuxWayland);

        viewModel.KeyboardEnabled = true;

        Assert.False(viewModel.IsKeyboardAvailable);
        Assert.False(viewModel.KeyboardEnabled);
    }

    [Fact]
    public void RestoreDefaultsUpdatesEditedBindings()
    {
        var viewModel = Create(OverlayHostKind.Windows);
        viewModel.Bindings[0].Chord = "ALT X";

        viewModel.ResetBindingsCommand.Execute(parameter: null);

        Assert.Equal(
            GlobalInputActionCatalog.Get(
                GlobalInputAction.ToggleAllVisibility).DefaultChord,
            viewModel.Bindings[0].Chord);
    }

    [Fact]
    public void SelectsAndEnablesDiscoveredController()
    {
        var path = Path.Combine(temporaryDirectory, "controller.json");
        var store = new GlobalInputSettingsStore(path);
        var provider = new StubControllerDeviceProvider(
            new ControllerDeviceInfo(
                "path:controller-1",
                "Test HOTAS",
                "FlightStick - USB 1234:5678",
                7));
        var viewModel = new GlobalInputSettingsViewModel(
            store,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows),
            provider);

        viewModel.SelectedController = Assert.Single(
            viewModel.ControllerDevices);
        viewModel.ControllerEnabled = true;

        var loaded = store.Load();
        Assert.True(loaded.ControllerEnabled);
        Assert.True(viewModel.CanEnableControllerInput);
        Assert.Equal("path:controller-1", loaded.ControllerDeviceId);
        Assert.Equal("Found 1 connected controller.",
            viewModel.ControllerDiscoveryStatus);
    }

    [Fact]
    public void PreservesDisconnectedConfiguredControllerForReconnect()
    {
        var path = Path.Combine(temporaryDirectory, "reconnect.json");
        var store = new GlobalInputSettingsStore(path);
        store.Save(GlobalInputSettings.Default with
        {
            ControllerEnabled = true,
            ControllerDeviceId = "path:missing-controller",
        });

        var viewModel = new GlobalInputSettingsViewModel(
            store,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.LinuxX11),
            new StubControllerDeviceProvider());

        var device = Assert.Single(viewModel.ControllerDevices);
        Assert.False(device.IsConnected);
        Assert.Equal(device, viewModel.SelectedController);
        Assert.True(viewModel.ControllerEnabled);
    }

    [Fact]
    public void ControllerCannotBeEnabledWithoutSelection()
    {
        var viewModel = Create(OverlayHostKind.Windows);

        viewModel.ControllerEnabled = true;

        Assert.False(viewModel.ControllerEnabled);
        Assert.False(viewModel.CanEnableControllerInput);
    }

    [Fact]
    public void UnhandledActionReportsContextInsteadOfAnUnportedFeature()
    {
        var viewModel = Create(OverlayHostKind.Windows);

        viewModel.ReportAction(GlobalInputAction.CopyNextBoxel, handled: false);

        Assert.Contains("not available in the current game context",
            viewModel.LastActionStatus);
        Assert.DoesNotContain("not ported", viewModel.LastActionStatus);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private GlobalInputSettingsViewModel Create(OverlayHostKind host)
    {
        return new GlobalInputSettingsViewModel(
            new GlobalInputSettingsStore(
                Path.Combine(temporaryDirectory, "ui-settings.json")),
            OverlayPlatformCapabilities.ForHost(host),
            new StubControllerDeviceProvider());
    }

    private sealed class StubControllerDeviceProvider(
        params ControllerDeviceInfo[] devices) : IControllerDeviceProvider
    {
        public ControllerDeviceDiscoveryResult Discover()
        {
            return new ControllerDeviceDiscoveryResult(
                devices,
                ErrorMessage: null);
        }
    }
}
