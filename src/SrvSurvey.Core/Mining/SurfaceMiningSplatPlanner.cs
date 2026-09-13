using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Mining;

/// <summary>
/// Builds a dense, deterministic hexagonal rig layout inside a traced deposit boundary.
/// Suggested positions are planning aids and remain separate from placed mining rigs.
/// </summary>
public static class SurfaceMiningSplatPlanner
{
    private const int MaximumSuggestions = 500;
    private const double MaximumBoundarySpanMeters = 20_000;
    private static readonly double[] CandidateRotations = [0, 15, 30, 45, 60, 75];
    private static readonly double[] CandidateOffsets = [0, 0.25, 0.5, 0.75];

    public static IReadOnlyList<SurfaceCoordinate> CreateRigLayout(
        IReadOnlyList<SurfaceCoordinate> boundary,
        double planetRadiusMeters,
        double minimumSpacingMeters
    )
    {
        ArgumentNullException.ThrowIfNull(boundary);
        if (
            boundary.Count < 3
            || !double.IsFinite(planetRadiusMeters)
            || planetRadiusMeters <= 0
            || !double.IsFinite(minimumSpacingMeters)
            || minimumSpacingMeters <= 0
        )
        {
            return [];
        }

        SurfaceCoordinate origin = boundary[0];
        Point2[] polygon = boundary.Select(point => Project(origin, point, planetRadiusMeters)).ToArray();
        if (Math.Abs(SignedArea(polygon)) < 1 || ExceedsMaximumSpan(polygon))
        {
            return [];
        }

        List<Point2> best = [];
        foreach (double rotation in CandidateRotations)
        {
            foreach (double offsetX in CandidateOffsets)
            {
                foreach (double offsetY in CandidateOffsets)
                {
                    List<Point2> candidate = CreateCandidateLayout(
                        polygon,
                        minimumSpacingMeters,
                        rotation,
                        offsetX,
                        offsetY
                    );
                    if (candidate.Count > best.Count)
                    {
                        best = candidate;
                    }
                }
            }
        }

        return best.Select(point => Unproject(origin, point, planetRadiusMeters)).ToArray();
    }

    private static bool ExceedsMaximumSpan(IReadOnlyList<Point2> polygon) =>
        polygon.Max(point => point.X) - polygon.Min(point => point.X) > MaximumBoundarySpanMeters
        || polygon.Max(point => point.Y) - polygon.Min(point => point.Y) > MaximumBoundarySpanMeters;

    private static List<Point2> CreateCandidateLayout(
        IReadOnlyList<Point2> polygon,
        double spacing,
        double rotationDegrees,
        double offsetX,
        double offsetY
    )
    {
        double radians = rotationDegrees * Math.PI / 180;
        Point2[] rotated = polygon.Select(point => Rotate(point, -radians)).ToArray();
        double minX = rotated.Min(point => point.X);
        double maxX = rotated.Max(point => point.X);
        double minY = rotated.Min(point => point.Y);
        double maxY = rotated.Max(point => point.Y);
        double rowHeight = spacing * Math.Sqrt(3) / 2;
        var result = new List<Point2>();
        int row = 0;
        for (double y = minY - rowHeight + offsetY * rowHeight; y <= maxY + rowHeight; y += rowHeight, row++)
        {
            double stagger = (row & 1) == 0 ? 0 : spacing / 2;
            for (double x = minX - spacing + offsetX * spacing + stagger; x <= maxX + spacing; x += spacing)
            {
                Point2 candidate = Rotate(new Point2(x, y), radians);
                if (Contains(polygon, candidate))
                {
                    result.Add(candidate);
                    if (result.Count >= MaximumSuggestions)
                    {
                        return result;
                    }
                }
            }
        }

        return result;
    }

    private static bool Contains(IReadOnlyList<Point2> polygon, Point2 point)
    {
        bool inside = false;
        for (int current = 0, previous = polygon.Count - 1; current < polygon.Count; previous = current++)
        {
            Point2 first = polygon[current];
            Point2 second = polygon[previous];
            bool crosses =
                (first.Y > point.Y) != (second.Y > point.Y)
                && point.X < ((second.X - first.X) * (point.Y - first.Y) / (second.Y - first.Y)) + first.X;
            if (crosses)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        double area = 0;
        for (int index = 0; index < polygon.Count; index++)
        {
            Point2 current = polygon[index];
            Point2 next = polygon[(index + 1) % polygon.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }

        return area / 2;
    }

    private static Point2 Project(SurfaceCoordinate origin, SurfaceCoordinate target, double planetRadiusMeters)
    {
        double distance = SurfaceNavigation.GetDistance(origin, target, planetRadiusMeters);
        double bearing = SurfaceNavigation.GetBearing(origin, target) * Math.PI / 180;
        return new Point2(Math.Sin(bearing) * distance, Math.Cos(bearing) * distance);
    }

    private static SurfaceCoordinate Unproject(SurfaceCoordinate origin, Point2 point, double planetRadiusMeters)
    {
        double distance = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        double bearing = SurfaceNavigation.NormalizeDegrees(Math.Atan2(point.X, point.Y) * 180 / Math.PI);
        return MineMapService.GetDestination(origin, bearing, distance, planetRadiusMeters);
    }

    private static Point2 Rotate(Point2 point, double radians)
    {
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new Point2((point.X * cosine) - (point.Y * sine), (point.X * sine) + (point.Y * cosine));
    }

    private readonly record struct Point2(double X, double Y);
}
