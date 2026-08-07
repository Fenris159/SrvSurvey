using Avalonia.Media;
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
}
