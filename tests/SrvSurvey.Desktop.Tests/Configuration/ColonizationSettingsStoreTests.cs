using System.Text.Json.Nodes;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class ColonizationSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-colonization-settings-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultsOffAndPersistsExplicitConsent()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);

        Assert.False(store.LoadEnabled());

        store.SaveEnabled(true);

        Assert.True(store.LoadEnabled());
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.True(root["Colonization"]?["Enabled"]?.GetValue<bool>());
    }

    [Fact]
    public void PreservesOtherUiSettings()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui.json");
        File.WriteAllText(path, "{\"Theme\":{\"Selected\":\"blue-dark\"}}");

        var store = new ColonizationSettingsStore(path);
        store.SaveEnabled(true);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("blue-dark",
            root["Theme"]?["Selected"]?.GetValue<string>());
    }

    [Fact]
    public void OverlayPreferencesUseLegacyDefaultsAndPersistOverrides()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);

        Assert.Equal(
            ColonizationOverlayPreferences.Default,
            store.LoadOverlayPreferences());

        var updated = ColonizationOverlayPreferences.Default with
        {
            AutoShow = false,
            ShowFleetCarrierDelta = true,
            InlineFleetCarrierCargo = true,
            CollapseCoveredGroups = false,
            HighlightAlmostCoveredFleetCarrierLoads = true,
        };
        store.SaveOverlayPreferences(updated);

        Assert.Equal(updated, store.LoadOverlayPreferences());
        Assert.False(store.LoadEnabled());
    }

    [Fact]
    public void SavingConsentPreservesOverlayPreferences()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);
        var preferences = ColonizationOverlayPreferences.Default with
        {
            ShowOnRightPanel = false,
        };
        store.SaveOverlayPreferences(preferences);

        store.SaveEnabled(true);

        Assert.Equal(preferences, store.LoadOverlayPreferences());
    }

    [Fact]
    public void FleetCarrierCargoSyncDefaultsOffAndPreservesConsent()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);

        Assert.False(store.LoadFleetCarrierCargoSyncEnabled());

        store.SaveEnabled(true);
        store.SaveFleetCarrierCargoSyncEnabled(true);

        Assert.True(store.LoadEnabled());
        Assert.True(store.LoadFleetCarrierCargoSyncEnabled());
        Assert.Equal(
            ColonizationOverlayPreferences.Default,
            store.LoadOverlayPreferences());
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
