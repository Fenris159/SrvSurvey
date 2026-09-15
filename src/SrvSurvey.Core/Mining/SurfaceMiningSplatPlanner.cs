using System.Diagnostics;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Mining;

/// <summary>
/// Builds a dense, deterministic hybrid rig layout inside a traced deposit boundary.
/// Suggested positions are planning aids and remain separate from placed mining rigs.
/// </summary>
public static class SurfaceMiningSplatPlanner
{
    private const int MaximumSuggestions = 6;
    private const double MaximumBoundarySpanMeters = 20_000;
    private const int MaximumHybridCandidates = 2_048;
    private const int MaximumBoundaryCandidates = 512;
    private const double CandidateMergeResolutionMeters = 0.1;
    private static readonly TimeSpan MaximumSearchDuration = TimeSpan.FromSeconds(8);
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

        var searchTimer = Stopwatch.StartNew();
        List<Point2> best = CreateBestHexagonalLayout(polygon, minimumSpacingMeters);
        Point2? interiorPoint = FindInteriorPoint(polygon);
        if (best.Count == 0 && interiorPoint is { } onlySuggestion)
        {
            best.Add(onlySuggestion);
        }

        List<Point2> hybridCandidates = CreateHybridCandidates(polygon, minimumSpacingMeters, best, interiorPoint);
        List<Point2> diameterLayout = CreateDiameterLayout(hybridCandidates, minimumSpacingMeters, interiorPoint);
        UseIfBetter(diameterLayout, ref best);
        foreach (
            IEnumerable<Point2> ordering in CreateCandidateOrderings(
                hybridCandidates,
                minimumSpacingMeters,
                interiorPoint
            )
        )
        {
            UseIfBetter(CreateGreedyLayout(ordering, minimumSpacingMeters), ref best);
        }

        SurfaceMiningCandidatePackingSolver.SearchResult exactSearch = SurfaceMiningCandidatePackingSolver.FindMaximum(
            hybridCandidates,
            minimumSpacingMeters,
            best,
            MaximumSuggestions,
            searchTimer,
            MaximumSearchDuration
        );
        if (exactSearch.Completed)
        {
            UseIfBetter(exactSearch.Layout, ref best);
        }

