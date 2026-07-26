using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Configuration;

public sealed class LocalizationSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;
    private readonly string legacySettingsPath;

    public LocalizationSettingsStore(
        string uiSettingsPath,
        string dataDirectory)
    {
        documentStore = new UiSettingsDocumentStore(uiSettingsPath);
        legacySettingsPath = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "settings.json");
    }

    public string Load()
    {
        var root = documentStore.Load();
        if (root["Localization"] is JsonObject settings
            && settings["Language"] is JsonValue language
            && language.TryGetValue<string>(out var selected))
        {
            return LocalizationCatalog.NormalizeLanguage(selected);
        }

        if (!File.Exists(legacySettingsPath))
        {
            return "en";
        }

        try
        {
            var legacy = JsonNode.Parse(File.ReadAllText(legacySettingsPath))
                as JsonObject;
            return LocalizationCatalog.NormalizeLanguage(
                legacy?["lang"]?.GetValue<string>());
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
        {
            return "en";
        }
    }

    public void Save(string language)
    {
        var normalized = LocalizationCatalog.NormalizeLanguage(language);
        documentStore.Update(root =>
        {
            var settings = root["Localization"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["Localization"] = settings;
            }

            root["Version"] = 1;
            settings["Language"] = normalized;
        });
    }

    public static string ResolveCurrent(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new LocalizationSettingsStore(
                paths.UiSettingsPath,
                paths.DataDirectory)
            .Load();
    }
}
