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
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows));
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
            OverlayPlatformCapabilities.ForHost(host));
    }
}
