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
        if (settings?["Directories"] is JsonArray directories)
        {
            string[] paths = NormalizePaths(
                directories.Select(node =>
                    node is JsonValue value && value.TryGetValue<string>(out string? path) ? path : null
                )
            );
            return new JournalPreferences(paths.FirstOrDefault(), paths.Length > 1 ? paths[1..] : null);
        }

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
            string[] paths = NormalizePaths(preferences.Directories);
            settings["Directory"] = paths.FirstOrDefault();
            settings["Directories"] = new JsonArray(paths.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());
        });
    }

    private static string? GetString(JsonObject? settings, string propertyName)
    {
        return settings?[propertyName] is JsonValue value && value.TryGetValue<string>(out string? result)
            ? Normalize(result)
            : null;
    }

    private static string? Normalize(string? path)
    {
        string? normalized = path?.Trim().Trim('"');
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string[] NormalizePaths(IEnumerable<string?> paths)
    {
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return paths.Select(Normalize).OfType<string>().Distinct(comparer).ToArray();
    }
}

public sealed record JournalPreferences(string? Directory, IReadOnlyList<string>? AdditionalDirectories = null)
{
    public IReadOnlyList<string> Directories =>
        Directory is null ? AdditionalDirectories ?? [] : [Directory, .. AdditionalDirectories ?? []];
}
