using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class BiologyPredictionsSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public BiologyPredictionsSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public BiologyPredictionsPreferences Load()
    {
        var settings = documentStore.Load()["BiologyPredictions"] as JsonObject;
        var defaults = BiologyPredictionsPreferences.Default;
        return new BiologyPredictionsPreferences(
            GetBoolean(
                settings,
                "CurrentBodyOnly",
                defaults.CurrentBodyOnly),
            GetRowSize(settings, defaults.RowSize));
    }

    public void Save(BiologyPredictionsPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["BiologyPredictions"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["BiologyPredictions"] = settings;
            }

            root["Version"] = 1;
            settings["CurrentBodyOnly"] = preferences.CurrentBodyOnly;
            settings["RowSize"] = Math.Clamp(preferences.RowSize, 1, 3);
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

    private static int GetRowSize(
        JsonObject? settings,
        int fallback)
    {
        return settings?["RowSize"] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? Math.Clamp(result, 1, 3)
                : fallback;
    }
}

public sealed record BiologyPredictionsPreferences(
    bool CurrentBodyOnly,
    int RowSize)
{
    public static BiologyPredictionsPreferences Default { get; } = new(
        CurrentBodyOnly: false,
        RowSize: 2);
}
