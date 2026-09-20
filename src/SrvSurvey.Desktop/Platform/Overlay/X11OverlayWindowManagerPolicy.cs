namespace SrvSurvey.Desktop.Platform.Overlay;

internal enum X11OverlayStackingMode
{
    StandardTopmost,
    KdeOnScreenDisplay,
}

internal sealed class X11StackingPolicyLogLimiter
{
    private readonly Lock gate = new();
    private readonly HashSet<X11OverlayStackingMode> reportedModes = [];

    public bool ShouldLog(X11OverlayStackingMode mode)
    {
        lock (gate)
        {
            return reportedModes.Add(mode);
        }
    }
}

internal static class X11OverlayWindowManagerPolicy
{
    internal const string SupportedAtomName = "_NET_SUPPORTED";
    internal const string WindowTypeAtomName = "_NET_WM_WINDOW_TYPE";
    internal const string KdeOnScreenDisplayAtomName = "_KDE_NET_WM_WINDOW_TYPE_ON_SCREEN_DISPLAY";
    internal const string NotificationWindowAtomName = "_NET_WM_WINDOW_TYPE_NOTIFICATION";
    internal const string NormalWindowAtomName = "_NET_WM_WINDOW_TYPE_NORMAL";

    internal static X11OverlayStackingMode Select(nuint kdeOnScreenDisplayAtom, ReadOnlySpan<nuint> supportedAtoms)
    {
        if (kdeOnScreenDisplayAtom == 0)
        {
            return X11OverlayStackingMode.StandardTopmost;
        }

        foreach (nuint atom in supportedAtoms)
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
        nuint notificationWindowAtom,
        nuint normalWindowAtom
    )
    {
        if (normalWindowAtom == 0)
        {
            return [];
        }

        return mode switch
        {
            X11OverlayStackingMode.KdeOnScreenDisplay when kdeOnScreenDisplayAtom != 0 =>
            [
                kdeOnScreenDisplayAtom,
                normalWindowAtom,
            ],
            X11OverlayStackingMode.StandardTopmost when notificationWindowAtom != 0 =>
            [
                notificationWindowAtom,
                normalWindowAtom,
            ],
            _ => [],
        };
    }
}
