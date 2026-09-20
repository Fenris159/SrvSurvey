using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayScaleSettingsViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-scale-vm-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SelectionPersistsAndUpdatesTheActiveOverlayContext()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        var store = new OverlayScaleSettingsStore(path);
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        var viewModel = new OverlayScaleSettingsViewModel(store, layout, new OverlayWindowRegistry());

        viewModel.ScalePercent = 200;

        int expectedIndex = OverlayScaleCatalog.GetIndex(200);
        Assert.Equal(expectedIndex, layout.ScaleIndex);
        Assert.Equal(new OverlayScalePreferences(expectedIndex), store.Load());
        Assert.Contains("+200%", viewModel.SettingsStatus);
        Assert.True(viewModel.HasSettingsStatus);
    }

    [Fact]
    public void ConstructionAppliesPersistedScaleToActiveLayout()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "ui-settings.json");
        var store = new OverlayScaleSettingsStore(path);
        int expectedIndex = OverlayScaleCatalog.GetIndex(-40);
        store.Save(new OverlayScalePreferences(expectedIndex));
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);

        var viewModel = new OverlayScaleSettingsViewModel(store, layout, new OverlayWindowRegistry());

        Assert.Equal(-40, viewModel.ScalePercent);
        Assert.Equal(expectedIndex, layout.ScaleIndex);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
