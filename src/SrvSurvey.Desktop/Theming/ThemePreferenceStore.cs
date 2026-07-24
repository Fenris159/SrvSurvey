using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Theming;

public sealed class ThemePreferenceStore
{
    private const int CurrentVersion = 1;
    private readonly UiSettingsDocumentStore documentStore;

    public ThemePreferenceStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? GetDefaultSettingsPath();
        documentStore = new UiSettingsDocumentStore(SettingsPath);
    }

    public string SettingsPath { get; }

    public string? LoadThemeKey()
    {
        var settings = documentStore.Load();
        if (settings["Version"] is not JsonValue version
            || !version.TryGetValue<int>(out var versionNumber)
            || versionNumber != CurrentVersion
            || settings["Theme"] is not JsonValue theme
            || !theme.TryGetValue<string>(out var themeKey))
        {
            return null;
        }

        return themeKey;
    }

    public void SaveThemeKey(string themeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeKey);

        documentStore.Update(settings =>
        {
            settings["Version"] = CurrentVersion;
            settings["Theme"] = themeKey;
        });
    }

    private static string GetDefaultSettingsPath()
    {
        return AppDataPaths.ResolveCurrent().UiSettingsPath;
    }
}
