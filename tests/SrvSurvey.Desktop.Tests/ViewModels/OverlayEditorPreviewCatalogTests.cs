using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayEditorPreviewCatalogTests
{
    [Fact]
    public async Task SphericalPreviewRejectsSessionMutationsWithoutFaulting()
    {
        using var preview = Assert.IsType<SphericalSearchOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotSphericalSearch", 0));

        var outcome = await preview.Boxel.Session.ExecuteAsync(
            new StopBoxelSearch());
        var cleared = await preview.Boxel.Session.ClearProfileAsync(
            BoxelSearchMessageCode.ProfileUnavailable);

        Assert.Equal(BoxelSearchOutcomeKind.Rejected, outcome.Kind);
        Assert.Equal(
            BoxelSearchMessageCode.SearchNotConfigured,
            outcome.Code);
        Assert.Equal(preview.Boxel.Session.Current.Version, outcome.SessionVersion);
        Assert.Equal(BoxelSearchOutcomeKind.Rejected, cleared.Kind);
        Assert.Equal(BoxelSearchMessageCode.ProfileUnavailable, cleared.Code);
    }

    [Fact]
    public void FlightWarningPreviewStatesUseDifficultyNames()
    {
        var states = OverlayEditorPreviewCatalog.GetStates("PlotFlightWarning");

        Assert.Equal(
            ["Noticeable", "Challenging", "High risk", "Expert only"],
            states.Select(state => state.DisplayName));
    }

    [Theory]
    [InlineData(0, 255, 215, 0, false)]
    [InlineData(1, 255, 165, 0, false)]
    [InlineData(2, 255, 69, 0, false)]
    [InlineData(3, 220, 20, 60, true)]
    public void FlightWarningPreviewCyclesSeverityStates(
        int stateIndex,
        byte red,
        byte green,
        byte blue,
        bool expectedExtreme)
    {
        var overlay = Assert.IsType<SystemSurveyOverlayViewModel>(
            OverlayEditorPreviewCatalog.Create("PlotFlightWarning", stateIndex));

        var brush = Assert.IsType<Avalonia.Media.ISolidColorBrush>(
            overlay.Survey.FlightWarningBrush,
            exactMatch: false);
        Assert.Equal(Avalonia.Media.Color.FromRgb(red, green, blue), brush.Color);
        Assert.Equal(expectedExtreme, overlay.Survey.IsExtremeFlightWarning);
    }

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

        Assert.Equal("BODY PREDICTIONS", biology.Title);
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

        Assert.Equal("IDENTIFIED BIO", biology.Title);
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
