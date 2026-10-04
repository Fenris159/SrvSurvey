using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class DesktopBehaviorSettingsPresentationTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-desktop-behavior-presentation-{Guid.NewGuid():N}"
    );

    [AvaloniaFact]
    public void MonitorAndApplicationScaleSelectorsRenderInsideDesktopCard()
    {
        Directory.CreateDirectory(temporaryDirectory);
        string settingsPath = Path.Combine(temporaryDirectory, "config", "cross-platform-ui.json");
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(
            Path.Combine(temporaryDirectory, "journals"),
            builder =>
                builder
                    .WithAppDataPaths(
                        new AppDataPaths(
                            Path.Combine(temporaryDirectory, "config"),
                            Path.Combine(temporaryDirectory, "data"),
                            Path.Combine(temporaryDirectory, "cache"),
                            []
                        )
                    )
                    .WithDesktopBehaviorSettingsStore(new DesktopBehaviorSettingsStore(settingsPath))
                    .WithGameWindowSwitcher(new UnavailableGameWindowSwitcher())
        );
        var secondaryMonitor = new ApplicationMonitorOption("DISPLAY2", "DISPLAY2 - 2560 x 1440 - 100%");
        viewModel.DesktopBehavior.SetAvailableMonitors([secondaryMonitor]);
        viewModel.DesktopBehavior.SelectedMonitor = secondaryMonitor;
        viewModel.DesktopBehavior.SelectedApplicationWindowScale = ApplicationWindowScaleCatalog.All.Single(option =>
            option.Percent == 125
        );
        viewModel.SettingsWorkspace.SelectCategory("desktop");
        var settings = new SettingsView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1180,
            Height = 760,
            Content = settings,
        };

        try
        {
            window.Show();
            Border? card = settings.FindControl<Border>("DesktopBehaviorCard");
            ComboBox? monitor = settings.FindControl<ComboBox>("DefaultMonitorComboBox");
            ComboBox? scale = settings.FindControl<ComboBox>("ApplicationWindowScaleComboBox");
            Assert.NotNull(card);
            Assert.NotNull(monitor);
            Assert.NotNull(scale);
            card.BringIntoView();

            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Equal(secondaryMonitor, monitor.SelectedItem);
            Assert.Equal("125%", scale.SelectedItem?.ToString());
            Assert.InRange(monitor.Bounds.Width, 200, card.Bounds.Width);
            Assert.InRange(scale.Bounds.Width, 200, card.Bounds.Width);
            Point? monitorOrigin = monitor.TranslatePoint(default, card);
            Point? scaleOrigin = scale.TranslatePoint(default, card);
            Assert.NotNull(monitorOrigin);
            Assert.NotNull(scaleOrigin);
            Assert.True(monitorOrigin.Value.X < scaleOrigin.Value.X);
            Assert.True(scaleOrigin.Value.X + scale.Bounds.Width <= card.Bounds.Width);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Checks the monitor-first layout, bypass choice placement, and independent persistence.</summary>
    [AvaloniaFact]
    public void OverlayMonitorIsTheFirstGlobalBehaviorSettingAndPersistsIndependently()
    {
        using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(
            Path.Combine(temporaryDirectory, "journals"),
            builder =>
                builder.WithAppDataPaths(
                    new AppDataPaths(
                        Path.Combine(temporaryDirectory, "config"),
                        Path.Combine(temporaryDirectory, "data"),
                        Path.Combine(temporaryDirectory, "cache"),
                        []
                    )
                )
        );
        var monitorOption = new ApplicationMonitorOption("DP-1", "Ultrawide (Primary) - 5120 x 1440 - 100%");
        viewModel.OverlayBehavior.SetAvailableMonitors([monitorOption]);
        var settings = new OverlaySettingsView { DataContext = viewModel };
        var window = new Window
        {
            Width = 1100,
            Height = 800,
            Content = settings,
        };
        try
        {
            window.Show();
            ComboBox monitor = Assert.IsType<ComboBox>(settings.FindControl<ComboBox>("OverlayMonitorComboBox"));
            Grid monitorSetting = Assert.IsType<Grid>(settings.FindControl<Grid>("OverlayMonitorSetting"));
            Border card = Assert.IsType<Border>(settings.FindControl<Border>("GlobalOverlayBehaviorCard"));
            StackPanel content = Assert.IsType<StackPanel>(card.Child);
            Assert.Same(monitorSetting, content.Children[1]);
            StackPanel bypassSetting = Assert.IsType<StackPanel>(
                settings.FindControl<StackPanel>("BypassWindowManagementSetting")
            );
            Assert.Same(bypassSetting, content.Children[2]);
            CheckBox bypass = Assert.IsType<CheckBox>(settings.FindControl<CheckBox>("BypassWindowManagementCheckBox"));
            Assert.Equal("Bypass Window Management", bypass.Content);
            Assert.False(bypass.IsChecked);
            TextBlock warning = Assert.IsType<TextBlock>(
                settings.FindControl<TextBlock>("WindowManagementRestartWarning")
            );
            Assert.False(warning.IsVisible);
            bypass.IsChecked = true;
            Assert.True(warning.IsVisible);
            Assert.Equal("**App Restart Required", warning.Text);
            Assert.True(viewModel.OverlayBehavior.BypassWindowManagement);
            Assert.True(
                new OverlayBehaviorSettingsStore(viewModel.AppDataPaths.UiSettingsPath).Load().BypassWindowManagement
            );
            string? help = Assert.IsType<TextBlock>(bypassSetting.Children[2]).Text;
            Assert.Contains("X11/XWayland", help);
            Assert.Contains("window rules", help);
            Assert.Contains("position editor", help);
            Assert.Contains("Restart", help);
            bypass.IsChecked = false;
            Assert.False(warning.IsVisible);
            monitor.SelectedItem = monitorOption;
            CheckBox monitorLock = Assert.IsType<CheckBox>(
                settings.FindControl<CheckBox>("LockOverlaysToMonitorCheckBox")
            );
            monitorLock.IsChecked = true;
            Assert.True(viewModel.OverlayBehavior.LockToMonitor);
            Assert.True(new OverlayBehaviorSettingsStore(viewModel.AppDataPaths.UiSettingsPath).Load().LockToMonitor);

            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.InRange(monitor.Bounds.Width, 200, card.Bounds.Width);
            Assert.Equal("DP-1", viewModel.OverlayBehavior.PreferredMonitorId);
            Assert.Null(viewModel.DesktopBehavior.PreferredMonitorId);
            Assert.Same(viewModel.OverlayBehavior, viewModel.OverlayInteraction.OverlayBehavior);
            Assert.Equal(
                "DP-1",
                new OverlayBehaviorSettingsStore(viewModel.AppDataPaths.UiSettingsPath).Load().PreferredMonitorId
            );
            monitor.SelectedIndex = 0;
            Assert.Null(viewModel.OverlayBehavior.PreferredMonitorId);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayMonitorSelectionSurvivesReopeningSettings()
    {
        var paths = new AppDataPaths(
            Path.Combine(temporaryDirectory, "config"),
            Path.Combine(temporaryDirectory, "data"),
            Path.Combine(temporaryDirectory, "cache"),
            []
        )
        {
            SettingsFrontierId = "F123",
        };
        var monitorOption = new ApplicationMonitorOption("DP-2", "Right display");
        using (
            MainWindowViewModel first = MainWindowViewModelTestBuilder.Create(
                Path.Combine(temporaryDirectory, "journals"),
                builder => builder.WithAppDataPaths(paths)
            )
        )
        {
            first.OverlayBehavior.SetAvailableMonitors([monitorOption]);
            var settings = new OverlaySettingsView { DataContext = first };
            var window = new Window { Content = settings };
            try
            {
                window.Show();
                ComboBox monitor = Assert.IsType<ComboBox>(settings.FindControl<ComboBox>("OverlayMonitorComboBox"));
                monitor.SelectedItem = monitorOption;
                Assert.Equal("DP-2", new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().PreferredMonitorId);
            }
            finally
            {
                window.Close();
            }
        }

        using MainWindowViewModel reopened = MainWindowViewModelTestBuilder.Create(
            Path.Combine(temporaryDirectory, "journals"),
            builder => builder.WithAppDataPaths(paths)
        );
        Assert.Equal("DP-2", reopened.OverlayBehavior.PreferredMonitorId);
        var reopenedSettings = new OverlaySettingsView { DataContext = reopened };
        Assert.Equal("DP-2", reopened.OverlayBehavior.PreferredMonitorId);
        var reopenedWindow = new Window { Content = reopenedSettings };
        try
        {
            reopenedWindow.Show();
            Assert.Equal("DP-2", reopened.OverlayBehavior.PreferredMonitorId);
            reopened.OverlayBehavior.SetAvailableMonitors([monitorOption]);
            Dispatcher.UIThread.RunJobs();
            ComboBox monitor = Assert.IsType<ComboBox>(
                reopenedSettings.FindControl<ComboBox>("OverlayMonitorComboBox")
            );
            Assert.Equal("DP-2", reopened.OverlayBehavior.PreferredMonitorId);
            Assert.NotNull(reopenedWindow.CaptureRenderedFrame());
            Assert.Equal("DP-2", (monitor.SelectedItem as ApplicationMonitorOption)?.Id);
            Assert.Equal("DP-2", new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().PreferredMonitorId);
            reopened.OverlayBehavior.SetAvailableMonitors([
                new ApplicationMonitorOption("DP-2", "Right display - new resolution"),
            ]);
            Assert.Equal(
                "Right display - new resolution",
                (monitor.SelectedItem as ApplicationMonitorOption)?.DisplayName
            );
            Assert.Equal("DP-2", new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().PreferredMonitorId);
            monitor.SelectedIndex = 0;
            Assert.Null(new OverlayBehaviorSettingsStore(paths.UiSettingsPath).Load().PreferredMonitorId);
        }
        finally
        {
            reopenedWindow.Close();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class UnavailableGameWindowSwitcher : IGameWindowSwitcher
    {
        public int GetAvailableWindowCount() => 0;

        public bool TryActivateCurrent() => false;

        public bool TryActivateNext() => false;

        public void Dispose() { }
    }
}
