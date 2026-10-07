using SharpHook.Data;

namespace SrvSurvey.Desktop.Input;

/// <summary>Associates a portal shortcut with the application's normalized binding and action.</summary>
internal sealed record PortalShortcutBinding(
    string Id,
    string Description,
    string Trigger,
    string Chord,
    GlobalInputAction Action
)
{
    /// <summary>Builds unique keyboard shortcuts using the same first-binding-wins rule as the existing router.</summary>
    public static IReadOnlyList<PortalShortcutBinding> FromSettings(GlobalInputSettings settings)
    {
        var bindings = new List<PortalShortcutBinding>();
        var chords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GlobalInputActionDefinition definition in GlobalInputActionCatalog.All)
        {
            if (
                !InputChord.TryNormalize(settings.Bindings.GetValueOrDefault(definition.Action), out string chord)
                || !chords.Add(chord)
            )
            {
                continue;
            }
            string[] tokens = chord.Split(' ');
            KeyCode? key = Enum.GetValues<KeyCode>()
                .Cast<KeyCode?>()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        KeyboardChordFormatter.GetKeyName(candidate!.Value),
                        tokens[^1],
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            if (key is null)
            {
                continue;
            }
            string keysym = KeysymNames.Get(key.Value);
            bindings.Add(
                new PortalShortcutBinding(
                    definition.LegacyName,
                    definition.DisplayName,
                    string.Join('+', tokens[..^1].Append(keysym)),
                    chord,
                    definition.Action
                )
            );
        }
        return bindings;
    }
}
