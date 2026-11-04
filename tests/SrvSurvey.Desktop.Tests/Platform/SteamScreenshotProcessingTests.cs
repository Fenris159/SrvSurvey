using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class SteamScreenshotProcessingTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-steam-processing-{Guid.NewGuid():N}"
    );

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task JpegUsesSharedLabelsAndRemovalOptionsAndIsNotImportedAgainAfterRestart(bool labels, bool delete)
    {
        Directory.CreateDirectory(directory);
        string jpeg = Path.Combine(directory, "capture.jpg");
        WriteJpeg(jpeg);
        string queue = Path.Combine(directory, "queue");
        string output = Path.Combine(directory, "converted");
        ScreenshotProcessingPreferences preferences = ScreenshotProcessingPreferences.CreateDefaults() with
        {
            Enabled = true,
            ProcessSteamScreenshots = true,
            SourceFolder = directory,
            TargetFolder = output,
            AddBanner = labels,
            DeleteOriginal = delete,
            UseGuardianAerialFolder = false,
        };
        JournalEventEnvelope entry = Capture(jpeg);
        var processor = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask, queue);

        ScreenshotProcessingResult result = await processor.ProcessAsync([entry], preferences, "Test");

        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.Equal(Path.Combine(output, "Sol", "Earth (1970-01-01 050653).png"), conversion.OutputPath);
        Assert.Equal(labels, conversion.IncludedBanner);
        Assert.Equal(delete, conversion.SourceDeleted);
        Assert.Equal(!delete, File.Exists(jpeg));
        Assert.Equal(
            new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
            (await File.ReadAllBytesAsync(conversion.OutputPath))[..8]
        );
        using (var image = SKBitmap.Decode(conversion.OutputPath))
        {
            Assert.Equal(320, image.Width);
            Assert.Equal(180, image.Height);
            Assert.Equal(labels, image.GetPixel(20, 20) != image.GetPixel(319, 179));
        }

        var restarted = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask, queue);
        ScreenshotProcessingResult repeated = await restarted.ProcessAsync(
            [entry],
            preferences with
            {
                SourceFolder = Path.Combine(directory, "changed-source"),
                AddBanner = !labels,
            },
            "Another commander"
        );
        Assert.Empty(repeated.Conversions);
        Assert.Empty(repeated.Warnings);
        Assert.Equal(0, restarted.PendingCount);
        Assert.Single(Directory.GetFiles(output, "*.png", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptJpegRemainsPendingAndIsRetainedUntilAReadableImageArrives(bool partiallyWritten)
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "capture.jpg");
        byte[] original = [1, 2, 3];
        if (partiallyWritten)
        {
            WriteJpeg(source);
            byte[] complete = await File.ReadAllBytesAsync(source);
            original = complete[..(complete.Length / 2)];
        }
        await File.WriteAllBytesAsync(source, original);
        ScreenshotProcessingPreferences preferences = ScreenshotProcessingPreferences.CreateDefaults() with
        {
            Enabled = true,
            ProcessSteamScreenshots = true,
            SourceFolder = directory,
            TargetFolder = Path.Combine(directory, "converted"),
            DeleteOriginal = true,
            AddBanner = false,
        };
        string queue = Path.Combine(directory, "queue");
        var processor = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        ScreenshotProcessingResult failed = await processor.ProcessAsync([Capture(source)], preferences, "Test");
        Assert.Empty(failed.Conversions);
        Assert.Single(failed.Warnings);
        Assert.Equal(1, failed.PendingCount);
        Assert.Equal(original, await File.ReadAllBytesAsync(source));
        Assert.False(Directory.Exists(preferences.TargetFolder));

        WriteJpeg(source);
        var restarted = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            pendingQueueDirectory: queue
        );
        ScreenshotProcessingResult recovered = await restarted.ProcessAsync([], preferences, "Another commander");
        Assert.Empty(recovered.Warnings);
        Assert.Equal(
            Path.Combine(preferences.TargetFolder, "Sol", "Earth (1970-01-01 050653).png"),
            Assert.Single(recovered.Conversions).OutputPath
        );
        Assert.False(File.Exists(source));
    }

    internal static void WriteJpeg(string path)
    {
        using var bitmap = new SKBitmap(320, 180);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        using FileStream stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static JournalEventEnvelope Capture(string path)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                timestamp = "1970-01-01T05:06:53Z",
                @event = "Screenshot",
                Filename = path,
                System = "Sol",
                Body = "Earth",
                SteamScreenshot = true,
            }
        );
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
