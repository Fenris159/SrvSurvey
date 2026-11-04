using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class SteamScreenshotSettingsTests
{
    [Fact]
    public async Task ABlockedSteamDestinationHasBoundedPollLatencyAndRecoversEveryQueuedImage()
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            """
        );
        string folder = Platform.SteamScreenshotSourceTests.CreateAccount(Path.Combine(directory, "Steam"), "42");
        for (int i = 0; i < 20; i++)
        {
            Platform.SteamScreenshotProcessingTests.WriteJpeg(Path.Combine(folder, $"197001010506{i:D2}_1.jpg"));
        }
        string output = Path.Combine(directory, "converted");
        await File.WriteAllTextAsync(output, "blocked output directory");
        var settings = new ScreenshotProcessingSettingsStore(Path.Combine(directory, "settings.json"));
        settings.Save(
            ScreenshotProcessingPreferences.CreateDefaults(journals) with
            {
                Enabled = true,
                ProcessSteamScreenshots = true,
                SourceFolder = folder,
                SteamSourceFolder = folder,
                TargetFolder = output,
                AddBanner = false,
                DeleteOriginal = true,
            }
        );
        TimeSpan waits = TimeSpan.Zero;
        var processor = new ScreenshotProcessingService(
            retryDelay: (delay, _) =>
            {
                waits += delay;
                return Task.CompletedTask;
            }
        );
        var clock = new SteamReviewClock();
        var model = new ScreenshotProcessingViewModel(
            settings,
            processor,
            journals,
            steamScreenshotSource: new SteamScreenshotSource([], TimeZoneInfo.Utc),
            timeProvider: clock
        );

        ScreenshotProcessingResult failed = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");

        Assert.Empty(failed.Conversions);
        Assert.Equal(20, failed.PendingCount);
        Assert.Equal(20, Directory.GetFiles(folder, "*.jpg").Length);
        Assert.InRange(waits, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        waits = TimeSpan.Zero;
        clock.Advance();
        await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
        Assert.InRange(waits, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        File.Delete(output);
        for (int i = 0; i < 20; i++)
        {
            clock.Advance();
            ScreenshotProcessingResult restored = await model.ProcessJournalEventsAsync([], "Test", frontierId: "F123");
            Assert.Single(restored.Conversions);
        }
        Assert.Equal(0, processor.PendingCount);
        Assert.Empty(Directory.GetFiles(folder, "*.jpg"));
        Assert.Equal(20, Directory.GetFiles(output, "*.png", SearchOption.AllDirectories).Length);
    }

    private sealed class SteamReviewClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance() => now = now.AddSeconds(6);
    }
}
