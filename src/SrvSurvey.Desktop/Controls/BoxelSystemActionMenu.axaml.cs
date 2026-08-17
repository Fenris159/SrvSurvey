using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class BoxelSystemActionMenu : UserControl
{
    private readonly DispatcherTimer closeTimer;

    public BoxelSystemActionMenu()
    {
        InitializeComponent();
        closeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        closeTimer.Tick += CloseTimer_Tick;
        DetachedFromVisualTree += (_, _) => CloseMenu();
    }

    private void Menu_PointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        closeTimer.Stop();
        OpenMenu();
    }

    private void Menu_PointerExited(object? sender, PointerEventArgs eventArgs)
    {
        closeTimer.Stop();
        closeTimer.Start();
    }

    private void Launcher_Click(object? sender, RoutedEventArgs eventArgs)
    {
        if (MenuPopup.IsOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    private void Launcher_KeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape)
        {
            return;
        }

        CloseMenu();
        eventArgs.Handled = true;
    }

    private void Action_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(CloseMenu, DispatcherPriority.Background);
    }

    private void CloseTimer_Tick(object? sender, EventArgs eventArgs)
    {
        closeTimer.Stop();
        if (!Launcher.IsPointerOver
            && !MenuSurface.IsPointerOver
            && !MenuSurface.IsKeyboardFocusWithin)
        {
            CloseMenu();
        }
    }

    private void OpenMenu()
    {
        closeTimer.Stop();
        if (!MenuPopup.IsOpen)
        {
            MenuPopup.IsOpen = true;
        }

        if (!Launcher.Classes.Contains("open"))
        {
            Launcher.Classes.Add("open");
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (MenuPopup.IsOpen && !MenuSurface.Classes.Contains("open"))
            {
                MenuSurface.Classes.Add("open");
            }
        });
    }

    private void CloseMenu()
    {
        closeTimer.Stop();
        Launcher.Classes.Remove("open");
        MenuSurface.Classes.Remove("open");
        MenuPopup.IsOpen = false;
    }
}
