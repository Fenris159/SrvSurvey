using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class NetworkPrivacySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public NetworkPrivacySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public NetworkPrivacyPreferences Load()
    {
        var settings = documentStore.Load()["NetworkPrivacy"] as JsonObject;
        var defaults = NetworkPrivacyPreferences.Default;
        return new NetworkPrivacyPreferences(
            GetBoolean(
                settings,
                "EddnUploadEnabled",
                defaults.EddnUploadEnabled),
            NormalizeEnvironment(
                GetString(settings, "EddnEnvironment")
                    ?? defaults.EddnEnvironment),
            GetBoolean(
                settings,
                "UploadGreenGasGiantCandidates",
                defaults.UploadGreenGasGiantCandidates),
            GetBoolean(
                settings,
                "UploadHumanSettlementGeometry",
                defaults.UploadHumanSettlementGeometry));
    }

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
            settings["EddnEnvironment"] = NormalizeEnvironment(
                preferences.EddnEnvironment);
            settings["UploadGreenGasGiantCandidates"] =
                preferences.UploadGreenGasGiantCandidates;
            settings["UploadHumanSettlementGeometry"] =
                preferences.UploadHumanSettlementGeometry;
        });
    }

    public static string NormalizeEnvironment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "live" => "live",
            "beta" => "beta",
            "dev" => "dev",
            _ => "live",
        };
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

    private static string? GetString(JsonObject? source, string propertyName)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }
}

public sealed record NetworkPrivacyPreferences(
    bool EddnUploadEnabled,
    string EddnEnvironment,
    bool UploadGreenGasGiantCandidates,
    bool UploadHumanSettlementGeometry = false)
{
    public static NetworkPrivacyPreferences Default { get; } = new(
        EddnUploadEnabled: false,
        EddnEnvironment: "live",
        UploadGreenGasGiantCandidates: false,
        UploadHumanSettlementGeometry: false);
}
