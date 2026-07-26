using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class FirstFootfallInferenceSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public FirstFootfallInferenceSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public FirstFootfallInferencePreferences Load()
    {
        var settings = documentStore.Load()["FirstFootfallInference"]
            as JsonObject;
        var color = settings?["Color"] as JsonObject;
        var defaults = FirstFootfallInferencePreferences.Default;
        return new FirstFootfallInferencePreferences(
            GetBoolean(settings, "Enabled", defaults.Enabled),
            GetInt32(color, "Red", defaults.Red, 0, 255),
            GetInt32(color, "Green", defaults.Green, 0, 255),
            GetInt32(color, "Blue", defaults.Blue, 0, 255),
            GetInt32(settings, "Tolerance", defaults.Tolerance, 0, 255),
            GetDouble(
                settings,
                "Threshold",
                defaults.Threshold,
                double.Epsilon,
                1),
            GetInt32(
                settings,
                "DurationSeconds",
                defaults.DurationSeconds,
                1,
                60),
            GetInt32(
                settings,
                "SamplesPerSecond",
                defaults.SamplesPerSecond,
                1,
                60));
    }

    public void Save(FirstFootfallInferencePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = Normalize(preferences);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["FirstFootfallInference"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["FirstFootfallInference"] = settings;
            }

            var color = settings["Color"] as JsonObject;
            if (color is null)
            {
                color = [];
                settings["Color"] = color;
            }

            settings["Enabled"] = normalized.Enabled;
            color["Red"] = normalized.Red;
            color["Green"] = normalized.Green;
            color["Blue"] = normalized.Blue;
            settings["Tolerance"] = normalized.Tolerance;
            settings["Threshold"] = normalized.Threshold;
            settings["DurationSeconds"] = normalized.DurationSeconds;
            settings["SamplesPerSecond"] = normalized.SamplesPerSecond;
        });
    }

    private static FirstFootfallInferencePreferences Normalize(
        FirstFootfallInferencePreferences preferences)
    {
        var defaults = FirstFootfallInferencePreferences.Default;
        return preferences with
        {
            Red = Math.Clamp(preferences.Red, 0, 255),
            Green = Math.Clamp(preferences.Green, 0, 255),
            Blue = Math.Clamp(preferences.Blue, 0, 255),
            Tolerance = Math.Clamp(preferences.Tolerance, 0, 255),
            Threshold = double.IsFinite(preferences.Threshold)
                && preferences.Threshold > 0
                    ? Math.Clamp(preferences.Threshold, double.Epsilon, 1)
                    : defaults.Threshold,
            DurationSeconds = Math.Clamp(
                preferences.DurationSeconds,
                1,
                60),
            SamplesPerSecond = Math.Clamp(
                preferences.SamplesPerSecond,
                1,
                60),
        };
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

    private static int GetInt32(
        JsonObject? source,
        string propertyName,
        int fallback,
        int minimum,
        int maximum)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<int>(out var result)
                ? Math.Clamp(result, minimum, maximum)
                : fallback;
    }

    private static double GetDouble(
        JsonObject? source,
        string propertyName,
        double fallback,
        double minimum,
        double maximum)
    {
        return source?[propertyName] is JsonValue value
            && value.TryGetValue<double>(out var result)
            && double.IsFinite(result)
            && result > 0
                ? Math.Clamp(result, minimum, maximum)
                : fallback;
    }
}

public sealed record FirstFootfallInferencePreferences(
    bool Enabled,
    int Red,
    int Green,
    int Blue,
    int Tolerance,
    double Threshold,
    int DurationSeconds,
    int SamplesPerSecond)
{
    public static FirstFootfallInferencePreferences Default { get; } = new(
        Enabled: true,
        Red: 102,
        Green: 255,
        Blue: 255,
        Tolerance: 25,
        Threshold: 0.002,
        DurationSeconds: 15,
        SamplesPerSecond: 20);
}
