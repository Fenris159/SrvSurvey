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
    public void StealthModePersistsIndependentlyAndFailedSavingKeepsItOff()
    {
        NetworkPrivacySettingsStore store = CreateStore();
        var model = new NetworkPrivacyViewModel(store);
        Assert.False(model.StealthModeEnabled);
        model.StealthModeEnabled = true;
        Assert.True(model.UploadPrivacy.Enabled);
        Assert.True(new NetworkPrivacyViewModel(store).StealthModeEnabled);
        Assert.False(model.EddnUploadEnabled);
        Assert.False(model.EdAstroUploadEnabled);
        model.RefreshStealthStatus();
        Assert.Contains("held locally", model.StealthStatus, StringComparison.Ordinal);
        model.StealthModeEnabled = false;
        Assert.False(model.UploadPrivacy.Enabled);
        Assert.Contains("off", model.StealthStatus, StringComparison.Ordinal);
        string blocked = Path.Combine(temporaryDirectory, "blocked");
        File.WriteAllText(blocked, "occupied");
        var unwritable = new NetworkPrivacyViewModel(
            new NetworkPrivacySettingsStore(Path.Combine(blocked, "settings.json"))
        );
        unwritable.StealthModeEnabled = true;
        Assert.False(unwritable.StealthModeEnabled);
        Assert.False(unwritable.UploadPrivacy.Enabled);
        Assert.Contains("could not be saved", unwritable.StatusMessage, StringComparison.Ordinal);
    }

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

    /// <summary>Verifies ed astro consent is independent persists and rolls back runtime failure.</summary>
    [Fact]
    public void EdAstroConsentIsIndependentPersistsAndRollsBackRuntimeFailure()
    {
        NetworkPrivacySettingsStore store = CreateStore();
        var vm = new NetworkPrivacyViewModel(store);
        Assert.False(vm.EdAstroUploadEnabled);
        Assert.Equal("EDAstro sharing is disabled.", vm.EdAstroConsentSummary);
        var changes = new List<bool>();
        vm.EdAstroUploadEnabledChanged += changes.Add;
        Assert.True(vm.TrySetEdAstroUploadEnabled(true));
        Assert.True(vm.TrySetEdAstroUploadEnabled(true));
        Assert.True(store.Load().EdAstroUploadEnabled);
        Assert.False(store.Load().EddnUploadEnabled);
        Assert.Contains("enabled", vm.EdAstroConsentSummary, StringComparison.Ordinal);
        Assert.Equal([true], changes);
        Assert.True(vm.TrySetEdAstroUploadEnabled(false));
        vm.EdAstroUploadEnabledChanged += enabled =>
        {
            if (enabled)
            {
                throw new InvalidOperationException("unavailable runtime");
            }
        };
        Assert.False(vm.TrySetEdAstroUploadEnabled(true));
        Assert.False(store.Load().EdAstroUploadEnabled);
        Assert.Contains("previous choice was restored", vm.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>Verifies ed astro failed save keeps consent disabled.</summary>
    [Fact]
    public void EdAstroFailedSaveKeepsConsentDisabled()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string blocked = Path.Combine(temporaryDirectory, "blocked");
        File.WriteAllText(blocked, "occupied");
        var vm = new NetworkPrivacyViewModel(new NetworkPrivacySettingsStore(Path.Combine(blocked, "settings.json")));
        vm.EdAstroUploadEnabled = true;
        Assert.False(vm.EdAstroUploadEnabled);
        Assert.Contains("could not be saved", vm.StatusMessage, StringComparison.Ordinal);
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
