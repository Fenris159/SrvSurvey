using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed record GamescopeDisplayIdentity(uint ServerId, uint? ProcessId);

internal sealed record GamescopeFocusSnapshot(
    string? Display,
    nuint Window,
    uint? InputApp,
    uint? GraphicsApp,
    PixelRect OutputBounds,
    uint ScalingMode = 0,
    string? KeyboardDisplay = null
);

/// <summary>Owns the Gamescope X11 protocol, including LP64 property encoding and display identity.</summary>
internal sealed class GamescopeX11Connection : IDisposable
{
    private const string SteamOverlayAtom = "STEAM_OVERLAY";
    private const string SteamInputFocusAtom = "STEAM_INPUT_FOCUS";

    private nint display;
    private readonly nuint root;

    private GamescopeX11Connection(nint display)
    {
        this.display = display;
        root = X11Native.XDefaultRootWindow(display);
    }

    public static GamescopeX11Connection? TryOpen(string? name)
    {
        if (!OperatingSystem.IsLinux() || !EliteKeyboardDisplayDiscovery.IsLocalDisplay(name))
        {
            return null;
        }

        nint nativeName = Marshal.StringToCoTaskMemUTF8(name);
        try
        {
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            nint connection = X11Native.XOpenDisplay(nativeName);
            if (connection == nint.Zero)
            {
                return null;
            }
            X11OverlayPlatformService.RegisterErrorHandledDisplay(connection);
            X11TransientDisplayRecovery.Register(connection);
            return new GamescopeX11Connection(connection);
        }
        finally
        {
            Marshal.FreeCoTaskMem(nativeName);
        }
    }

    public GamescopeDisplayIdentity? ReadIdentity()
    {
        uint? serverId = ReadNumber("GAMESCOPE_XWAYLAND_SERVER_ID");
        uint? check = ReadNumber("_NET_SUPPORTING_WM_CHECK");
        if (serverId is null || check is null || ReadNumber("_NET_SUPPORTING_WM_CHECK", check.Value) != check)
        {
            return null;
        }
        if (X11Native.XFetchName(display, check.Value, out nint name) == 0)
        {
            return null;
        }
        try
        {
            return Marshal.PtrToStringUTF8(name) == "steamcompmgr"
                ? new GamescopeDisplayIdentity(serverId.Value, ReadNumber("GAMESCOPE_PID"))
                : null;
        }
        finally
        {
            _ = X11Native.XFree(name);
        }
    }

    public GamescopeFocusSnapshot ReadFocus()
    {
        return new GamescopeFocusSnapshot(
            DecodeDisplay(ReadNumbers("GAMESCOPE_FOCUS_DISPLAY")),
            ReadNumber("GAMESCOPE_FOCUSED_WINDOW") ?? 0,
            ReadNumber("GAMESCOPE_FOCUSED_APP"),
            ReadNumber("GAMESCOPE_FOCUSED_APP_GFX"),
            ReadBounds(),
            ReadNumber("GAMESCOPE_NEW_SCALING_SCALER") ?? 0,
            DecodeDisplay(ReadNumbers("GAMESCOPE_KEYBOARD_FOCUS_DISPLAY"))
        );
    }

    public PixelRect ReadBounds()
    {
        return IsAlive && X11Native.XGetWindowAttributes(display, root, out X11Native.XWindowAttributes attributes) != 0
            ? new PixelRect(0, 0, Math.Max(0, attributes.Width), Math.Max(0, attributes.Height))
            : default;
    }

    /// <summary>Registers only a primary-server host and verifies the property after synchronizing.</summary>
    public bool PrepareExternalOverlay(nuint window) => SetOverlayRole(window, false);

    /// <summary>Switches only our window between passive rendering and pointer-only compositor routing.</summary>
    internal bool SetOverlayRole(nuint window, bool pointerRouter)
    {
        if (window == 0 || ReadIdentity()?.ServerId != 0)
        {
            return false;
        }
        WriteNumber(window, SteamInputFocusAtom, 0);
        // Retain an overlay role throughout the transition so the router is never a base-game candidate.
        if (pointerRouter)
        {
            WriteNumber(window, SteamOverlayAtom, 1);
            WriteNumber(window, "GAMESCOPE_EXTERNAL_OVERLAY", 0);
        }
        else
        {
            WriteNumber(window, "GAMESCOPE_EXTERNAL_OVERLAY", 1);
            WriteNumber(window, SteamOverlayAtom, 0);
        }
        WriteNumber(window, SteamInputFocusAtom, pointerRouter ? 2u : 0u);
        _ = X11Native.XSync(display, 0);
        return ReadNumber("GAMESCOPE_EXTERNAL_OVERLAY", window) == (pointerRouter ? 0 : 1)
            && ReadNumber(SteamOverlayAtom, window) == (pointerRouter ? 1 : 0)
            && ReadNumber(SteamInputFocusAtom, window) == (pointerRouter ? 2 : 0);
    }

