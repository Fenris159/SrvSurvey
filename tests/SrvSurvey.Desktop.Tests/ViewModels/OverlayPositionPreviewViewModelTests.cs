using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayPositionPreviewViewModelTests
{
    [Fact]
    public void FssPreviewUsesOverlayTitleAndExplorationSampleData()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotFSSInfo");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Equal("FSS information", preview.Title);
        Assert.Equal(3, preview.Rows.Count);
        Assert.Contains(preview.Rows, row => row.Label == "Scan progress");
        Assert.True(preview.ShowFooter);
    }

    [Fact]
    public void CompactPreviewKeepsItsTruePlacementSizeWithoutOverflowRows()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotPulse");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Equal("Journal activity and SCO status", preview.Title);
        Assert.Empty(preview.Rows);
        Assert.False(preview.ShowFooter);
        Assert.Equal(new PixelSize(32, 32), definition.PreviewSize);
    }
}
