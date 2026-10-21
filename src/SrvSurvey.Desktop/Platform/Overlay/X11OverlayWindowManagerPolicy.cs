namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Classifies overlays as tools rather than notifications or animated application windows.</summary>
internal static class X11OverlayWindowManagerPolicy
{
    internal const string SupportedAtomName = "_NET_SUPPORTED";
    internal const string WindowTypeAtomName = "_NET_WM_WINDOW_TYPE";
    internal const string UtilityWindowAtomName = "_NET_WM_WINDOW_TYPE_UTILITY";
    internal const string NormalWindowAtomName = "_NET_WM_WINDOW_TYPE_NORMAL";

    /// <summary>Uses utility classification for gameplay panels and normal classification for the background capture.</summary>
    internal static nuint[] CreateWindowTypes(
        nuint utilityWindowAtom,
        nuint normalWindowAtom,
        bool captureWindow = false
    )
    {
        if (captureWindow)
        {
            return normalWindowAtom == 0 ? [] : [normalWindowAtom];
        }
        return utilityWindowAtom == 0 || normalWindowAtom == 0 ? [] : [utilityWindowAtom, normalWindowAtom];
    }
}
