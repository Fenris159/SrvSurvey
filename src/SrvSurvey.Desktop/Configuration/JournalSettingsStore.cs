using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class JournalSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public JournalSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public JournalPreferences Load()
    {
        var settings = documentStore.Load()["Journal"] as JsonObject;
        return new JournalPreferences(GetString(settings, "Directory"));
    }

    public void Save(JournalPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            var settings = root["Journal"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Journal"] = settings;
            }

            root["Version"] = 1;
            settings["Directory"] = Normalize(preferences.Directory);
        });
    }

    private static string? GetString(JsonObject? settings, string propertyName)
    {
        return settings?[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? Normalize(result)
                : null;
    }

    private static string? Normalize(string? path)
    {
        var normalized = path?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public sealed record JournalPreferences(string? Directory);
