using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class DockToDockSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-DockToDockSettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingSettingIsOffAndSavePreservesUnknownValues()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"Future\":true,\"Travel\":{\"FutureTravel\":42}}");
        var store = new DockToDockSettingsStore(path);

        Assert.False(store.LoadEnabled());
        store.SaveEnabled(true);

        Assert.True(store.LoadEnabled());
        var saved = File.ReadAllText(path);
        Assert.Contains("\"Future\": true", saved);
        Assert.Contains("\"FutureTravel\": 42", saved);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
