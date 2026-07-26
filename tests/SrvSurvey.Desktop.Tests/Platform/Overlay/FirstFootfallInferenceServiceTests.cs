using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class FirstFootfallInferenceServiceTests
{
    [Fact]
    public void ColorDetectorUsesLegacyStrictToleranceBounds()
    {
        var preferences = FirstFootfallInferencePreferences.Default with
        {
            Red = 100,
            Green = 100,
            Blue = 100,
            Tolerance = 10,
        };
        var source = CreateBuffer(
            new FssRgbPixel(100, 100, 100),
            new FssRgbPixel(91, 109, 100),
            new FssRgbPixel(90, 100, 100),
            new FssRgbPixel(110, 100, 100));

        Assert.Equal(
            0.5,
            FirstFootfallColorDetector.GetMatchRatio(source, preferences));
    }

    [Fact]
    public async Task DetectionUsesLegacyWatchAreaAndStopsAboveThreshold()
    {
        var tracker = new StubWindowTracker(new GameWindowSnapshot(
            (nint)1,
            42,
            new PixelRect(100, 200, 1920, 1080),
            true,
            true));
        var capture = new StubScreenCapture(CreateBuffer(
            new FssRgbPixel(102, 255, 255),
            new FssRgbPixel(0, 0, 0),
            new FssRgbPixel(0, 0, 0),
            new FssRgbPixel(0, 0, 0)));
        using var service = new FirstFootfallInferenceService(
            tracker,
            capture,
            (_, _) => Task.CompletedTask);

        var result = await service.DetectAsync(
            FirstFootfallInferencePreferences.Default with
            {
                Threshold = 0.2,
                DurationSeconds = 1,
                SamplesPerSecond = 1,
            });

        Assert.True(result.Detected);
        Assert.Equal(0.25, result.MaximumMatchRatio);
        Assert.Equal(1, result.SampleCount);
        Assert.Equal(new PixelRect(820, 383, 480, 154), capture.LastBounds);
    }

    [Fact]
    public async Task DetectionFailsClosedWhenEliteIsNotForeground()
    {
        var tracker = new StubWindowTracker(new GameWindowSnapshot(
            (nint)1,
            42,
            new PixelRect(0, 0, 1920, 1080),
            true,
            false));
        var capture = new StubScreenCapture(CreateBuffer(
            new FssRgbPixel(102, 255, 255)));
        using var service = new FirstFootfallInferenceService(
            tracker,
            capture,
            (_, _) => Task.CompletedTask);

        var result = await service.DetectAsync(
            FirstFootfallInferencePreferences.Default with
            {
                DurationSeconds = 1,
                SamplesPerSecond = 1,
            });

        Assert.Equal(
            FirstFootfallInferenceOutcome.GameNotForeground,
            result.Outcome);
        Assert.Equal(0, capture.CaptureCount);
    }

    private static CapturedPixelBuffer CreateBuffer(params FssRgbPixel[] pixels)
    {
        var bytes = pixels.SelectMany(pixel => new byte[]
        {
            pixel.Blue,
            pixel.Green,
            pixel.Red,
            255,
        }).ToArray();
        return new CapturedPixelBuffer(pixels.Length, 1, bytes);
    }

    private sealed class StubWindowTracker(GameWindowSnapshot snapshot)
        : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot()
        {
            return snapshot;
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubScreenCapture(CapturedPixelBuffer buffer)
        : IGameScreenCapture
    {
        public int CaptureCount { get; private set; }

        public PixelRect? LastBounds { get; private set; }

        public bool IsAvailable => true;

        public string? UnavailableReason => null;

        public CapturedPixelBuffer Capture(PixelRect bounds)
        {
            CaptureCount++;
            LastBounds = bounds;
            return buffer;
        }

        public void Dispose()
        {
        }
    }
}
