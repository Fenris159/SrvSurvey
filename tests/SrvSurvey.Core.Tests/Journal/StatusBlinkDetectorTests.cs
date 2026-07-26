using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Journal;

public sealed class StatusBlinkDetectorTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DetectsSecondCockpitModeToggleInsideWindow()
    {
        var detector = new StatusBlinkDetector(
            StatusFlags.HudInAnalysisMode,
            TimeSpan.FromSeconds(3));
        var normal = new EliteStatus { Flags = StatusFlags.InSrv };
        var analysis = normal with
        {
            Flags = StatusFlags.InSrv | StatusFlags.HudInAnalysisMode,
        };

        Assert.False(detector.Update(normal, Start).Detected);
        var first = detector.Update(analysis, Start.AddSeconds(1));
        var second = detector.Update(normal, Start.AddSeconds(2));

        Assert.True(first.IsPrimed);
        Assert.False(first.Detected);
        Assert.True(second.Detected);
        Assert.False(second.IsPrimed);
    }

    [Fact]
    public void ExpiredToggleStartsANewGesture()
    {
        var detector = new StatusBlinkDetector(
            StatusFlags.HudInAnalysisMode,
            TimeSpan.FromSeconds(3));
        var normal = new EliteStatus();
        var analysis = normal with { Flags = StatusFlags.HudInAnalysisMode };
        detector.Update(normal, Start);
        detector.Update(analysis, Start.AddSeconds(1));

        var result = detector.Update(normal, Start.AddSeconds(4));

        Assert.False(result.Detected);
        Assert.True(result.IsPrimed);
    }

    [Fact]
    public void UsesShieldToggleOnFootAndResetsWhenTriggerChanges()
    {
        var detector = new StatusBlinkDetector(
            StatusFlags.HudInAnalysisMode,
            TimeSpan.FromSeconds(3));
        var ship = new EliteStatus();
        var onFoot = new EliteStatus
        {
            Flags2 = StatusFlags2.OnFoot | StatusFlags2.OnFootExterior,
        };
        detector.Update(ship, Start);
        detector.Update(
            ship with { Flags = StatusFlags.HudInAnalysisMode },
            Start.AddMilliseconds(100));

        var transition = detector.Update(onFoot, Start.AddMilliseconds(200));
        var firstShield = detector.Update(
            onFoot with { Flags = StatusFlags.ShieldsUp },
            Start.AddMilliseconds(300));
        var secondShield = detector.Update(
            onFoot,
            Start.AddMilliseconds(400));

        Assert.False(transition.Detected);
        Assert.Equal(StatusFlags.ShieldsUp, transition.ActiveTrigger);
        Assert.True(firstShield.IsPrimed);
        Assert.True(secondShield.Detected);
    }

    [Fact]
    public void ResetRequiresTwoFreshChanges()
    {
        var detector = new StatusBlinkDetector(
            StatusFlags.LightsOn,
            TimeSpan.FromSeconds(3));
        detector.Update(new EliteStatus(), Start);
        detector.Update(
            new EliteStatus { Flags = StatusFlags.LightsOn },
            Start.AddSeconds(1));
        detector.Reset();

        var first = detector.Update(new EliteStatus(), Start.AddSeconds(2));
        var second = detector.Update(
            new EliteStatus { Flags = StatusFlags.LightsOn },
            Start.AddSeconds(3));

        Assert.False(first.Detected);
        Assert.False(second.Detected);
        Assert.True(second.IsPrimed);
    }
}
