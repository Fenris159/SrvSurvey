using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class QuestSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-quest-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void PreferenceRoundTripsWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui.json");
        File.WriteAllText(
            path,
            "{\"Theme\":\"green-dark\",\"Future\":{\"Value\":42}}");
        var store = new QuestSettingsStore(path);

        Assert.False(store.LoadEnabled());

        store.SaveEnabled(true);

        Assert.True(store.LoadEnabled());
        var saved = File.ReadAllText(path);
        Assert.Contains("green-dark", saved);
        Assert.Contains("42", saved);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
