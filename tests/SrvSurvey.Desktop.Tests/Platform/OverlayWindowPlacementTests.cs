using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayWindowPlacementTests
{
    [Fact]
    public void UsesIntersectionOfHostAndOperatingSystemWorkingArea()
    {
        PixelRect usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040)
        );

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), usableBounds);
    }

    [Fact]
    public void UsesWorkingAreaWhenHostDoesNotOverlapIt()
    {
        PixelRect usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(3000, 0, 1280, 720),
            new PixelRect(0, 0, 1920, 1040)
        );

        Assert.Equal(new PixelRect(0, 0, 1920, 1040), usableBounds);
    }

    [Fact]
    public void BottomCenterStaysAboveTaskbarWithinGameClient()
    {
        PixelRect usableBounds = OverlayWindowPlacement.GetUsableBounds(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1040)
        );
        PixelPoint position = OverlayWindowPlacement.BottomCenter(usableBounds, new PixelSize(620, 116), margin: 12);

        Assert.Equal(new PixelPoint(650, 912), position);
    }

    [Theory]
    [InlineData(0, 912)]
    [InlineData(100, 12)]
    [InlineData(-100, 952)]
    [InlineData(50, 462)]
    [InlineData(-50, 932)]
    public void EditorHeightAdjustmentCoversBothSidesOfTheAutomaticPosition(int percent, int expectedY)
    {
        PixelPoint position = OverlayWindowPlacement.BottomCenterWithHeightAdjustment(
            new PixelRect(0, 0, 1920, 1040),
            new PixelRect(0, 0, 1920, 1080),
            new PixelSize(620, 116),
            percent,
            margin: 12
        );

        Assert.Equal(new PixelPoint(650, expectedY), position);
    }

    [Fact]
    public void EditorHeightAdjustmentCanClearAnUnreportedDock()
    {
        PixelPoint position = OverlayWindowPlacement.BottomCenterWithHeightAdjustment(
            new PixelRect(0, 0, 1920, 1080),
            new PixelRect(0, 0, 1920, 1080),
            new PixelSize(620, 116),
            10,
            margin: 12
        );

        Assert.True(position.Y + 116 < 1030);
    }

    [Fact]
    public void CorrectsDesktopWideBottomReservationProjectedFromShorterMonitor()
    {
        var shortScreen = new OverlayScreenGeometry(new PixelRect(0, 0, 3840, 2160), new PixelRect(0, 0, 3840, 2076));
        var tallScreen = new OverlayScreenGeometry(
            new PixelRect(3840, 0, 5120, 2880),
            new PixelRect(3840, 0, 5120, 2076)
        );

        PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
            tallScreen,
            [shortScreen, tallScreen]
        );

        Assert.Equal(new PixelRect(3840, 0, 5120, 2796), workingArea);
    }

    [Fact]
    public void PreservesOrdinaryPerMonitorWorkingArea()
    {
        var leftScreen = new OverlayScreenGeometry(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040));
        var rightScreen = new OverlayScreenGeometry(
            new PixelRect(1920, 0, 2560, 1440),
            new PixelRect(1920, 0, 2560, 1400)
        );

        PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
            rightScreen,
            [leftScreen, rightScreen]
        );

        Assert.Equal(rightScreen.WorkingArea, workingArea);
    }

    [Fact]
    public void PreservesUnequalPerMonitorInsetsWhenWorkingAreaBottomsDiffer()
    {
        var shortScreen = new OverlayScreenGeometry(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040));
        var tallScreen = new OverlayScreenGeometry(
            new PixelRect(1920, 0, 2560, 1440),
            new PixelRect(1920, 0, 2560, 1200)
        );

        PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
            tallScreen,
            [shortScreen, tallScreen]
        );

        Assert.Equal(tallScreen.WorkingArea, workingArea);
    }

    [Fact]
    public void PlacesOverlayInsideTopCenterOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.TopCenter(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(620, 390)
        );

        Assert.Equal(new PixelPoint(-1270, 220), position);
    }

    [Fact]
    public void PlacesOverlayInsideTopLeftOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.TopLeft(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(390, 270)
        );

        Assert.Equal(new PixelPoint(-1900, 220), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomRightOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.BottomRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(620, 760)
        );

        Assert.Equal(new PixelPoint(1380, 500), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomLeftOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.BottomLeft(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(560, 210)
        );

        Assert.Equal(new PixelPoint(120, 1050), position);
    }

    [Fact]
    public void PlacesOverlayInsideMiddleRightOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.MiddleRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(380, 320),
            margin: 8
        );

        Assert.Equal(new PixelPoint(1632, 580), position);
    }

    [Fact]
    public void PlacesOverlayInsideMiddleLeftOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.MiddleLeft(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(340, 500),
            margin: 8
        );

        Assert.Equal(new PixelPoint(-1912, 490), position);
    }

    [Fact]
    public void PlacesOverlayInsideBottomCenterOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.BottomCenter(
            new PixelRect(-1920, 200, 1920, 1080),
            new PixelSize(360, 250)
        );

        Assert.Equal(new PixelPoint(-1140, 1010), position);
    }

    [Fact]
    public void PlacesOverlayInsideTopRightOfGameClient()
    {
        PixelPoint position = OverlayWindowPlacement.TopRight(
            new PixelRect(100, 200, 1920, 1080),
            new PixelSize(460, 720)
        );

        Assert.Equal(new PixelPoint(1540, 220), position);
    }

    [Fact]
    public void KeepsOversizedOverlayAnchoredInsideTopLeftMargin()
    {
        PixelPoint position = OverlayWindowPlacement.BottomRight(
            new PixelRect(-1920, 0, 1280, 720),
            new PixelSize(1400, 900)
        );

        Assert.Equal(new PixelPoint(-1900, 20), position);
    }

    [Fact]
    public void RejectsInvalidGeometry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(default, new PixelSize(100, 100))
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(new PixelRect(0, 0, 100, 100), default)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.BottomRight(new PixelRect(0, 0, 100, 100), new PixelSize(50, 50), margin: -1)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayWindowPlacement.TopCenter(default, new PixelSize(100, 100))
        );
    }
}
