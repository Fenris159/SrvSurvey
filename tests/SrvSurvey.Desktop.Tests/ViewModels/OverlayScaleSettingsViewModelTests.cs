using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayScaleSettingsViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-scale-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void SelectionPersistsAndUpdatesTheActiveOverlayContext()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-settings.json");
        var store = new OverlayScaleSettingsStore(path);
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(),
            null,
            null);
        var viewModel = new OverlayScaleSettingsViewModel(
            store,
            layout,
            new OverlayWindowRegistry());

        viewModel.SelectedOption = viewModel.Options.Single(option =>
            option.Index == 19);

        Assert.Equal(19, layout.ScaleIndex);
        Assert.Equal(new OverlayScalePreferences(19), store.Load());
        Assert.Contains("250%", viewModel.SettingsStatus);
        Assert.True(viewModel.HasSettingsStatus);
    }

    [Fact]
    public void ConstructionAppliesPersistedScaleToActiveLayout()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui-settings.json");
        var store = new OverlayScaleSettingsStore(path);
        store.Save(new OverlayScalePreferences(24));
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(),
            null,
            null);

        var viewModel = new OverlayScaleSettingsViewModel(
            store,
            layout,
            new OverlayWindowRegistry());

        Assert.Equal(24, viewModel.SelectedOption.Index);
        Assert.Equal(24, layout.ScaleIndex);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
