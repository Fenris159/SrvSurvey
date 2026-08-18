using System.Runtime.InteropServices;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11GameWindowTracker : IGameWindowTracker
{
    private const int PropertyReadLength = 16_384;
    private readonly object gate = new();
    private nint display;
    private readonly nuint rootWindow;
    private readonly nuint activeWindowAtom;
    private readonly nuint clientListAtom;
    private readonly nuint clientListStackingAtom;
    private readonly nuint processIdAtom;
    private nuint gameWindow;
    private nuint inspectedActiveWindow;
    private bool inspectedActiveWindowIsElite;

    private X11GameWindowTracker(nint display)
    {
        this.display = display;
        rootWindow = X11Native.XDefaultRootWindow(display);
        activeWindowAtom = GetAtom("_NET_ACTIVE_WINDOW");
        clientListAtom = GetAtom("_NET_CLIENT_LIST");
        clientListStackingAtom = GetAtom("_NET_CLIENT_LIST_STACKING");
        processIdAtom = GetAtom("_NET_WM_PID");
    }

    public static IGameWindowTracker? TryCreate()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var display = nint.Zero;
        try
        {
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            display = X11Native.XOpenDisplay(nint.Zero);
            if (display == nint.Zero)
            {
                return null;
            }

            X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
            return new X11GameWindowTracker(display);
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException)
        {
            if (display != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(display);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(
                        display);
                }
            }

            return null;
        }
    }

    public GameWindowSnapshot GetSnapshot()
    {
        lock (gate)
        {
            if (display == nint.Zero)
            {
                return GameWindowSnapshot.Unavailable;
            }

            var activeWindow = ReadSingleWindow(activeWindowAtom);
            if (activeWindow != 0
                && activeWindow != gameWindow
                && activeWindow != inspectedActiveWindow)
            {
                inspectedActiveWindow = activeWindow;
                inspectedActiveWindowIsElite = IsEliteWindow(activeWindow);
            }

            if (activeWindow != 0
                && (activeWindow == gameWindow
                    || (activeWindow == inspectedActiveWindow
                        && inspectedActiveWindowIsElite)))
            {
                gameWindow = activeWindow;
            }

            if (gameWindow == 0 || !TryGetBounds(gameWindow, out _, out _))
            {
                gameWindow = FindGameWindow(activeWindow);
            }

            if (gameWindow == 0
                || !TryGetBounds(
                    gameWindow,
                    out var clientBounds,
                    out var isVisible))
            {
                gameWindow = 0;
                return GameWindowSnapshot.Unavailable;
            }

            return new GameWindowSnapshot(
                unchecked((nint)gameWindow),
                ReadProcessId(gameWindow),
                clientBounds,
                isVisible,
                activeWindow == gameWindow);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            var currentDisplay = display;
            display = nint.Zero;
            if (currentDisplay != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(currentDisplay);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(
                        currentDisplay);
                }
            }
        }
    }

    private nuint GetAtom(string name)
    {
        return X11Native.XInternAtom(display, name, onlyIfExists: 0);
    }

    private nuint FindGameWindow(nuint activeWindow)
    {
        var windows = ReadWindowList(clientListStackingAtom);
        if (windows.Length == 0)
        {
            windows = ReadWindowList(clientListAtom);
        }

        if (windows.Length == 0)
        {
            windows = ReadRootChildren();
        }

        nuint firstWindow = 0;
        foreach (var window in windows)
        {
            if (!IsEliteWindow(window))
            {
                continue;
            }

            if (window == activeWindow)
            {
                return window;
            }

            if (firstWindow == 0)
            {
                firstWindow = window;
            }
        }

        return firstWindow;
    }

    private bool IsEliteWindow(nuint window)
    {
        string? resourceName = null;
        string? resourceClass = null;
        if (X11Native.XGetClassHint(
                display,
                window,
                out var classHint) != 0)
        {
            try
            {
                resourceName = ReadNativeString(classHint.ResourceName);
                resourceClass = ReadNativeString(classHint.ResourceClass);
            }
            finally
            {
                Free(classHint.ResourceName);
                Free(classHint.ResourceClass);
            }
        }

        string? title = null;
        if (X11Native.XFetchName(display, window, out var nativeTitle) != 0)
        {
            try
            {
                title = ReadNativeString(nativeTitle);
            }
            finally
            {
                Free(nativeTitle);
            }
        }

        return EliteGameWindowIdentity.MatchesX11(
            resourceName,
            resourceClass,
            title);
    }

    private bool TryGetBounds(
        nuint window,
        out PixelRect bounds,
        out bool isVisible)
    {
        bounds = default;
        isVisible = false;
        if (X11Native.XGetWindowAttributes(
                display,
                window,
                out var attributes) == 0
            || attributes.Width <= 0
            || attributes.Height <= 0
            || X11Native.XTranslateCoordinates(
                display,
                window,
                rootWindow,
                0,
                0,
                out var rootX,
                out var rootY,
                out _) == 0)
        {
            return false;
        }

        bounds = new PixelRect(
            rootX,
            rootY,
            attributes.Width,
            attributes.Height);
        isVisible = attributes.MapState == X11Native.IsViewable;
        return true;
    }

    private int? ReadProcessId(nuint window)
    {
        var values = ReadProperty(processIdAtom, window);
        return values.Length == 0 || values[0] > int.MaxValue
            ? null
            : (int)values[0];
    }

    private nuint ReadSingleWindow(nuint atom)
    {
        var values = ReadProperty(atom, rootWindow);
        return values.Length == 0 ? 0 : values[0];
    }

    private nuint[] ReadWindowList(nuint atom)
    {
        return ReadProperty(atom, rootWindow);
    }

    private nuint[] ReadProperty(nuint atom, nuint window)
    {
        if (atom == 0
            || X11Native.XGetWindowProperty(
                display,
                window,
                atom,
                nint.Zero,
                (nint)PropertyReadLength,
                delete: 0,
                requestedType: 0,
                out _,
                out var actualFormat,
                out var itemCount,
                out _,
                out var propertyData) != 0
            || propertyData == nint.Zero)
        {
            return [];
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0 || itemCount > int.MaxValue)
            {
                return [];
            }

            var values = new nuint[(int)itemCount];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((nuint)Marshal.ReadIntPtr(
                    propertyData,
                    index * nint.Size));
            }

            return values;
        }
        finally
        {
            Free(propertyData);
        }
    }

    private nuint[] ReadRootChildren()
    {
        if (X11Native.XQueryTree(
                display,
                rootWindow,
                out _,
                out _,
                out var children,
                out var childCount) == 0
            || children == nint.Zero)
        {
            return [];
        }

        try
        {
            var values = new nuint[childCount];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((nuint)Marshal.ReadIntPtr(
                    children,
                    index * nint.Size));
            }

            return values;
        }
        finally
        {
            Free(children);
        }
    }

    private static string? ReadNativeString(nint value)
    {
        return value == nint.Zero ? null : Marshal.PtrToStringUTF8(value);
    }

    private static void Free(nint value)
    {
        if (value != nint.Zero)
        {
            _ = X11Native.XFree(value);
        }
    }
}
