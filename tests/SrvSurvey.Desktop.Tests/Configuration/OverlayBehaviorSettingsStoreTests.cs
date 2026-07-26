using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class OverlayBehaviorSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-overlay-behavior-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsUseLegacyDefaults()
    {
        Assert.Equal(
            new OverlayBehaviorPreferences(false, false, false),
            CreateStore().Load());
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new OverlayBehaviorSettingsStore(path);
        var expected = new OverlayBehaviorPreferences(true, true, true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
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
        return new OverlayBehaviorSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
