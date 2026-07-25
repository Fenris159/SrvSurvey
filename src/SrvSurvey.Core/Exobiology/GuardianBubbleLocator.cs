using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exobiology;

public static class GuardianBubbleLocator
{
    private const double LargeBubbleRadiusLy = 750;
    private const double SmallBubbleRadiusLy = 100;

    private static readonly GalacticCoordinate[] LargeBubbleCenters =
    [
        new(1099.21875, -146.6875, -133.59375),
        new(-840.65625, -561.15625, 13361.8125),
    ];

    private static readonly GalacticCoordinate[] SmallBubbleCenters =
    [
        new(-9298.6875, -419.40625, 7911.15625),
        new(-5479.28125, -574.84375, 10468.96875),
        new(1228.1875, -694.5625, 12341.65625),
        new(4961.1875, 158.09375, 20642.65625),
        new(14602.75, -237.90625, 3561.875),
        new(8649.125, -154.71875, 2686.03125),
    ];

    public static bool IsWithinKnownBubble(GalacticCoordinate position)
    {
        return LargeBubbleCenters.Any(
                center => Distance(position, center) < LargeBubbleRadiusLy)
            || SmallBubbleCenters.Any(
                center => Distance(position, center) < SmallBubbleRadiusLy);
    }

    private static double Distance(
        GalacticCoordinate first,
        GalacticCoordinate second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        var z = first.Z - second.Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
