using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ApplicationStartupWindowPresenterTests
{
    [AvaloniaFact]
    public void ReplacementStartupRestoresAndActivatesMainWindow()
    {
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var startupDialog = new Window();
        desktop.MainWindow = startupDialog;
        startupDialog.Show();
        startupDialog.Close();
        var mainWindow = new Window { ShowInTaskbar = false, WindowState = WindowState.Minimized };
        try
        {
            ApplicationStartupWindowPresenter.Present(desktop, mainWindow, bringToFront: true);

            Assert.Same(mainWindow, desktop.MainWindow);
            Assert.True(mainWindow.IsVisible);
            Assert.True(mainWindow.ShowInTaskbar);
            Assert.Equal(WindowState.Normal, mainWindow.WindowState);
        }
        finally
        {
            mainWindow.Close();
            desktop.MainWindow = null;
        }
    }

    [AvaloniaFact]
    public void OrdinaryStartupShowsMainWindowAfterDeferredAssignment()
    {
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var mainWindow = new Window { ShowInTaskbar = false, WindowState = WindowState.Minimized };
        try
        {
            ApplicationStartupWindowPresenter.Present(desktop, mainWindow, bringToFront: false);

            Assert.Same(mainWindow, desktop.MainWindow);
            Assert.True(mainWindow.IsVisible);
            Assert.True(mainWindow.ShowInTaskbar);
            // Ordinary startup shows the window but does not force Normal/Activate (replacement does).
            Assert.Equal(WindowState.Minimized, mainWindow.WindowState);
        }
        finally
        {
            mainWindow.Close();
            desktop.MainWindow = null;
        }
    }
}
