using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class BoxelSurveyStatsSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public BoxelSurveyStatsSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public BoxelSurveyStatsPreferences Load()
    {
        var defaults = BoxelSurveyStatsPreferences.Default;
        var settings = documentStore.Load()["BoxelSurveyStats"] as JsonObject;
        return new BoxelSurveyStatsPreferences(
            GetInt32(
                settings,
                "MinSystemsForAverages",
                defaults.MinSystemsForAverages,
                1,
                1000),
            GetInt32(
                settings,
                "MinSystemsForExport",
                defaults.MinSystemsForExport,
                1,
                1000),
            GetBoolean(
                settings,
                "TreatNavBeaconAsFullyScanned",
                defaults.TreatNavBeaconAsFullyScanned));
    }

    public void Save(BoxelSurveyStatsPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["BoxelSurveyStats"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["BoxelSurveyStats"] = settings;
            }

            root["Version"] = 1;
            settings["MinSystemsForAverages"] = Clamp(preferences.MinSystemsForAverages);
            settings["MinSystemsForExport"] = Clamp(preferences.MinSystemsForExport);
            settings["TreatNavBeaconAsFullyScanned"] =
                preferences.TreatNavBeaconAsFullyScanned;
        });
    }

    private static int Clamp(int value) => Math.Clamp(value, 1, 1000);

    private static bool GetBoolean(JsonObject? settings, string propertyName, bool fallback)
        => settings?[propertyName] is JsonValue value && value.TryGetValue<bool>(out var result)
            ? result
            : fallback;

    private static int GetInt32(
        JsonObject? settings,
        string propertyName,
        int fallback,
        int minimum,
        int maximum)
    {
        if (settings?[propertyName] is not JsonValue value
            || !value.TryGetValue<int>(out var number))
        {
            return fallback;
        }

        return Math.Clamp(number, minimum, maximum);
    }
}

public sealed record BoxelSurveyStatsPreferences(
    int MinSystemsForAverages,
    int MinSystemsForExport,
    bool TreatNavBeaconAsFullyScanned)
{
    public static BoxelSurveyStatsPreferences Default { get; } = new(
        MinSystemsForAverages: 10,
        MinSystemsForExport: 5,
        TreatNavBeaconAsFullyScanned: false);
}
