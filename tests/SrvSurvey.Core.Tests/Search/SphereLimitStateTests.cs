using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SphereLimitStateTests
{
    [Fact]
    public void EnablePersistsCenterAndUsesEuclideanDistance()
    {
        var state = new SphereLimitState();
        var center = new StarSystemReference(
            "Center",
            42,
            new GalacticCoordinate(10, 20, 30));

        var enabled = state.TryEnable(center, 100, out var error);
        var evaluation = state.Evaluate(
            "Target",
            new GalacticCoordinate(13, 24, 30));

        Assert.True(enabled, error);
        Assert.NotNull(evaluation);
        Assert.Equal(5, evaluation.Distance);
        Assert.True(evaluation.IsInside);
        Assert.Equal(
            new SphereLimitSnapshot(true, "Center", center.Position, 100),
            state.CreateSnapshot());
    }

    [Fact]
    public void BoundaryAtRadiusIsOutsideLikeLegacyOverlay()
    {
        var state = new SphereLimitState();
        state.TryEnable(
            new StarSystemReference(
                "Center",
                42,
                new GalacticCoordinate(0, 0, 0)),
            5,
            out _);

        var evaluation = state.Evaluate(
            "Boundary",
            new GalacticCoordinate(3, 4, 0));

        Assert.NotNull(evaluation);
        Assert.Equal(5, evaluation.Distance);
        Assert.False(evaluation.IsInside);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EnableRejectsInvalidRadiusWithoutChangingState(double radius)
    {
        var state = new SphereLimitState();

        var enabled = state.TryEnable(
            new StarSystemReference(
                "Sol",
                10477373803,
                new GalacticCoordinate(0, 0, 0)),
            radius,
            out var error);

        Assert.False(enabled);
        Assert.NotNull(error);
        Assert.False(state.IsActive);
        Assert.Null(state.Center);
    }

    [Fact]
    public void DisableRetainsConfigurationForLaterReEnable()
    {
        var state = new SphereLimitState();
        var center = new StarSystemReference(
            "Sol",
            10477373803,
            new GalacticCoordinate(0, 0, 0));
        state.TryEnable(center, 250, out _);

        state.Disable();

        Assert.Equal(
            new SphereLimitSnapshot(false, "Sol", center.Position, 250),
            state.CreateSnapshot());
        Assert.Null(state.Evaluate("Target", new GalacticCoordinate(1, 1, 1)));
    }

    [Fact]
    public void ResetRejectsIncompleteActiveConfigurationAndBadRadius()
    {
        var state = new SphereLimitState(
            new SphereLimitSnapshot(true, null, null, -10));

        Assert.False(state.IsActive);
        Assert.Equal(SphereLimitState.DefaultRadius, state.Radius);
    }
}
