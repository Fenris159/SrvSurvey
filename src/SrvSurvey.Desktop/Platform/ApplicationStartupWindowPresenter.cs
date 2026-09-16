using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace SrvSurvey.Desktop.Platform;

internal static class ApplicationStartupWindowPresenter
{
    public static void Present(IClassicDesktopStyleApplicationLifetime desktop, Window mainWindow, bool bringToFront)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(mainWindow);
        // Avalonia only Show()s MainWindow once at lifetime start. Async startup assigns the
        // real window after that one-shot, so Present must always Show() here.
        desktop.MainWindow = mainWindow;
        mainWindow.ShowInTaskbar = true;
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (!bringToFront)
        {
            return;
        }

        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }
}
