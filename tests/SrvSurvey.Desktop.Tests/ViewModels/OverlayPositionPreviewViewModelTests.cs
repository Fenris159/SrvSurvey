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

        Assert.Contains(preview.Rows, row => row.Label == "A4");
        Assert.All(preview.Rows, row => Assert.True(row.HasRewardBands));
        Assert.DoesNotContain(preview.Rows, row => row.HasProgress);
        Assert.Contains(
            preview.Rows.SelectMany(row => row.RewardBands!),
            band => band.IsPrediction);
        Assert.Contains(
            preview.Rows.SelectMany(row => row.RewardBands!),
            band => band.MinimumReward == 0);
        Assert.Contains("Rewards", preview.Footer);
    }

    [Fact]
    public void RouteBodyPreviewUsesCheckboxesAndBodyArtworkInsteadOfProgressBars()
    {
        var definition = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotRouteBio");

        var preview = OverlayPositionPreviewViewModel.Create(definition);

        Assert.Equal(220, preview.PreferredWidth);
        Assert.Equal(5, preview.Rows.Count);
        Assert.Equal(5, preview.RouteBioTargets.Count);
        Assert.True(preview.IsRouteBio);
        Assert.Equal("1 / 5 BODIES COMPLETE", preview.Footer);
        Assert.All(preview.Rows, row => Assert.True(row.ShowCompletionCheckBox));
        Assert.All(preview.Rows, row => Assert.True(row.IsRouteBody));
        Assert.All(preview.Rows, row => Assert.True(row.HasBodyIcon));
        Assert.All(preview.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Value)));
        Assert.All(preview.Rows, row => Assert.True(row.RouteBody!.HasSpecies));
        Assert.DoesNotContain(preview.Rows, row => row.HasProgress);
        Assert.DoesNotContain(
            preview.Rows,
            row => row.Value.Contains(
                "Scan for biological signals",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preview.Rows, row => row.IsCompleted);
        Assert.Contains(
            preview.Rows,
            row => row.Label == "A 4"
                && row.Value == "Rocky body | 1,245 LS | Bio 27.4 M CR"
                && row.RouteBody!.CompactDetailSegments.Select(segment =>
                    segment.Text).SequenceEqual(
                    ["Rocky body", "1,245 LS", "Bio 27.4 M CR"])
                && row.RouteBody.InlineSegments.Select(segment =>
                    segment.Text).SequenceEqual(
                    ["A 4", "Rocky body", "1,245 LS", "Bio 27.4 M CR"])
                && row.RouteBody.InlineSegments[0].IsBodyName
                && row.RouteBody.InlineSegments[1].IsDetail
                && row.RouteBody.CompactDetailSegments[^1].HasSeparator == false
                && row.RouteBody!.Species.SequenceEqual(
                    ["Tussock Stigmasis", "Recepta Conditivus"]));
        Assert.Contains(
            preview.Rows,
            row => row.BodyIconAssetPath.EndsWith(
                "/Assets/Bodies/earth-like-world.png",
                StringComparison.Ordinal));
        Assert.Equal(
            3,
            preview.Rows.Take(3).Count());
        Assert.True(
            preview.EstimatedHeight
            < 70 + preview.Rows.Sum(row => row.EstimatedHeight) + 22);
    }

    [Fact]
    public void PreviewWrapsItsSimulatedContentInsteadOfUsingLegacyCanvasSize()
    {
        var jump = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotJumpInfo");
        var biology = OverlayLayoutCatalog.Supported.Single(item =>
            item.Name == "PlotBioSystem");

        var jumpPreview = OverlayPositionPreviewViewModel.Create(jump);
        var biologyPreview = OverlayPositionPreviewViewModel.Create(biology);

        Assert.InRange(jumpPreview.PreferredWidth, 190, 480);
        Assert.True(jumpPreview.PreferredWidth < jump.PreviewSize.Width);
        Assert.True(jumpPreview.EstimatedHeight < jump.PreviewSize.Height * 2);
        Assert.True(biologyPreview.EstimatedHeight > jumpPreview.EstimatedHeight);
        Assert.Equal(
            biologyPreview.Rows.Count,
            biologyPreview.Rows.Count(row => row.HasRewardBands));
    }

    [Fact]
    public void SimulatedStateIncludesLegacySemanticGlyphs()
    {
        var definitions = new[] { "PlotFSSInfo", "PlotJumpInfo", "PlotFlightWarning" }
            .Select(name => OverlayLayoutCatalog.Supported.Single(item =>
                item.Name == name));

        var glyphs = definitions
            .SelectMany(definition =>
                OverlayPositionPreviewViewModel.Create(definition).Rows)
            .Where(row => row.HasGlyph)
            .ToArray();

        Assert.Contains(glyphs, row => row.Glyph == "☀");
        Assert.Contains(glyphs, row => row.Glyph == "►");
        Assert.Contains(glyphs, row => row.Glyph == "⚠");
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
