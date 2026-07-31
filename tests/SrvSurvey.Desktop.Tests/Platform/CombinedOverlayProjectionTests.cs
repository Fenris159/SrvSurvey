using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class CombinedOverlayProjectionTests
{
    [Fact]
    public void ConvertsPhysicalWindowPositionToHostLogicalCoordinates()
    {
        var projection = CombinedOverlayProjection.Create(
            new PixelRect(100, 200, 1920, 1080),
            new PixelPoint(400, 500),
            new Size(240, 100),
            scaling: 1.5);

        Assert.NotNull(projection);
        Assert.Equal(200, projection.Left);
        Assert.Equal(200, projection.Top);
        Assert.Equal(new PixelRect(300, 300, 360, 150), projection.InputRegion);
    }

    [Fact]
    public void ClipsInputRegionToGameWindowWithoutMovingThePanel()
    {
        var projection = CombinedOverlayProjection.Create(
            new PixelRect(100, 100, 800, 600),
            new PixelPoint(50, 650),
            new Size(200, 100),
            scaling: 1);

        Assert.NotNull(projection);
        Assert.Equal(-50, projection.Left);
        Assert.Equal(550, projection.Top);
        Assert.Equal(new PixelRect(0, 550, 150, 50), projection.InputRegion);
    }

    [Fact]
    public void IgnoresPanelCompletelyOutsideHost()
    {
        var projection = CombinedOverlayProjection.Create(
            new PixelRect(100, 100, 800, 600),
            new PixelPoint(1000, 800),
            new Size(200, 100),
            scaling: 1);

        Assert.Null(projection);
    }
}
