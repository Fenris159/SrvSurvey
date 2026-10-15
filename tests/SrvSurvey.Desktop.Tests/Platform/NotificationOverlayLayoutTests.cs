using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class NotificationOverlayLayoutTests
{
    /// <summary>Countdown painting never invalidates measurement, including invalid or out-of-range percentages.</summary>
    [AvaloniaTheory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(double.NaN)]
    public void LifetimeChangesOnlyInvalidateRendering(double percent)
    {
        var bar = new NotificationLifetimeBar
        {
            Height = 2,
            TrackBrush = Avalonia.Media.Brushes.Gray,
            FillBrush = Avalonia.Media.Brushes.Green,
        };
        var window = new Window
        {
            Width = 200,
            Height = 20,
            Content = bar,
        };
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            bar.Percent = percent;
            Assert.True(bar.IsMeasureValid);
            Assert.True(window.IsMeasureValid);
            using WriteableBitmap? changed = window.CaptureRenderedFrame();
            Assert.Equal(2, bar.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Real notification coordinator reconciliation must settle between bursts and timer ticks.</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(10)]
    public void HostedNotificationBurstsDoNotCycle(int scaleIndex)
    {
        string settingsPath = Path.GetTempFileName();
        var model = new NotificationViewModel(new NotificationSettingsStore(settingsPath));
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        layout.SetScaleIndex(scaleIndex);
        using var harness = new HostedOverlayTestHarness(overlayLayout: layout);
        using var coordinator = new NotificationOverlayCoordinator(model, harness.Session);
        ILogSink? previous = Logger.Sink;
        var log = new LayoutLogSink();
        Logger.Sink = log;
        try
        {
            for (int index = 0; index < 50; index++)
            {
                model.ShowMessage("Collected: 3x Chemical Distillery, new total " + index);
                harness.Tick();
                Window window = harness.PreparedWindows[^1];
                using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
            }
            Assert.Empty(log.Messages);
        }
        finally
        {
            Logger.Sink = previous;
            File.Delete(settingsPath);
        }
    }

    /// <summary>A timer must not retain a wider layout after the visible messages become short.</summary>
    [AvaloniaFact]
    public void NotificationShrinksAfterWideMessageLeavesVisibleRows()
    {
        string settingsPath = Path.GetTempFileName();
        var model = new NotificationViewModel(new NotificationSettingsStore(settingsPath));
        var window = new NotificationOverlayWindow(model);
        ILogSink? previous = Logger.Sink;
        var log = new LayoutLogSink();
        Logger.Sink = log;
        try
        {
            OverlayThemeResources.Apply(window, LegacyOverlayLayout.Empty, "PlotFloatie", new OverlayWindowRegistry());
            model.ShowMessage(new string('x', 500));
            window.Show();
            using WriteableBitmap? wide = window.CaptureRenderedFrame();
            Assert.InRange(window.Bounds.Width, 300, 360);
            for (int index = 0; index < 3; index++)
            {
                model.ShowMessage("OK " + index);
                using WriteableBitmap? shrinking = window.CaptureRenderedFrame();
            }
            Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages));
            Assert.InRange(window.Bounds.Width, 160, 220);
        }
        finally
        {
            window.Close();
            Logger.Sink = previous;
            File.Delete(settingsPath);
        }
    }

    /// <summary>Long service warnings and changing short notifications must settle without a layout cycle.</summary>
    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(5, 1)]
    [InlineData(10, 1)]
    [InlineData(0, 1.5)]
    [InlineData(5, 1.5)]
    [InlineData(10, 1.5)]
    [InlineData(0, 2)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    public void LongWarningsSettleWhenMessagesAndProgressChange(int scaleIndex, double renderScaling)
    {
        string settingsPath = Path.GetTempFileName();
        var model = new NotificationViewModel(new NotificationSettingsStore(settingsPath));
        var window = new NotificationOverlayWindow(model);
        ILogSink? previous = Logger.Sink;
        var log = new LayoutLogSink();
        Logger.Sink = log;
        try
        {
            window.SetRenderScaling(renderScaling);
            OverlayThemeResources.Apply(
                window,
                new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null),
                "PlotFloatie",
                new OverlayWindowRegistry()
            );
            OverlayThemeResources.ApplyScale(window, scaleIndex, renderScaling);
            window.Show();
            for (int index = 0; index < 12; index++)
            {
                model.ShowMessage(
                    index % 2 == 0
                        ? "Active Raven quests could not be loaded: " + new string('x', 1800) + " " + index
                        : "Cargo.json was ignored because multiple Elite windows prevent safe commander attribution. "
                            + index
                );
                model.Refresh();
                using WriteableBitmap? bitmap = window.CaptureRenderedFrame();
                window.UpdateLayout();
            }
            Assert.Empty(log.Messages);
        }
        finally
        {
            window.Close();
            Logger.Sink = previous;
            File.Delete(settingsPath);
        }
    }

    /// <summary>Records layout warnings from the real Avalonia layout manager.</summary>
    private sealed class LayoutLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        /// <summary>Captures the layout warning level used by Avalonia's cycle detector.</summary>
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == "Layout";

        /// <summary>Records unstructured layout warnings.</summary>
        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate);
            }
        }

        /// <summary>Retains the affected control and enqueue count for a reproduced layout cycle.</summary>
        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues
        )
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate + " " + string.Join(" ", propertyValues));
            }
        }
    }
}
