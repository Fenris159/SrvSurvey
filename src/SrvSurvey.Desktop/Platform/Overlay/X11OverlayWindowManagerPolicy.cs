namespace SrvSurvey.Desktop.Platform.Overlay;

internal enum X11OverlayStackingMode
{
    StandardTopmost,
    KdeOnScreenDisplay,
}

internal static class X11OverlayWindowManagerPolicy
{
    internal const string SupportedAtomName = "_NET_SUPPORTED";
    internal const string WindowTypeAtomName = "_NET_WM_WINDOW_TYPE";
    internal const string KdeOnScreenDisplayAtomName =
        "_KDE_NET_WM_WINDOW_TYPE_ON_SCREEN_DISPLAY";
    internal const string NormalWindowAtomName = "_NET_WM_WINDOW_TYPE_NORMAL";

    internal static X11OverlayStackingMode Select(
        nuint kdeOnScreenDisplayAtom,
        ReadOnlySpan<nuint> supportedAtoms)
    {
        if (kdeOnScreenDisplayAtom == 0)
        {
            return X11OverlayStackingMode.StandardTopmost;
        }

        foreach (var atom in supportedAtoms)
        {
            if (atom == kdeOnScreenDisplayAtom)
            {
                return X11OverlayStackingMode.KdeOnScreenDisplay;
            }
        }

        return X11OverlayStackingMode.StandardTopmost;
    }

    internal static nuint[] CreateWindowTypes(
        X11OverlayStackingMode mode,
        nuint kdeOnScreenDisplayAtom,
        nuint normalWindowAtom)
    {
        return mode == X11OverlayStackingMode.KdeOnScreenDisplay
            && kdeOnScreenDisplayAtom != 0
            && normalWindowAtom != 0
            ? [kdeOnScreenDisplayAtom, normalWindowAtom]
            : [];
    }
}
