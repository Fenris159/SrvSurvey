using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayBehaviorSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayBehaviorSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public OverlayBehaviorPreferences Load()
    {
        var settings = documentStore.Load()["OverlayBehavior"] as JsonObject;
        return new OverlayBehaviorPreferences(
            GetBoolean(settings, "KeepWhenGameLosesFocus", false),
            GetBoolean(settings, "HideInDominatorSuit", false),
            GetBoolean(settings, "HideInMaverickSuit", false));
    }

    public void Save(OverlayBehaviorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["OverlayBehavior"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayBehavior"] = settings;
            }

            root["Version"] = 1;
            settings["KeepWhenGameLosesFocus"] =
                preferences.KeepWhenGameLosesFocus;
            settings["HideInDominatorSuit"] = preferences.HideInDominatorSuit;
            settings["HideInMaverickSuit"] = preferences.HideInMaverickSuit;
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

public sealed record OverlayBehaviorPreferences(
    bool KeepWhenGameLosesFocus,
    bool HideInDominatorSuit,
    bool HideInMaverickSuit);
