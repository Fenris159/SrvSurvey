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
        var preferences = CreateStore().Load();

        Assert.Equal(NetworkPrivacyPreferences.Default, preferences);
        Assert.False(preferences.UploadHumanSettlementGeometry);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"blue-dark\"}");
        var store = new NetworkPrivacySettingsStore(path);
        var expected = new NetworkPrivacyPreferences(true, true, true, true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        var saved = File.ReadAllText(path);
        Assert.Contains("blue-dark", saved);
        Assert.Contains("EddnUseTestSchemas", saved);
        Assert.DoesNotContain("EddnEnvironment", saved);
    }

    [Theory]
    [InlineData("live", false)]
    [InlineData("unknown", false)]
    [InlineData(" BETA ", true)]
    [InlineData("DEV", true)]
    public void LegacyEnvironmentMigratesWithoutChangingTestIntent(
        string value,
        bool expected)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            "{\"NetworkPrivacy\":{\"EddnEnvironment\":\""
                + value
                + "\"}}");

        Assert.Equal(
            expected,
            new NetworkPrivacySettingsStore(path).Load().EddnUseTestSchemas);
    }

    [Fact]
    public void ExplicitTestSchemaSettingTakesPrecedenceOverLegacyEnvironment()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {"NetworkPrivacy":{"EddnUseTestSchemas":false,"EddnEnvironment":"dev"}}
            """);

        Assert.False(
            new NetworkPrivacySettingsStore(path).Load().EddnUseTestSchemas);
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
