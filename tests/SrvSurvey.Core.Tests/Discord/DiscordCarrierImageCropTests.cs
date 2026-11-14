using SrvSurvey.Core.Discord;

namespace SrvSurvey.Core.Tests.Discord;

public sealed class DiscordCarrierImageCropTests
{
    [Theory]
    [InlineData(1600, 1200, 1, 0, 150, 1600, 900)]
    [InlineData(900, 1600, 1, 0, 546.875, 900, 506.25)]
    [InlineData(1600, 900, 2, 400, 225, 800, 450)]
    [InlineData(1600, 900, 100, 700, 393.75, 200, 112.5)]
    public void FramesImagesWithoutStretchingAtTheSelectedZoom(
        int width,
        int height,
        double zoom,
        double x,
        double y,
        double cropWidth,
        double cropHeight
    )
    {
        DiscordCarrierCropRectangle area = new DiscordCarrierImageCrop(zoom).GetRectangle(width, height);
        Assert.Equal(new DiscordCarrierCropRectangle(x, y, cropWidth, cropHeight), area);
    }

    [Fact]
    public void PanningStopsAtTheImageEdgesAndKeepsTheFrameFilled()
    {
        var crop = new DiscordCarrierImageCrop(2, -10, 10);
        Assert.Equal(new DiscordCarrierCropRectangle(0, 90, 160, 90), crop.GetRectangle(320, 180));
        Assert.Equal(new DiscordCarrierImageCrop(2, 0.25, 0.75), crop.Constrain(320, 180));
    }

    [Fact]
    public void InvalidSavedCoordinatesFallBackToTheCenteredFrame()
    {
        var crop = new DiscordCarrierImageCrop(double.NaN, double.PositiveInfinity, double.NaN);
        Assert.Equal(new DiscordCarrierCropRectangle(0, 0, 1600, 900), crop.GetRectangle(1600, 900));
        Assert.Equal(new DiscordCarrierImageCrop(), crop.Constrain(1600, 900));
        Assert.Equal(
            new DiscordCarrierCropRectangle(0, 0, 1600, 900),
            new DiscordCarrierImageCrop(0).GetRectangle(1600, 900)
        );
    }

    [Theory]
    [InlineData(0, 900)]
    [InlineData(1600, 0)]
    public void EmptyImagesCannotBeFramed(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscordCarrierImageCrop().GetRectangle(width, height));
}
