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
            GetBoolean(settings, "MinimizeToTray", false),
            GetString(settings, "PreferredMonitor"),
            ApplicationWindowScaleCatalog.Normalize(
                GetInt32(
                    settings,
                    "ApplicationWindowScalePercent",
                    ApplicationWindowScaleCatalog.DefaultPercent)),
            GetApplicationWindowPosition(settings),
            GetBoolean(settings, "ReduceMotion", false));
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
            if (string.IsNullOrWhiteSpace(preferences.PreferredMonitorId))
            {
                settings.Remove("PreferredMonitor");
            }
            else
            {
                settings["PreferredMonitor"] = preferences.PreferredMonitorId;
            }

            settings["ApplicationWindowScalePercent"] =
                ApplicationWindowScaleCatalog.Normalize(
                    preferences.ApplicationWindowScalePercent);
            settings["ReduceMotion"] = preferences.ReduceMotion;
            if (preferences.LastApplicationWindowPosition is not { } position)
            {
                settings.Remove("ApplicationWindowPosition");
            }
            else
            {
                settings["ApplicationWindowPosition"] = new JsonObject
                {
                    ["X"] = position.X,
                    ["Y"] = position.Y,
                    ["Monitor"] = position.MonitorId,
                };
            }
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

    private static int GetInt32(
        JsonObject? settings,
        string propertyName,
        int fallback)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? result
                : fallback;
    }

    private static string? GetString(
        JsonObject? settings,
        string propertyName)
    {
        if (settings?[propertyName] is not JsonValue value
            || !value.TryGetValue<string>(out var result)
            || string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return result.Trim();
    }

    private static ApplicationWindowPosition? GetApplicationWindowPosition(
        JsonObject? settings)
    {
        if (settings?["ApplicationWindowPosition"] is not JsonObject position
            || position["X"] is not JsonValue xValue
            || !xValue.TryGetValue<int>(out var x)
            || position["Y"] is not JsonValue yValue
            || !yValue.TryGetValue<int>(out var y))
        {
            return null;
        }

        return new ApplicationWindowPosition(
            x,
            y,
            GetString(position, "Monitor"));
    }
}

public sealed record DesktopBehaviorPreferences(
    bool FocusGameOnStart,
    bool FocusGameOnMinimize,
    bool FocusGameAfterFsdJump,
    bool MinimizeToTray,
    string? PreferredMonitorId = null,
    int ApplicationWindowScalePercent =
        ApplicationWindowScaleCatalog.DefaultPercent,
    ApplicationWindowPosition? LastApplicationWindowPosition = null,
    bool ReduceMotion = false);

public sealed record ApplicationWindowPosition(
    int X,
    int Y,
    string? MonitorId);
