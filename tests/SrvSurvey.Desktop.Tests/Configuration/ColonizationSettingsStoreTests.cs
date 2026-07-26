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

    [Fact]
    public void ShipCargoPublishingDefaultsOffAndPersistsOptIn()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);

        Assert.False(store.LoadShipCargoPublishingEnabled());

        store.SaveEnabled(true);
        store.SaveShipCargoPublishingEnabled(true);

        Assert.True(store.LoadEnabled());
        Assert.True(store.LoadShipCargoPublishingEnabled());
        Assert.False(store.LoadFleetCarrierCargoSyncEnabled());
    }

    [Fact]
    public void BuildSiteRepairCachePersistsLatestFiftyUniqueVisits()
    {
        var path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);
        var visits = Enumerable.Range(1, 52)
            .Select(index => new ColonizationBuildSiteRepairVisit(
                4_300_000_000 + index,
                $" Station {index} "))
            .Append(new ColonizationBuildSiteRepairVisit(
                4_300_000_052,
                "STATION 52"));

        store.SaveBuildSiteRepairVisits(visits);

        var loaded = store.LoadBuildSiteRepairVisits();
        Assert.Equal(50, loaded.Count);
        Assert.Equal(4_300_000_003, loaded[0].MarketId);
        Assert.Equal("station 52", loaded[^1].StationKey);
        Assert.True(store.LoadBuildSiteRepairVisits().SequenceEqual(loaded));
    }

    [Fact]
    public void BuildSiteRepairCacheIgnoresMalformedEntries()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ui.json");
        File.WriteAllText(
            path,
            """
            {"Colonization":{"BuildSiteRepairVisits":[
              {"MarketId":0,"StationKey":"invalid"},
              {"MarketId":4300000001},
              {"MarketId":4300000002,"StationKey":"Valid Port"}
            ]}}
            """);
        var store = new ColonizationSettingsStore(path);

        var visit = Assert.Single(store.LoadBuildSiteRepairVisits());

        Assert.Equal(4_300_000_002, visit.MarketId);
        Assert.Equal("valid port", visit.StationKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
