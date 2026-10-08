using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MainWindowGeometryTests
{
    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void UnchangedScreenNotificationPreservesUserSize(WindowState state)
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            window.Width = 1370;
            window.Height = 870;
            Layout(window);
            window.WindowState = state;
            NotifyScreensChanged(window);
            Assert.Equal(state, window.WindowState);
            Layout(window);
            Assert.Equal(1370, window.Width);
            Assert.Equal(870, window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.Minimized)]
    public void UserSizeAndStateSurviveRestart(WindowState state)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-window-geometry-{Guid.NewGuid():N}");
        var store = new DesktopBehaviorSettingsStore(Path.Combine(directory, "ui-settings.json"));
        using MainWindowViewModel firstViewModel = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithDesktopBehaviorSettingsStore(store)
        );
        var first = new MainWindow(firstViewModel);
        try
        {
            first.Show();
            first.Width = 1370;
            first.Height = 870;
            Layout(first);
            first.WindowState = state;
            first.RememberCurrentPositionForShutdown();
        }
        finally
        {
            first.Close();
        }

        using MainWindowViewModel secondViewModel = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithDesktopBehaviorSettingsStore(store)
        );
        var second = new MainWindow(secondViewModel);
        try
        {
            second.Show();
            Assert.Equal(state == WindowState.Minimized ? WindowState.Normal : state, second.WindowState);
            second.WindowState = WindowState.Normal;
            Layout(second);
            Assert.Equal(1370, second.Width);
            Assert.Equal(870, second.Height);
        }
        finally
        {
            second.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivationAndTrayRestoreKeepMaximizedState(bool minimized)
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            window.WindowState = WindowState.Maximized;
            if (minimized)
            {
                window.WindowState = WindowState.Minimized;
            }
            window.RestoreAndActivate();
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplicationScaleChangeResetsToItsScaledDefault()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(null, _ => { });
        var window = new MainWindow(viewModel);
        try
        {
            window.Show();
            window.Width = 1370;
            window.Height = 870;
            Layout(window);
            viewModel.DesktopBehavior.SelectedApplicationWindowScale = ApplicationWindowScaleCatalog.All.Single(
                option => option.Percent == 125
            );
            Assert.Equal(1475, window.Width);
            Assert.Equal(950, window.Height);
        }
        finally
        {
            window.Close();
        }
    }

    private static void NotifyScreensChanged(MainWindow window)
    {
        // Exercise the actual callback: headless screens cannot emit native display notifications.
        typeof(MainWindow)
            .GetMethod("OnScreensChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [window.Screens, EventArgs.Empty]);
    }

    private static void Layout(Window window)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
