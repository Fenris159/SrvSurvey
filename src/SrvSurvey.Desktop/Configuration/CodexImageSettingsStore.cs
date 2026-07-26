using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class CodexImageSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;
    private readonly string defaultCacheDirectory;

    public CodexImageSettingsStore(
        string path,
        string defaultCacheDirectory)
    {
        documentStore = new UiSettingsDocumentStore(path);
        this.defaultCacheDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(defaultCacheDirectory)
                ? throw new ArgumentException(
                    "A default Codex image cache directory is required.",
                    nameof(defaultCacheDirectory))
                : defaultCacheDirectory);
    }

    public CodexImagePreferences Load()
    {
        var settings = documentStore.Load()["CodexImages"] as JsonObject;
        return new CodexImagePreferences(
            GetString(settings, "CacheDirectory") ?? defaultCacheDirectory,
            GetString(settings, "LocalFloraDirectory"),
            GetBoolean(settings, "PreDownload") ?? false);
    }

    public void Save(CodexImagePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["CodexImages"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["CodexImages"] = settings;
            }

            settings["CacheDirectory"] = preferences.CacheDirectory;
            settings["LocalFloraDirectory"] = preferences.LocalFloraDirectory;
            settings["PreDownload"] = preferences.PreDownload;
        });
    }

    private static bool? GetBoolean(JsonObject? root, string name)
    {
        return root?[name] is JsonValue value
            && value.TryGetValue<bool>(out var result)
                ? result
                : null;
    }

    private static string? GetString(JsonObject? root, string name)
    {
        return root?[name] is JsonValue value
            && value.TryGetValue<string>(out var result)
            && !string.IsNullOrWhiteSpace(result)
                ? result.Trim()
                : null;
    }
}

public sealed record CodexImagePreferences(
    string CacheDirectory,
    string? LocalFloraDirectory,
    bool PreDownload);
