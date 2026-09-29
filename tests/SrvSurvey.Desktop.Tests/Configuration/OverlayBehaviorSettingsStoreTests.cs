using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayBehaviorSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-behavior-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void MissingSettingsUseLegacyDefaults()
    {
        Assert.Equal(new OverlayBehaviorPreferences(false, false, false), CreateStore().Load());
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new OverlayBehaviorSettingsStore(path);
        var expected = new OverlayBehaviorPreferences(true, true, true, true, "DP-1", true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("\"  \"")]
    public void InvalidMonitorPreferencesUseAutomatic(string jsonValue)
    {
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "ui-settings.json"),
            "{\"OverlayBehavior\":{\"PreferredMonitor\":" + jsonValue + "}}"
        );

        Assert.Null(CreateStore().Load().PreferredMonitorId);
    }

    [Fact]
    public void AutomaticRemovesOnlyTheOverlayMonitorPreference()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"DesktopBehavior\":{\"PreferredMonitor\":\"HDMI-A-1\"}}");
        OverlayBehaviorSettingsStore store = CreateStore();
        store.Save(new OverlayBehaviorPreferences(false, false, false, PreferredMonitorId: " DP-1 "));
        Assert.Equal("DP-1", store.Load().PreferredMonitorId);

        store.Save(store.Load() with { PreferredMonitorId = null });

        Assert.Null(store.Load().PreferredMonitorId);
        Assert.Equal("HDMI-A-1", new DesktopBehaviorSettingsStore(path).Load().PreferredMonitorId);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private OverlayBehaviorSettingsStore CreateStore()
    {
        return new OverlayBehaviorSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