    private void WriteNumber(nuint window, string atom, uint number)
    {
        nint value = Marshal.AllocHGlobal(nint.Size);
        try
        {
            // Xlib format 32 consumes native longs, including on LP64.
            Marshal.WriteIntPtr(value, unchecked((nint)number));
            _ = X11Native.XChangeProperty(
                display,
                window,
                X11Native.XInternAtom(display, atom, 0),
                6,
                32,
                X11Native.PropertyReplace,
                value,
                1
            );
        }
        finally
        {
            Marshal.FreeHGlobal(value);
        }
    }

    /// <summary>Steam/QAM input requests take precedence even for windows that are currently unmapped.</summary>
    internal bool HasForeignInput(nuint router)
    {
        nuint[]? children = ReadChildren();
        return children is null
            || children.Any(window =>
                window != router
                && ReadNumber(SteamOverlayAtom, window) == 1
                && ReadNumber(SteamInputFocusAtom, window) is > 0
            );
    }

    internal void ClearInputRegion(nuint window)
    {
        X11Native.XShapeCombineRectangles(
            display,
            window,
            X11Native.ShapeInput,
            0,
            0,
            0,
            0,
            X11Native.ShapeSet,
            X11Native.Unsorted
        );
        _ = X11Native.XSync(display, 0);
    }

    /// <summary>The compositor picks the lowest equal-opacity overlay; XWayland hit-tests the raised HUD.</summary>
    internal void StackPointerRouter(nuint router, nuint canvas)
    {
        nuint[]? children = ReadChildren();
        if (children is null || children.Length == 0)
        {
            return;
        }
        if (children[0] != router)
        {
            _ = X11Native.XLowerWindow(display, router);
        }
        if (children[^1] != canvas)
        {
            _ = X11Native.XRaiseWindow(display, canvas);
        }
        _ = X11Native.XFlush(display);
    }

    private nuint[]? ReadChildren()
    {
        if (!IsAlive)
        {
            return null;
        }
        int result = X11Native.XQueryTree(display, root, out _, out _, out nint data, out uint count);
        try
        {
            if (result == 0 || count > 4096)
            {
                return null;
            }
            nuint[] children = new nuint[count];
            for (int index = 0; index < children.Length; index++)
            {
                children[index] = unchecked((nuint)Marshal.ReadIntPtr(data, index * nint.Size));
            }
            return children;
        }
        finally
        {
            if (data != nint.Zero)
            {
                _ = X11Native.XFree(data);
            }
        }
    }

    private bool IsAlive => display != nint.Zero && !X11TransientDisplayRecovery.HasFailed(display);

    private uint? ReadNumber(string atom, nuint? window = null)
    {
        uint[] values = ReadNumbers(atom, window);
        return values.Length == 1 ? values[0] : null;
    }

    internal uint[] ReadNumbers(string atom, nuint? window = null)
    {
        if (!IsAlive)
        {
            return [];
        }
        nuint property = X11Native.XInternAtom(display, atom, 1);
        if (property == 0)
        {
            return [];
        }
        int result = X11Native.XGetWindowProperty(
            display,
            window ?? root,
            property,
            0,
            64,
            0,
            0,
            out nuint actualType,
            out int format,
            out nuint count,
            out nuint remaining,
            out nint data
        );
        try
        {
            nuint expectedType = atom == "_NET_SUPPORTING_WM_CHECK" ? 33u : 6u;
            if (
                result != 0
                || data == nint.Zero
                || actualType != expectedType
                || format != 32
                || count > 64
                || remaining != 0
            )
            {
                return [];
            }
            uint[] values = new uint[(int)count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((uint)Marshal.ReadIntPtr(data, index * nint.Size));
            }
            return values;
        }
        finally
        {
            if (data != nint.Zero)
            {
                _ = X11Native.XFree(data);
            }
        }
    }

    /// <summary>Decodes Gamescope's packed display bytes without reading Xlib padding or trailing garbage.</summary>
    internal static string? DecodeDisplay(IReadOnlyList<uint> values)
    {
        var bytes = new List<byte>();
        foreach (uint value in values)
        {
            foreach (byte part in BitConverter.GetBytes(value))
            {
                if (part == 0)
                {
                    string name = Encoding.ASCII.GetString(bytes.ToArray());
                    return EliteKeyboardDisplayDiscovery.IsLocalDisplay(name) ? name : null;
                }
                bytes.Add(part);
            }
        }
        return null;
    }

    public void Dispose()
    {
        nint connection = display;
        display = nint.Zero;
        if (connection != nint.Zero)
        {
            _ = X11Native.XCloseDisplay(connection);
            X11OverlayPlatformService.UnregisterErrorHandledDisplay(connection);
            X11TransientDisplayRecovery.Unregister(connection);
        }
    }
}
