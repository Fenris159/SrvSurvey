using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class GuardianOverlaySettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public GuardianOverlaySettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public GuardianOverlayPreferences Load()
    {
        var settings = documentStore.Load()["GuardianOverlays"] as JsonObject;
        var defaults = GuardianOverlayPreferences.Default;
        return new GuardianOverlayPreferences(
            GetBoolean(
                settings,
                "EnableGuardianSites",
                defaults.EnableGuardianSites),
            GetBoolean(
                settings,
                "AutoShowGuardianSummary",
                defaults.AutoShowGuardianSummary),
            GetBoolean(
                settings,
                "AutoShowRamTah",
                defaults.AutoShowRamTah),
            GetBoolean(
                settings,
                "SuppressForActiveBuildProjects",
                defaults.SuppressForActiveBuildProjects));
    }

    public void Save(GuardianOverlayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["GuardianOverlays"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["GuardianOverlays"] = settings;
            }

            root["Version"] = 1;
            settings["EnableGuardianSites"] = preferences.EnableGuardianSites;
            settings["AutoShowGuardianSummary"] =
                preferences.AutoShowGuardianSummary;
            settings["AutoShowRamTah"] = preferences.AutoShowRamTah;
            settings["SuppressForActiveBuildProjects"] =
                preferences.SuppressForActiveBuildProjects;
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

public sealed record GuardianOverlayPreferences(
    bool EnableGuardianSites,
    bool AutoShowGuardianSummary,
    bool AutoShowRamTah,
    bool SuppressForActiveBuildProjects)
{
    public static GuardianOverlayPreferences Default { get; } = new(
        EnableGuardianSites: true,
        AutoShowGuardianSummary: true,
        AutoShowRamTah: true,
        SuppressForActiveBuildProjects: false);
}
