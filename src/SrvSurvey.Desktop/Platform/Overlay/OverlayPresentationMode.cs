namespace SrvSurvey.Desktop.Platform.Overlay;

public enum OverlayPresentationMode
{
    MultipleWindows,
    CombinedWindow,
}

public sealed record OverlayPresentationDecision(
    OverlayPresentationMode Mode,
    string Reason);

public static class OverlayPresentationModeSelector
{
    public const string HostOverrideVariable = "SRVSURVEY_OVERLAY_HOST";

    public static OverlayPresentationDecision DetectCurrent(
        OverlayPlatformCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return Select(
            capabilities,
            Environment.GetEnvironmentVariable(HostOverrideVariable),
            Environment.GetEnvironmentVariable("GAMESCOPE_WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("GAMESCOPE_DISPLAY"),
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
    }

    internal static OverlayPresentationDecision Select(
        OverlayPlatformCapabilities capabilities,
        string? hostOverride,
        string? gamescopeWaylandDisplay,
        string? gamescopeDisplay,
        string? currentDesktop)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var requested = hostOverride?.Trim();
        if (IsMultipleWindowOverride(requested))
        {
            return new OverlayPresentationDecision(
                OverlayPresentationMode.MultipleWindows,
                $"{HostOverrideVariable} selected separate native overlay windows.");
        }

        if (IsCombinedWindowOverride(requested))
        {
            return SupportsCombinedWindow(capabilities)
                ? new OverlayPresentationDecision(
                    OverlayPresentationMode.CombinedWindow,
                    $"{HostOverrideVariable} selected one combined overlay window.")
                : new OverlayPresentationDecision(
                    OverlayPresentationMode.MultipleWindows,
                    "The combined overlay window was requested, but this display host does not expose the required native window controls.");
        }

        if (SupportsCombinedWindow(capabilities)
            && capabilities.UsesX11Compatibility
            && IsGamescope(
                gamescopeWaylandDisplay,
                gamescopeDisplay,
                currentDesktop))
        {
            return new OverlayPresentationDecision(
                OverlayPresentationMode.CombinedWindow,
                "Gamescope was detected; live panels will share one native overlay window.");
        }

        return new OverlayPresentationDecision(
            OverlayPresentationMode.MultipleWindows,
            "Using separate native overlay windows for the current desktop host.");
    }

    private static bool SupportsCombinedWindow(
        OverlayPlatformCapabilities capabilities)
    {
        return capabilities.Host == OverlayHostKind.Windows
            || capabilities.UsesX11Compatibility;
    }

    private static bool IsGamescope(
        string? gamescopeWaylandDisplay,
        string? gamescopeDisplay,
        string? currentDesktop)
    {
        return !string.IsNullOrWhiteSpace(gamescopeWaylandDisplay)
            || !string.IsNullOrWhiteSpace(gamescopeDisplay)
            || currentDesktop?.Contains(
                "gamescope",
                StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsMultipleWindowOverride(string? value)
    {
        return value?.ToLowerInvariant() is "multiple"
            or "multi"
            or "separate"
            or "windows";
    }

    private static bool IsCombinedWindowOverride(string? value)
    {
        return value?.ToLowerInvariant() is "combined"
            or "single"
            or "canvas";
    }
}
