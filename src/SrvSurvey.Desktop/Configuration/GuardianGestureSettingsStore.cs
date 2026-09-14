using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.Configuration;

public sealed class GuardianGestureSettingsStore
{
    public const int DefaultBlinkDurationMilliseconds = 3_000;

    private readonly UiSettingsDocumentStore documentStore;

    public GuardianGestureSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public GuardianGesturePreferences Load()
    {
        var settings = documentStore.Load()["GuardianGestures"] as JsonObject;
        return new GuardianGesturePreferences(GetTrigger(settings), GetDuration(settings));
    }

    public void Save(GuardianGesturePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        StatusFlags trigger = NormalizeTrigger(preferences.BlinkTrigger);
        int duration = NormalizeDuration(preferences.BlinkDurationMilliseconds);
        documentStore.Update(root =>
        {
            root["Version"] = 1;
            var settings = root["GuardianGestures"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["GuardianGestures"] = settings;
            }

            settings["BlinkTrigger"] = (uint)trigger;
            settings["BlinkDurationMilliseconds"] = duration;
        });
    }

    private static StatusFlags GetTrigger(JsonObject? settings)
    {
        if (settings?["BlinkTrigger"] is not JsonValue value)
        {
            return StatusFlags.HudInAnalysisMode;
        }

        if (value.TryGetValue<uint>(out uint unsigned))
        {
            return NormalizeTrigger((StatusFlags)unsigned);
        }

        if (value.TryGetValue<int>(out int signed) && signed >= 0)
        {
            return NormalizeTrigger((StatusFlags)(uint)signed);
        }

        return
            value.TryGetValue<string>(out string? text)
            && Enum.TryParse<StatusFlags>(text, true, out StatusFlags parsed)
            ? NormalizeTrigger(parsed)
            : StatusFlags.HudInAnalysisMode;
    }

    private static int GetDuration(JsonObject? settings)
    {
        return settings?["BlinkDurationMilliseconds"] is JsonValue value && value.TryGetValue<int>(out int duration)
            ? NormalizeDuration(duration)
            : DefaultBlinkDurationMilliseconds;
    }

    private static StatusFlags NormalizeTrigger(StatusFlags value)
    {
        uint raw = (uint)value;
        return raw != 0 && (raw & (raw - 1)) == 0 ? value : StatusFlags.HudInAnalysisMode;
    }

    private static int NormalizeDuration(int value)
    {
        return value is >= 250 and <= 60_000 ? value : DefaultBlinkDurationMilliseconds;
    }
}

public sealed record GuardianGesturePreferences(StatusFlags BlinkTrigger, int BlinkDurationMilliseconds)
{
    public static GuardianGesturePreferences Default { get; } =
        new(StatusFlags.HudInAnalysisMode, GuardianGestureSettingsStore.DefaultBlinkDurationMilliseconds);
}
