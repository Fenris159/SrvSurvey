using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class PulseOverlaySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-pulse-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsKeepTheLegacyOverlayEnabled()
    {
        Assert.Equal(new PulseOverlayPreferences(true), CreateStore().Load());
    }

    [Fact]
    public void SavedPreferencePreservesUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new PulseOverlaySettingsStore(path);

        store.Save(new PulseOverlayPreferences(false));

        Assert.Equal(new PulseOverlayPreferences(false), store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private PulseOverlaySettingsStore CreateStore()
    {
        return new PulseOverlaySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
