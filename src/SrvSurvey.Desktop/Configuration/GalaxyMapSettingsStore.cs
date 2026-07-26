using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class GalaxyMapSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public GalaxyMapSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public GalaxyMapPreferences Load()
    {
        var settings = documentStore.Load()["GalaxyMap"] as JsonObject;
        return new GalaxyMapPreferences(
            GetBoolean(settings, "AutoShow", true),
            GetBoolean(settings, "ShowFactions", true));
    }

    public void Save(GalaxyMapPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["GalaxyMap"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["GalaxyMap"] = settings;
            }

            root["Version"] = 1;
            settings["AutoShow"] = preferences.AutoShow;
            settings["ShowFactions"] = preferences.ShowFactions;
        });
    }

    private static bool GetBoolean(
        JsonObject? settings,
        string propertyName,
        bool fallback)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }
}

public sealed record GalaxyMapPreferences(
    bool AutoShow,
    bool ShowFactions);
