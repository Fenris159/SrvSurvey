using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SrvSurvey.Desktop.Platform;

internal static class ApplicationStartupWindowPresenter
{
    public static void Present(IClassicDesktopStyleApplicationLifetime desktop, Window mainWindow, bool bringToFront)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(mainWindow);
        desktop.MainWindow = mainWindow;
        if (!bringToFront)
        {
            return;
        }

        mainWindow.ShowInTaskbar = true;
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }
}
