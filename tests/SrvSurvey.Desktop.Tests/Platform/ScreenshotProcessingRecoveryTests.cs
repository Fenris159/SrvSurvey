using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class ScreenshotProcessingServiceTests
{
    [Fact]
    public async Task AConcurrentPublicationPreservesBothImagesBeforeDeletingTheOriginal()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(source);
        string bitmap = Path.Combine(source, "blue.bmp");
        CreateBitmap(bitmap, SKColors.Blue);
        bool collided = false;
        bool Publish(string pending, string destination)
        {
            if (!collided)
            {
                collided = true;
                using var other = new SKBitmap(320, 180);
                other.Erase(SKColors.Red);
                using SKData data = other.Encode(SKEncodedImageFormat.Png, 100);
                using FileStream stream = File.Create(destination);
                data.SaveTo(stream);
                return false;
            }
            File.Move(pending, destination, false);
            return true;
        }
        var service = new ScreenshotProcessingService(
            () => 320,
            (_, _) => Task.CompletedTask,
            tryPublishImage: Publish
        );
        ScreenshotProcessingResult result = await service.ProcessLiveAsync(
            [
                Parse(
                    """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\blue.bmp","System":"Sol","Body":"Earth"}"""
                ),
            ],
            Preferences(source, target) with
            {
                AddBanner = false,
                DeleteOriginal = true,
            },
            null
        );
        ScreenshotConversion converted = Assert.Single(result.Conversions);
        Assert.True(converted.SourceDeleted);
        using var ours = SKBitmap.Decode(converted.OutputPath);
        Assert.Equal(SKColors.Blue, ours.GetPixel(100, 100));
        using var otherImage = SKBitmap.Decode(Path.Combine(target, "Sol", "Earth (2026-11-03 050653).png"));
        Assert.Equal(SKColors.Red, otherImage.GetPixel(100, 100));
        Assert.Equal(2, Directory.GetFiles(target, "*.png", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task RecoveryAfterPublicationReusesThePngBeforeDeletingItsOriginal()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        string queue = Path.Combine(temporaryDirectory, "queue");
        string heldQueue = Path.Combine(temporaryDirectory, "held-queue");
        Directory.CreateDirectory(source);
        bool interruptPersistence = false;
        Task Delay(TimeSpan _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (interruptPersistence && Directory.Exists(queue))
            {
                Directory.Move(queue, heldQueue);
                File.WriteAllText(queue, "unavailable destination");
            }
            return Task.CompletedTask;
        }
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\late.bmp","System":"Sol","Body":"Earth"}"""
        );
        ScreenshotProcessingPreferences preferences = Preferences(source, target) with
        {
            AddBanner = false,
            DeleteOriginal = true,
        };
        var service = new ScreenshotProcessingService(() => 320, Delay, queue);
        await service.ProcessLiveAsync([entry], preferences, "Original");
        string bitmap = Path.Combine(source, "late.bmp");
        CreateBitmap(bitmap, SKColors.Blue);
        interruptPersistence = true;
        ScreenshotProcessingResult interrupted = await service.ProcessLiveAsync([], preferences, "Original");
        Assert.Equal(1, interrupted.PendingCount);
        Assert.Single(interrupted.Warnings);
        Assert.True(File.Exists(bitmap));
        Assert.Single(Directory.GetFiles(target, "*.png", SearchOption.AllDirectories));
        File.Delete(queue);
        Directory.Move(heldQueue, queue);
        var restarted = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask, queue);
        ScreenshotConversion converted = Assert.Single(
            (await restarted.ProcessLiveAsync([], preferences, "Original")).Conversions
        );
        Assert.True(converted.SourceDeleted);
        Assert.False(File.Exists(bitmap));
        Assert.Single(Directory.GetFiles(target, "*.png", SearchOption.AllDirectories));
        Assert.Equal(0, new ScreenshotProcessingService(pendingQueueDirectory: queue).PendingCount);
    }

    [Fact]
    public async Task RestartResumesALateCaptureWithItsOriginalFoldersAndPreferences()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        string queue = Path.Combine(temporaryDirectory, "queue");
        Directory.CreateDirectory(source);
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\late.bmp","System":"Sol","Body":"Earth"}"""
        );
        ScreenshotProcessingPreferences original = Preferences(source, target) with { AddBanner = false };
        var service = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask, queue);
        Assert.Equal(1, (await service.ProcessLiveAsync([entry], original, "Original")).PendingCount);
        CreateBitmap(Path.Combine(source, "late.bmp"), SKColors.Cyan);
        var restarted = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask, queue);
        ScreenshotProcessingResult result = await restarted.ProcessLiveAsync(
            [],
            original with
            {
                SourceFolder = Path.Combine(temporaryDirectory, "changed-source"),
                TargetFolder = Path.Combine(temporaryDirectory, "changed-output"),
                AddBanner = true,
                DeleteOriginal = true,
            },
            "Another commander"
        );
        ScreenshotConversion converted = Assert.Single(result.Conversions);
        Assert.Equal(Path.Combine(target, "Sol", "Earth (2026-11-03 050653).png"), converted.OutputPath);
        Assert.False(converted.IncludedBanner);
        Assert.False(converted.SourceDeleted);
        Assert.True(File.Exists(Path.Combine(source, "late.bmp")));
        Assert.Empty(result.Warnings);
        Assert.Equal(0, new ScreenshotProcessingService(pendingQueueDirectory: queue).PendingCount);
        using var image = SKBitmap.Decode(converted.OutputPath);
        Assert.Equal(SKColors.Cyan, image.GetPixel(15, 15));
    }

    [Fact]
    public async Task UnchangedCorruptBmpSkipsReadinessUntilItsContentChangesEvenAfterRestart()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        string queue = Path.Combine(temporaryDirectory, "queue");
        Directory.CreateDirectory(source);
        string bmp = Path.Combine(source, "bad.bmp");
        await File.WriteAllTextAsync(bmp, "not a bitmap");
        int delays = 0;
        Task Delay(TimeSpan _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            delays++;
            return Task.CompletedTask;
        }
        var service = new ScreenshotProcessingService(() => 320, Delay, queue);
        ScreenshotProcessingPreferences preferences = Preferences(source, target) with
        {
            AddBanner = false,
            DeleteOriginal = true,
        };
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\bad.bmp","System":"Sol","Body":"Earth"}"""
        );
        Assert.Equal(1, (await service.ProcessLiveAsync([entry], preferences, null)).PendingCount);
        int afterFirstAttempt = delays;
        ScreenshotProcessingResult unchanged = await service.ProcessLiveAsync([], preferences, null);
        Assert.Empty(unchanged.Warnings);
        Assert.Equal(afterFirstAttempt, delays);
        var restarted = new ScreenshotProcessingService(() => 320, Delay, queue);
        Assert.Single((await restarted.ProcessLiveAsync([], preferences, null)).Warnings);
        Assert.Equal(afterFirstAttempt, delays);
        File.Delete(bmp);
        CreateBitmap(bmp, SKColors.Blue);
        ScreenshotConversion converted = Assert.Single(
            (await restarted.ProcessLiveAsync([], preferences, null)).Conversions
        );
        Assert.True(converted.SourceDeleted);
        Assert.False(File.Exists(bmp));
        using var image = SKBitmap.Decode(converted.OutputPath);
        Assert.Equal(SKColors.Blue, image.GetPixel(100, 100));
    }

    [Fact]
    public async Task DifferentJournalRecordIdentitiesNamingTheSameBmpProduceOneImage()
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(source);
        CreateBitmap(Path.Combine(source, "same.bmp"), SKColors.Blue);
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\same.bmp","System":"Sol","Body":"Earth"}"""
        );
        var service = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask);
        ScreenshotProcessingPreferences preferences = Preferences(source, target) with { AddBanner = false };
        Assert.Single(
            (
                await service.ProcessLiveAsync([entry with { SourceRecordIdentity = "first" }], preferences, null)
            ).Conversions
        );
        Assert.Empty(
            (
                await service.ProcessLiveAsync([entry with { SourceRecordIdentity = "second" }], preferences, null)
            ).Conversions
        );
        Assert.Single(Directory.GetFiles(target, "*.png", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ARecordIdentityReusedInAnotherJournalFolderDoesNotSuppressItsCapture()
    {
        string first = Path.Combine(temporaryDirectory, "first");
        string second = Path.Combine(temporaryDirectory, "second");
        string target = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        CreateBitmap(Path.Combine(first, "same.bmp"), SKColors.Blue);
        CreateBitmap(Path.Combine(second, "same.bmp"), SKColors.Red);
        JournalEventEnvelope entry = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\same.bmp","System":"Sol","Body":"Earth"}"""
        ) with
        {
            SourceRecordIdentity = "Journal.same.log:123",
        };
        var service = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask);
        Assert.Single(
            (
                await service.ProcessLiveAsync(
                    [entry],
                    Preferences(first, target) with
                    {
                        AddBanner = false,
                    },
                    "First commander"
                )
            ).Conversions
        );
        ScreenshotConversion other = Assert.Single(
            (
                await service.ProcessLiveAsync(
                    [entry],
                    Preferences(second, target) with
                    {
                        AddBanner = false,
                    },
                    "Second commander"
                )
            ).Conversions
        );
        using var image = SKBitmap.Decode(other.OutputPath);
        Assert.Equal(SKColors.Red, image.GetPixel(100, 100));
        Assert.Equal(2, Directory.GetFiles(target, "*.png", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task MalformedRecoveryFilesWarnWithoutPreventingStartup()
    {
        string queue = Path.Combine(temporaryDirectory, "queue");
        Directory.CreateDirectory(queue);
        await File.WriteAllTextAsync(Path.Combine(queue, "broken.json"), "{}");
        var service = new ScreenshotProcessingService(pendingQueueDirectory: queue);
        ScreenshotProcessingResult result = await service.ProcessLiveAsync(
            [],
            Preferences(temporaryDirectory, Path.Combine(temporaryDirectory, "out")),
            null
        );
        Assert.Contains("could not be recovered", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Equal(0, result.PendingCount);
        Assert.Empty(
            (
                await service.ProcessLiveAsync(
                    [],
                    Preferences(temporaryDirectory, Path.Combine(temporaryDirectory, "out")),
                    null
                )
            ).Warnings
        );
    }
}
