using System.Security.Cryptography;
using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class ScreenshotProcessingServiceTests
{
    [Fact]
    public async Task SuccessfulRemovalsDoNotRestartTheFailedPrefixBeforeEveryReadyImage()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        var events = new List<JournalEventEnvelope>();
        for (int i = 0; i < 7; i++)
        {
            if (i >= 4)
            {
                CreateBitmap(Path.Combine(source, $"{i}.bmp"), SKColors.Blue);
            }
            events.Add(
                Parse(
                    $$"""{"timestamp":"2026-11-03T05:06:0{{i}}Z","event":"Screenshot","Filename":"{{i}}.bmp","System":"Sol","Body":"Earth"}"""
                )
            );
        }
        var processor = new ScreenshotProcessingService(retryDelay: (_, _) => Task.CompletedTask);
        ScreenshotProcessingPreferences preferences = Preferences(
            source,
            Path.Combine(temporaryDirectory, "output")
        ) with
        {
            AddBanner = false,
        };
        var results = new List<ScreenshotConversion>();
        for (int i = 0; i < 5; i++)
        {
            results.AddRange((await processor.ProcessLiveAsync(i == 0 ? events : [], preferences, "Test")).Conversions);
        }
        Assert.Equal(3, results.Count);
        Assert.Equal(4, processor.PendingCount);
    }

    [Fact]
    public async Task AFailedAdmissionSaveIsRetriedBeforeTheSourceArrivesAndSurvivesRestart()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        string queue = Path.Combine(temporaryDirectory, "queue");
        await File.WriteAllTextAsync(queue, "blocked queue");
        var processor = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        ScreenshotProcessingPreferences preferences = Preferences(
            source,
            Path.Combine(temporaryDirectory, "output")
        ) with
        {
            AddBanner = false,
        };
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"late.bmp","System":"Sol","Body":"Earth"}"""
        );
        Assert.Equal(1, (await processor.ProcessLiveAsync([entry], preferences, "Test")).PendingCount);
        File.Delete(queue);
        await processor.ProcessLiveAsync([], preferences, "Test");
        var restarted = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        Assert.Equal(1, restarted.PendingCount);
        CreateBitmap(Path.Combine(source, "late.bmp"), SKColors.Blue);
        Assert.Single((await restarted.ProcessLiveAsync([], preferences, "Test")).Conversions);
    }

    [Fact]
    public async Task ARepairedImageWithUnchangedMetadataIsEventuallyRetriedWithoutHotPolling()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        Directory.CreateDirectory(source);
        string bmp = Path.Combine(source, "repaired.bmp");
        CreateBitmap(bmp, SKColors.Blue);
        byte[] valid = await File.ReadAllBytesAsync(bmp);
        await File.WriteAllBytesAsync(bmp, new byte[valid.Length]);
        var stamp = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(bmp, stamp);
        int delays = 0;
        var clock = new FollowupClock();
        var service = new ScreenshotProcessingService(
            retryDelay: (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            },
            timeProvider: clock
        );
        ScreenshotProcessingPreferences preferences = Preferences(
            source,
            Path.Combine(temporaryDirectory, "output")
        ) with
        {
            AddBanner = false,
        };
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"repaired.bmp","System":"Sol","Body":"Earth"}"""
        );
        await service.ProcessLiveAsync([entry], preferences, "Test");
        int initial = delays;
        await service.ProcessLiveAsync([], preferences, "Test");
        Assert.Equal(initial, delays);
        await File.WriteAllBytesAsync(bmp, valid);
        File.SetLastWriteTimeUtc(bmp, stamp);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single((await service.ProcessLiveAsync([], preferences, "Test")).Conversions);
    }

    [Fact]
    public async Task SameSecondCollisionsKeepDistinctImagesWithoutRehashingTheGrowingPrefix()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string output = Path.Combine(temporaryDirectory, "output");
        Directory.CreateDirectory(source);
        int reads = 0;
        string Hash(string path)
        {
            if (path.EndsWith(".png", StringComparison.Ordinal))
            {
                reads++;
            }
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        var service = new ScreenshotProcessingService(retryDelay: (_, _) => Task.CompletedTask, readImageHash: Hash);
        ScreenshotProcessingPreferences preferences = Preferences(source, output) with { AddBanner = false };
        var conversions = new List<ScreenshotConversion>();
        for (int i = 0; i < 8; i++)
        {
            CreateBitmap(Path.Combine(source, $"{i}.bmp"), new SKColor((byte)i, (byte)(255 - i), 0));
            JournalEventEnvelope entry = Parse(
                $$"""{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"{{i}}.bmp","System":"Sol","Body":"Earth"}"""
            );
            conversions.Add(Assert.Single((await service.ProcessLiveAsync([entry], preferences, "Test")).Conversions));
        }
        Assert.Equal(8, conversions.Select(item => item.OutputPath).Distinct().Count());
        Assert.InRange(reads, 0, 8);
        CreateBitmap(Path.Combine(source, "same.bmp"), new SKColor(0, 255, 0));
        ScreenshotConversion reused = Assert.Single(
            (
                await service.ProcessLiveAsync(
                    [
                        Parse(
                            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"same.bmp","System":"Sol","Body":"Earth"}"""
                        ),
                    ],
                    preferences,
                    "Test"
                )
            ).Conversions
        );
        Assert.Equal(conversions[0].OutputPath, reused.OutputPath);
    }

    private sealed class FollowupClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        internal void Advance(TimeSpan delay) => now += delay;
    }
}
