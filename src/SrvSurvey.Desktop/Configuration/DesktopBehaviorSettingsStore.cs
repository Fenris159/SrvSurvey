using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class DesktopBehaviorSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public DesktopBehaviorSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public DesktopBehaviorPreferences Load()
    {
        var settings = documentStore.Load()["DesktopBehavior"] as JsonObject;
        return new DesktopBehaviorPreferences(
            GetBoolean(settings, "FocusGameOnStart", true),
            GetBoolean(settings, "FocusGameOnMinimize", true),
            GetBoolean(settings, "FocusGameAfterFsdJump", false),
            GetBoolean(settings, "MinimizeToTray", false));
    }

    public void Save(DesktopBehaviorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["DesktopBehavior"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["DesktopBehavior"] = settings;
            }

            settings["FocusGameOnStart"] = preferences.FocusGameOnStart;
            settings["FocusGameOnMinimize"] = preferences.FocusGameOnMinimize;
            settings["FocusGameAfterFsdJump"] =
                preferences.FocusGameAfterFsdJump;
            settings["MinimizeToTray"] = preferences.MinimizeToTray;
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

public sealed record DesktopBehaviorPreferences(
    bool FocusGameOnStart,
    bool FocusGameOnMinimize,
    bool FocusGameAfterFsdJump,
    bool MinimizeToTray);
