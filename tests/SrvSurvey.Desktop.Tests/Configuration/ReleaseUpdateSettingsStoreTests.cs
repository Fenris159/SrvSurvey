using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ReleaseUpdateSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-release-update-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingOptsIntoDevelopmentReleases()
    {
        Assert.True(CreateStore().LoadUseDevelopmentReleases());
    }

    [Fact]
    public void ChannelRoundTripsWithoutRemovingUnknownSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new ReleaseUpdateSettingsStore(path);

        store.SaveUseDevelopmentReleases(false);

        Assert.False(store.LoadUseDevelopmentReleases());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));

        store.SaveUseDevelopmentReleases(true);

        Assert.True(store.LoadUseDevelopmentReleases());
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private ReleaseUpdateSettingsStore CreateStore()
    {
        return new ReleaseUpdateSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
