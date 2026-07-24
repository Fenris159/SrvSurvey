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
            ? "Windows topmost transparency and native click-through are available; game-window following and global input are pending."
            : "Windows overlay input pass-through could not be enabled.",
        OverlayHostKind.LinuxX11 =>
            "X11 topmost transparency is available; click-through, game-window following, and global input are pending runtime adapters.",
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
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false),
            OverlayHostKind.LinuxX11 => new OverlayPlatformCapabilities(
                host,
                SupportsTopmost: true,
                SupportsTransparency: true,
                SupportsClickThrough: false,
                SupportsGameWindowTracking: false,
                SupportsGlobalInput: false),
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
