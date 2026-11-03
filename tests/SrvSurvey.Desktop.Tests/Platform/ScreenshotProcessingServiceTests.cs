using SkiaSharp;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ScreenshotProcessingServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-service-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public void SystemFolderPathMatchesScreenshotConversionNaming()
    {
        Assert.Equal(
            Path.Combine(temporaryDirectory, "Test_System"),
            ScreenshotProcessingService.GetSystemFolderPath(temporaryDirectory, "Test/System")
        );
    }

    [Fact]
    public void BannerLocationUsesRecentLiveStatusFallbackOnly()
    {
        JournalEventEnvelope screenshot = Parse(
            """
            {"timestamp":"2026-08-03T12:00:00Z","event":"Screenshot","Altitude":850}
            """
        );
        var recent = new ScreenshotNavigationContext(
            DateTimeOffset.Parse("2026-08-03T12:00:05Z", global::System.Globalization.CultureInfo.InvariantCulture),
            12.5,
            -42.25,
            180,
            true
        );

        string? location = ScreenshotProcessingService.CreateLocationLine(screenshot, recent);

        Assert.Equal("Lat: 12.500000°  Long: -42.250000°  Heading: 180°  Altitude: 850m", location);
        Assert.Null(
            ScreenshotProcessingService.CreateLocationLine(
                screenshot,
                recent with
                {
                    ObservedAt = DateTimeOffset.Parse(
                        "2026-08-03T12:00:10Z",
                        global::System.Globalization.CultureInfo.InvariantCulture
                    ),
                }
            )
        );
        Assert.Null(
            ScreenshotProcessingService.CreateLocationLine(screenshot, recent with { HasLatitudeLongitude = false })
        );
    }

    [Fact]
    public async Task HighResolutionBitmapThatArrivesAfterSixSecondsIsConvertedFromTheConfiguredFolder()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "HighResScreenShot_2026-11-03_05-06-54.bmp");
        TimeSpan elapsed = TimeSpan.Zero;
        Task Advance(TimeSpan interval, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            elapsed += interval;
            if (elapsed >= TimeSpan.FromSeconds(6) && !File.Exists(sourcePath))
            {
                CreateBitmap(sourcePath, SKColors.Cyan);
            }
            return Task.CompletedTask;
        }

        ScreenshotProcessingResult result = await new ScreenshotProcessingService(() => 1920, Advance).ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"C:\\xxx\\Pictures\\Frontier Developments\\Elite Dangerous\\HighResScreenShot_2026-11-03_05-06-54.bmp","Width":3840,"System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
            },
            null
        );

        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.Equal(sourcePath, conversion.SourcePath);
        Assert.True(File.Exists(sourcePath));
        using var image = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(image);
        Assert.Equal(SKColors.Cyan, image.GetPixel(100, 100));
    }

    [Fact]
    public async Task ABitmapArrivingAfterTheReadinessWindowIsConvertedOnceOnALaterPoll()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        var service = new ScreenshotProcessingService(() => 320, (_, _) => Task.CompletedTask);
        JournalEventEnvelope screenshot = Parse(
            """
            {"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\late.bmp","System":"Sol","Body":"Earth"}
            """
        );
        ScreenshotProcessingPreferences preferences = Preferences(sourceDirectory, targetDirectory) with
        {
            AddBanner = false,
        };
        ScreenshotProcessingResult waiting = await service.ProcessAsync(
            [screenshot],
            preferences,
            "Original commander"
        );
        Assert.Empty(waiting.Conversions);
        string source = Path.Combine(sourceDirectory, "late.bmp");
        CreateBitmap(source, SKColors.Cyan);

        ScreenshotProcessingResult completed = await service.ProcessAsync([], preferences, "Original commander");

        ScreenshotConversion conversion = Assert.Single(completed.Conversions);
        Assert.Equal(Path.Combine(targetDirectory, "Sol", "Earth (2026-11-03 050653).png"), conversion.OutputPath);
        using var image = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(image);
        Assert.Equal(SKColors.Cyan, image.GetPixel(100, 100));
        Assert.Empty((await service.ProcessAsync([screenshot], preferences, "Original commander")).Conversions);
        Assert.Single(Directory.GetFiles(Path.Combine(targetDirectory, "Sol"), "*.png"));
    }

    [Fact]
    public async Task LivePollingRetainsALockedCaptureWithoutWaitingAndHonorsItsOriginalSettings()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "locked.bmp");
        CreateBitmap(sourcePath, SKColors.Cyan);
        ScreenshotProcessingPreferences original = Preferences(sourceDirectory, targetDirectory) with
        {
            AddBanner = false,
        };
        JournalEventEnvelope screenshot = Parse(
            """{"timestamp":"2026-11-03T05:06:53Z","event":"Screenshot","Filename":"\\ED_Pictures\\locked.bmp","System":"Sol","Body":"Earth"}"""
        );
        var guardian = new ScreenshotGuardianContext("Beta", 100, 10, GuardianSiteKind.Ruins, 3);
        // A readiness delay that cannot complete exposes accidental waiting for a locked BMP.
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liveService = new ScreenshotProcessingService(() => 320, (_, _) => waiting.Task);
        using (FileStream locked = File.Open(sourcePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ScreenshotProcessingResult pendingResult = await liveService
                .ProcessLiveAsync(
                    [screenshot],
                    original,
                    "Original commander",
                    new Dictionary<JournalEventEnvelope, ScreenshotGuardianContext> { [screenshot] = guardian }
                )
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(pendingResult.Conversions);
            Assert.Equal(1, pendingResult.PendingCount);
            Assert.Contains("could not be read", Assert.Single(pendingResult.Warnings), StringComparison.Ordinal);
            Assert.Contains(sourcePath, Assert.Single(pendingResult.Warnings), StringComparison.Ordinal);
        }
        // Use the same gate for ready-file probes once the original is unlocked.
        waiting.SetResult();
        ScreenshotProcessingPreferences changed = original with
        {
            SourceFolder = Path.Combine(temporaryDirectory, "another-source"),
            TargetFolder = Path.Combine(temporaryDirectory, "another-target"),
            AddBanner = true,
            DeleteOriginal = true,
        };
        Assert.Empty(
            (await liveService.ProcessLiveAsync([], changed with { Enabled = false }, "Another commander")).Conversions
        );
        Assert.Equal(1, liveService.PendingCount);
        ScreenshotProcessingResult completed = await liveService.ProcessLiveAsync([], changed, "Another commander");
        ScreenshotConversion conversion = Assert.Single(completed.Conversions);
        Assert.Equal(
            Path.Combine(targetDirectory, "Sol", "Earth (2026-11-03 050653), Ruins3 Beta.png"),
            conversion.OutputPath
        );
        Assert.False(conversion.IncludedBanner);
        Assert.False(conversion.SourceDeleted);
        Assert.True(File.Exists(sourcePath));
        using var image = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(image);
        Assert.Equal(SKColors.Cyan, image.GetPixel(15, 15));
        Assert.Equal(0, completed.PendingCount);
        Assert.Empty((await liveService.ProcessLiveAsync([screenshot], original, "Original commander")).Conversions);
        Assert.Single(Directory.GetFiles(Path.Combine(targetDirectory, "Sol"), "*.png"));
    }

    [Fact]
    public async Task NativeAbsoluteJournalFilenameIsUsedEvenWhenTheConfiguredSourceFolderIsMissing()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "actual-source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "absolute.bmp");
        CreateBitmap(sourcePath, SKColors.Cyan);
        string json = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                timestamp = "2026-11-03T05:06:53Z",
                @event = "Screenshot",
                Filename = sourcePath,
                System = "Sol",
                Body = "Earth",
            }
        );
        ScreenshotProcessingResult result = await new ScreenshotProcessingService(
            () => 320,
            (_, _) => Task.CompletedTask
        ).ProcessLiveAsync(
            [Parse(json)],
            Preferences(Path.Combine(temporaryDirectory, "missing-source"), targetDirectory) with
            {
                AddBanner = false,
            },
            null
        );
        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.Equal(sourcePath, conversion.SourcePath);
        Assert.Equal(Path.Combine(targetDirectory, "Sol", "Earth (2026-11-03 050653).png"), conversion.OutputPath);
        Assert.Empty(result.Warnings);
        using var image = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(image);
        Assert.Equal(SKColors.Cyan, image.GetPixel(100, 100));
    }

    [Fact]
    public async Task MissingScreenshotReportsFailureWithoutCreatingAnOutput()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        ScreenshotProcessingResult result = await new ScreenshotProcessingService(
            retryDelay: (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        ).ProcessAsync(
            [Parse("""{"event":"Screenshot","Filename":"missing.bmp"}""")],
            Preferences(sourceDirectory, targetDirectory),
            null
        );

        Assert.Empty(result.Conversions);
        Assert.Contains(Path.Combine(sourceDirectory, "missing.bmp"), Assert.Single(result.Warnings));
        Assert.Contains("was not found", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Contains("remains pending", Assert.Single(result.Warnings), StringComparison.Ordinal);
        Assert.Equal(1, result.PendingCount);
        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public async Task HighResolutionScreenshotRetainsLegacyFilenameSuffix()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_HighRes.bmp");
        CreateBitmap(sourcePath, SKColors.Cyan);

        ScreenshotProcessingResult result = await new ScreenshotProcessingService(() => 1920).ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-08-03T12:00:00Z","event":"Screenshot","Filename":"Screenshot_HighRes.bmp","Width":3840,"System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
            },
            null
        );

        Assert.EndsWith(
            "Earth (2026-08-03 120000) (HighRes).png",
            Assert.Single(result.Conversions).OutputPath,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ScreenshotMatchingGameClientWidthIsNotMarkedHighResolution()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_0001.bmp");
        CreateBitmap(sourcePath, SKColors.Cyan);

        ScreenshotProcessingResult result = await new ScreenshotProcessingService(() => 1920).ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-08-03T12:00:00Z","event":"Screenshot","Filename":"Screenshot_0001.bmp","Width":1920,"System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
            },
            null
        );

        Assert.EndsWith(
            "Earth (2026-08-03 120000).png",
            Assert.Single(result.Conversions).OutputPath,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task BitmapIsBanneredEncodedVerifiedAndKeptByDefault()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_0001.bmp");
        CreateBitmap(sourcePath, SKColors.Blue);
        JournalEventEnvelope screenshot = Parse(
            """
            {"timestamp":"2026-07-25T12:34:56Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0001.bmp","Width":320,"Height":180,"System":"Test/System","Body":"Planet: A","Latitude":12.5,"Longitude":-42.25,"Heading":180,"Altitude":850}
            """
        );

        ScreenshotProcessingResult result = await new ScreenshotProcessingService().ProcessAsync(
            [screenshot],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = true,
            },
            "Test Commander"
        );

        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.False(conversion.SourceDeleted);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(
            Path.Combine(targetDirectory, "Test_System", "Planet_ A (2026-07-25 123456).png"),
            conversion.OutputPath
        );
        using var converted = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(converted);
        Assert.Equal(320, converted.Width);
        Assert.Equal(180, converted.Height);
        Assert.NotEqual(SKColors.Blue, converted.GetPixel(15, 15));
    }

    [Fact]
    public async Task OriginalIsDeletedOnlyAfterVerifiedConversion()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_0002.bmp");
        CreateBitmap(sourcePath, SKColors.Green);

        ScreenshotProcessingResult result = await new ScreenshotProcessingService().ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"/untrusted/path/Screenshot_0002.bmp","System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
                DeleteOriginal = true,
            },
            null
        );

        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.True(conversion.SourceDeleted);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(conversion.OutputPath));
        using var converted = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(converted);
        Assert.Equal(SKColors.Green, converted.GetPixel(100, 100));
    }

    [Fact]
    public async Task InvalidBitmapIsNotDeletedAndProducesWarning()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "broken.bmp");
        await File.WriteAllTextAsync(sourcePath, "not a bitmap");

        ScreenshotProcessingResult result = await new ScreenshotProcessingService().ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\broken.bmp","System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                DeleteOriginal = true,
            },
            null
        );

        Assert.Empty(result.Conversions);
        Assert.Single(result.Warnings);
        Assert.Contains("not a supported bitmap", result.Warnings[0]);
        Assert.True(File.Exists(sourcePath));
        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public async Task ExistingDestinationGetsCollisionSafeSuffix()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        string systemDirectory = Path.Combine(targetDirectory, "Sol");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(systemDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_0003.bmp");
        CreateBitmap(sourcePath, SKColors.Red);
        await File.WriteAllTextAsync(Path.Combine(systemDirectory, "Earth (2026-07-25 010203).png"), "existing file");

        ScreenshotProcessingResult result = await new ScreenshotProcessingService().ProcessAsync(
            [
                Parse(
                    """
                    {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0003.bmp","System":"Sol","Body":"Earth"}
                    """
                ),
            ],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
            },
            null
        );

        Assert.EndsWith("Earth (2026-07-25 010203) (2).png", Assert.Single(result.Conversions).OutputPath);
        Assert.Equal(
            "existing file",
            await File.ReadAllTextAsync(Path.Combine(systemDirectory, "Earth (2026-07-25 010203).png"))
        );
    }

    [Fact]
    public async Task QualifiedAlphaSiteCreatesVerifiedRotatedAerialCopy()
    {
        string sourceDirectory = Path.Combine(temporaryDirectory, "source");
        string targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        string sourcePath = Path.Combine(sourceDirectory, "Screenshot_0004.bmp");
        CreateBitmap(sourcePath, SKColors.Purple);
        JournalEventEnvelope screenshot = Parse(
            """
            {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0004.bmp","System":"Synuefe","Body":"Synuefe 1"}
            """
        );
        var guardianContext = new ScreenshotGuardianContext(
            "Alpha",
            12.5,
            1200,
            GuardianSiteKind.Ruins,
            4,
            "Ancient Ruins (4)"
        );

        ScreenshotProcessingResult result = await new ScreenshotProcessingService().ProcessAsync(
            [screenshot],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
                DeleteOriginal = true,
                UseGuardianAerialFolder = true,
                RotateAlphaAerial = true,
            },
            "Commander Test",
            guardianContexts: new Dictionary<JournalEventEnvelope, ScreenshotGuardianContext>
            {
                [screenshot] = guardianContext,
            }
        );

        ScreenshotConversion conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.True(conversion.SourceDeleted);
        Assert.NotNull(conversion.AerialOutputPath);
        Assert.Contains("Aerial Alpha", conversion.AerialOutputPath);
        Assert.EndsWith(
            "Synuefe 1 (2026-07-25 010203), Ruins4 Alpha.png",
            conversion.OutputPath,
            StringComparison.Ordinal
        );
        Assert.True(File.Exists(conversion.OutputPath));
        Assert.True(File.Exists(conversion.AerialOutputPath));
        using var aerial = SKBitmap.Decode(await File.ReadAllBytesAsync(conversion.AerialOutputPath));
        Assert.NotNull(aerial);
        Assert.Equal(180, aerial.Width);
        Assert.Equal(233, aerial.Height);
    }

    private static ScreenshotProcessingPreferences Preferences(string sourceDirectory, string targetDirectory)
    {
        return ScreenshotProcessingPreferences.CreateDefaults() with
        {
            Enabled = true,
            SourceFolder = sourceDirectory,
            TargetFolder = targetDirectory,
        };
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error),
            error
        );
        return journalEvent!;
    }

    private static void CreateBitmap(string path, SKColor color)
    {
        const int width = 320;
        const int height = 180;
        int rowSize = ((width * 3) + 3) & ~3;
        int imageSize = rowSize * height;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + imageSize);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);
        int padding = rowSize - (width * 3);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                writer.Write(color.Blue);
                writer.Write(color.Green);
                writer.Write(color.Red);
            }

            for (int index = 0; index < padding; index++)
            {
                writer.Write((byte)0);
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
