using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Platform;

internal static class ApplicationStartupWindowPresenter
{
    public static void Present(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window mainWindow,
        bool bringToFront,
        Action? onReady = null
    )
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

        if (bringToFront)
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }
        if (onReady is not null)
        {
            mainWindow.RequestAnimationFrame(_ =>
                Dispatcher.UIThread.Post(
                    () =>
                    {
                        if (mainWindow.IsVisible && ReferenceEquals(desktop.MainWindow, mainWindow))
                        {
                            onReady();
                        }
                    },
                    DispatcherPriority.Background
                )
            );
        }
    }
}
