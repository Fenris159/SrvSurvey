using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Input;

/// <summary>Knows which keyboard sources each overlay host provides, before and after listeners start.</summary>
public static class KeyboardInputHost
{
    /// <summary>Allows Wayland keyboard enablement while runtime portal discovery reports actual availability.</summary>
    public static bool CanEnableKeyboard(OverlayPlatformCapabilities capabilities) =>
        capabilities.SupportsGlobalInput || capabilities.Host == OverlayHostKind.LinuxWayland;

    /// <summary>Describes a host's keyboard sources before any listener has reported.</summary>
    public static KeyboardInputDiagnostics InitialDiagnostics(OverlayHostKind host) =>
        new(
            null,
            false,
            false,
            false,
            "No configured shortcut received yet.",
            "Waiting for Elite Dangerous.",
            "No separate game display is connected."
        )
        {
            DesktopShortcutSettings = HasLinuxSources(host) ? GlobalShortcutsPortalInput.InitialSettings : null,
        };

    /// <summary>Creates the desktop hook plus, on Linux desktops, game-display and portal sources in startup order.</summary>
    internal static IReadOnlyList<IKeyboardActivationSource> CreateSources(
        OverlayHostKind host,
        Func<PixelRect?>? overlayMonitorBounds
    )
    {
        var desktop = new DesktopKeyboardHookSource(host);
        if (!OperatingSystem.IsLinux() || !HasLinuxSources(host))
        {
            return [desktop];
        }

        return
        [
            new GamescopeKeyboardInput(
                GamescopeGameWindowBridge.TryReadCurrent,
                readDisplay: () => EliteKeyboardDisplayDiscovery.ReadCurrent(overlayMonitorBounds?.Invoke()),
                preferredMonitorBounds: overlayMonitorBounds
            ),
            new GlobalShortcutsPortalInput(),
            desktop,
        ];
    }

    private static bool HasLinuxSources(OverlayHostKind host) =>
        host is OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland or OverlayHostKind.LinuxWayland;
}
