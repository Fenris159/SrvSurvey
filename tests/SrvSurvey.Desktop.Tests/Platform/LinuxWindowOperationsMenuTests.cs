using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class LinuxWindowOperationsMenuTests
{
    /// <summary>Accepts descendants of the drawn title bar while rejecting content.</summary>
    [AvaloniaFact]
    public void RightClickTargetMustBeInsideTheDrawnTitleBar()
    {
        var titleBar = new Panel { Name = "PART_TitleBar" };
        var titleChild = new Border();
        titleBar.Children.Add(titleChild);
        var content = new Border();
        var root = new Panel();
        root.Children.Add(titleBar);
        root.Children.Add(content);

        Assert.True(LinuxWindowOperationsMenu.IsTitleBarSource(titleBar));
        Assert.True(LinuxWindowOperationsMenu.IsTitleBarSource(titleChild));
        Assert.False(LinuxWindowOperationsMenu.IsTitleBarSource(content));
        Assert.False(LinuxWindowOperationsMenu.IsTitleBarSource(null));
    }

    /// <summary>Preserves the target window and screen coordinates in the X11 request.</summary>
    [Fact]
    public void NativeMenuRequestCarriesClientWindowAndRootCoordinates()
    {
        var position = new PixelPoint(5210, 750);

        SrvSurvey.Desktop.Platform.Overlay.X11Native.XClientMessageEvent request =
            LinuxWindowOperationsMenu.CreateNativeMenuEvent(42, 100, 200, position);

        Assert.Equal(33, request.Type);
        Assert.Equal((nint)42, request.Display);
        Assert.Equal((nuint)100, request.Window);
        Assert.Equal((nuint)200, request.MessageType);
        Assert.Equal(32, request.Format);
        Assert.Equal((nint)0, request.Data.L0);
        Assert.Equal((nint)position.X, request.Data.L1);
        Assert.Equal((nint)position.Y, request.Data.L2);
    }

    /// <summary>Provides working window actions when the native menu is unavailable.</summary>
    [AvaloniaFact]
    public void FallbackMenuProvidesWindowActionsWhenNativeMenuIsUnavailable()
    {
        var window = new Window();
        ContextMenu menu = LinuxWindowOperationsMenu.CreateFallbackMenu(window);
        MenuItem[] items = Assert.IsType<MenuItem[]>(menu.ItemsSource);

        Assert.Equal(
            ["Minimize", "Maximize", "Fullscreen", "Always on top", "Close"],
            items.Select(item => item.Header)
        );

        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.Maximized, window.WindowState);

        items[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.FullScreen, window.WindowState);

        items[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(window.Topmost);

        ContextMenu restoreMenu = LinuxWindowOperationsMenu.CreateFallbackMenu(window);
        MenuItem[] restoreItems = Assert.IsType<MenuItem[]>(restoreMenu.ItemsSource);
        restoreItems[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.WindowState = WindowState.Maximized;
        MenuItem[] maximizedItems = Assert.IsType<MenuItem[]>(
            LinuxWindowOperationsMenu.CreateFallbackMenu(window).ItemsSource
        );
        Assert.Equal("Restore", maximizedItems[1].Header);
        maximizedItems[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    /// <summary>Requires an advertised atom and successful send before reporting success.</summary>
    [Theory]
    [InlineData(0, 8, true, true, false, false)]
    [InlineData(1, 0, true, true, false, true)]
    [InlineData(1, 8, false, true, false, true)]
    [InlineData(1, 8, true, false, false, true)]
    [InlineData(1, 8, true, true, true, true)]
    public void NativeMenuRequiresAnAdvertisedAtomAndSuccessfulSend(
        int display,
        int atom,
        bool supportsAtom,
        bool sendSucceeds,
        bool expectedResult,
        bool expectedClose
    )
    {
        var client = new RecordingWindowMenuClient
        {
            Display = display,
            MenuAtom = (nuint)atom,
            SupportsAtom = supportsAtom,
            SendSucceeds = sendSucceeds,
        };

        bool result = LinuxWindowOperationsMenu.TryShowNative(42, new PixelPoint(250, 300), client);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedClose, client.WasClosed);
        Assert.Equal(expectedResult || (supportsAtom && atom != 0 && display != 0), client.WasSent);
        if (client.WasSent)
        {
            Assert.Equal((nuint)42, client.Request.Window);
            Assert.Equal((nint)250, client.Request.Data.L1);
            Assert.Equal((nint)300, client.Request.Data.L2);
        }
    }

    /// <summary>Falls back safely when an X11 entry point cannot be loaded.</summary>
    [Fact]
    public void UnavailableX11LibraryFallsBackAndClosesOpenedDisplay()
    {
        var client = new RecordingWindowMenuClient { ThrowFromSend = true };

        Assert.False(LinuxWindowOperationsMenu.TryShowNative(42, new PixelPoint(10, 20), client));
        Assert.True(client.WasClosed);
    }

    /// <summary>Routes only a Linux title-bar right-click to window operations.</summary>
    [AvaloniaFact]
    public void OnlyRightClickOnTitleBarOpensTheWindowMenu()
    {
        var titleBar = new StackPanel { Name = "PART_TitleBar" };
        var content = new Border();
        var root = new StackPanel { Children = { titleBar, content } };
        var window = new Window { Content = root };
        window.Show();
        using var pointer = new Pointer(1, PointerType.Mouse, true);
        try
        {
            Assert.False(LinuxWindowOperationsMenu.TryShow(window, Press(content, pointer, window, right: true), true));
            Assert.False(
                LinuxWindowOperationsMenu.TryShow(window, Press(titleBar, pointer, window, right: false), true)
            );
            Assert.False(
                LinuxWindowOperationsMenu.TryShow(window, Press(titleBar, pointer, window, right: true), false)
            );
            Assert.True(LinuxWindowOperationsMenu.TryShow(window, Press(titleBar, pointer, window, right: true), true));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Builds a synthetic pointer press against the requested control.</summary>
    private static PointerPressedEventArgs Press(Control target, Pointer pointer, Window window, bool right)
    {
        RawInputModifiers button = right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton;
        PointerUpdateKind update = right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed;
        return new PointerPressedEventArgs(
            target,
            pointer,
            window,
            new Point(10, 10),
            1,
            new PointerPointProperties(button, update),
            KeyModifiers.None,
            1
        );
    }

    private sealed class RecordingWindowMenuClient : IX11WindowMenuClient
    {
        public nint Display { get; init; } = 1;

        public nuint MenuAtom { get; init; } = 8;

        public bool SupportsAtom { get; init; } = true;

        public bool SendSucceeds { get; init; } = true;

        public bool ThrowFromSend { get; init; }

        public bool WasClosed { get; private set; }

        public bool WasSent { get; private set; }

        public SrvSurvey.Desktop.Platform.Overlay.X11Native.XClientMessageEvent Request { get; private set; }

        /// <summary>Returns the configured test display handle.</summary>
        public nint OpenDisplay() => Display;

        /// <summary>Returns the configured test menu atom.</summary>
        public nuint GetMenuAtom(nint display) => MenuAtom;

        /// <summary>Advertises the configured atom only when enabled.</summary>
        public nuint[] GetSupportedAtoms(nint display) => SupportsAtom ? [MenuAtom] : [];

        /// <summary>Records a menu request or simulates a missing X11 library.</summary>
        public bool Send(nint display, ref SrvSurvey.Desktop.Platform.Overlay.X11Native.XClientMessageEvent request)
        {
            if (ThrowFromSend)
            {
                throw new DllNotFoundException();
            }

            WasSent = true;
            Request = request;
            return SendSucceeds;
        }

        /// <summary>Records that the test display was closed.</summary>
        public void CloseDisplay(nint display) => WasClosed = true;
    }
}
