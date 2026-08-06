using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class ColonizationSettingsStore
{
    private const string ColonizationSectionKey = "Colonization";
    private const string VersionKey = "Version";

    private readonly UiSettingsDocumentStore documentStore;

    public ColonizationSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public bool LoadEnabled()
    {
        var root = documentStore.Load();
        return root[ColonizationSectionKey] is JsonObject colonization
            && colonization["Enabled"] is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public ColonizationOverlayPreferences LoadOverlayPreferences()
    {
        var root = documentStore.Load();
        var colonization = root[ColonizationSectionKey] as JsonObject;
        var overlay = colonization?["Overlay"] as JsonObject;
        var defaults = ColonizationOverlayPreferences.Default;
        return new ColonizationOverlayPreferences(
            GetBoolean(overlay, "AutoShow", defaults.AutoShow),
            GetBoolean(
                overlay,
                "ShowOnRightPanel",
                defaults.ShowOnRightPanel),
            GetBoolean(
                overlay,
                "ShowFleetCarrierCargo",
                defaults.ShowFleetCarrierCargo),
            GetBoolean(
                overlay,
                "ShowFleetCarrierDelta",
                defaults.ShowFleetCarrierDelta),
            GetBoolean(
                overlay,
                "InlineFleetCarrierCargo",
                defaults.InlineFleetCarrierCargo),
            GetBoolean(
                overlay,
                "CollapseCoveredGroups",
                defaults.CollapseCoveredGroups),
            GetBoolean(
                overlay,
                "HighlightAlmostCoveredFleetCarrierLoads",
                defaults.HighlightAlmostCoveredFleetCarrierLoads));
    }

    public bool LoadFleetCarrierCargoSyncEnabled()
    {
        var root = documentStore.Load();
        return root[ColonizationSectionKey] is JsonObject colonization
            && colonization["FleetCarrierCargoSyncEnabled"]
                is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public bool LoadShipCargoPublishingEnabled()
    {
        var root = documentStore.Load();
        return root[ColonizationSectionKey] is JsonObject colonization
            && colonization["ShipCargoPublishingEnabled"]
                is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value;
    }

    public IReadOnlyList<ColonizationBuildSiteRepairVisit>
        LoadBuildSiteRepairVisits()
    {
        var root = documentStore.Load();
        var visits = root[ColonizationSectionKey]?["BuildSiteRepairVisits"]
            as JsonArray;
        if (visits is null)
        {
            return [];
        }

        var loaded = new List<ColonizationBuildSiteRepairVisit>();
        foreach (var item in visits.OfType<JsonObject>())
        {
            if (item["MarketId"] is not JsonValue marketValue
                || !marketValue.TryGetValue<long>(out var marketId)
                || marketId <= 0
                || item["StationKey"] is not JsonValue stationValue
                || !stationValue.TryGetValue<string>(out var stationKey)
                || string.IsNullOrWhiteSpace(stationKey))
            {
                continue;
            }

            var visit = new ColonizationBuildSiteRepairVisit(
                marketId,
                stationKey.Trim().ToLowerInvariant());
            loaded.RemoveAll(existing => existing == visit);
            loaded.Add(visit);
        }

        return loaded.TakeLast(50).ToArray();
    }

    public void SaveEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var colonization = root[ColonizationSectionKey] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root[ColonizationSectionKey] = colonization;
            }

            root[VersionKey] = 1;
            colonization["Enabled"] = enabled;
        });
    }

    public void SaveOverlayPreferences(
        ColonizationOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var colonization = root[ColonizationSectionKey] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root[ColonizationSectionKey] = colonization;
            }

            var overlay = colonization["Overlay"] as JsonObject;
            if (overlay is null)
            {
                overlay = [];
                colonization["Overlay"] = overlay;
            }

            root[VersionKey] = 1;
            overlay["AutoShow"] = preferences.AutoShow;
            overlay["ShowOnRightPanel"] = preferences.ShowOnRightPanel;
            overlay["ShowFleetCarrierCargo"] =
                preferences.ShowFleetCarrierCargo;
            overlay["ShowFleetCarrierDelta"] =
                preferences.ShowFleetCarrierDelta;
            overlay["InlineFleetCarrierCargo"] =
                preferences.InlineFleetCarrierCargo;
            overlay["CollapseCoveredGroups"] =
                preferences.CollapseCoveredGroups;
            overlay["HighlightAlmostCoveredFleetCarrierLoads"] =
                preferences.HighlightAlmostCoveredFleetCarrierLoads;
        });
    }

    public void SaveFleetCarrierCargoSyncEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var colonization = root[ColonizationSectionKey] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root[ColonizationSectionKey] = colonization;
            }

            root[VersionKey] = 1;
            colonization["FleetCarrierCargoSyncEnabled"] = enabled;
        });
    }

    public void SaveShipCargoPublishingEnabled(bool enabled)
    {
        documentStore.Update(root =>
        {
            var colonization = root[ColonizationSectionKey] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root[ColonizationSectionKey] = colonization;
            }

            root[VersionKey] = 1;
            colonization["ShipCargoPublishingEnabled"] = enabled;
        });
    }

    public void SaveBuildSiteRepairVisits(
        IEnumerable<ColonizationBuildSiteRepairVisit> visits)
    {
        ArgumentNullException.ThrowIfNull(visits);
        var normalized = visits
            .Where(visit => visit.MarketId > 0
                && !string.IsNullOrWhiteSpace(visit.StationKey))
            .Select(visit => visit with
            {
                StationKey = visit.StationKey.Trim().ToLowerInvariant(),
            })
            .Distinct()
            .TakeLast(50)
            .ToArray();
        documentStore.Update(root =>
        {
            var colonization = root[ColonizationSectionKey] as JsonObject;
            if (colonization is null)
            {
                colonization = [];
                root[ColonizationSectionKey] = colonization;
            }

            root[VersionKey] = 1;
            colonization["BuildSiteRepairVisits"] = new JsonArray(
                normalized.Select(visit => new JsonObject
                {
                    ["MarketId"] = visit.MarketId,
                    ["StationKey"] = visit.StationKey,
                }).ToArray<JsonNode?>());
        });
    }

    private static bool GetBoolean(
        JsonObject? source,
        string propertyName,
        bool fallback)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }
}

public sealed record ColonizationBuildSiteRepairVisit(
    long MarketId,
    string StationKey);

public sealed record ColonizationOverlayPreferences(
    bool AutoShow,
    bool ShowOnRightPanel,
    bool ShowFleetCarrierCargo,
    bool ShowFleetCarrierDelta,
    bool InlineFleetCarrierCargo,
    bool CollapseCoveredGroups,
    bool HighlightAlmostCoveredFleetCarrierLoads)
{
    public static ColonizationOverlayPreferences Default { get; } = new(
        AutoShow: true,
        ShowOnRightPanel: true,
        ShowFleetCarrierCargo: true,
        ShowFleetCarrierDelta: false,
        InlineFleetCarrierCargo: false,
        CollapseCoveredGroups: true,
        HighlightAlmostCoveredFleetCarrierLoads: false);
}
