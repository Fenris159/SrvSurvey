namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed record OverlayPlatformCapabilities(
    OverlayHostKind Host,
    bool SupportsTopmost,
    bool SupportsTransparency,
    bool SupportsClickThrough,
    bool SupportsGameWindowTracking,
    bool SupportsGlobalInput)
{
    public bool SupportsPassiveOverlay => SupportsTopmost
        && SupportsTransparency;

    public string StatusText => Host switch
    {
        OverlayHostKind.Windows => SupportsClickThrough
            ? SupportsGameWindowTracking
                ? "Windows topmost transparency, native click-through, game-window tracking, and global keyboard input are available."
                : "Windows topmost transparency, native click-through, and global keyboard input are available; game-window following is pending."
            : "Windows overlay input pass-through could not be enabled.",
        OverlayHostKind.LinuxX11 =>
            SupportsClickThrough && SupportsGameWindowTracking
                ? "X11 topmost transparency, XShape click-through, game-window tracking, and global keyboard input are available."
                : "X11 is present, but click-through or game-window tracking could not be initialized; detached overlays are disabled.",
        OverlayHostKind.LinuxWayland =>
            "Wayland overlay positioning, transparency, click-through, and global input require compositor support and are not enabled.",
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
            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            return ForHost(string.Equals(
                session,
                "wayland",
                StringComparison.OrdinalIgnoreCase)
                    ? OverlayHostKind.LinuxWayland
                    : OverlayHostKind.LinuxX11);
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
                SupportsGlobalInput: true),
            OverlayHostKind.LinuxX11 => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: true,
                SupportsTransparency: true,
                SupportsClickThrough: true,
                SupportsGameWindowTracking: true,
                SupportsGlobalInput: true),
            OverlayHostKind.LinuxWayland => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: false,
                SupportsTransparency: false,
                SupportsClickThrough: false,
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false),
            _ => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: false,
                SupportsTransparency: false,
                SupportsClickThrough: false,
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false),
        };
    }
}

public enum OverlayHostKind
{
    Windows,
    LinuxX11,
    LinuxWayland,
    Other,
}
