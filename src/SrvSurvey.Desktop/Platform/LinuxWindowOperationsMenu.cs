using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Platform;

internal static class LinuxWindowOperationsMenu
{
    private const string TitleBarName = "PART_TitleBar";
    private const int ClientMessage = 33;

    /// <summary>Shows window operations only for a Linux title-bar right-click.</summary>
    internal static bool TryShow(Window window, PointerPressedEventArgs eventArgs) =>
        TryShow(window, eventArgs, OperatingSystem.IsLinux());

    /// <summary>Applies the title-bar input rule with an injectable platform result for testing.</summary>
    internal static bool TryShow(Window window, PointerPressedEventArgs eventArgs, bool isLinux)
    {
        if (
            !isLinux
            || !eventArgs.GetCurrentPoint(window).Properties.IsRightButtonPressed
            || !IsTitleBarSource(eventArgs.Source as Visual)
        )
        {
            return false;
        }

        if (!TryShowNative(window, window.PointToScreen(eventArgs.GetPosition(window))))
        {
            CreateFallbackMenu(window).Open(window);
        }

        return true;
    }

    /// <summary>Checks whether the pointer originated in Avalonia's drawn title bar.</summary>
    internal static bool IsTitleBarSource(Visual? source) =>
        source is Control { Name: TitleBarName }
        || (source?.GetVisualAncestors().OfType<Control>().Any(control => control.Name == TitleBarName) ?? false);

    /// <summary>Requests the compositor menu when the window has an X11 handle.</summary>
    private static bool TryShowNative(Window window, PixelPoint point)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
        {
            return false;
        }

        return TryShowNative(handle.Handle, point, X11WindowMenuClient.Instance);
    }

    /// <summary>Sends a supported X11 menu request and releases the display connection.</summary>
    internal static bool TryShowNative(nint window, PixelPoint point, IX11WindowMenuClient client)
    {
        nint display = nint.Zero;
        try
        {
            display = client.OpenDisplay();
            if (display == nint.Zero)
            {
                return false;
            }

            nuint menuAtom = client.GetMenuAtom(display);
            if (menuAtom == 0 || !client.GetSupportedAtoms(display).Contains(menuAtom))
            {
                return false;
            }

            X11Native.XClientMessageEvent menuEvent = CreateNativeMenuEvent(
                display,
                unchecked((nuint)window),
                menuAtom,
                point
            );
            return client.Send(display, ref menuEvent);
        }
        catch (Exception exception)
            when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
        finally
        {
            if (display != nint.Zero)
            {
                client.CloseDisplay(display);
            }
        }
    }

    /// <summary>Creates the X11 client message with the menu's screen coordinates.</summary>
    internal static X11Native.XClientMessageEvent CreateNativeMenuEvent(
        nint display,
        nuint window,
        nuint menuAtom,
        PixelPoint point
    ) =>
        new()
        {
            Type = ClientMessage,
            Display = display,
            Window = window,
            MessageType = menuAtom,
            Format = 32,
            Data = new X11Native.XClientMessageData
            {
                L0 = 0,
                L1 = point.X,
                L2 = point.Y,
            },
        };

    /// <summary>Creates window operations for hosts without the native menu protocol.</summary>
    internal static ContextMenu CreateFallbackMenu(Window window)
    {
        var minimize = new MenuItem { Header = "Minimize", IsEnabled = window.CanMinimize };
        minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;

        var maximize = new MenuItem
        {
            Header = window.WindowState == WindowState.Maximized ? "Restore" : "Maximize",
            IsEnabled = window.CanMaximize,
        };
        maximize.Click += (_, _) =>
            window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        var fullscreen = new MenuItem
        {
            Header = "Fullscreen",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = window.WindowState == WindowState.FullScreen,
        };
        fullscreen.Click += (_, _) =>
            window.WindowState =
                window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;

        var alwaysOnTop = new MenuItem
        {
            Header = "Always on top",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = window.Topmost,
        };
        alwaysOnTop.Click += (_, _) => window.Topmost = !window.Topmost;

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) => window.Close();

        return new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new[] { minimize, maximize, fullscreen, alwaysOnTop, close },
        };
    }
}

internal interface IX11WindowMenuClient
{
    /// <summary>Opens a connection to the X server.</summary>
    nint OpenDisplay();

    /// <summary>Finds the native window-menu atom.</summary>
    nuint GetMenuAtom(nint display);

    /// <summary>Reads atoms advertised by the window manager.</summary>
    nuint[] GetSupportedAtoms(nint display);

    /// <summary>Sends a client message to the window manager.</summary>
    bool Send(nint display, ref X11Native.XClientMessageEvent request);

    /// <summary>Releases the X server connection.</summary>
    void CloseDisplay(nint display);
}

internal sealed class X11WindowMenuClient : IX11WindowMenuClient
{
    private const nint WindowManagerEventMask = (1 << 19) | (1 << 20);

    public static X11WindowMenuClient Instance { get; } = new();

    /// <summary>Restricts native menu requests to the shared client instance.</summary>
    private X11WindowMenuClient() { }

    /// <summary>Opens the native X11 display.</summary>
    public nint OpenDisplay() => X11Native.XOpenDisplay(nint.Zero);

    /// <summary>Looks up the native window-menu atom without creating it.</summary>
    public nuint GetMenuAtom(nint display) => X11Native.XInternAtom(display, "_GTK_SHOW_WINDOW_MENU", onlyIfExists: 1);

    /// <summary>Reads the window manager's supported atom list.</summary>
    public nuint[] GetSupportedAtoms(nint display) =>
        X11OverlayPlatformService.ReadSupportedAtoms(display, atomType: 4);

    /// <summary>Sends the menu request to the X11 root window.</summary>
    public bool Send(nint display, ref X11Native.XClientMessageEvent request)
    {
        nuint root = X11Native.XDefaultRootWindow(display);
        bool sent = X11Native.XSendEvent(display, root, 0, WindowManagerEventMask, ref request) != 0;
        _ = X11Native.XFlush(display);
        return sent;
    }

    /// <summary>Closes the native X11 display.</summary>
    public void CloseDisplay(nint display) => _ = X11Native.XCloseDisplay(display);
}
