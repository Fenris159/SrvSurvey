using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayEditorPreviewCatalogTests
{
    [Fact]
    public void SystemBiologyOverviewDemonstratesAlternativePredictionPips()
    {
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 0));

        Assert.Contains(
            overlay.Survey.BiologySurveyDisplay.Bodies,
            body => body.RewardBands.Count > body.SignalCount
                && body.HasAlternativeRewardBands);
    }

    [Theory]
    [InlineData(false, 0, 3, false)]
    [InlineData(false, 1, 3, true)]
    [InlineData(false, 3, 3, false)]
    [InlineData(true, 0, 3, true)]
    public void RewardBandGroupHighlightMatchesLegacyBodyState(
        bool isDestination,
        int analyzedSignalCount,
        int signalCount,
        bool expected)
    {
        var body = new BiologyBodyRowViewModel
        {
            IsDestination = isDestination,
            AnalyzedSignalCount = analyzedSignalCount,
            SignalCount = signalCount,
        };

        Assert.Equal(expected, body.IsRewardBandGroupHighlighted);
    }
}
