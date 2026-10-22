using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ApplicationStartupWindowPresenterTests
{
    [AvaloniaFact]
    public void ReadinessWaitsForAFrameAndRunsOnceOnTheUiDispatcher()
    {
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var window = new Window();
        int confirmations = 0;
        try
        {
            ApplicationStartupWindowPresenter.Present(
                desktop,
                window,
                true,
                () =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    Assert.True(window.IsVisible);
                    confirmations++;
                }
            );
            Assert.Equal(0, confirmations);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, confirmations);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, confirmations);
        }
        finally
        {
            window.Close();
            desktop.MainWindow = null;
        }
    }

    [AvaloniaFact]
    public void AWindowClosedBeforeItsFirstFrameCannotConfirmStartup()
    {
        var desktop = new ClassicDesktopStyleApplicationLifetime();
        var window = new Window();
        bool confirmed = false;
        ApplicationStartupWindowPresenter.Present(desktop, window, true, () => confirmed = true);
        window.Close();

        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Dispatcher.UIThread.RunJobs();

        Assert.False(confirmed);
        desktop.MainWindow = null;
    }

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
