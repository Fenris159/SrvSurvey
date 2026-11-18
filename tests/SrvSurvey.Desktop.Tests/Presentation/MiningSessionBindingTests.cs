using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningSessionBindingTests
{
    [AvaloniaFact]
    public void EmptyChangedAndClearedSessionDetailsRemainQuietAndAccurate()
    {
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(null);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        ILogSink? previousSink = Logger.Sink;
        var log = new BindingLogSink();
        Window? window = null;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            main.MiningWorkspace.SelectedTab = 1;
            main.MiningWorkspace.SelectedSession = null;
            Logger.Sink = log;
            var view = new MiningView { DataContext = main };
            window = new Window
            {
                Content = view,
                Width = 1000,
                Height = 800,
            };
            window.Show();
            Render(window);
            Border report = view.FindControl<Border>("SelectedReport")!;
            Assert.Contains(
                "Choose a session above to edit notes, attach screenshots or export its report.",
                VisibleText(report)
            );
            Assert.Empty(log.Messages);

            main.MiningWorkspace.SelectedSession = new MiningSession
            {
                Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
                System = "First system",
                Ring = "First ring",
                Ship = "First ship",
                Started = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero),
                Ended = new(2026, 11, 18, 12, 30, 0, TimeSpan.Zero),
            };
            Render(window);
            Assert.Contains("First system", VisibleText(report));
            Assert.Contains("First ring", VisibleText(report));
            Assert.Contains("First ship", VisibleText(report));
            Assert.Contains("11/18/2026 12:00:00 +00:00", VisibleText(report));

            main.MiningWorkspace.SelectedSession = new MiningSession
            {
                Id = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100"),
                System = "Second system",
                Ring = "Second ring",
                Ship = "Second ship",
                Started = new(2026, 11, 18, 13, 15, 0, TimeSpan.Zero),
                Ended = new(2026, 11, 18, 13, 45, 0, TimeSpan.Zero),
            };
            Render(window);
            Assert.Contains("Second system", VisibleText(report));
            Assert.Contains("Second ring", VisibleText(report));
            Assert.Contains("Second ship", VisibleText(report));
            Assert.Contains("11/18/2026 13:15:00 +00:00", VisibleText(report));
            Assert.DoesNotContain("First system", VisibleText(report));

            main.MiningWorkspace.SelectedSession = null;
            Render(window);
            Assert.Contains(
                "Choose a session above to edit notes, attach screenshots or export its report.",
                VisibleText(report)
            );
            Assert.DoesNotContain("Second system", VisibleText(report));
            Assert.Empty(log.Messages);
        }
        finally
        {
            window?.Close();
            Logger.Sink = previousSink;
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static string?[] VisibleText(Control parent) =>
        parent
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible)
            .Select(text => text.Text)
            .ToArray();

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
    }

    private sealed class BindingLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => area == "Binding" && level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);

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
                Messages.Add(messageTemplate + " " + string.Join(" | ", propertyValues));
            }
        }
    }
}
