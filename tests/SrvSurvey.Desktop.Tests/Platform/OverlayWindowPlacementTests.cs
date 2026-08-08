using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayWindowPlacementTests
{
    [Fact]
    public void UsesIntersectionOfHostAndOperatingSystemWorkingArea()
    {
        var usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), usableBounds);
    }

    [Fact]
    public void UsesWorkingAreaWhenHostDoesNotOverlapIt()
    {
        var usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(3000, 0, 1280, 720),
            new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), usableBounds);
    }

    [Fact]
    public void BottomCenterStaysAboveTaskbarWithinGameClient()
    {
        var usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040));
        var position = OverlayWindowPlacement.BottomCenter(
            usableBounds,
            new PixelSize(620, 116),
            margin: 12);

        Assert.Equal(new PixelPoint(650, 912), position);
    }

    [Fact]
    public void PlacesOverlayInsideTopCenterOfGameClient()
    {
        var position = OverlayWindowPlacement.TopCenter(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(620, 390));

        Assert.Equal(new PixelPoint(-1270, 220), position);
    }

    [Fact]
    public void PlacesOverlayInsideTopLeftOfGameClient()
    {
        var position = OverlayWindowPlacement.TopLeft(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(390, 270));

        Assert.Equal(new PixelPoint(-1900, 220), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomRightOfGameClient()
    {
        var position = OverlayWindowPlacement.BottomRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(620, 760));

        Assert.Equal(new PixelPoint(1380, 500), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomLeftOfGameClient()
    {
        var position = OverlayWindowPlacement.BottomLeft(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(560, 210));

        Assert.Equal(new PixelPoint(120, 1050), position);
    }

    [Fact]
    public void PlacesOverlayInsideMiddleRightOfGameClient()
    {
        var position = OverlayWindowPlacement.MiddleRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(380, 320),
            margin: 8);

        Assert.Equal(new PixelPoint(1632, 580), position);
    }

    [Fact]
    public void PlacesOverlayInsideMiddleLeftOfGameClient()
    {
        var position = OverlayWindowPlacement.MiddleLeft(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(340, 500),
            margin: 8);

        Assert.Equal(new PixelPoint(-1912, 490), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomCenterOfGameClient()
    {
        var position = OverlayWindowPlacement.BottomCenter(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(360, 250));

        Assert.Equal(new PixelPoint(-1140, 1010), position);
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
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.TopCenter(
                default,
                new PixelSize(100, 100)));
    }
}
