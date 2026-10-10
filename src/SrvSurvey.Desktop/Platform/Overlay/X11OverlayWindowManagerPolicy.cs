namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Classifies overlays as tools rather than notifications or animated application windows.</summary>
internal static class X11OverlayWindowManagerPolicy
{
    internal const string SupportedAtomName = "_NET_SUPPORTED";
    internal const string WindowTypeAtomName = "_NET_WM_WINDOW_TYPE";
    internal const string UtilityWindowAtomName = "_NET_WM_WINDOW_TYPE_UTILITY";
    internal const string NormalWindowAtomName = "_NET_WM_WINDOW_TYPE_NORMAL";

    /// <summary>Uses a utility type in both management modes, with normal fallback for other window managers.</summary>
    internal static nuint[] CreateWindowTypes(nuint utilityWindowAtom, nuint normalWindowAtom)
    {
        return utilityWindowAtom == 0 || normalWindowAtom == 0 ? [] : [utilityWindowAtom, normalWindowAtom];
    }
}
