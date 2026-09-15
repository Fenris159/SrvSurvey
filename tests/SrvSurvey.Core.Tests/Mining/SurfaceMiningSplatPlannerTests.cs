using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class SurfaceMiningSplatPlannerTests
{
    private const double PlanetRadiusMeters = 4_393_535;

    [Fact]
    public void IrregularOvalCanFitTwoRigSuggestions()
    {
        SurfaceCoordinate[] boundary =
        [
            new(0, 0),
            new(0.000008, 0.0000655637043794971),
            new(0.000035, 0.00019292926124223056),
            new(0.000229, 0.0004514222268563212),
            new(0.000283, 0.000504625560739171),
            new(0.000393, 0.0005739511170072918),
            new(0.000584, 0.0005949100061119626),
            new(0.000881, 0.0005825496356153154),
            new(0.00095, 0.0005535296353141906),
            new(0.001076, 0.0004514222268563212),
            new(0.001129, 0.00013542666805918747),
            new(0.001007, -0.00002418333358172501),
            new(0.000931, -0.00009834555656924566),
            new(0.000595, -0.00029503666970009994),
            new(0.000481, -0.0003648996333797903),
            new(0.00029, -0.0003810218557650613),
            new(0.00016, -0.00033211778119004174),
            new(0.000015, -0.00023753407651705684),
            new(-0.000004, -0.0000897470379612221),
        ];

        IReadOnlyList<SurfaceCoordinate> suggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            PlanetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );
        IReadOnlyList<SurfaceCoordinate> repeatedSuggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            PlanetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );

        Assert.True(suggestions.Count >= 2, $"Expected at least two rig suggestions, but found {suggestions.Count}.");
        Assert.Equal(suggestions, repeatedSuggestions);
        Assert.All(
            suggestions.SelectMany((first, index) => suggestions.Skip(index + 1).Select(second => (first, second))),
            pair =>
                Assert.True(
                    SurfaceNavigation.GetDistance(pair.first, pair.second, PlanetRadiusMeters)
                        >= SurfaceMiningGeometry.ExclusionDistanceMeters - 0.01
                )
        );
    }

    [Fact]
    public void SmallValidSplatReturnsOneRigSuggestion()
    {
        SurfaceCoordinate[] boundary = [AtOffset(-20, -20), AtOffset(20, -20), AtOffset(20, 20), AtOffset(-20, 20)];

        IReadOnlyList<SurfaceCoordinate> suggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            PlanetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );

        Assert.Single(suggestions);
    }

    [Fact]
    public void TightTriangleReturnsItsThreeCornerPacking()
    {
        const double sideLengthMeters = 80;
        double height = sideLengthMeters * Math.Sqrt(3) / 2;
        SurfaceCoordinate[] boundary =
        [
            AtOffset(-sideLengthMeters / 2, -height / 3),
            AtOffset(sideLengthMeters / 2, -height / 3),
            AtOffset(0, height * 2 / 3),
        ];

        IReadOnlyList<SurfaceCoordinate> suggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            PlanetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );

        Assert.Equal(3, suggestions.Count);
    }

    [Fact]
    public void DenseSquareReturnsTheSixRigMaximumWithinThePlannerDeadline()
    {
        SurfaceCoordinate[] boundary =
        [
            AtOffset(-500, -500),
            AtOffset(500, -500),
            AtOffset(500, 500),
            AtOffset(-500, 500),
        ];
        var timer = System.Diagnostics.Stopwatch.StartNew();

        IReadOnlyList<SurfaceCoordinate> suggestions = SurfaceMiningSplatPlanner.CreateRigLayout(
            boundary,
            PlanetRadiusMeters,
            SurfaceMiningGeometry.ExclusionDistanceMeters
        );

        Assert.Equal(6, suggestions.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Packing took {timer.Elapsed}.");
    }

    private static SurfaceCoordinate AtOffset(double east, double north)
    {
        double distance = Math.Sqrt((east * east) + (north * north));
        double bearing = SurfaceNavigation.NormalizeDegrees(Math.Atan2(east, north) * 180 / Math.PI);
        return MineMapService.GetDestination(new SurfaceCoordinate(0, 0), bearing, distance, PlanetRadiusMeters);
    }
}
