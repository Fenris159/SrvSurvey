using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Configuration;

public sealed class ScreenshotProcessingSettingsStore
{
    private const string SettingsKey = "Screenshots";
    private const string SourceFolderKey = nameof(ScreenshotProcessingPreferences.SourceFolder);
    private const string TargetFolderKey = nameof(ScreenshotProcessingPreferences.TargetFolder);
    private readonly UiSettingsDocumentStore documentStore;

    public ScreenshotProcessingSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public ScreenshotProcessingPreferences Load(string? journalDirectory = null) =>
        LoadWithFolderModes(journalDirectory).Preferences;

    internal (
        ScreenshotProcessingPreferences Preferences,
        bool AutomaticSource,
        bool AutomaticTarget
    ) LoadWithFolderModes(string? journalDirectory)
    {
        var defaults = ScreenshotProcessingPreferences.CreateDefaults(journalDirectory);
        var settings = documentStore.Load()[SettingsKey] as JsonObject;
        string? source = GetString(settings, SourceFolderKey);
        string? target = GetString(settings, TargetFolderKey);
        bool automaticSource = UsesAutomaticSource(settings);
        var preferences = new ScreenshotProcessingPreferences(
            GetBoolean(settings, "Enabled") ?? defaults.Enabled,
            GetBoolean(settings, "AddBanner") ?? defaults.AddBanner,
            GetBoolean(settings, "DeleteOriginal") ?? defaults.DeleteOriginal,
            GetBoolean(settings, "UseGuardianAerialFolder") ?? defaults.UseGuardianAerialFolder,
            automaticSource ? defaults.SourceFolder : source ?? defaults.SourceFolder,
            target ?? defaults.TargetFolder,
            GetBoolean(settings, "RotateAlphaAerial") ?? defaults.RotateAlphaAerial,
            GetBannerColor(settings?["BannerColor"], defaults.BannerColor),
            GetBoolean(settings, "BannerLocalTime") ?? defaults.BannerLocalTime,
            GetDouble(settings, "AerialAltitudeAlpha") ?? defaults.AerialAltitudeAlpha,
            GetDouble(settings, "AerialAltitudeBeta") ?? defaults.AerialAltitudeBeta,
            GetDouble(settings, "AerialAltitudeGamma") ?? defaults.AerialAltitudeGamma
        );
        return (preferences, automaticSource, target is null);
    }

