using SrvSurvey.game;
using Xunit;

namespace SrvSurvey.OrbitalTests;

public class OrbitalMechanicsTests
{
    private static readonly DateTime Epoch = DateTime.UnixEpoch;

    [Fact]
    public void UpdatePositions_accumulates_parent_positions()
    {
        var calculator = new OrbitalCalculator();
        calculator.AddBody(Root(0));
        calculator.AddBody(OrbitingBody(1, 0, semiMajorAxisMeters: 1_000));
        calculator.AddBody(OrbitingBody(2, 1, semiMajorAxisMeters: 100));

        calculator.UpdatePositions(Epoch);

        Assert.True(calculator.HasValidPosition(2));
        double expectedLightSeconds = 1.1 / 299_792.458;
        Assert.Equal(expectedLightSeconds, calculator.GetDistanceLightSeconds(0, 2), precision: 12);
    }

    [Fact]
    public void UpdatePositions_rejects_missing_parent_and_non_elliptical_orbits()
    {
        var calculator = new OrbitalCalculator();
        calculator.AddBody(Root(0));
        calculator.AddBody(OrbitingBody(1, 99, semiMajorAxisMeters: 1_000));
        calculator.AddBody(OrbitingBody(2, 0, semiMajorAxisMeters: 1_000, eccentricity: 1));

        calculator.UpdatePositions(Epoch);

        Assert.False(calculator.HasValidPosition(1));
        Assert.False(calculator.HasValidPosition(2));
        Assert.True(double.IsPositiveInfinity(calculator.GetDistanceLightSeconds(0, 1)));
    }

    [Fact]
    public void InferParentIds_recovers_nested_barycentre_relationships()
    {
        var bodies = new[]
        {
            (BodyId: 54, ParentChain: (IReadOnlyList<int>)new[] { 48, 47, 1, 0 }),
            (BodyId: 55, ParentChain: (IReadOnlyList<int>)new[] { 48, 47, 1, 0 }),
        };

        var parents = OrbitalHierarchy.InferParentIds(bodies);

        Assert.Equal(48, parents[54]);
        Assert.Equal(47, parents[48]);
        Assert.Equal(1, parents[47]);
        Assert.Equal(0, parents[1]);
    }

    [Fact]
    public void IsSystemRoot_accepts_a_root_barycentre_but_not_an_orbiting_one()
    {
        Assert.True(OrbitalHierarchy.IsSystemRoot(-1, isMainStar: false, isBarycentre: true));
        Assert.False(OrbitalHierarchy.IsSystemRoot(0, isMainStar: false, isBarycentre: true));
    }

    [Fact]
    public void TryNormalizeSpanshOrbit_converts_units_and_preserves_the_mean_anomaly_epoch()
    {
        var epoch = DateTimeOffset.Parse("2026-07-28T05:13:09-05:00");

        bool success = OrbitalDataConversions.TryNormalizeSpanshOrbit(
            semiMajorAxisAu: 1,
            orbitalPeriodDays: 2,
            eccentricity: 0.25,
            inclination: 3,
            argumentOfPeriapsis: 4,
            longitudeAscendingNode: 5,
            meanAnomaly: 6,
            meanAnomalyTimestamp: epoch,
            out var snapshot);

        Assert.True(success);
        Assert.Equal(149_597_870_700d, snapshot.SemiMajorAxisMeters);
        Assert.Equal(172_800d, snapshot.OrbitalPeriodSeconds);
        Assert.Equal(0.25, snapshot.Eccentricity);
        Assert.Equal(3, snapshot.Inclination);
        Assert.Equal(4, snapshot.ArgumentOfPeriapsis);
        Assert.Equal(5, snapshot.LongitudeAscendingNode);
        Assert.Equal(6, snapshot.MeanAnomalyAtEpoch);
        Assert.Equal(epoch.ToUniversalTime(), snapshot.Epoch);
    }

