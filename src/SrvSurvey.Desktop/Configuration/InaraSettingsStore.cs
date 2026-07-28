using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class InaraSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public InaraSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public InaraPreferences Load()
    {
        var settings = documentStore.Load()["Inara"] as JsonObject;
        return new InaraPreferences(
            GetBoolean(settings, "UploadEnabled"),
            GetBoolean(settings, "DeveloperTestMode"));
    }

    public void Save(InaraPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["Inara"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Inara"] = settings;
            }

            root["Version"] = 1;
            settings["UploadEnabled"] = preferences.UploadEnabled;
            settings["DeveloperTestMode"] =
                preferences.DeveloperTestMode;
        });
    }

    private static bool GetBoolean(
        JsonObject? settings,
        string propertyName)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<bool>(out var result)
            && result;
    }
}

public sealed record InaraPreferences(
    bool UploadEnabled,
    bool DeveloperTestMode)
{
    public static InaraPreferences Default { get; } = new(
        UploadEnabled: false,
        DeveloperTestMode: false);
}
