using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class MiningReliabilityTests
{
    [Fact]
    public void CalibrationIdentityPreservesEveryExplicitAdjustment()
    {
        var settings = new MiningDetectionSettings().Normalize();
        Assert.True(settings.HasSameCalibration(settings.Normalize()));
        Assert.False(settings.HasSameCalibration(settings with { X = double.BitIncrement(settings.X) }));
        Assert.False(settings.HasSameCalibration(settings with { CircleAspectRatio = double.BitIncrement(settings.CircleAspectRatio) }));
        Assert.False(settings.HasSameCalibration(settings with { RotationDegrees = double.BitIncrement(settings.RotationDegrees) }));
    }
    [Theory]
    [InlineData(5, 10, 4, 100, true)]
    [InlineData(4, 100, 5, 10, false)]
    [InlineData(4, 20, 4, 10, true)]
    [InlineData(4, 10, 4, 20, false)]
    [InlineData(4, 20, 4, 20, false)]
    public void CircleGridPrioritizesCorroboratingCirclesThenConfidence(int count, double score, int bestCount, double bestScore, bool expected)
    {
        Assert.Equal(expected, MiningCircleMask.IsBetterMatch(count, score, bestCount, bestScore));
    }
    [Fact]
    public async Task DisposingUnusedSpeechQueueIsIdempotentAndDoesNotStartSpeech()
    {
        var speech = new MiningSpeechOutput();
        speech.Dispose(); speech.Dispose();
        speech.Speak("Must not speak", "", 100, 0);
        Assert.Empty(await speech.GetVoicesAsync());
    }
}