    [Theory]
    [InlineData(null, 1d, 0d)]
    [InlineData(1d, null, 0d)]
    [InlineData(1d, 1d, -0.1d)]
    [InlineData(1d, 1d, 1d)]
    public void TryNormalizeSpanshOrbit_rejects_incomplete_or_non_elliptical_data(
        double? semiMajorAxisAu,
        double? orbitalPeriodDays,
        double? eccentricity)
    {
        bool success = OrbitalDataConversions.TryNormalizeSpanshOrbit(
            semiMajorAxisAu,
            orbitalPeriodDays,
            eccentricity,
            inclination: 0,
            argumentOfPeriapsis: 0,
            longitudeAscendingNode: 0,
            meanAnomaly: 0,
            meanAnomalyTimestamp: DateTimeOffset.UnixEpoch,
            out _);

        Assert.False(success);
    }

    [Fact]
    public void TryNormalizeSpanshOrbit_requires_the_mean_anomaly_timestamp()
    {
        bool success = OrbitalDataConversions.TryNormalizeSpanshOrbit(
            semiMajorAxisAu: 1,
            orbitalPeriodDays: 1,
            eccentricity: 0,
            inclination: 0,
            argumentOfPeriapsis: 0,
            longitudeAscendingNode: 0,
            meanAnomaly: 0,
            meanAnomalyTimestamp: null,
            out _);

        Assert.False(success);
    }

    [Fact]
    public void OptimizeRoute_finds_the_shortest_open_path_and_removes_duplicates()
    {
        var calculator = CalculatorWithFixedPositions(0, 1, 2, 3);

        var route = RouteOptimizer.OptimizeRoute(0, new List<int> { 3, 1, 2, 2 }, calculator);

        Assert.Equal(new[] { 0, 1, 2, 3 }, route);
    }

    [Fact]
    public void OptimizeRoute_returns_empty_when_any_required_position_is_invalid()
    {
        var calculator = CalculatorWithFixedPositions(0, 1);
        calculator.AddBody(new OrbitalCalculator.OrbitalBody { BodyId = 2, Name = "invalid" });

        var route = RouteOptimizer.OptimizeRoute(0, new List<int> { 1, 2 }, calculator);

        Assert.Empty(route);
    }

    [Fact]
    public void OptimizeRoute_heuristic_keeps_every_target()
    {
        var ids = Enumerable.Range(0, 18).ToArray();
        var calculator = CalculatorWithFixedPositions(ids);

        var route = RouteOptimizer.OptimizeRoute(0, ids.Skip(1).Reverse().ToList(), calculator);

        Assert.Equal(18, route.Count);
        Assert.Equal(0, route[0]);
        Assert.Equal(ids.OrderBy(id => id), route.OrderBy(id => id));
    }

    [Fact]
    public void OptimizeRoute_exact_solver_handles_fifteen_targets()
    {
        var ids = Enumerable.Range(0, 16).ToArray();
        var calculator = CalculatorWithFixedPositions(ids);

        var route = RouteOptimizer.OptimizeRoute(0, ids.Skip(1).Reverse().ToList(), calculator);

        Assert.Equal(ids, route);
    }

    private static OrbitalCalculator CalculatorWithFixedPositions(params int[] ids)
    {
        var calculator = new OrbitalCalculator();
        foreach (int id in ids)
        {
            calculator.AddBody(new OrbitalCalculator.OrbitalBody
            {
                BodyId = id,
                Name = id.ToString(),
                Position = new OrbitalCalculator.Vec3d(id, 0, 0),
                PositionValid = true,
            });
        }
        return calculator;
    }

    private static OrbitalCalculator.OrbitalBody Root(int id)
        => new()
        {
            BodyId = id,
            Name = "root",
            IsRoot = true,
        };

    private static OrbitalCalculator.OrbitalBody OrbitingBody(
        int id,
        int parentId,
        double semiMajorAxisMeters,
        double eccentricity = 0)
        => new()
        {
            BodyId = id,
            Name = id.ToString(),
            ParentId = parentId,
            HasOrbitalElements = true,
            SemiMajorAxis = semiMajorAxisMeters,
            Eccentricity = eccentricity,
            OrbitalPeriod = 1_000,
            Epoch = Epoch,
        };
}
