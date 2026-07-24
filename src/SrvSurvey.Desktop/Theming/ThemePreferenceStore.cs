using System.Text.Json;

namespace SrvSurvey.Desktop.Theming;

public sealed class ThemePreferenceStore
{
    private const int CurrentVersion = 1;

    public ThemePreferenceStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? GetDefaultSettingsPath();
    }

    public string SettingsPath { get; }

    public string? LoadThemeKey()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(SettingsPath);
            var settings = JsonSerializer.Deserialize<UiSettings>(stream);
            return settings?.Version == CurrentVersion ? settings.Theme : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return null;
        }
    }

    public void SaveThemeKey(string themeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeKey);

        var directory = Path.GetDirectoryName(SettingsPath)
            ?? throw new InvalidOperationException("The UI settings path has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = $"{SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    new UiSettings(CurrentVersion, themeKey),
                    new JsonSerializerOptions { WriteIndented = true });
            }

            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetDefaultSettingsPath()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(applicationData))
        {
            applicationData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(
            applicationData,
            "SrvSurvey",
            "cross-platform-ui.json");
    }

    private sealed record UiSettings(int Version, string Theme);
}
