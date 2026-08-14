using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class VoxStellarSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-VoxStellarSettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentKeepsJournalSharingOptedOut()
    {
        var preferences = CreateStore().Load();

        Assert.Equal(VoxStellarPreferences.Default, preferences);
        Assert.False(preferences.JournalUploadEnabled);
    }

    [Fact]
    public void PreferenceRoundTripsWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"blue-dark\"}");
        var store = new VoxStellarSettingsStore(path);

        store.Save(new VoxStellarPreferences(true));

        Assert.True(store.Load().JournalUploadEnabled);
        Assert.Contains("blue-dark", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private VoxStellarSettingsStore CreateStore() => new(
        Path.Combine(temporaryDirectory, "ui-settings.json"));
}
