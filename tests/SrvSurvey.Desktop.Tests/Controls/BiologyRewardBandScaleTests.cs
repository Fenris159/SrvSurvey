using Avalonia.Media;
using Avalonia.Controls;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

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
