using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class WaylandCaptureSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-wayland-capture-settings-store-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MissingSettingsDefaultToDisabled()
    {
        Assert.Equal(new WaylandCapturePreferences(false), CreateStore().Load());
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new WaylandCaptureSettingsStore(path);

        var enabled = new WaylandCapturePreferences(
            Enabled: true,
            FssTuningEnabled: true,
            FirstFootfallEnabled: false,
            SurfaceMiningRigEnabled: true
        );
        store.Save(enabled);

        Assert.Equal(enabled, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path), StringComparison.Ordinal);
        store.Save(new WaylandCapturePreferences(false));
        Assert.Equal(new WaylandCapturePreferences(false), store.Load());
    }

    [Fact]
    public void NonBooleanEnabledValueDefaultsToDisabled()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"WaylandCapture\":{\"Enabled\":\"yes\"}}");

        Assert.Equal(new WaylandCapturePreferences(false), new WaylandCaptureSettingsStore(path).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private WaylandCaptureSettingsStore CreateStore()
    {
        return new WaylandCaptureSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
