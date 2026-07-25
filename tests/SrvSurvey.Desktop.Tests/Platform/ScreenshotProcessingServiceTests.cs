using SkiaSharp;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ScreenshotProcessingServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-screenshot-service-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task BitmapIsBanneredEncodedVerifiedAndKeptByDefault()
    {
        var sourceDirectory = Path.Combine(temporaryDirectory, "source");
        var targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Screenshot_0001.bmp");
        CreateBitmap(sourcePath, SKColors.Blue);
        var screenshot = Parse(
            """
            {"timestamp":"2026-07-25T12:34:56Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0001.bmp","Width":320,"Height":180,"System":"Test/System","Body":"Planet: A","Latitude":12.5,"Longitude":-42.25,"Heading":180,"Altitude":850}
            """);

        var result = await new ScreenshotProcessingService().ProcessAsync(
            [screenshot],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = true,
            },
            "Test Commander");

        var conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.False(conversion.SourceDeleted);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(
            Path.Combine(
                targetDirectory,
                "Test_System",
                "Planet_ A (2026-07-25 123456).png"),
            conversion.OutputPath);
        using var converted = SKBitmap.Decode(conversion.OutputPath);
        Assert.NotNull(converted);
        Assert.Equal(320, converted.Width);
        Assert.Equal(180, converted.Height);
        Assert.NotEqual(SKColors.Blue, converted.GetPixel(15, 15));
    }

    [Fact]
    public async Task OriginalIsDeletedOnlyAfterVerifiedConversion()
    {
        var sourceDirectory = Path.Combine(temporaryDirectory, "source");
        var targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Screenshot_0002.bmp");
        CreateBitmap(sourcePath, SKColors.Green);

        var result = await new ScreenshotProcessingService().ProcessAsync(
            [Parse(
                """
                {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"/untrusted/path/Screenshot_0002.bmp","System":"Sol","Body":"Earth"}
                """)],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
                DeleteOriginal = true,
            },
            null);

        var conversion = Assert.Single(result.Conversions);
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
        var sourceDirectory = Path.Combine(temporaryDirectory, "source");
        var targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "broken.bmp");
        await File.WriteAllTextAsync(sourcePath, "not a bitmap");

        var result = await new ScreenshotProcessingService().ProcessAsync(
            [Parse(
                """
                {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\broken.bmp","System":"Sol","Body":"Earth"}
                """)],
            Preferences(sourceDirectory, targetDirectory) with
            {
                DeleteOriginal = true,
            },
            null);

        Assert.Empty(result.Conversions);
        Assert.Single(result.Warnings);
        Assert.Contains("not a supported bitmap", result.Warnings[0]);
        Assert.True(File.Exists(sourcePath));
        Assert.False(Directory.Exists(targetDirectory));
    }

    [Fact]
    public async Task ExistingDestinationGetsCollisionSafeSuffix()
    {
        var sourceDirectory = Path.Combine(temporaryDirectory, "source");
        var targetDirectory = Path.Combine(temporaryDirectory, "target");
        var systemDirectory = Path.Combine(targetDirectory, "Sol");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(systemDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Screenshot_0003.bmp");
        CreateBitmap(sourcePath, SKColors.Red);
        File.WriteAllText(
            Path.Combine(systemDirectory, "Earth (2026-07-25 010203).png"),
            "existing file");

        var result = await new ScreenshotProcessingService().ProcessAsync(
            [Parse(
                """
                {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0003.bmp","System":"Sol","Body":"Earth"}
                """)],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
            },
            null);

        Assert.EndsWith(
            "Earth (2026-07-25 010203) (2).png",
            Assert.Single(result.Conversions).OutputPath);
        Assert.Equal(
            "existing file",
            File.ReadAllText(Path.Combine(
                systemDirectory,
                "Earth (2026-07-25 010203).png")));
    }

    [Fact]
    public async Task QualifiedAlphaSiteCreatesVerifiedRotatedAerialCopy()
    {
        var sourceDirectory = Path.Combine(temporaryDirectory, "source");
        var targetDirectory = Path.Combine(temporaryDirectory, "target");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Screenshot_0004.bmp");
        CreateBitmap(sourcePath, SKColors.Purple);

        var result = await new ScreenshotProcessingService().ProcessAsync(
            [Parse(
                """
                {"timestamp":"2026-07-25T01:02:03Z","event":"Screenshot","Filename":"\\ED_Pictures\\Screenshot_0004.bmp","System":"Synuefe","Body":"Synuefe 1"}
                """)],
            Preferences(sourceDirectory, targetDirectory) with
            {
                AddBanner = false,
                DeleteOriginal = true,
                UseGuardianAerialFolder = true,
                RotateAlphaAerial = true,
            },
            "Commander Test",
            guardianContext: new ScreenshotGuardianContext(
                "Alpha",
                12.5,
                1200));

        var conversion = Assert.Single(result.Conversions);
        Assert.Empty(result.Warnings);
        Assert.True(conversion.SourceDeleted);
        Assert.NotNull(conversion.AerialOutputPath);
        Assert.Contains("Aerial Alpha", conversion.AerialOutputPath);
        Assert.True(File.Exists(conversion.OutputPath));
        Assert.True(File.Exists(conversion.AerialOutputPath));
        using var aerial = SKBitmap.Decode(conversion.AerialOutputPath);
        Assert.NotNull(aerial);
        Assert.Equal(180, aerial.Width);
        Assert.Equal(233, aerial.Height);
    }

    private static ScreenshotProcessingPreferences Preferences(
        string sourceDirectory,
        string targetDirectory)
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
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return journalEvent!;
    }

    private static void CreateBitmap(string path, SKColor color)
    {
        const int width = 320;
        const int height = 180;
        var rowSize = ((width * 3) + 3) & ~3;
        var imageSize = rowSize * height;
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
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
        var padding = rowSize - (width * 3);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                writer.Write(color.Blue);
                writer.Write(color.Green);
                writer.Write(color.Red);
            }

            for (var index = 0; index < padding; index++)
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
