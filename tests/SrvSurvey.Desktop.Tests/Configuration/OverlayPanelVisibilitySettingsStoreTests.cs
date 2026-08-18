using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayPanelVisibilitySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-visibility-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsDefaultEveryCatalogPanelToVisible()
    {
        var settings = CreateStore().Load();

        Assert.Equal(OverlayLayoutCatalog.Supported.Count, settings.Count);
        Assert.All(settings.Values, Assert.True);
    }

    [Fact]
    public void SaveRoundTripPreservesPanelVisibilityAndUnrelatedSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(path, "{ \"Theme\": \"purple-dark\" }");
        var store = new OverlayPanelVisibilitySettingsStore(path);
        var settings = store.Load().ToDictionary();
        settings["PlotGuardians"] = false;

        store.Save(settings);
        var loaded = store.Load();

        Assert.False(loaded["PlotGuardians"]);
        Assert.True(loaded["PlotGuardianStatus"]);
        Assert.Contains("\"Theme\": \"purple-dark\"", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private OverlayPanelVisibilitySettingsStore CreateStore()
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new OverlayPanelVisibilitySettingsStore(
            Path.Combine(temporaryDirectory, "ui.json"));
    }
}
