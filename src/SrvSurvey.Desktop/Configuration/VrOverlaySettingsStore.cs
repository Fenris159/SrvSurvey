using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class VrOverlaySettingsStore
{
    private const string DefaultProfileId = "steamvr";
    private const string CustomProfileId = "custom-openvr";
    private readonly UiSettingsDocumentStore documentStore;

    public VrOverlaySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public VrOverlayPreferences Load()
    {
        var settings = documentStore.Load()["VirtualReality"] as JsonObject;
        string runtimeProcessName = GetString(settings, "RuntimeProcessName", "vrserver");
        return new VrOverlayPreferences(
            GetBoolean(settings, "Enabled", false),
            GetString(
                settings,
                "RuntimeProfileId",
                string.Equals(runtimeProcessName, "vrserver", StringComparison.OrdinalIgnoreCase)
                    ? DefaultProfileId
                    : CustomProfileId
            ),
            runtimeProcessName
        );
    }

    public void Save(VrOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["VirtualReality"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["VirtualReality"] = settings;
            }

            root["Version"] = 1;
            settings["Enabled"] = preferences.Enabled;
            settings["RuntimeProfileId"] = preferences.RuntimeProfileId;
            settings["RuntimeProcessName"] = preferences.RuntimeProcessName;
        });
    }

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result
            : fallback;
    }

    private static string GetString(JsonObject? settings, string propertyName, string fallback)
    {
        return
            settings?[propertyName] is JsonValue value
            && value.TryGetValue<string>(out string? result)
            && !string.IsNullOrWhiteSpace(result)
            ? result.Trim()
            : fallback;
    }
}

public sealed record VrOverlayPreferences(bool Enabled, string RuntimeProfileId, string RuntimeProcessName);
