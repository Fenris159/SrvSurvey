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
        $"SrvSurvey-desktop-behavior-presentation-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public void MonitorAndApplicationScaleSelectorsRenderInsideDesktopCard()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var settingsPath = Path.Combine(
            temporaryDirectory,
            "config",
            "cross-platform-ui.json");
        using var viewModel = MainWindowViewModelTestBuilder.Create(
            Path.Combine(temporaryDirectory, "journals"),
            builder => builder
                .WithAppDataPaths(
                    new AppDataPaths(
                        Path.Combine(temporaryDirectory, "config"),
                        Path.Combine(temporaryDirectory, "data"),
                        Path.Combine(temporaryDirectory, "cache"),
                        []))
                .WithDesktopBehaviorSettingsStore(new DesktopBehaviorSettingsStore(settingsPath))
                .WithGameWindowSwitcher(new UnavailableGameWindowSwitcher()));
        var secondaryMonitor = new ApplicationMonitorOption(
            "DISPLAY2",
            "DISPLAY2 - 2560 x 1440 - 100%");
        viewModel.DesktopBehavior.SetAvailableMonitors([secondaryMonitor]);
        viewModel.DesktopBehavior.SelectedMonitor = secondaryMonitor;
        viewModel.DesktopBehavior.SelectedApplicationWindowScale =
            ApplicationWindowScaleCatalog.All.Single(option =>
                option.Percent == 125);
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
            var card = settings.FindControl<Border>("DesktopBehaviorCard");
            var monitor = settings.FindControl<ComboBox>(
                "DefaultMonitorComboBox");
            var scale = settings.FindControl<ComboBox>(
                "ApplicationWindowScaleComboBox");
            Assert.NotNull(card);
            Assert.NotNull(monitor);
            Assert.NotNull(scale);
            card.BringIntoView();

            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Equal(secondaryMonitor, monitor.SelectedItem);
            Assert.Equal("125%", scale.SelectedItem?.ToString());
            Assert.InRange(monitor.Bounds.Width, 200, card.Bounds.Width);
            Assert.InRange(scale.Bounds.Width, 200, card.Bounds.Width);
            var monitorOrigin = monitor.TranslatePoint(default, card);
            var scaleOrigin = scale.TranslatePoint(default, card);
            Assert.NotNull(monitorOrigin);
            Assert.NotNull(scaleOrigin);
            Assert.True(monitorOrigin.Value.X < scaleOrigin.Value.X);
            Assert.True(
                scaleOrigin.Value.X + scale.Bounds.Width
                <= card.Bounds.Width);
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

        public void Dispose()
        {
        }
    }
}
