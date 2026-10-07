namespace SrvSurvey.Desktop.Input;

/// <summary>Selects one keyboard pathway for every shortcut in an application instance.</summary>
public enum KeyboardInputMode
{
    Automatic,
    Desktop,
    GameDisplay,
    WaylandPortal,
}

/// <summary>Describes listener availability and the most recent configured shortcut without recording arbitrary keys.</summary>
public sealed record KeyboardInputDiagnostics(
    KeyboardInputMode? SelectedSource,
    bool DesktopAvailable,
    bool GameDisplayAvailable,
    bool PortalAvailable,
    string LastInput,
    string FocusStatus,
    string GameDisplayStatus
)
{
    /// <summary>Describes desktop-managed shortcut configuration when a keyboard source offers it.</summary>
    public DesktopShortcutSettingsState? DesktopShortcutSettings { get; init; }

    /// <summary>Lists every source choice with its current availability and the explanation shown in input settings.</summary>
    public IReadOnlyList<KeyboardInputSourceOption> SourceOptions =>
        [
            new(
                KeyboardInputMode.Automatic,
                "Automatic (recommended)",
                true,
                "Uses one detected source for all keyboard shortcuts. Relearns when the game changes or repeated input proves another source is working."
            ),
            new(
                KeyboardInputMode.Desktop,
                GetLabel(KeyboardInputSource.Desktop),
                DesktopAvailable,
                "Uses the desktop keyboard listener. A separate game display or native Wayland game may not deliver input to this listener."
            ),
            new(
                KeyboardInputMode.GameDisplay,
                GetLabel(KeyboardInputSource.NestedDisplay),
                GameDisplayAvailable,
                GameDisplayStatus
                    + " Uses the game's separate X11 display. The overlay monitor helps select a client when desktop geometry is available."
            ),
            new(
                KeyboardInputMode.WaylandPortal,
                GetLabel(KeyboardInputSource.Portal),
                PortalAvailable,
                "Requires desktop portal support and approval for all configured keyboard shortcuts. Use Desktop shortcut settings to approve or change bindings. The desktop may override requested keys; its approved keys are shown below. Native Wayland may not expose which window has focus."
            ),
        ];

    /// <summary>Names a source identically in source choices and last-shortcut reports.</summary>
    internal static string GetLabel(KeyboardInputSource source) =>
        source switch
        {
            KeyboardInputSource.Desktop => "Desktop keyboard",
            KeyboardInputSource.NestedDisplay => "Game display",
            _ => "Wayland portal",
        };
}

/// <summary>Provides a source choice and explains why it is currently unavailable.</summary>
public sealed record KeyboardInputSourceOption(KeyboardInputMode Mode, string Label, bool IsAvailable, string Details);

/// <summary>Describes desktop-managed shortcut configuration and the readable shortcuts the desktop approved.</summary>
public sealed record DesktopShortcutSettingsState(bool CanOpen, string Status, IReadOnlyList<string> ApprovedShortcuts)
{
    /// <summary>Compares approvals by content so unchanged diagnostics do not refresh the settings view.</summary>
    public bool Equals(DesktopShortcutSettingsState? other) =>
        other is not null
        && CanOpen == other.CanOpen
        && string.Equals(Status, other.Status, StringComparison.Ordinal)
        && ApprovedShortcuts.SequenceEqual(other.ApprovedShortcuts, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(CanOpen, Status, ApprovedShortcuts.Count);
}
