using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class CombatSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public CombatSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public CombatPreferences Load()
    {
        var settings = documentStore.Load()["Combat"] as JsonObject;
        var defaults = CombatPreferences.Default;
        return new CombatPreferences(
            GetBoolean(
                settings,
                "AutoShowFootCombat",
                defaults.AutoShowFootCombat),
            GetBoolean(
                settings,
                "AutoShowMassacreMissions",
                defaults.AutoShowMassacreMissions),
            GetBoolean(
                settings,
                "SuppressForActiveBuildProjects",
                defaults.SuppressForActiveBuildProjects));
    }

    public void Save(CombatPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["Combat"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Combat"] = settings;
            }

            root["Version"] = 1;
            settings["AutoShowFootCombat"] = preferences.AutoShowFootCombat;
            settings["AutoShowMassacreMissions"] =
                preferences.AutoShowMassacreMissions;
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

public sealed record CombatPreferences(
    bool AutoShowFootCombat,
    bool AutoShowMassacreMissions,
    bool SuppressForActiveBuildProjects)
{
    public static CombatPreferences Default { get; } = new(
        AutoShowFootCombat: false,
        AutoShowMassacreMissions: false,
        SuppressForActiveBuildProjects: false);
}
