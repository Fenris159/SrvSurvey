using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class VrOverlaySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-vr-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsUseSafeDisabledDefaults()
    {
        var store = CreateStore();

        Assert.Equal(
            new VrOverlayPreferences(false, "vrserver"),
            store.Load());
    }

    [Fact]
    public void SavedPreferencesRoundTripWithoutRemovingFutureSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new VrOverlaySettingsStore(path);

        store.Save(new VrOverlayPreferences(true, "vrcompositor"));

        Assert.Equal(
            new VrOverlayPreferences(true, "vrcompositor"),
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

    private VrOverlaySettingsStore CreateStore()
    {
        return new VrOverlaySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
