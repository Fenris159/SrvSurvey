using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class NetworkPrivacySettingsStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-NetworkPrivacySettings-" + Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void MissingDocumentKeepsPublicationDisabled()
    {
        NetworkPrivacyPreferences preferences = CreateStore().Load();

        Assert.Equal(NetworkPrivacyPreferences.Default, preferences);
        Assert.False(preferences.EddnUploadEnabled);
        Assert.False(preferences.UploadHumanSettlementGeometry);
    }

    [Fact]
    public void EddnConsentPersistsAndNotifiesOnlyWhenItChanges()
    {
        NetworkPrivacySettingsStore store = CreateStore();
        var viewModel = new NetworkPrivacyViewModel(store);
        var changes = new List<bool>();
        viewModel.EddnUploadEnabledChanged += changes.Add;

        Assert.Equal("EDDN sharing is disabled.", viewModel.EddnConsentSummary);
        Assert.True(viewModel.TrySetEddnUploadEnabled(true));
        Assert.True(viewModel.TrySetEddnUploadEnabled(true));

        Assert.True(viewModel.EddnUploadEnabled);
        Assert.Equal("EDDN sharing is enabled for live Commander sessions.", viewModel.EddnConsentSummary);
        Assert.Equal([true], changes);
        Assert.True(store.Load().EddnUploadEnabled);
    }

    [Fact]
    public void EddnPublicationStatusDescribesLiveQueueing()
    {
        var viewModel = new NetworkPrivacyViewModel(CreateStore());

        viewModel.ReportPublicationResult(
            new EddnPublicationResult(
                [
                    new EddnPublishedEvent("FSDJump", "schema/1", false),
                    new EddnPublishedEvent("Scan", "schema/1", false),
                ],
                []
            )
        );

        Assert.Equal("Queued 2 journal events for EDDN.", viewModel.StatusMessage);

        viewModel.ReportPublicationResult(new EddnPublicationResult([], ["EDDN warning"]));
        Assert.Equal("EDDN warning", viewModel.StatusMessage);
    }

    [Fact]
    public void FailedSaveDoesNotActivateEddnConsent()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string blockedParent = Path.Combine(temporaryDirectory, "not-a-folder");
        File.WriteAllText(blockedParent, "occupied");
        var viewModel = new NetworkPrivacyViewModel(
            new NetworkPrivacySettingsStore(Path.Combine(blockedParent, "ui-settings.json"))
        );
        var changes = new List<bool>();
        viewModel.EddnUploadEnabledChanged += changes.Add;

        bool saved = viewModel.TrySetEddnUploadEnabled(true);

        Assert.False(saved);
        Assert.False(viewModel.EddnUploadEnabled);
        Assert.Empty(changes);
        Assert.Contains("was not changed", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedRuntimeTransitionRestoresPersistedEddnConsent()
    {
        NetworkPrivacySettingsStore store = CreateStore();
        var viewModel = new NetworkPrivacyViewModel(store);
        var transitions = new List<bool>();
        viewModel.EddnUploadEnabledChanged += enabled =>
        {
            transitions.Add(enabled);
            if (enabled)
            {
                throw new InvalidOperationException("simulated EDDN runtime failure");
            }
        };

        bool saved = viewModel.TrySetEddnUploadEnabled(true);

        Assert.False(saved);
        Assert.False(viewModel.EddnUploadEnabled);
        Assert.False(store.Load().EddnUploadEnabled);
        Assert.Equal([true, false], transitions);
        Assert.Contains("previous choice was restored", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidEddnConsentFallsBackToDisabled()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {"NetworkPrivacy":{"EddnUploadEnabled":"yes"}}
            """
        );

        NetworkPrivacyPreferences preferences = new NetworkPrivacySettingsStore(path).Load();

        Assert.False(preferences.EddnUploadEnabled);
    }

    [Fact]
    public void PreferencesRoundTripWithoutRemovingOtherSettings()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
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
    public void LegacySchemaPreferencesAreIgnoredAndRemovedOnSave(string environment)
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(path, $"{{\"NetworkPrivacy\":{{\"EddnEnvironment\":\"{environment}\"}}}}");
        var store = new NetworkPrivacySettingsStore(path);

        NetworkPrivacyPreferences preferences = store.Load();
        store.Save(preferences);

        Assert.False(preferences.EddnUploadEnabled);
        string saved = File.ReadAllText(path);
        Assert.DoesNotContain("EddnUseTestSchemas", saved);
        Assert.DoesNotContain("EddnEnvironment", saved);
    }

    [Fact]
    public void ExplicitLegacySchemaModeIsRemovedWhenPreferencesAreSaved()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string path = Path.Combine(temporaryDirectory, "ui-settings.json");
        File.WriteAllText(
            path,
            """
            {"NetworkPrivacy":{"EddnUseTestSchemas":false,"EddnEnvironment":"dev"}}
            """
        );

        var store = new NetworkPrivacySettingsStore(path);
        NetworkPrivacyPreferences preferences = store.Load();
        store.Save(preferences);

        Assert.DoesNotContain("EddnUseTestSchemas", File.ReadAllText(path));
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
        return new NetworkPrivacySettingsStore(Path.Combine(temporaryDirectory, "ui-settings.json"));
    }
}
