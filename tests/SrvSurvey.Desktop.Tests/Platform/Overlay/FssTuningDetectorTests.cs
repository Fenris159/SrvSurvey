using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class FssTuningDetectorTests
{
    [Fact]
    public void WhiteFssTextCompletesThePendingScan()
    {
        var source = CreateFssPanel();
        FillDetectedText(source, new FssRgbPixel(255, 255, 255), 30);

        var result = FssTuningDetector.Analyze(
            source,
            FssTuningDetectorSettings.Default,
            FssTuningDetectionState.Waiting);

        Assert.Equal(FssTuningDetectionState.White, result.State);
        Assert.Equal(new FssPixelRegion(110, 86, 51, 16), result.WatchArea);
        Assert.Equal(30, result.WhitePixelCount);
        Assert.Equal(0, result.YellowPixelCount);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void YellowTextWinsAtTheLegacyQuarterRatio()
    {
        var source = CreateFssPanel();
        FillDetectedText(source, new FssRgbPixel(255, 255, 255), 30);
        FillDetectedText(
            source,
            new FssRgbPixel(233, 197, 24),
            8,
            offset: 30);

        var result = FssTuningDetector.Analyze(
            source,
            FssTuningDetectorSettings.Default,
            FssTuningDetectionState.Skipped);

        Assert.Equal(FssTuningDetectionState.Yellow, result.State);
        Assert.Equal(30, result.WhitePixelCount);
        Assert.Equal(8, result.YellowPixelCount);
    }

    [Fact]
    public void MissingTuningBarPreservesThePendingState()
    {
        var source = new MemoryPixelSource(200, 120);

        var result = FssTuningDetector.Analyze(
            source,
            FssTuningDetectorSettings.Default,
            FssTuningDetectionState.Skipped);

        Assert.Equal(FssTuningDetectionState.Skipped, result.State);
        Assert.Null(result.WatchArea);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public void InvalidCaptureDimensionsAreRejectedWithoutPixelAccess()
    {
        var source = new MemoryPixelSource(2, 2);

        var result = FssTuningDetector.Analyze(
            source,
            FssTuningDetectorSettings.Default,
            FssTuningDetectionState.Waiting);

        Assert.Equal(FssTuningDetectionState.Waiting, result.State);
        Assert.Contains("too small", result.Failure);
    }

    private static MemoryPixelSource CreateFssPanel()
    {
        var source = new MemoryPixelSource(
            200,
            120,
            new FssRgbPixel(100, 0, 100));
        var yellowBar = new FssRgbPixel(193, 156, 65);
        for (var x = 60; x <= 160; x++)
        {
            source.SetPixel(x, 80, yellowBar);
        }

        source.SetPixel(131, 85, new FssRgbPixel(0, 0, 0));
        return source;
    }

    private static void FillDetectedText(
        MemoryPixelSource source,
        FssRgbPixel color,
        int count,
        int offset = 0)
    {
        const int areaX = 110;
        const int areaY = 86;
        const int areaWidth = 51;
        for (var index = 0; index < count; index++)
        {
            var position = offset + index;
            source.SetPixel(
                areaX + position % areaWidth,
                areaY + position / areaWidth,
                color);
        }
    }

    private sealed class MemoryPixelSource : IFssPixelSource
    {
        private readonly FssRgbPixel[] pixels;

        public MemoryPixelSource(
            int width,
            int height,
            FssRgbPixel background = default)
        {
            Width = width;
            Height = height;
            pixels = Enumerable.Repeat(background, width * height).ToArray();
        }

        public int Width { get; }

        public int Height { get; }

        public FssRgbPixel GetPixel(int x, int y)
        {
            return pixels[(y * Width) + x];
        }

        public void SetPixel(int x, int y, FssRgbPixel value)
        {
            pixels[(y * Width) + x] = value;
        }
    }
}