        return best.Select(point => Unproject(origin, point, planetRadiusMeters)).ToArray();
    }

    private static List<Point2> CreateBestHexagonalLayout(IReadOnlyList<Point2> polygon, double spacing)
    {
        List<Point2> best = [];
        foreach (double rotation in CandidateRotations)
        {
            foreach (double offsetX in CandidateOffsets)
            {
                foreach (double offsetY in CandidateOffsets)
                {
                    List<Point2> candidate = CreateCandidateLayout(polygon, spacing, rotation, offsetX, offsetY);
                    if (candidate.Count > best.Count)
                    {
                        best = candidate;
                    }
                }
            }
        }

        return best;
    }

    private static List<Point2> CreateHybridCandidates(
        IReadOnlyList<Point2> polygon,
        double spacing,
        IReadOnlyList<Point2> baseline,
        Point2? interiorPoint
    )
    {
        var candidates = new List<Point2>(MaximumHybridCandidates);
        var occupiedCells = new HashSet<(long X, long Y)>();
        if (interiorPoint is { } interior)
        {
            AddCandidate(interior, candidates, occupiedCells);
        }

        foreach (Point2 point in baseline)
        {
            AddCandidate(point, candidates, occupiedCells);
        }

        AddBoundaryCandidates(polygon, candidates, occupiedCells);
        AddInteriorGridCandidates(polygon, spacing, candidates, occupiedCells);
        return candidates;
    }

    private static void AddBoundaryCandidates(
        IReadOnlyList<Point2> polygon,
        List<Point2> candidates,
        HashSet<(long X, long Y)> occupiedCells
    )
    {
        int stride = Math.Max(1, (int)Math.Ceiling((double)polygon.Count / MaximumBoundaryCandidates));
        for (int index = 0; index < polygon.Count && candidates.Count < MaximumHybridCandidates; index += stride)
        {
            AddCandidate(polygon[index], candidates, occupiedCells);
        }

        AddCandidate(polygon.MinBy(point => point.X), candidates, occupiedCells);
        AddCandidate(polygon.MaxBy(point => point.X), candidates, occupiedCells);
        AddCandidate(polygon.MinBy(point => point.Y), candidates, occupiedCells);
        AddCandidate(polygon.MaxBy(point => point.Y), candidates, occupiedCells);
    }

    private static void AddInteriorGridCandidates(
        IReadOnlyList<Point2> polygon,
        double spacing,
        List<Point2> candidates,
        HashSet<(long X, long Y)> occupiedCells
    )
    {
        double minX = polygon.Min(point => point.X);
        double maxX = polygon.Max(point => point.X);
        double minY = polygon.Min(point => point.Y);
        double maxY = polygon.Max(point => point.Y);
        int remaining = MaximumHybridCandidates - candidates.Count;
        if (remaining <= 0)
        {
            return;
        }

        double step = spacing / 8;
        double columns = Math.Floor((maxX - minX) / step) + 1;
        double rows = Math.Floor((maxY - minY) / step) + 1;
        double gridPointCount = columns * rows;
        if (gridPointCount > remaining)
        {
            step *= Math.Sqrt(gridPointCount / remaining);
        }

        for (double y = minY + (step / 2); y < maxY && candidates.Count < MaximumHybridCandidates; y += step)
        {
            for (double x = minX + (step / 2); x < maxX && candidates.Count < MaximumHybridCandidates; x += step)
            {
                var candidate = new Point2(x, y);
                if (Contains(polygon, candidate))
                {
                    AddCandidate(candidate, candidates, occupiedCells);
                }
            }
        }
    }

    private static void AddCandidate(Point2 candidate, List<Point2> candidates, HashSet<(long X, long Y)> occupiedCells)
    {
        if (candidates.Count >= MaximumHybridCandidates)
        {
            return;
        }

        var cell = (
            X: (long)Math.Round(candidate.X / CandidateMergeResolutionMeters),
            Y: (long)Math.Round(candidate.Y / CandidateMergeResolutionMeters)
        );
        if (occupiedCells.Add(cell))
        {
            candidates.Add(candidate);
        }
    }

    private static List<Point2> CreateDiameterLayout(List<Point2> candidates, double spacing, Point2? interiorPoint)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        Point2 first = interiorPoint ?? candidates[0];
        Point2 second = first;
        double farthestSquared = 0;
        for (int left = 0; left < candidates.Count; left++)
        {
            for (int right = left + 1; right < candidates.Count; right++)
            {
                double distanceSquared = DistanceSquared(candidates[left], candidates[right]);
                if (distanceSquared > farthestSquared)
                {
                    farthestSquared = distanceSquared;
                    first = candidates[left];
                    second = candidates[right];
                }
            }
        }

        return farthestSquared >= spacing * spacing ? [first, second] : [interiorPoint ?? candidates[0]];
    }

    private static IEnumerable<IEnumerable<Point2>> CreateCandidateOrderings(
        IReadOnlyList<Point2> candidates,
        double spacing,
        Point2? interiorPoint
    )
    {
        Point2 center =
            interiorPoint ?? new Point2(candidates.Average(point => point.X), candidates.Average(point => point.Y));
        Dictionary<Point2, int> conflictDegrees = CountConflicts(candidates, spacing);
        yield return candidates
            .OrderBy(point => conflictDegrees[point])
            .ThenBy(point => point.X)
            .ThenBy(point => point.Y);
        yield return candidates.OrderBy(point => point.X).ThenBy(point => point.Y);
        yield return candidates.OrderByDescending(point => point.X).ThenBy(point => point.Y);
        yield return candidates.OrderBy(point => point.Y).ThenBy(point => point.X);
        yield return candidates.OrderByDescending(point => point.Y).ThenBy(point => point.X);
        yield return candidates.OrderBy(point => Math.Atan2(point.Y - center.Y, point.X - center.X));
        yield return candidates.OrderByDescending(point => DistanceSquared(point, center));
    }

    private static Dictionary<Point2, int> CountConflicts(IReadOnlyList<Point2> points, double spacing)
    {
        double spacingSquared = spacing * spacing;
        var counts = points.ToDictionary(point => point, _ => 0);
        for (int left = 0; left < points.Count; left++)
        {
            for (int right = left + 1; right < points.Count; right++)
            {
                if (DistanceSquared(points[left], points[right]) < spacingSquared)
                {
                    counts[points[left]]++;
                    counts[points[right]]++;
                }
            }
        }

        return counts;
    }

    private static List<Point2> CreateGreedyLayout(IEnumerable<Point2> candidates, double spacing)
    {
        double spacingSquared = spacing * spacing;
        var result = new List<Point2>();
        foreach (
            Point2 candidate in candidates.Where(candidate =>
                result.All(existing => DistanceSquared(existing, candidate) >= spacingSquared)
            )
        )
        {
            result.Add(candidate);
            if (result.Count >= MaximumSuggestions)
            {
                break;
            }
        }

        return result;
    }

    private static Point2? FindInteriorPoint(Point2[] polygon)
    {
        double y = (polygon.Min(point => point.Y) + polygon.Max(point => point.Y)) / 2;
        List<double> intersections = [];
        for (int current = 0, previous = polygon.Length - 1; current < polygon.Length; previous = current++)
        {
            Point2 first = polygon[current];
            Point2 second = polygon[previous];
            if ((first.Y > y) == (second.Y > y))
            {
                continue;
            }

            intersections.Add(((second.X - first.X) * (y - first.Y) / (second.Y - first.Y)) + first.X);
        }

        intersections.Sort();
        Point2? best = null;
        double widest = 0;
        for (int index = 0; index + 1 < intersections.Count; index += 2)
        {
            double width = intersections[index + 1] - intersections[index];
            if (width > widest)
            {
                widest = width;
                best = new Point2((intersections[index] + intersections[index + 1]) / 2, y);
            }
        }

        return best;
    }

    private static void UseIfBetter(List<Point2> candidate, ref List<Point2> best)
    {
        if (candidate.Count > best.Count)
        {
            best = candidate;
        }
    }

    private static double DistanceSquared(Point2 first, Point2 second)
    {
        double deltaX = first.X - second.X;
        double deltaY = first.Y - second.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
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

    private static double SignedArea(Point2[] polygon)
    {
        double area = 0;
        for (int index = 0; index < polygon.Length; index++)
        {
            Point2 current = polygon[index];
            Point2 next = polygon[(index + 1) % polygon.Length];
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

    internal readonly record struct Point2(double X, double Y);
}
