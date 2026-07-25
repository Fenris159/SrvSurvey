using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class NetworkPrivacySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-NetworkPrivacySettings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingDocumentKeepsPublicationDisabled()
    {
        Assert.Equal(
            NetworkPrivacyPreferences.Default,
            CreateStore().Load());
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"blue-dark\"}");
        var store = new NetworkPrivacySettingsStore(path);
        var expected = new NetworkPrivacyPreferences(true, "live", true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("blue-dark", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null, "dev")]
    [InlineData("", "dev")]
    [InlineData("unexpected", "dev")]
    [InlineData(" BETA ", "beta")]
    [InlineData("LIVE", "live")]
    public void EnvironmentIsRestrictedToKnownDestinations(
        string? value,
        string expected)
    {
        Assert.Equal(
            expected,
            NetworkPrivacySettingsStore.NormalizeEnvironment(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private NetworkPrivacySettingsStore CreateStore()
    {
        return new NetworkPrivacySettingsStore(
            Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
