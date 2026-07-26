using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class GalaxyMapSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-galaxy-map-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsUseLegacyEnabledDefaults()
    {
        Assert.Equal(
            new GalaxyMapPreferences(true, true),
            CreateStore().Load());
    }

    [Fact]
    public void SavedPreferencesPreserveUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new GalaxyMapSettingsStore(path);

        store.Save(new GalaxyMapPreferences(false, false));

        Assert.Equal(
            new GalaxyMapPreferences(false, false),
            store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private GalaxyMapSettingsStore CreateStore()
    {
        return new GalaxyMapSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
