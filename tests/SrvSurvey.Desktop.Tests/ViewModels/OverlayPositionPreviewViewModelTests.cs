using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayPositionPreviewViewModelTests
{
    [Fact]
    public void FssPreviewUsesOverlaySpecificSimulatedSystemData()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotFSSInfo");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Equal("FSS information", preview.Title);
        Assert.Equal("Synuefe NL-N C23-4", preview.Subtitle);
        Assert.Contains(preview.Rows, row => row.Label == "B 3");
        Assert.Contains(preview.Rows, row => row.Value.Contains("BIO 6"));
        Assert.True(preview.ShowFooter);
    }

    [Fact]
    public void BiologySystemPreviewContainsSignalRewardBars()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotBioSystem");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Contains(preview.Rows, row => row.Label == "6a");
        Assert.Contains(preview.Rows, row => row.HasProgress);
        Assert.Contains("REWARDS", preview.Footer);
    }

    [Fact]
    public void EverySupportedOverlayProjectsFromTheSimulatedSession()
    {
        foreach (var definition in OverlayLayoutCatalog.Supported)
        {
            var preview = OverlayPositionPreviewViewModel.Create(definition);

            Assert.False(string.IsNullOrWhiteSpace(preview.Title));
            Assert.False(string.IsNullOrWhiteSpace(preview.Footer));
            Assert.True(preview.IsCompact || preview.Rows.Count > 0);
        }
    }

    [Fact]
    public void SimulatedSessionCanBeReplacedWithoutChangingDefaultState()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotStationInfo");
        var simulation = OverlayPreviewSimulationState.Default with
        {
            StationName = "Test Preview Orbital",
        };

        var preview = OverlayPositionPreviewViewModel.Create(
            definition,
            simulation);

        Assert.Equal("Test Preview Orbital", preview.Subtitle);
        Assert.Equal(
            "Raven Colonial Port",
            OverlayPreviewSimulationState.Default.StationName);
    }

    [Fact]
    public void CompactPreviewKeepsItsTruePlacementSizeWithoutOverflowRows()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotPulse");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Equal("Journal activity and SCO status", preview.Title);
        Assert.Empty(preview.Rows);
        Assert.True(preview.IsCompact);
        Assert.Equal("SCO", preview.CompactText);
        Assert.False(preview.ShowFooter);
        Assert.Equal(new PixelSize(32, 32), definition.PreviewSize);
    }
}
