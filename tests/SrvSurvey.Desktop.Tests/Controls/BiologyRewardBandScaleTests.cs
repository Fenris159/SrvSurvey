using Avalonia.Media;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SkiaSharp;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class BiologyRewardBandScaleTests
{
    [Fact]
    public void EmptyBrushPropertyRoundTripsThroughTheControl()
    {
        var control = new BiologyRewardBandControl();
        var brush = Brushes.Gray;

        control.EmptyBrush = brush;

        Assert.Same(brush, control.EmptyBrush);
    }

    [Fact]
    public void UnknownGlyphHasASeparateBrushFromPredictionFill()
    {
        var control = new BiologyRewardBandControl();
        var prediction = Brushes.Gold;
        var unknownGlyph = Brushes.LightGray;

        control.PredictionFilledBrush = prediction;
        control.UnknownGlyphBrush = unknownGlyph;

        Assert.Same(prediction, control.PredictionFilledBrush);
        Assert.Same(unknownGlyph, control.UnknownGlyphBrush);
    }

    [Fact]
    public void LegacyDimmedAndHighlightedUpperRangesHaveIndependentBrushes()
    {
        var control = new BiologyRewardBandControl
        {
            DimmedPotentialBrush = Brushes.Brown,
            HighlightPotentialBrush = Brushes.Goldenrod,
            DimmedHighlightPotentialBrush = Brushes.Olive,
        };

        Assert.Same(Brushes.Brown, control.DimmedPotentialBrush);
        Assert.Same(Brushes.Goldenrod, control.HighlightPotentialBrush);
        Assert.Same(Brushes.Olive, control.DimmedHighlightPotentialBrush);
    }

    [Fact]
    public void PredictionPipsClipHatchToTheControlBounds()
    {
        var control = new BiologyRewardBandControl
        {
            Width = 13,
            Height = 28,
            MinimumReward = 1_000_000,
            MaximumReward = 9_000_000,
            IsPrediction = true,
        };

        Assert.True(control.ClipToBounds);
    }

    [Fact]
    public void GalacticRegionCandidateHasIndependentStateAndBrushes()
    {
        var control = new BiologyRewardBandControl();

        control.IsGlobalRegionalFirst = true;
        control.GlobalRegionalBrush = Brushes.White;
        control.GlobalRegionalPotentialBrush = Brushes.Gray;

        Assert.True(control.IsGlobalRegionalFirst);
        Assert.Same(Brushes.White, control.GlobalRegionalBrush);
        Assert.Same(Brushes.Gray, control.GlobalRegionalPotentialBrush);
    }

    [Fact]
    public void EveryLegacyPipStateHasAnIndependentOuterEdgeBrush()
    {
        var control = new BiologyRewardBandControl
        {
            FilledEdgeBrush = Brushes.Orange,
            DimmedFilledEdgeBrush = Brushes.DarkOrange,
            PredictionEdgeBrush = Brushes.DarkCyan,
            HighlightEdgeBrush = Brushes.Gold,
            DimmedHighlightEdgeBrush = Brushes.DarkGoldenrod,
            GlobalRegionalEdgeBrush = Brushes.White,
            UnknownEdgeBrush = Brushes.Gray,
        };

        Assert.Same(Brushes.Orange, control.FilledEdgeBrush);
        Assert.Same(Brushes.DarkOrange, control.DimmedFilledEdgeBrush);
        Assert.Same(Brushes.DarkCyan, control.PredictionEdgeBrush);
        Assert.Same(Brushes.Gold, control.HighlightEdgeBrush);
        Assert.Same(Brushes.DarkGoldenrod, control.DimmedHighlightEdgeBrush);
        Assert.Same(Brushes.White, control.GlobalRegionalEdgeBrush);
        Assert.Same(Brushes.Gray, control.UnknownEdgeBrush);
    }

    [Fact]
    public void EveryLegacyPipStateHasIndependentSegmentEdgeBrushes()
    {
        var control = new BiologyRewardBandControl
        {
            FilledSegmentEdgeBrush = Brushes.Orange,
            PotentialSegmentEdgeBrush = Brushes.OrangeRed,
            DimmedFilledSegmentEdgeBrush = Brushes.SaddleBrown,
            DimmedPotentialSegmentEdgeBrush = Brushes.Brown,
            PredictionFilledSegmentEdgeBrush = Brushes.Cyan,
            PredictionPotentialSegmentEdgeBrush = Brushes.DarkCyan,
            HighlightFilledSegmentEdgeBrush = Brushes.Gold,
            HighlightPotentialSegmentEdgeBrush = Brushes.Goldenrod,
            DimmedHighlightFilledSegmentEdgeBrush = Brushes.DarkGoldenrod,
            DimmedHighlightPotentialSegmentEdgeBrush = Brushes.Olive,
            GlobalRegionalFilledSegmentEdgeBrush = Brushes.Gray,
            GlobalRegionalPotentialSegmentEdgeBrush = Brushes.White,
        };

        Assert.Same(Brushes.Orange, control.FilledSegmentEdgeBrush);
        Assert.Same(Brushes.OrangeRed, control.PotentialSegmentEdgeBrush);
        Assert.Same(Brushes.SaddleBrown, control.DimmedFilledSegmentEdgeBrush);
        Assert.Same(Brushes.Brown, control.DimmedPotentialSegmentEdgeBrush);
        Assert.Same(Brushes.Cyan, control.PredictionFilledSegmentEdgeBrush);
        Assert.Same(Brushes.DarkCyan, control.PredictionPotentialSegmentEdgeBrush);
        Assert.Same(Brushes.Gold, control.HighlightFilledSegmentEdgeBrush);
        Assert.Same(Brushes.Goldenrod, control.HighlightPotentialSegmentEdgeBrush);
        Assert.Same(
            Brushes.DarkGoldenrod,
            control.DimmedHighlightFilledSegmentEdgeBrush);
        Assert.Same(
            Brushes.Olive,
            control.DimmedHighlightPotentialSegmentEdgeBrush);
        Assert.Same(Brushes.Gray, control.GlobalRegionalFilledSegmentEdgeBrush);
        Assert.Same(
            Brushes.White,
            control.GlobalRegionalPotentialSegmentEdgeBrush);
    }

    [AvaloniaFact]
    public void OuterFrameAndSolidSegmentBordersBothRender()
    {
        var control = new BiologyRewardBandControl
        {
            Width = 13,
            Height = 28,
            MinimumReward = 13_000_000,
            MaximumReward = 13_000_000,
            FilledBrush = Brushes.Blue,
            FilledEdgeBrush = Brushes.Magenta,
            FilledSegmentEdgeBrush = Brushes.Lime,
            EmptyBrush = Brushes.Black,
        };
        var window = new Window
        {
            Width = 20,
            Height = 35,
            Background = Brushes.Black,
            Content = control,
        };

        try
        {
            window.Show();
            var frame = window.CaptureRenderedFrame();

            Assert.NotNull(frame);
            using var stream = new MemoryStream();
            frame.Save(stream, PngBitmapEncoderOptions.Default);
            stream.Position = 0;
            using var bitmap = SKBitmap.Decode(stream);
            Assert.NotNull(bitmap);
            Assert.Contains(bitmap.Pixels, pixel =>
                pixel.Green > 180 && pixel.Red < 100 && pixel.Blue < 100);
            Assert.Contains(bitmap.Pixels, pixel =>
                pixel.Red > 150 && pixel.Blue > 150 && pixel.Green < 140);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void UnknownRewardUsesQuestionStateEvenWithMaximum()
    {
        var state = BiologyRewardBandScale.Calculate(
            0,
            20_000_000,
            BiologyRewardThresholds.Default);

        Assert.True(state.IsUnknown);
        Assert.Empty(state.Segments);
    }

    [Fact]
    public void MinimumAndMaximumPreserveLegacyStrictBucketRules()
    {
        var state = BiologyRewardBandScale.Calculate(
            3_000_000,
            12_000_000,
            BiologyRewardThresholds.Default);

        Assert.False(state.IsUnknown);
        Assert.Equal(
            [
                BiologyRewardBandSegment.Filled,
                BiologyRewardBandSegment.Potential,
                BiologyRewardBandSegment.Potential,
                BiologyRewardBandSegment.Empty,
            ],
            state.Segments);
    }

    [Fact]
    public void RewardAboveHighestThresholdFillsAllBands()
    {
        var state = BiologyRewardBandScale.Calculate(
            12_000_001,
            12_000_001,
            BiologyRewardThresholds.Default);

        Assert.All(
            state.Segments,
            segment => Assert.Equal(BiologyRewardBandSegment.Filled, segment));
    }

    [Fact]
    public void SignalGroupFrameAddsInsetsWithoutConstrainingItsChild()
    {
        var child = new Border
        {
            Width = 30,
            Height = 28,
        };
        var control = new BiologyRewardBandGroupControl
        {
            Child = child,
            FrameBrush = Brushes.Orange,
        };

        control.Measure(Size.Infinity);
        control.Arrange(new Rect(control.DesiredSize));

        Assert.Equal(new Size(34, 30), control.DesiredSize);
        Assert.Equal(new Rect(2, 1, 30, 28), child.Bounds);
        Assert.Same(Brushes.Orange, control.FrameBrush);
    }
}
