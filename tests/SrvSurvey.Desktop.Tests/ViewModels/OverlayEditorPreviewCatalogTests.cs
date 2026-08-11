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

    [Fact]
    public void SystemBiologyOverviewUsesCompactLeftColumnRewardText()
    {
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 0));
        var biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal(
            "10.89–\n34.34 M",
            biology.Bodies.Single(body => body.Name == "A4").RewardText);
        Assert.Equal(
            "20.70 M",
            biology.Bodies.Single(body => body.Name == "BC3").RewardText);
        Assert.Equal(
            "Estimated reward:\n42.75 M – 106 M",
            biology.RewardSummary);
    }

    [Fact]
    public void BodyPredictionsUsesCompactHeadingAndRewardSummary()
    {
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 1));
        var biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal("Body Predictions", biology.Title);
        Assert.Equal(OverlayPreviewSimulationState.Default.CurrentBody, biology.Heading);
        Assert.DoesNotContain("biology", biology.Heading, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Estimated reward:\n11.13 M – 33.24 M",
            biology.RewardSummary);
    }

    [Fact]
    public void IdentifiedBioUsesCompactHeadingStatusAndRewardRows()
    {
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotBioSystem", 2));
        var biology = overlay.Survey.BiologySurveyDisplay;

        Assert.Equal("Identified Bio", biology.Title);
        Assert.Equal(OverlayPreviewSimulationState.Default.CurrentBody, biology.Heading);
        Assert.DoesNotContain("biology", biology.Heading, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "DSS Scan Complete\nExact Organisms Identified",
            biology.PredictionStatus);
        Assert.Equal("Known reward:\n121.82 M", biology.RewardSummary);
        Assert.Equal(
            "First-footfall total:\n609.10 M",
            biology.FirstFootfallRewardSummary);
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
