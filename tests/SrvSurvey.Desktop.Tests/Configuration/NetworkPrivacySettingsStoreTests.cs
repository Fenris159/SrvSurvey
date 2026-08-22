using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

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
        Assert.False(preferences.EddnUploadEnabled);
        Assert.False(preferences.UploadHumanSettlementGeometry);
    }

    [Fact]
    public void FailedSaveDoesNotActivateEddnConsent()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var blockedParent = Path.Combine(temporaryDirectory, "not-a-folder");
        File.WriteAllText(blockedParent, "occupied");
        var viewModel = new NetworkPrivacyViewModel(
            new NetworkPrivacySettingsStore(
                Path.Combine(blockedParent, "ui-settings.json")));
        var changes = new List<bool>();
        viewModel.EddnUploadEnabledChanged += changes.Add;

        var saved = viewModel.TrySetEddnUploadEnabled(true);

        Assert.False(saved);
        Assert.False(viewModel.EddnUploadEnabled);
        Assert.Empty(changes);
        Assert.Contains(
            "was not changed",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, "{\"Theme\":\"blue-dark\"}");
        var store = new NetworkPrivacySettingsStore(path);
        var expected = new NetworkPrivacyPreferences(true, true, true);

        store.Save(expected);

        Assert.Equal(expected, store.Load());
        Assert.Contains("blue-dark", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("live")]
    [InlineData("beta")]
    [InlineData("dev")]
    [InlineData("unexpected")]
    public void LegacySchemaPreferencesAreIgnoredAndRemovedOnSave(
        string environment)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            $"{{\"NetworkPrivacy\":{{\"EddnEnvironment\":\"{environment}\"}}}}");
        var store = new NetworkPrivacySettingsStore(path);

        var preferences = store.Load();
        store.Save(preferences);

        Assert.False(preferences.EddnUploadEnabled);
        var saved = File.ReadAllText(path);
        Assert.DoesNotContain("EddnUseTestSchemas", saved);
        Assert.DoesNotContain("EddnEnvironment", saved);
    }

    [Fact]
    public void ExplicitLegacySchemaModeIsRemovedWhenPreferencesAreSaved()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {"NetworkPrivacy":{"EddnUseTestSchemas":false,"EddnEnvironment":"dev"}}
            """);

        var store = new NetworkPrivacySettingsStore(path);
        var preferences = store.Load();
        store.Save(preferences);

        Assert.DoesNotContain(
            "EddnUseTestSchemas",
            File.ReadAllText(path));
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
