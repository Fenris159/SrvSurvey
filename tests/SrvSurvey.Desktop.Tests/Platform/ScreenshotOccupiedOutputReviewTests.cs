using System.Security.Cryptography;
using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class ScreenshotProcessingServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnreadableOccupiedPngsDoNotBlockPublishingADistinctCapture(bool denied)
    {
        string source = Path.Combine(temporaryDirectory, "source");
        string target = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(source);
        CreateBitmap(Path.Combine(source, "first.bmp"), SKColors.Blue);
        ScreenshotProcessingPreferences preferences = Preferences(source, target) with
        {
            AddBanner = false,
            DeleteOriginal = true,
        };
        JournalEventEnvelope first = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"first.bmp","System":"Sol","Body":"Earth"}"""
        );
        string occupied = Assert
            .Single(
                (
                    await new ScreenshotProcessingService(retryDelay: (_, _) => Task.CompletedTask).ProcessAsync(
                        [first],
                        preferences,
                        "Test"
                    )
                ).Conversions
            )
            .OutputPath;
        byte[] existing = await File.ReadAllBytesAsync(occupied);
        CreateBitmap(Path.Combine(source, "next.bmp"), SKColors.Green);
        string ReadHash(string path)
        {
            if (path == occupied)
            {
                if (denied)
                {
                    throw new UnauthorizedAccessException("Read protected output");
                }
                throw new IOException("Locked output");
            }
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        var restarted = new ScreenshotProcessingService(
            retryDelay: (_, _) => Task.CompletedTask,
            readImageHash: ReadHash
        );
        JournalEventEnvelope next = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"next.bmp","System":"Sol","Body":"Earth"}"""
        );
        ScreenshotProcessingResult result = await restarted.ProcessAsync([next], preferences, "Test");
        Assert.Empty(result.Warnings);
        string output = Assert.Single(result.Conversions).OutputPath;
        Assert.Equal(Path.Combine(target, "Sol", "Earth (2026-11-03 050653) (2).png"), output);
        Assert.Equal(existing, await File.ReadAllBytesAsync(occupied));
        using var image = SKBitmap.Decode(output);
        Assert.Equal(SKColors.Green, image.GetPixel(0, 0));
        Assert.False(File.Exists(Path.Combine(source, "next.bmp")));
        Assert.Equal(0, restarted.PendingCount);
    }
}
