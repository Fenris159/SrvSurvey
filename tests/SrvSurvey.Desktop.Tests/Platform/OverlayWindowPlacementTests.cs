using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayWindowPlacementTests
{
    [Fact]
    public void PlacesOverlayInsideBottomRightOfGameClient()
    {
        var position = OverlayWindowPlacement.BottomRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(620, 760));

        Assert.Equal(new PixelPoint(1380, 500), position);
    }

    [Fact]
    public void PlacesOverlayInsideTopRightOfGameClient()
    {
        var position = OverlayWindowPlacement.TopRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(460, 720));

        Assert.Equal(new PixelPoint(1540, 220), position);
    }

    [Fact]
    public void KeepsOversizedOverlayAnchoredInsideTopLeftMargin()
    {
        var position = OverlayWindowPlacement.BottomRight(
            new PixelRect(-1920, 0, 1280, 720),
            new PixelSize(1400, 900));

        Assert.Equal(new PixelPoint(-1900, 20), position);
    }

    [Fact]
    public void RejectsInvalidGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(
                default,
                new PixelSize(100, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(
                new PixelRect(0, 0, 100, 100),
                default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(
                new PixelRect(0, 0, 100, 100),
                new PixelSize(50, 50),
                margin: -1));
    }
}
