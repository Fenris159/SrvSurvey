using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class StreamOverlayProjectionTests
{
    [Fact]
    public void ConvertsScreenPixelsToGameRelativeLogicalCoordinates()
    {
        StreamOverlayFrame? frame = StreamOverlayProjection.Create(
            new PixelRect(100, 200, 2560, 1440),
            new PixelPoint(400, 500),
            new PixelSize(600, 300),
            1.5
        );

        Assert.Equal(new StreamOverlayFrame(200, 200, 400, 200), frame);
    }

    [Theory]
    [InlineData(99, 200)]
    [InlineData(100, 199)]
    public void ExcludesOverlaysStartingOutsideTheGameClient(int x, int y)
    {
        StreamOverlayFrame? frame = StreamOverlayProjection.Create(
            new PixelRect(100, 200, 1920, 1080),
            new PixelPoint(x, y),
            new PixelSize(300, 200),
            1
        );

        Assert.Null(frame);
    }

    /// <summary>A fitting stream panel is shifted back inside the canvas instead of clipping its growing edges.</summary>
    [Theory]
    [InlineData(762, 600, 400, 400, 500, 300)]
    [InlineData(50, 50, 400, 400, 100, 100)]
    [InlineData(50, 50, 1000, 1000, 100, 100)]
    public void IntersectingPanelsStayInsideTheCaptureCanvas(
        int x,
        int y,
        int width,
        int height,
        int expectedX,
        int expectedY
    )
    {
        var bounds = new PixelRect(100, 100, 800, 600);
        StreamOverlayFrame frame = Assert.IsType<StreamOverlayFrame>(
            StreamOverlayProjection.Create(
                bounds,
                new PixelPoint(x, y),
                new PixelSize(width, height),
                2,
                keepInsideCanvas: true
            )
        );
        Assert.Equal((expectedX - 100) / 2d, frame.Left);
        Assert.Equal((expectedY - 100) / 2d, frame.Top);
        Assert.Equal(width / 2d, frame.Width);
        Assert.Equal(height / 2d, frame.Height);
        Assert.Null(
            StreamOverlayProjection.Create(
                bounds,
                new PixelPoint(-1000, -1000),
                new PixelSize(50, 50),
                1,
                keepInsideCanvas: true
            )
        );
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(100, 0, 1)]
    [InlineData(100, 100, 0)]
    public void RejectsInvalidDimensions(int gameWidth, int gameHeight, double scaling)
    {
        StreamOverlayFrame? frame = StreamOverlayProjection.Create(
            new PixelRect(0, 0, gameWidth, gameHeight),
            new PixelPoint(0, 0),
            new PixelSize(10, 10),
            scaling
        );

        Assert.Null(frame);
    }
}
