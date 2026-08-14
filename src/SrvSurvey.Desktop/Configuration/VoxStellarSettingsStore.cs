using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class VoxStellarSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public VoxStellarSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public VoxStellarPreferences Load()
    {
        var settings = documentStore.Load()["VoxStellar"] as JsonObject;
        return new VoxStellarPreferences(
            JournalUploadEnabled: GetBoolean(
                settings,
                "JournalUploadEnabled",
                fallback: false));
    }

    public void Save(VoxStellarPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["VoxStellar"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["VoxStellar"] = settings;
            }

            root["Version"] = 1;
            settings["JournalUploadEnabled"] =
                preferences.JournalUploadEnabled;
        });
    }

    private static bool GetBoolean(
        JsonObject? source,
        string propertyName,
        bool fallback)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : fallback;
    }
}

public sealed record VoxStellarPreferences(bool JournalUploadEnabled)
{
    public static VoxStellarPreferences Default { get; } = new(false);
}
