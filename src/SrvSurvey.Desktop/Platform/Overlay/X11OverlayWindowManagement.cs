namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Configures X11 overlays and position-editor windows independently of desktop placement policy.</summary>
internal static class X11OverlayWindowManagement
{
    /// <summary>Sets override-redirect before mapping and confirms it before another connection can show the window.</summary>
    internal static bool TryEnable(nint display, nuint window)
    {
        if (
            X11Native.XGetWindowAttributes(display, window, out X11Native.XWindowAttributes attributes) == 0
            || attributes.MapState != 0
        )
        {
            return false;
        }

        var settings = new X11Native.XSetWindowAttributes { OverrideRedirect = 1 };
        _ = X11Native.XChangeWindowAttributes(display, window, 1u << 9, ref settings);
        // The reply also synchronizes this connection before Avalonia maps through its own connection.
        return X11Native.XGetWindowAttributes(display, window, out attributes) != 0 && attributes.OverrideRedirect != 0;
    }

    /// <summary>Raises mapped unmanaged windows, optionally giving editors input focus while preserving hidden state.</summary>
    internal static void Raise(nint display, nuint window, bool activate = false)
    {
        if (
            X11Native.XGetWindowAttributes(display, window, out X11Native.XWindowAttributes attributes) != 0
            && attributes.OverrideRedirect != 0
            && attributes.MapState == X11Native.IsViewable
        )
        {
            _ = X11Native.XRaiseWindow(display, window);
            if (activate)
            {
                _ = X11Native.XSetInputFocus(display, window, revertTo: 2, time: 0);
            }
            _ = X11Native.XFlush(display);
        }
    }
}
