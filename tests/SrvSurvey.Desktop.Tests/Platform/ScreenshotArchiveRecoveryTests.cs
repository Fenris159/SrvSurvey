using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class ScreenshotProcessingServiceTests
{
    [Fact]
    public async Task UnreadableCapturesCannotStarveAReadyImageOnLaterPolls()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string output = Path.Combine(temporaryDirectory, "output");
        Directory.CreateDirectory(source);
        CreateBitmap(Path.Combine(source, "ready.bmp"), SKColors.Blue);
        JournalEventEnvelope[] events =
        [
            Parse(
                """{"timestamp":"2026-11-03T05:06:51Z","event":"Screenshot","Filename":"missing-one.bmp","System":"Sol","Body":"Earth"}"""
            ),
            Parse(
                """{"timestamp":"2026-11-03T05:06:52Z","event":"Screenshot","Filename":"missing-two.bmp","System":"Sol","Body":"Earth"}"""
            ),
            Parse(
                """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"ready.bmp","System":"Sol","Body":"Earth"}"""
            ),
        ];
        var processor = new ScreenshotProcessingService(retryDelay: (_, _) => Task.CompletedTask);
        ScreenshotProcessingPreferences preferences = Preferences(source, output) with { AddBanner = false };
        var conversions = new List<ScreenshotConversion>();
        conversions.AddRange((await processor.ProcessLiveAsync(events, preferences, "Test")).Conversions);
        for (int poll = 0; poll < 2; poll++)
        {
            conversions.AddRange((await processor.ProcessLiveAsync([], preferences, "Test")).Conversions);
        }
        Assert.Equal(
            Path.Combine(output, "Sol", "Earth (2026-11-03 050653).png"),
            Assert.Single(conversions).OutputPath
        );
        Assert.Equal(2, processor.PendingCount);
    }

    [Fact]
    public async Task AnUnreadableCompletionArchiveCannotStrandReadablePendingCaptures()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string output = Path.Combine(temporaryDirectory, "output");
        string queue = Path.Combine(temporaryDirectory, "queue");
        Directory.CreateDirectory(source);
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"late.bmp","System":"Sol","Body":"Earth"}"""
        );
        ScreenshotProcessingPreferences preferences = Preferences(source, output) with { AddBanner = false };
        var initial = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        Assert.Equal(1, (await initial.ProcessLiveAsync([entry], preferences, "Test")).PendingCount);
        CreateBitmap(Path.Combine(source, "late.bmp"), SKColors.Blue);
        string archive = Path.Combine(queue, "steam-completed");
        Directory.CreateDirectory(archive);
        IEnumerable<string> ReadRecoveryFiles(string folder) =>
            folder == archive
                ? throw new UnauthorizedAccessException("Simulated archive listing failure")
                : Directory.EnumerateFiles(folder, "*.json");

        var recovered = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue,
            enumerateRecoveryFiles: ReadRecoveryFiles
        );
        Assert.Equal(1, recovered.PendingCount);
        ScreenshotProcessingResult result = await recovered.ProcessLiveAsync([], preferences, "Test");
        Assert.Equal(
            Path.Combine(output, "Sol", "Earth (2026-11-03 050653).png"),
            Assert.Single(result.Conversions).OutputPath
        );
        Assert.Single(result.Warnings);
        Assert.Equal(0, recovered.PendingCount);
        Assert.True(File.Exists(Path.Combine(source, "late.bmp")));
    }
}
