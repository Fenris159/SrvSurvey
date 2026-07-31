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
        Assert.Contains("blue-dark", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("live", false)]
    [InlineData("beta", true)]
    [InlineData("dev", true)]
    [InlineData("unexpected", true)]
    public void LegacyGatewayPreferenceMigratesToSchemaMode(
        string environment,
        bool expected)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            $"{{\"NetworkPrivacy\":{{\"EddnEnvironment\":\"{environment}\"}}}}");
        var store = new NetworkPrivacySettingsStore(path);

        var preferences = store.Load();
        store.Save(preferences);

        Assert.Equal(expected, preferences.EddnUseTestSchemas);
        var saved = File.ReadAllText(path);
        Assert.Contains("EddnUseTestSchemas", saved);
        Assert.DoesNotContain("EddnEnvironment", saved);
    }

    [Fact]
    public void ExplicitSchemaModeTakesPrecedenceOverLegacyGateway()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {"NetworkPrivacy":{"EddnUseTestSchemas":false,"EddnEnvironment":"dev"}}
            """);

        var preferences = new NetworkPrivacySettingsStore(path).Load();

        Assert.False(preferences.EddnUseTestSchemas);
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
