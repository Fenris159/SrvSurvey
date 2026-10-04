namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Configures X11 overlays and position-editor windows independently of desktop placement policy.</summary>
internal static class X11OverlayWindowManagement
{
    /// <summary>Sets override-redirect before mapping and confirms it before another connection can show the window.</summary>
    internal static bool TryEnable(IX11OverlayWindowOperations native, nint display, nuint window)
    {
        if (
            native.GetAttributes(display, window, out X11Native.XWindowAttributes attributes) == 0
            || attributes.MapState != 0
        )
        {
            return false;
        }

        var settings = new X11Native.XSetWindowAttributes { OverrideRedirect = 1 };
        native.ChangeAttributes(display, window, 1u << 9, ref settings);
        // The reply also synchronizes this connection before Avalonia maps through its own connection.
        return native.GetAttributes(display, window, out attributes) != 0 && attributes.OverrideRedirect != 0;
    }

    /// <summary>Raises mapped unmanaged windows, optionally giving editors input focus while preserving hidden state.</summary>
    internal static void Raise(IX11OverlayWindowOperations native, nint display, nuint window, bool activate = false)
    {
        if (
            native.GetAttributes(display, window, out X11Native.XWindowAttributes attributes) != 0
            && attributes.OverrideRedirect != 0
            && attributes.MapState == X11Native.IsViewable
        )
        {
            native.RaiseWindow(display, window);
            if (activate)
            {
                native.SetInputFocus(display, window, revertTo: 2, time: 0);
            }
            native.Flush(display);
        }
    }
}

/// <summary>Provides the X11 operations needed to test placement and focus policy independently of the host OS.</summary>
internal interface IX11OverlayWindowOperations
{
    /// <summary>Reads window attributes and synchronizes prior requests, returning zero for a missing window.</summary>
    int GetAttributes(nint display, nuint window, out X11Native.XWindowAttributes attributes);

    /// <summary>Changes only the attributes selected by the X11 value mask.</summary>
    void ChangeAttributes(nint display, nuint window, nuint valueMask, ref X11Native.XSetWindowAttributes attributes);

    /// <summary>Raises the existing window without mapping it.</summary>
    void RaiseWindow(nint display, nuint window);

    /// <summary>Sets keyboard focus using the caller's reversion policy and event time.</summary>
    void SetInputFocus(nint display, nuint window, int revertTo, nuint time);

    /// <summary>Flushes queued requests to the display server.</summary>
    void Flush(nint display);
}