    internal string PinOutputFolder(string fallbackFolder)
    {
        string selected = fallbackFolder;
        documentStore.Update(root =>
        {
            var settings = root[SettingsKey] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root[SettingsKey] = settings;
            }
            string? existing = GetString(settings, TargetFolderKey);
            selected = existing is not null && Path.IsPathFullyQualified(existing) ? existing : fallbackFolder;
            settings[TargetFolderKey] = selected;
        });
        return selected;
    }

    public void Save(
        ScreenshotProcessingPreferences preferences,
        string? journalDirectory = null,
        bool? useAutomaticSource = null,
        bool? useAutomaticTarget = null,
        bool preserveTargetFolder = false
    )
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var defaults = ScreenshotProcessingPreferences.CreateDefaults(journalDirectory);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root[SettingsKey] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root[SettingsKey] = settings;
            }

            settings["Enabled"] = preferences.Enabled;
            settings["AddBanner"] = preferences.AddBanner;
            settings["DeleteOriginal"] = preferences.DeleteOriginal;
            settings["UseGuardianAerialFolder"] = preferences.UseGuardianAerialFolder;
            bool automaticSource =
                useAutomaticSource
                ?? (UsesAutomaticSource(settings) && PathsEqual(preferences.SourceFolder, defaults.SourceFolder));
            settings["UseAutomaticSourceFolder"] = automaticSource;
            if (automaticSource)
            {
                settings.Remove(SourceFolderKey);
            }
            else
            {
                settings[SourceFolderKey] = preferences.SourceFolder;
            }
            if (!preserveTargetFolder)
            {
                SaveFolder(
                    settings,
                    TargetFolderKey,
                    preferences.TargetFolder,
                    defaults.TargetFolder,
                    useAutomaticTarget
                );
            }
            settings["RotateAlphaAerial"] = preferences.RotateAlphaAerial;
            settings["BannerColor"] = preferences.BannerColor;
            settings["BannerLocalTime"] = preferences.BannerLocalTime;
            settings["AerialAltitudeAlpha"] = preferences.AerialAltitudeAlpha;
            settings["AerialAltitudeBeta"] = preferences.AerialAltitudeBeta;
            settings["AerialAltitudeGamma"] = preferences.AerialAltitudeGamma;
        });
    }

    private static bool UsesAutomaticSource(JsonObject? settings)
    {
        if (GetBoolean(settings, "UseAutomaticSourceFolder") is { } automatic)
        {
            return automatic;
        }
        string? source = GetString(settings, SourceFolderKey);
        return source is null
            || PathsEqual(source, ScreenshotProcessingPreferences.CreateDefaults().SourceFolder)
            || PathsEqual(source, Path.Combine("Frontier Developments", "Elite Dangerous"));
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            first,
            second,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );

    private static void SaveFolder(JsonObject settings, string name, string value, string defaultValue, bool? automatic)
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        bool manual = automatic == false || (automatic is null && GetString(settings, name) is not null);
        if (!manual && string.Equals(value, defaultValue, comparison))
        {
            // Keep automatic folders linked to the journal instead of freezing today's derived path.
            settings.Remove(name);
        }
        else
        {
            settings[name] = value;
        }
    }

    private static bool? GetBoolean(JsonObject? root, string name)
    {
        return root?[name] is JsonValue value && value.TryGetValue<bool>(out bool result) ? result : null;
    }

    private static double? GetDouble(JsonObject? root, string name)
    {
        return root?[name] is JsonValue value && value.TryGetValue<double>(out double result) && double.IsFinite(result)
            ? result
            : null;
    }

    private static string? GetString(JsonObject? root, string name)
    {
        return
            root?[name] is JsonValue value
            && value.TryGetValue<string>(out string? result)
            && !string.IsNullOrWhiteSpace(result)
            ? result
            : null;
    }

    private static string GetBannerColor(JsonNode? value, string fallback)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out string? color) && !string.IsNullOrWhiteSpace(color))
        {
            return color.Trim();
        }

        if (value is not JsonObject legacy)
        {
            return fallback;
        }

        byte? red = GetByte(legacy, "R");
        byte? green = GetByte(legacy, "G");
        byte? blue = GetByte(legacy, "B");
        return red is not null && green is not null && blue is not null
            ? $"#{red.Value:X2}{green.Value:X2}{blue.Value:X2}"
            : fallback;
    }

    private static byte? GetByte(JsonObject root, string name)
    {
        return
            root[name] is JsonValue value
            && value.TryGetValue<int>(out int result)
            && result is >= byte.MinValue and <= byte.MaxValue
            ? (byte)result
            : null;
    }
}

public sealed record ScreenshotProcessingPreferences(
    bool Enabled,
    bool AddBanner,
    bool DeleteOriginal,
    bool UseGuardianAerialFolder,
    string SourceFolder,
    string TargetFolder,
    bool RotateAlphaAerial,
    string BannerColor,
    bool BannerLocalTime,
    double AerialAltitudeAlpha,
    double AerialAltitudeBeta,
    double AerialAltitudeGamma
)
{
    public static ScreenshotProcessingPreferences CreateDefaults(string? journalDirectory = null)
    {
        return CreateDefaults(
            journalDirectory,
            Environment.GetFolderPath(
                Environment.SpecialFolder.MyPictures,
                Environment.SpecialFolderOption.DoNotVerify
            ),
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify
            )
        );
    }

    internal static ScreenshotProcessingPreferences CreateDefaults(
        string? journalDirectory,
        string picturesDirectory,
        string userProfileDirectory
    )
    {
        string pictures = Path.IsPathFullyQualified(picturesDirectory)
            ? picturesDirectory
            : Path.GetFullPath(Path.Combine(userProfileDirectory, "Pictures"));
        string source = JournalFolderLocator.ResolveScreenshotDirectory(journalDirectory, pictures);
        return new ScreenshotProcessingPreferences(
            false,
            true,
            false,
            true,
            source,
            Path.Combine(source, "converted"),
            true,
            "#FFFF00",
            false,
            1200,
            1550,
            1600
        );
    }
}
