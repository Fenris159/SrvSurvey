using System.Runtime.InteropServices;
using Avalonia;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class X11GameWindowTracker : IGameWindowTracker
{
    /// <summary>Samples desktop window ownership and geometry for matching verified game processes to monitors.</summary>
    internal static IReadOnlyList<GameWindowSnapshot> ReadDesktopWindows()
    {
        using IGameWindowTracker? tracker = TryCreate();
        return tracker is X11GameWindowTracker native ? native.ReadDesktopWindowSnapshots() : [];
    }

    /// <summary>Reads mapped desktop clients without treating nested-display coordinates as desktop coordinates.</summary>
    private List<GameWindowSnapshot> ReadDesktopWindowSnapshots()
    {
        lock (gate)
        {
            nuint[] windows = ReadWindowList(clientListAtom);
            if (windows.Length == 0)
            {
                windows = ReadRootChildren();
            }
            nuint active = ReadSingleWindow(activeWindowAtom);
            var result = new List<GameWindowSnapshot>();
            foreach (nuint window in windows)
            {
                if (
                    ReadProcessId(window) is int pid
                    && TryGetBounds(window, out PixelRect bounds, out bool visible)
                    && visible
                )
                {
                    result.Add(new GameWindowSnapshot(unchecked((nint)window), pid, bounds, visible, window == active));
                }
            }
            return result;
        }
    }

    private const int PropertyReadLength = 16_384;
    private readonly Lock gate = new();
    private nint display;
    private readonly nuint rootWindow;
    private readonly nuint activeWindowAtom;
    private readonly nuint clientListAtom;
    private readonly nuint clientListStackingAtom;
    private readonly nuint processIdAtom;
    private readonly bool recoverTransientDisplay;
    private nuint gameWindow;
    private nuint inspectedActiveWindow;
    private bool inspectedActiveWindowIsElite;

    /// <summary>Initializes X11 atoms and recovery behavior for one display connection.</summary>
    private X11GameWindowTracker(nint display, bool recoverTransientDisplay)
    {
        this.display = display;
        this.recoverTransientDisplay = recoverTransientDisplay;
        rootWindow = X11Native.XDefaultRootWindow(display);
        activeWindowAtom = GetAtom("_NET_ACTIVE_WINDOW");
        clientListAtom = GetAtom("_NET_CLIENT_LIST");
        clientListStackingAtom = GetAtom("_NET_CLIENT_LIST_STACKING");
        processIdAtom = GetAtom("_NET_WM_PID");
    }

    /// <summary>Opens an X11 tracker, marking named transient displays for recovery.</summary>
    public static IGameWindowTracker? TryCreate(string? displayName = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        nint display = nint.Zero;
        nint displayNamePointer = nint.Zero;
        try
        {
            X11OverlayPlatformService.EnsureErrorHandlerInstalled();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                displayNamePointer = Marshal.StringToHGlobalAnsi(displayName);
            }

            display = X11Native.XOpenDisplay(displayNamePointer);
            if (display == nint.Zero)
            {
                return null;
            }

            bool recoverTransientDisplay = !string.IsNullOrWhiteSpace(displayName);
            if (recoverTransientDisplay)
            {
                X11TransientDisplayRecovery.Register(display);
            }

            X11OverlayPlatformService.RegisterErrorHandledDisplay(display);
            return new X11GameWindowTracker(display, recoverTransientDisplay);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            if (display != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(display);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(display);
                    X11TransientDisplayRecovery.Unregister(display);
                }
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(displayNamePointer);
        }
    }

    /// <summary>Returns the current game window or unavailable when its X11 display fails.</summary>
    public GameWindowSnapshot GetSnapshot()
    {
        lock (gate)
        {
            if (display == nint.Zero || HasFailedTransientDisplay())
            {
                return GameWindowSnapshot.Unavailable;
            }

            nuint activeWindow = ReadSingleWindow(activeWindowAtom);
            if (activeWindow != 0 && activeWindow != gameWindow && activeWindow != inspectedActiveWindow)
            {
                inspectedActiveWindow = activeWindow;
                inspectedActiveWindowIsElite = IsEliteWindow(activeWindow);
            }

            if (
                activeWindow != 0
                && (
                    activeWindow == gameWindow
                    || (activeWindow == inspectedActiveWindow && inspectedActiveWindowIsElite)
                )
            )
            {
                gameWindow = activeWindow;
            }

            if (gameWindow == 0 || !TryGetBounds(gameWindow, out _, out _))
            {
                gameWindow = FindGameWindow(activeWindow);
            }

            if (
                HasFailedTransientDisplay()
                || gameWindow == 0
                || !TryGetBounds(gameWindow, out PixelRect clientBounds, out bool isVisible)
            )
            {
                gameWindow = 0;
                return GameWindowSnapshot.Unavailable;
            }

            GameWindowSnapshot snapshot = new(
                unchecked((nint)gameWindow),
                ReadProcessId(gameWindow),
                clientBounds,
                isVisible,
                activeWindow == gameWindow
            )
            {
                DisplayBounds =
                    X11Native.XGetWindowAttributes(display, rootWindow, out X11Native.XWindowAttributes rootAttributes)
                    != 0
                        ? new PixelRect(0, 0, rootAttributes.Width, rootAttributes.Height)
                        : null,
            };
            return HasFailedTransientDisplay() ? GameWindowSnapshot.Unavailable : snapshot;
        }
    }

    /// <summary>Reports whether a recoverable nested X11 display has failed.</summary>
    private bool HasFailedTransientDisplay() =>
        recoverTransientDisplay && X11TransientDisplayRecovery.HasFailed(display);

    /// <summary>Closes the X11 display and unregisters its error handling.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            nint currentDisplay = display;
            display = nint.Zero;
            if (currentDisplay != nint.Zero)
            {
                try
                {
                    _ = X11Native.XCloseDisplay(currentDisplay);
                }
                finally
                {
                    X11OverlayPlatformService.UnregisterErrorHandledDisplay(currentDisplay);
                    if (recoverTransientDisplay)
                    {
                        X11TransientDisplayRecovery.Unregister(currentDisplay);
                    }
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
        nuint[] windows = ReadWindowList(clientListStackingAtom);
        if (windows.Length == 0)
        {
            windows = ReadWindowList(clientListAtom);
        }

        if (windows.Length == 0)
        {
            windows = ReadRootChildren();
        }

        nuint firstWindow = 0;
        foreach (nuint window in windows)
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
        if (X11Native.XGetClassHint(display, window, out X11Native.XClassHint classHint) != 0)
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
        if (X11Native.XFetchName(display, window, out nint nativeTitle) != 0)
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

        return EliteGameWindowIdentity.MatchesX11(resourceName, resourceClass, title);
    }

    private bool TryGetBounds(nuint window, out PixelRect bounds, out bool isVisible)
    {
        bounds = default;
        isVisible = false;
        if (
            X11Native.XGetWindowAttributes(display, window, out X11Native.XWindowAttributes attributes) == 0
            || attributes.Width <= 0
            || attributes.Height <= 0
            || X11Native.XTranslateCoordinates(display, window, rootWindow, 0, 0, out int rootX, out int rootY, out _)
                == 0
        )
        {
            return false;
        }

        bounds = new PixelRect(rootX, rootY, attributes.Width, attributes.Height);
        isVisible = attributes.MapState == X11Native.IsViewable;
        return true;
    }

    private int? ReadProcessId(nuint window)
    {
        nuint[] values = ReadProperty(processIdAtom, window);
        return values.Length == 0 || values[0] > int.MaxValue ? null : (int)values[0];
    }

    private nuint ReadSingleWindow(nuint atom)
    {
        nuint[] values = ReadProperty(atom, rootWindow);
        return values.Length == 0 ? 0 : values[0];
    }

    private nuint[] ReadWindowList(nuint atom)
    {
        return ReadProperty(atom, rootWindow);
    }

    private nuint[] ReadProperty(nuint atom, nuint window)
    {
        if (
            atom == 0
            || X11Native.XGetWindowProperty(
                display,
                window,
                atom,
                nint.Zero,
                (nint)PropertyReadLength,
                delete: 0,
                requestedType: 0,
                out _,
                out int actualFormat,
                out nuint itemCount,
                out _,
                out nint propertyData
            ) != 0
            || propertyData == nint.Zero
        )
        {
            return [];
        }

        try
        {
            if (actualFormat != 32 || itemCount == 0 || itemCount > int.MaxValue)
            {
                return [];
            }

            nuint[] values = new nuint[(int)itemCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((nuint)Marshal.ReadIntPtr(propertyData, index * nint.Size));
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
        if (
            X11Native.XQueryTree(display, rootWindow, out _, out _, out nint children, out uint childCount) == 0
            || children == nint.Zero
        )
        {
            return [];
        }

        try
        {
            nuint[] values = new nuint[childCount];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = unchecked((nuint)Marshal.ReadIntPtr(children, index * nint.Size));
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

internal static class X11TransientDisplayRecovery
{
    // Gamescope's nested X server ends with the game. Xlib exits the process on I/O failure
    // unless both its global error callback and the display's exit callback are handled.
    private static readonly Lock Gate = new();
    private static readonly HashSet<nint> RecoverableDisplays = [];
    private static readonly HashSet<nint> FailedDisplays = [];
    private static readonly XIoErrorHandler IoErrorHandler = HandleIoError;
    private static readonly XIoErrorExitHandler IoErrorExitHandler = static (_, _) => { };
    private static nint previousHandler;
    private static bool installed;

    /// <summary>Installs the process-wide I/O error handler and registers a transient display.</summary>
    public static void Register(nint display)
    {
        lock (Gate)
        {
            if (!installed)
            {
                nint handlerPointer = Marshal.GetFunctionPointerForDelegate(IoErrorHandler);
                previousHandler = X11Native.XSetIOErrorHandler(handlerPointer);
                installed = true;
            }

            RecoverableDisplays.Add(display);
            X11Native.XSetIOErrorExitHandler(
                display,
                Marshal.GetFunctionPointerForDelegate(IoErrorExitHandler),
                nint.Zero
            );
        }
    }

    /// <summary>Reports whether an X11 I/O error invalidated the display.</summary>
    public static bool HasFailed(nint display)
    {
        lock (Gate)
        {
            return FailedDisplays.Contains(display);
        }
    }

    /// <summary>Removes a display from transient I/O error tracking.</summary>
    public static void Unregister(nint display)
    {
        lock (Gate)
        {
            RecoverableDisplays.Remove(display);
            FailedDisplays.Remove(display);
        }
    }

    /// <summary>Records I/O failures on transient displays without letting Xlib exit the process.</summary>
    private static int HandleIoError(nint display)
    {
        lock (Gate)
        {
            if (RecoverableDisplays.Contains(display))
            {
                FailedDisplays.Add(display);
                return 0;
            }
        }

        try
        {
            return previousHandler == nint.Zero
                ? 0
                : Marshal.GetDelegateForFunctionPointer<XIoErrorHandler>(previousHandler)(display);
        }
        catch (Exception)
        {
            // Managed exceptions must never unwind through an Xlib callback.
            return 0;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XIoErrorHandler(nint display);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void XIoErrorExitHandler(nint display, nint userData);
}
