using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CarrierReadinessBindingTests
{
    [AvaloniaFact]
    public async Task PreviewIsQuietWithoutASnapshotAndUpdatesReadinessWhenDataArrives()
    {
        bool enabled = false;
        int maximum = 100;
        var reader = new ColonizationProjectPreviewViewModelTests.Reader(
            (_, _) =>
            {
                ColonizationProjectPreviewData data = ColonizationProjectPreviewViewModelTests.Data();
                return Task.FromResult<ColonizationProjectPreviewData?>(
                    data with
                    {
                        Project = data.Project with { MaximumRequired = maximum },
                    }
                );
            }
        );
        using var model = new ColonizationProjectPreviewViewModel(
            reader,
            "build",
            () => enabled,
            delayAsync: (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token)
        );
        ILogSink? previous = Logger.Sink;
        var log = new BindingLogSink();
        Logger.Sink = log;
        ColonizationProjectPreviewWindow? window = null;
        try
        {
            window = new ColonizationProjectPreviewWindow(model);
            window.Show();
            using WriteableBitmap? empty = window.CaptureRenderedFrame();
            Assert.Null(model.Snapshot);
            Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages));
            CarrierReadinessBar bar = window.FindControl<CarrierReadinessBar>("CarrierReadyProgress")!;
            Assert.Equal(0, bar.Maximum);

            enabled = true;
            await model.RefreshAsync();
            using WriteableBitmap? loaded = window.CaptureRenderedFrame();
            Assert.Equal(100, bar.Maximum);
            maximum = 200;
            await model.RefreshAsync();
            using WriteableBitmap? refreshed = window.CaptureRenderedFrame();
            Assert.Equal(200, bar.Maximum);
            Assert.True(log.Messages.Count == 0, string.Join(Environment.NewLine, log.Messages));
        }
        finally
        {
            window?.Close();
            Logger.Sink = previous;
        }
    }

    private sealed class BindingLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == "Binding";

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
