using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class StationInfoSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public StationInfoSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public StationInfoPreferences Load()
    {
        var settings = documentStore.Load()["StationInfo"] as JsonObject;
        return new StationInfoPreferences(
            settings?["AutoShow"] is JsonValue value
            && value.TryGetValue<bool>(out var autoShow)
                ? autoShow
                : StationInfoPreferences.Default.AutoShow);
    }

    public void Save(StationInfoPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["StationInfo"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["StationInfo"] = settings;
            }

            root["Version"] = 1;
            settings["AutoShow"] = preferences.AutoShow;
        });
    }
}

public sealed record StationInfoPreferences(bool AutoShow)
{
    public static StationInfoPreferences Default { get; } = new(
        AutoShow: true);
}
