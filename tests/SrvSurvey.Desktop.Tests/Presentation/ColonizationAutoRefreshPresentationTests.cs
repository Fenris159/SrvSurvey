using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    [AvaloniaFact]
    public async Task AutoRefreshButtonShowsActivityAndExplainsSessionLimits()
    {
        using var http = new HttpClient(new RavenHandler());
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        ILogSink? previousSink = Logger.Sink;
        var animationLog = new AnimationLogSink();
        Logger.Sink = animationLog;
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        window.Resources["RavenControlAccentBrush"] = new SolidColorBrush(Color.Parse("#F5F5F5"));
        window.Resources["RavenAccentForegroundBrush"] = new SolidColorBrush(Color.Parse("#0A0A0A"));
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ToggleButton toggle = view.FindControl<ToggleButton>("AutoRefreshProjects")!;
            TextBlock spinner = view.FindControl<TextBlock>("AutoRefreshSpinner")!;
            Assert.Same(view.FindControl<Button>("RefreshProjects")!.Parent, toggle.Parent);
            Assert.True(toggle.IsEnabled);
            Assert.False(toggle.IsChecked);
            Assert.False(spinner.IsVisible);
            string help = Assert.IsType<string>(ToolTip.GetTip(toggle));
            Assert.Contains("60 seconds", help);
            Assert.Contains("Session only", help);
            Assert.Contains("one hour", help);
            toggle.IsChecked = true;
            Assert.True(main.Colonization.IsAutoRefreshEnabled);
            Assert.True(spinner.IsVisible);
            using WriteableBitmap? active = window.CaptureRenderedFrame();
            Assert.NotNull(active);
            Assert.All(
                toggle.GetVisualDescendants().OfType<TextBlock>(),
                label =>
                    Assert.Equal(
                        Color.Parse("#0A0A0A"),
                        Assert.IsType<ISolidColorBrush>(label.Foreground, exactMatch: false).Color
                    )
            );
            // The spinner must own the transform targeted by its animation, including after restyling.
            RotateTransform rotation = Assert.IsType<RotateTransform>(spinner.RenderTransform);
            double initialAngle = rotation.Angle;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(15);
            using WriteableBitmap? rotated = window.CaptureRenderedFrame();
            Assert.NotNull(rotated);
            Assert.NotEqual(initialAngle, rotation.Angle);
            active.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-auto-refresh-preview.png"),
                PngBitmapEncoderOptions.Default
            );
            toggle.IsChecked = false;
            await main.Colonization.AutoRefreshTask;
            Assert.False(spinner.IsVisible);
            main.Colonization.IsEnabled = false;
            Assert.False(toggle.IsEnabled);
            Assert.Empty(animationLog.Messages);
        }
        finally
        {
            window.Close();
            Logger.Sink = previousSink;
        }
    }

    private sealed class AnimationLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) =>
            level >= LogEventLevel.Warning && area == "Animations";

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate);
            }
        }

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
