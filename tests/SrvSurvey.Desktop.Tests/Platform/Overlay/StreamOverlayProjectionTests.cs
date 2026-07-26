using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class StreamOverlayProjectionTests
{
    [Fact]
    public void ConvertsScreenPixelsToGameRelativeLogicalCoordinates()
    {
        var frame = StreamOverlayProjection.Create(
            new PixelRect(100, 200, 2560, 1440),
            new PixelPoint(400, 500),
            new PixelSize(600, 300),
            1.5);

        Assert.Equal(
            new StreamOverlayFrame(200, 200, 400, 200),
            frame);
    }

    [Theory]
    [InlineData(99, 200)]
    [InlineData(100, 199)]
    public void ExcludesOverlaysStartingOutsideTheGameClient(int x, int y)
    {
        var frame = StreamOverlayProjection.Create(
            new PixelRect(100, 200, 1920, 1080),
            new PixelPoint(x, y),
            new PixelSize(300, 200),
            1);

        Assert.Null(frame);
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(100, 0, 1)]
    [InlineData(100, 100, 0)]
    public void RejectsInvalidDimensions(
        int gameWidth,
        int gameHeight,
        double scaling)
    {
        var frame = StreamOverlayProjection.Create(
            new PixelRect(0, 0, gameWidth, gameHeight),
            new PixelPoint(0, 0),
            new PixelSize(10, 10),
            scaling);

        Assert.Null(frame);
    }
}
