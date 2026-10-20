using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class SurfaceVisualMotionTests
{
    [Fact]
    public void FlightMovesBetweenSnapshotsAndStopsAtPredictionLimit()
    {
        var motion = new SurfaceVisualMotion();
        motion.Update(Flight(0), TimeSpan.Zero);
        EliteStatus current = Flight(0.01);
        motion.Update(current, TimeSpan.FromSeconds(1));

        SurfaceVisualPose midway = motion.GetPose(TimeSpan.FromSeconds(1.5))!.Value;
        Assert.InRange(midway.Location.Latitude, 0.0149, 0.0151);
        Assert.Equal(current.NormalizedHeading, midway.Heading);
        Assert.True(motion.IsAnimating(TimeSpan.FromSeconds(1.5)));
        SurfaceVisualPose endpoint = motion.GetPose(TimeSpan.FromSeconds(2))!.Value;
        Assert.Equal(endpoint, motion.GetPose(TimeSpan.FromSeconds(60)));
        Assert.False(motion.IsAnimating(TimeSpan.FromSeconds(60)));
        Assert.Equal(0.01, current.Latitude);
    }

    [Fact]
    public void CorrectionConvergesWithinEightyMillisecondsWithoutLosingFractionalHeading()
    {
        var motion = new SurfaceVisualMotion();
        motion.Update(Flight(0) with { Heading = 359 }, TimeSpan.Zero);
        motion.Update(Flight(0.01) with { Heading = 1 }, TimeSpan.FromSeconds(1));

        SurfaceVisualPose half = motion.GetPose(TimeSpan.FromSeconds(1.04))!.Value;
        Assert.InRange(Math.Min(half.Heading, 360 - half.Heading), 0, 0.001);
        Assert.Equal(1, motion.GetPose(TimeSpan.FromSeconds(1.08))!.Value.Heading, 6);
        Assert.NotEqual(0.01, half.Location.Latitude);
    }

    [Theory]
    [InlineData(StatusFlags.Docked)]
    [InlineData(StatusFlags.Landed)]
    [InlineData(StatusFlags.Supercruise)]
    [InlineData(StatusFlags.FsdJump)]
    [InlineData(StatusFlags.InSrv)]
    public void OtherModesNeverPredict(StatusFlags mode)
    {
        var motion = new SurfaceVisualMotion();
        motion.Update(Flight(0), TimeSpan.Zero);
        EliteStatus next = Flight(0.01) with { Flags = Flight(0).Flags | mode };
        motion.Update(next, TimeSpan.FromSeconds(1));
        Assert.Equal(
            new SurfaceCoordinate(next.Latitude, next.Longitude),
            motion.GetPose(TimeSpan.FromSeconds(1.5))!.Value.Location
        );
        Assert.False(motion.IsAnimating(TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public void FootAndMapScreensNeverPredictButCockpitPanelsCan()
    {
        foreach (
            EliteStatus next in new[]
            {
                Flight(0.01) with
                {
                    Flags2 = StatusFlags2.OnFoot,
                },
                Flight(0.01) with
                {
                    GuiFocus = GuiFocus.GalaxyMap,
                },
            }
        )
        {
            var motion = new SurfaceVisualMotion();
            motion.Update(Flight(0), TimeSpan.Zero);
            motion.Update(next, TimeSpan.FromSeconds(1));
            Assert.Equal(0.01, motion.GetPose(TimeSpan.FromSeconds(1.5))!.Value.Location.Latitude);
        }
        var panel = new SurfaceVisualMotion();
        panel.Update(Flight(0) with { GuiFocus = GuiFocus.InternalPanel }, TimeSpan.Zero);
        panel.Update(Flight(0.01) with { GuiFocus = GuiFocus.InternalPanel }, TimeSpan.FromSeconds(1));
        Assert.True(panel.GetPose(TimeSpan.FromSeconds(1.5))!.Value.Location.Latitude > 0.01);
    }

    [Fact]
    public void BodyChangesOldSamplesLongGapsAndTeleportsResetMotion()
    {
        foreach (
            EliteStatus next in new[]
            {
                Flight(0.01) with
                {
                    BodyName = "Other",
                },
                Flight(0.01) with
                {
                    PlanetRadius = 2_000_000,
                },
                Flight(0.01) with
                {
                    Timestamp = DateTimeOffset.MinValue,
                },
                Flight(20),
            }
        )
        {
            var motion = new SurfaceVisualMotion();
            motion.Update(Flight(0), TimeSpan.Zero);
            motion.Update(next, TimeSpan.FromSeconds(1));
            Assert.Equal(next.Latitude, motion.GetPose(TimeSpan.FromSeconds(1.5))!.Value.Location.Latitude);
            Assert.False(motion.IsAnimating(TimeSpan.FromSeconds(1.5)));
        }
        var stale = new SurfaceVisualMotion();
        stale.Update(Flight(0), TimeSpan.Zero);
        stale.Update(Flight(0.01), TimeSpan.FromSeconds(4));
        Assert.Equal(0.01, stale.GetPose(TimeSpan.FromSeconds(4.5))!.Value.Location.Latitude);
    }

    [Fact]
    public void AbruptReversalStopsPredictionAndRepeatedObjectDoesNotRestartIt()
    {
        var motion = new SurfaceVisualMotion();
        motion.Update(Flight(0), TimeSpan.Zero);
        motion.Update(Flight(0.01), TimeSpan.FromSeconds(1));
        EliteStatus reversed = Flight(0);
        motion.Update(reversed, TimeSpan.FromSeconds(2));
        motion.Update(reversed, TimeSpan.FromSeconds(2.05));
        Assert.Equal(0, motion.GetPose(TimeSpan.FromSeconds(2.1))!.Value.Location.Latitude, 6);
        Assert.False(motion.IsAnimating(TimeSpan.FromSeconds(2.1)));
    }

    [Fact]
    public void InvalidOrClearedPositionHasNoPose()
    {
        var motion = new SurfaceVisualMotion();
        Assert.Null(motion.GetPose(TimeSpan.Zero));
        foreach (
            EliteStatus invalid in new[]
            {
                Flight(100),
                Flight(0) with
                {
                    Longitude = double.NaN,
                },
                Flight(0) with
                {
                    PlanetRadius = 0,
                },
                Flight(0) with
                {
                    Flags = StatusFlags.None,
                },
            }
        )
        {
            motion.Update(invalid, TimeSpan.Zero);
            Assert.Null(motion.GetPose(TimeSpan.FromSeconds(1)));
        }
        motion.Update(null, TimeSpan.Zero);
        Assert.Null(motion.GetPose(TimeSpan.Zero));
    }

    [Fact]
    public void LongitudeWrapAndStrafingPreserveReportedFacing()
    {
        var motion = new SurfaceVisualMotion();
        motion.Update(Flight(0) with { Longitude = 179.99, Heading = 0 }, TimeSpan.Zero);
        motion.Update(Flight(0) with { Longitude = -179.99, Heading = 0 }, TimeSpan.FromSeconds(1));
        SurfaceVisualPose pose = motion.GetPose(TimeSpan.FromSeconds(1.5))!.Value;
        Assert.InRange(pose.Location.Longitude, -179.981, -179.979);
        Assert.Equal(0, pose.Heading);
    }

    internal static EliteStatus Flight(double latitude) =>
        new()
        {
            Latitude = latitude,
            BodyName = "Test body",
            PlanetRadius = 1_000_000,
            Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
            Timestamp = DateTimeOffset.UnixEpoch,
        };
}
