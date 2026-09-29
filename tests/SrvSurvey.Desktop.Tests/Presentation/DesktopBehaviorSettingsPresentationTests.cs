using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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
