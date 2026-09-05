using System.Text.Json.Nodes;
using System.Collections.Frozen;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Input;

public sealed record GlobalInputSettings(
    bool KeyboardEnabled,
    bool ControllerEnabled,
    string? ControllerDeviceId,
    IReadOnlyDictionary<GlobalInputAction, string> Bindings)
{
    public static GlobalInputSettings Default { get; } = new(
        KeyboardEnabled: false,
        ControllerEnabled: false,
        ControllerDeviceId: null,
        GlobalInputActionCatalog.All.ToFrozenDictionary(
            definition => definition.Action,
            definition => definition.DefaultChord));
}

public sealed class GlobalInputSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public GlobalInputSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public GlobalInputSettings Load()
    {
        var root = documentStore.Load();
        if (root["Input"] is not JsonObject input)
        {
            return GlobalInputSettings.Default;
        }

        var bindings = GlobalInputSettings.Default.Bindings.ToDictionary();
        if (input["Bindings"] is JsonObject storedBindings)
        {
            foreach (var entry in storedBindings)
            {
                if (entry.Value is JsonValue value
                    && value.TryGetValue<string>(out var chord)
                    && GlobalInputActionCatalog.TryGetByLegacyName(
                        entry.Key,
                        out var definition)
                    && definition is not null)
                {
                    bindings[definition.Action] = chord;
                }
            }

            MigrateMiningBindings(storedBindings, bindings);
        }

        return new GlobalInputSettings(
            GetBoolean(input, "KeyboardEnabled"),
            GetBoolean(input, "ControllerEnabled"),
            GetString(input, "ControllerDeviceId"),
            bindings);
    }

    public void Save(GlobalInputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        documentStore.Update(root =>
        {
            var input = root["Input"] as JsonObject;
            if (input is null)
            {
                input = [];
                root["Input"] = input;
            }

            var bindings = input["Bindings"] as JsonObject;
            if (bindings is null)
            {
                bindings = [];
                input["Bindings"] = bindings;
            }

            foreach (var definition in GlobalInputActionCatalog.All)
            {
                bindings[definition.LegacyName] = settings.Bindings
                    .GetValueOrDefault(definition.Action)
                    ?? definition.DefaultChord;
            }

            for (var number = 1; number <= 6; number++)
            {
                bindings.Remove($"miningRig{number}");
            }

            root["Version"] = 1;
            input["KeyboardEnabled"] = settings.KeyboardEnabled;
            input["ControllerEnabled"] = settings.ControllerEnabled;
            input["ControllerDeviceId"] = settings.ControllerDeviceId;
        });
    }

    private static void MigrateMiningBindings(
        JsonObject storedBindings,
        Dictionary<GlobalInputAction, string> bindings)
    {
        for (var number = 1; number <= 6; number++)
        {
            var action = GlobalInputAction.Track1 + number - 1;
            var defaultChord = GlobalInputActionCatalog.Get(action).DefaultChord;
            if (storedBindings[$"miningRig{number}"] is JsonValue value
                && value.TryGetValue<string>(out var chord)
                && !string.Equals(chord, $"ALT {number}", StringComparison.OrdinalIgnoreCase)
                && string.Equals(bindings[action], defaultChord, StringComparison.OrdinalIgnoreCase))
            {
                // Preserve a customized RC43 rig chord when the tracker still uses its default.
                // An explicit tracker customization wins if the old settings disagree.
                bindings[action] = chord;
            }
        }
    }

    private static bool GetBoolean(JsonObject root, string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<bool>(out var result)
            && result;
    }

    private static string? GetString(JsonObject root, string name)
    {
        return root[name] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }
}
