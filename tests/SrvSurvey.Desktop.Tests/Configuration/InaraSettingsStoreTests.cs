using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class InaraSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-InaraSettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentKeepsUploadsAndTestModeDisabled()
    {
        var preferences = CreateStore().Load();

        Assert.Equal(InaraPreferences.Default, preferences);
        Assert.False(preferences.UploadEnabled);
        Assert.False(preferences.DeveloperTestMode);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"blue-dark\"}");
        var store = new InaraSettingsStore(path);
        var expected = new InaraPreferences(
            UploadEnabled: true,
            DeveloperTestMode: true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("blue-dark", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private InaraSettingsStore CreateStore()
    {
        return new InaraSettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
