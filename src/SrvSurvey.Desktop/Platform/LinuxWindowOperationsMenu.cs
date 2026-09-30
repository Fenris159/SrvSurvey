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

    internal static bool TryShow(Window window, PointerPressedEventArgs eventArgs)
    {
        if (
            !OperatingSystem.IsLinux()
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

    internal static bool IsTitleBarSource(Visual? source) =>
        source is Control { Name: TitleBarName }
        || (source?.GetVisualAncestors().OfType<Control>().Any(control => control.Name == TitleBarName) ?? false);

    private static bool TryShowNative(Window window, PixelPoint point)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "XID" } handle)
        {
            return false;
        }

        return TryShowNative(handle.Handle, point, X11WindowMenuClient.Instance);
    }

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
    nint OpenDisplay();

    nuint GetMenuAtom(nint display);

    nuint[] GetSupportedAtoms(nint display);

    bool Send(nint display, ref X11Native.XClientMessageEvent request);

    void CloseDisplay(nint display);
}

internal sealed class X11WindowMenuClient : IX11WindowMenuClient
{
    private const nint WindowManagerEventMask = (1 << 19) | (1 << 20);

    public static X11WindowMenuClient Instance { get; } = new();

    private X11WindowMenuClient() { }

    public nint OpenDisplay() => X11Native.XOpenDisplay(nint.Zero);

    public nuint GetMenuAtom(nint display) => X11Native.XInternAtom(display, "_GTK_SHOW_WINDOW_MENU", onlyIfExists: 1);

    public nuint[] GetSupportedAtoms(nint display) =>
        X11OverlayPlatformService.ReadSupportedAtoms(display, atomType: 4);

    public bool Send(nint display, ref X11Native.XClientMessageEvent request)
    {
        nuint root = X11Native.XDefaultRootWindow(display);
        bool sent = X11Native.XSendEvent(display, root, 0, WindowManagerEventMask, ref request) != 0;
        _ = X11Native.XFlush(display);
        return sent;
    }

    public void CloseDisplay(nint display) => _ = X11Native.XCloseDisplay(display);
}
