using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Persists the cockpit views allowed to display the Firegroups overlay.</summary>
public sealed class FiregroupsOverlaySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    /// <summary>Uses the existing UI settings document for the current application profile.</summary>
    public FiregroupsOverlaySettingsStore(string path) => documentStore = new(path);

    /// <summary>Loads view preferences, defaulting missing or invalid values to main-cockpit visibility only.</summary>
    public FiregroupsOverlayPreferences Load()
    {
        var settings = documentStore.Load()["FiregroupsOverlay"] as JsonObject;
        FiregroupsOverlayPreferences defaults = FiregroupsOverlayPreferences.Default;
        return new(
            GetBoolean(settings, "ShowInLeftView", defaults.ShowInLeftView),
            GetBoolean(settings, "ShowInMainView", defaults.ShowInMainView),
            GetBoolean(settings, "ShowInRightView", defaults.ShowInRightView)
        );
    }

    /// <summary>Saves cockpit-view choices without replacing other overlay or application preferences.</summary>
    public void Save(FiregroupsOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            JsonObject settings = root["FiregroupsOverlay"] as JsonObject ?? [];
            root["FiregroupsOverlay"] = settings;
            settings["ShowInLeftView"] = preferences.ShowInLeftView;
            settings["ShowInMainView"] = preferences.ShowInMainView;
            settings["ShowInRightView"] = preferences.ShowInRightView;
            root["Version"] = 1;
        });
    }

    /// <summary>Accepts only stored booleans so older or malformed settings keep safe defaults.</summary>
    private static bool GetBoolean(JsonObject? settings, string name, bool fallback) =>
        settings?[name] is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : fallback;
}

/// <summary>Independent visibility choices for the left panel, main cockpit and right panel.</summary>
public sealed record FiregroupsOverlayPreferences(bool ShowInLeftView, bool ShowInMainView, bool ShowInRightView)
{
    /// <summary>Shows Firegroups only in the main cockpit unless the user enables a side panel.</summary>
    public static FiregroupsOverlayPreferences Default { get; } = new(false, true, false);

    /// <summary>Maps Elite's panel focus to the selected views and hides the overlay in other interfaces.</summary>
    public bool Allows(GuiFocus focus) =>
        focus switch
        {
            GuiFocus.ExternalPanel => ShowInLeftView,
            GuiFocus.NoFocus => ShowInMainView,
            GuiFocus.InternalPanel => ShowInRightView,
            _ => false,
        };
}
