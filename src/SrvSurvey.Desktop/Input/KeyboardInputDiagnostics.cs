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
    public bool CanOpenDesktopShortcutSettings { get; init; }
    public string DesktopShortcutSettingsStatus { get; init; } = "Desktop shortcuts: checking availability.";
}

/// <summary>Provides a source choice and explains why it is currently unavailable.</summary>
public sealed record KeyboardInputSourceOption(KeyboardInputMode Mode, string Label, bool IsAvailable, string Details);
