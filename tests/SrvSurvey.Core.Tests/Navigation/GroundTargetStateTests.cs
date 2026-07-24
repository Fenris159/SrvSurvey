using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Navigation;

public sealed class GroundTargetStateTests
{
    [Theory]
    [InlineData("12.5, -45.25", 12.5, -45.25)]
    [InlineData("12.5 | -45.25", 12.5, -45.25)]
    [InlineData("12.5/-45.25", 12.5, -45.25)]
    [InlineData("12.5°N 45.25°W", 12.5, -45.25)]
    [InlineData("12.5 S / 45.25 E", -12.5, 45.25)]
    public void ParsesLegacyAndCardinalCoordinatePairs(
        string text,
        double expectedLatitude,
        double expectedLongitude)
    {
        Assert.True(GroundTargetState.TryParse(text, out var coordinate));

        Assert.Equal(expectedLatitude, coordinate.Latitude);
        Assert.Equal(expectedLongitude, coordinate.Longitude);
    }

    [Fact]
    public void CalculatesDistanceBearingAndLegacyApproachBand()
    {
        var state = new GroundTargetState();
        state.SetTarget(new SurfaceCoordinate(0, 1));
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = 0,
            Longitude = 0,
            PlanetRadius = 1_000,
            Heading = 45,
            Altitude = 17.4532925,
        });

        Assert.NotNull(state.Solution);
        Assert.Equal(17.453, state.Solution.Distance, 3);
        Assert.Equal(90, state.Solution.Bearing, 6);
        Assert.Equal(45, state.Solution.RelativeBearing, 6);
        Assert.Equal(45, state.Solution.AttackAngle, 3);
        Assert.Equal(GroundTargetApproach.Ideal, state.Solution.Approach);
    }

    [Fact]
    public void CurrentLocationCanBecomeTargetAndClearMatchesLegacySettings()
    {
        var state = new GroundTargetState();
        state.UpdateStatus(new EliteStatus
        {
            Flags = StatusFlags.HasLatLong,
            Latitude = -12.25,
            Longitude = 88.5,
            PlanetRadius = 6_000_000,
        });

        Assert.True(state.TryUseCurrentLocation(out var error), error);
        Assert.True(state.IsActive);
        Assert.Equal(new SurfaceCoordinate(-12.25, 88.5), state.Target);

        state.Clear();

        Assert.False(state.IsActive);
        Assert.Equal(default, state.Target);
        Assert.Null(state.Solution);
    }

    [Fact]
    public void InvalidOrOutOfRangeCoordinatesAreRejectedWithoutChangingTarget()
    {
        var state = new GroundTargetState(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(1, 2)));

        Assert.False(state.TrySetTarget("north", "west", out var parseError));
        Assert.NotNull(parseError);
        Assert.False(state.TrySetTarget("95", "2", out var rangeError));
        Assert.Contains("Latitude", rangeError);
        Assert.Equal(new SurfaceCoordinate(1, 2), state.Target);
    }

    [Fact]
    public void NoSolutionIsReportedWithoutSurfaceStatus()
    {
        var state = new GroundTargetState(
            new GroundTargetSnapshot(true, new SurfaceCoordinate(1, 2)));

        Assert.Null(state.Solution);
        Assert.False(state.TryUseCurrentLocation(out var error));
        Assert.Contains("no surface coordinates", error);
    }
}
