using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class StreamOverlaySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-stream-settings-tests-{Guid.NewGuid():N}");

    [Fact]
    public void MissingSettingsDefaultToDisabled()
    {
        var store = CreateStore();

        Assert.False(store.LoadEnabled());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SavedSettingRoundTripsWithoutRemovingOtherSections(bool enabled)
    {
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(path, "{\"Future\":{\"Keep\":42}}");
        var store = new StreamOverlaySettingsStore(path);

        store.SaveEnabled(enabled);

        Assert.Equal(enabled, store.LoadEnabled());
        Assert.Contains("\"Keep\": 42", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private StreamOverlaySettingsStore CreateStore()
    {
        return new StreamOverlaySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
