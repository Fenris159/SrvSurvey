using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class JumpInfoSettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-JumpInfoSettings-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void MissingDocumentUsesLegacyCompatibleDefaults()
    {
        JumpInfoSettingsStore store = CreateStore();

        JumpInfoPreferences preferences = store.Load();

        Assert.True(preferences.AutoShow);
        Assert.False(preferences.Minimal);
        Assert.False(preferences.ShowWhenNextHopSelected);
        Assert.False(preferences.UseSpanshLastUpdated);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherUiSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"Blue-dark\"}");
        var store = new JumpInfoSettingsStore(path);

        store.Save(new JumpInfoPreferences(false, true, true, true));
        JumpInfoPreferences reloaded = store.Load();

        Assert.Equal(new JumpInfoPreferences(false, true, true, true), reloaded);
        Assert.Contains("Blue-dark", File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private JumpInfoSettingsStore CreateStore()
    {
        return new JumpInfoSettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
