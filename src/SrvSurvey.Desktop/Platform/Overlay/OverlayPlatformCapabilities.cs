namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed record OverlayPlatformCapabilities(
    OverlayHostKind Host,
    bool SupportsTopmost,
    bool SupportsTransparency,
    bool SupportsClickThrough,
    bool SupportsGameWindowTracking,
    bool SupportsGlobalInput
)
{
    /// <summary>Whether monitor-locked drags replace the platform move-drag with the managed drag that applies the lock.</summary>
    public bool UsesManagedDragForMonitorLock { get; init; }

    public bool UsesGamescopeExternalOverlay { get; init; }

    public bool SupportsLiveOverlayInteraction => !UsesGamescopeExternalOverlay;

    public bool SupportsPassiveOverlay => SupportsTopmost && SupportsTransparency;

    public bool UsesX11Compatibility => IsX11Compatible(Host);

    public string StatusText => UsesGamescopeExternalOverlay ? GetGamescopeStatusText() : GetHostStatusText();

    private string GetGamescopeStatusText() =>
        SupportsPassiveOverlay && SupportsClickThrough && SupportsGameWindowTracking
            ? "Gamescope passive overlays are available. Turn off the performance HUD; configure panels in the position editor. Live HUD interaction is unavailable."
            : "Gamescope was detected, but passive overlay initialization failed. Check the application log.";

    private string GetHostStatusText() =>
        Host switch
        {
            OverlayHostKind.Windows => SupportsClickThrough
                ? (SupportsGameWindowTracking) switch
                {
                    true =>
                        "Windows topmost transparency, native click-through, game-window tracking, and global keyboard input are available.",
                    false =>
                        "Windows topmost transparency, native click-through, and global keyboard input are available; game-window following is pending.",
                }
                : "Windows overlay input pass-through could not be enabled.",
            OverlayHostKind.LinuxX11 => SupportsClickThrough && SupportsGameWindowTracking
                ? "X11 topmost transparency, XShape click-through, game-window tracking, and global keyboard input are available."
                : "X11 is present, but click-through or game-window tracking could not be initialized; detached overlays are disabled.",
            OverlayHostKind.LinuxXWayland => SupportsClickThrough && SupportsGameWindowTracking
                ? "XWayland topmost transparency, XShape click-through, game-window tracking, and global keyboard input are available."
                : "XWayland is present, but click-through or game-window tracking could not be initialized; detached overlays are disabled.",
            OverlayHostKind.LinuxWayland =>
                "Native Wayland overlays require compositor support. Keyboard shortcuts require a supported Global Shortcuts portal.",
            _ => "Detached overlays are unavailable on this platform.",
        };

    public static OverlayPlatformCapabilities DetectCurrent()
    {
        if (OperatingSystem.IsWindows())
        {
            return ForHost(OverlayHostKind.Windows);
        }

        if (OperatingSystem.IsLinux())
        {
            OverlayPlatformCapabilities capabilities = ForHost(
                DetectLinuxHost(
                    Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                    Environment.GetEnvironmentVariable("DISPLAY"),
                    Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")
                )
            );
            return GamescopeOverlaySession.UnavailableReason is not null
                ? capabilities with
                {
                    SupportsTopmost = false,
                    SupportsGameWindowTracking = false,
                }
                : capabilities with
                {
                    UsesGamescopeExternalOverlay = GamescopeOverlaySession.Current is not null,
                };
        }

        return ForHost(OverlayHostKind.Other);
    }

    public static OverlayPlatformCapabilities ForHost(OverlayHostKind host)
    {
        return host switch
        {
            OverlayHostKind.Windows => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: true,
                SupportsTransparency: true,
                SupportsClickThrough: true,
                SupportsGameWindowTracking: true,
                SupportsGlobalInput: true
            )
            {
                UsesManagedDragForMonitorLock = true,
            },
            OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: true,
                SupportsTransparency: true,
                SupportsClickThrough: true,
                SupportsGameWindowTracking: true,
                SupportsGlobalInput: true
            ),
            OverlayHostKind.LinuxWayland => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: false,
                SupportsTransparency: false,
                SupportsClickThrough: false,
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false
            ),
            _ => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: false,
                SupportsTransparency: false,
                SupportsClickThrough: false,
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false
            ),
        };
    }

    public static bool IsX11Compatible(OverlayHostKind host)
    {
        return host is OverlayHostKind.LinuxX11 or OverlayHostKind.LinuxXWayland;
    }

    internal static OverlayHostKind DetectLinuxHost(string? sessionType, string? display, string? waylandDisplay)
    {
        bool isWaylandSession =
            string.Equals(sessionType?.Trim(), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(waylandDisplay);
        if (!string.IsNullOrWhiteSpace(display))
        {
            return isWaylandSession ? OverlayHostKind.LinuxXWayland : OverlayHostKind.LinuxX11;
        }

        return isWaylandSession ? OverlayHostKind.LinuxWayland : OverlayHostKind.Other;
    }
}

public enum OverlayHostKind
{
    Windows,
    LinuxX11,
    LinuxXWayland,
    LinuxWayland,
    Other,
}
