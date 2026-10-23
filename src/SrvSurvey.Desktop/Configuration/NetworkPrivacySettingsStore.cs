using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class NetworkPrivacySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public NetworkPrivacySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    /// <summary>Restores explicit sharing consent with disabled defaults for absent or invalid values.</summary>
    public NetworkPrivacyPreferences Load()
    {
        var settings = documentStore.Load()["NetworkPrivacy"] as JsonObject;
        NetworkPrivacyPreferences defaults = NetworkPrivacyPreferences.Default;
        return new NetworkPrivacyPreferences(
            GetBoolean(settings, "EddnUploadEnabled", defaults.EddnUploadEnabled),
            GetBoolean(settings, "UploadGreenGasGiantCandidates", defaults.UploadGreenGasGiantCandidates),
            GetBoolean(settings, "UploadHumanSettlementGeometry", defaults.UploadHumanSettlementGeometry),
            GetBoolean(settings, "EdAstroUploadEnabled", defaults.EdAstroUploadEnabled)
        );
    }

    /// <summary>Saves sharing preferences while preserving unrelated settings and removing obsolete EDDN schema choices.</summary>
    public void Save(NetworkPrivacyPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["NetworkPrivacy"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["NetworkPrivacy"] = settings;
            }

            root["Version"] = 1;
            settings["EddnUploadEnabled"] = preferences.EddnUploadEnabled;
            settings["EdAstroUploadEnabled"] = preferences.EdAstroUploadEnabled;
            settings.Remove("EddnUseTestSchemas");
            settings.Remove("EddnEnvironment");
            settings["UploadGreenGasGiantCandidates"] = preferences.UploadGreenGasGiantCandidates;
            settings["UploadHumanSettlementGeometry"] = preferences.UploadHumanSettlementGeometry;
        });
    }

    private static bool GetBoolean(JsonObject? source, string propertyName, bool fallback)
    {
        return source?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : fallback;
    }
}

public sealed record NetworkPrivacyPreferences(
    bool EddnUploadEnabled,
    bool UploadGreenGasGiantCandidates,
    bool UploadHumanSettlementGeometry = false,
    bool EdAstroUploadEnabled = false
)
{
    public static NetworkPrivacyPreferences Default { get; } =
        new(EddnUploadEnabled: false, UploadGreenGasGiantCandidates: false, UploadHumanSettlementGeometry: false);
}
