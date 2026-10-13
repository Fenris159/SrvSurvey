using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class NotificationSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public NotificationSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public NotificationPreferences Load()
    {
        NotificationPreferences defaults = NotificationPreferences.Default;
        var settings = documentStore.Load()["Notifications"] as JsonObject;
        return new NotificationPreferences(
            GetBoolean(settings, "Enabled", defaults.Enabled),
            GetBoolean(settings, "MaterialCountAfterPickup", defaults.MaterialCountAfterPickup),
            GetBoolean(settings, "CargoMissionRemaining", defaults.CargoMissionRemaining),
            GetBoolean(settings, "CurrentBoxelSearchStatus", defaults.CurrentBoxelSearchStatus),
            GetBoolean(settings, "ShowNextBoxelToSearch", defaults.ShowNextBoxelToSearch),
            GetBoolean(settings, "ShowScreenshot", defaults.ShowScreenshot)
        )
        {
            VisibleMessageLimit = GetVisibleMessageLimit(settings),
        };
    }

    public void Save(NotificationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["Notifications"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Notifications"] = settings;
            }

            root["Version"] = 1;
            settings["Enabled"] = preferences.Enabled;
            settings["MaterialCountAfterPickup"] = preferences.MaterialCountAfterPickup;
            settings["CargoMissionRemaining"] = preferences.CargoMissionRemaining;
            settings["CurrentBoxelSearchStatus"] = preferences.CurrentBoxelSearchStatus;
            settings["ShowNextBoxelToSearch"] = preferences.ShowNextBoxelToSearch;
            settings["ShowScreenshot"] = preferences.ShowScreenshot;
            settings["VisibleMessageLimit"] = Math.Clamp(
                preferences.VisibleMessageLimit,
                NotificationPreferences.MinimumVisibleMessageLimit,
                NotificationPreferences.MaximumVisibleMessageLimit
            );
        });
    }

    /// <summary>Reads the persisted row limit, using three for older settings and bounding malformed numeric values.</summary>
    private static int GetVisibleMessageLimit(JsonObject? settings)
    {
        return settings?["VisibleMessageLimit"] is JsonValue value && value.TryGetValue<int>(out int result)
            ? Math.Clamp(
                result,
                NotificationPreferences.MinimumVisibleMessageLimit,
                NotificationPreferences.MaximumVisibleMessageLimit
            )
            : NotificationPreferences.Default.VisibleMessageLimit;
    }

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result
            : fallback;
    }
}

public sealed record NotificationPreferences(
    bool Enabled,
    bool MaterialCountAfterPickup,
    bool CargoMissionRemaining,
    bool CurrentBoxelSearchStatus,
    bool ShowNextBoxelToSearch,
    bool ShowScreenshot
)
{
    /// <summary>The smallest permitted number of visible notification messages.</summary>
    public const int MinimumVisibleMessageLimit = 1;

    /// <summary>The largest permitted number of visible notification messages.</summary>
    public const int MaximumVisibleMessageLimit = 20;

    /// <summary>The maximum number of newest messages displayed at once; existing profiles default to three.</summary>
    public int VisibleMessageLimit { get; init; } = 3;

    public static NotificationPreferences Default { get; } =
        new(
            Enabled: true,
            MaterialCountAfterPickup: true,
            CargoMissionRemaining: true,
            CurrentBoxelSearchStatus: true,
            ShowNextBoxelToSearch: true,
            ShowScreenshot: true
        );
}
