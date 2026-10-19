using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal static class WindowsOverlayWindowStyles
{
    private const long ToolWindow = 0x00000080L;
    private const long Transparent = 0x00000020L;
    private const long Layered = 0x00080000L;
    private const long NoActivate = 0x08000000L;

    public static nint Apply(Window window, nint style, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(window);
        nint updated = interactive
            ? (style | (nint)(ToolWindow | Layered)) & ~(nint)(Transparent | NoActivate)
            : style | (nint)(ToolWindow | Transparent | Layered | NoActivate);
        // OBS excludes utility windows. The capture surface retains passive input flags without this flag.
        return window is StreamOverlayWindow ? updated & ~(nint)ToolWindow : updated;
    }
}
