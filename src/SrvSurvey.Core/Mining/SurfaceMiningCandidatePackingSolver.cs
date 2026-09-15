using System.Diagnostics;
using Point2 = SrvSurvey.Core.Mining.SurfaceMiningSplatPlanner.Point2;

namespace SrvSurvey.Core.Mining;

internal sealed class SurfaceMiningCandidatePackingSolver
{
    private readonly IReadOnlyList<Point2> points;
    private readonly bool[,] compatible;
    private readonly int[] compatibilityDegrees;
    private readonly int maximumSuggestions;
    private readonly Stopwatch timer;
    private readonly TimeSpan deadline;
    private readonly List<int> current = [];
    private List<Point2> best;

    private SurfaceMiningCandidatePackingSolver(
        IReadOnlyList<Point2> points,
        double spacing,
        IReadOnlyList<Point2> seed,
        int maximumSuggestions,
        Stopwatch timer,
        TimeSpan deadline
    )
    {
        this.points = points;
        this.maximumSuggestions = maximumSuggestions;
        this.timer = timer;
        this.deadline = deadline;
        best = [.. seed];
        compatible = new bool[points.Count, points.Count];
        compatibilityDegrees = new int[points.Count];
        BuildCompatibilityGraph(spacing);
    }

    public static List<Point2> FindMaximum(
        IReadOnlyList<Point2> points,
        double spacing,
        IReadOnlyList<Point2> seed,
        int maximumSuggestions,
        Stopwatch timer,
        TimeSpan deadline
    )
    {
        if (points.Count == 0 || seed.Count >= maximumSuggestions || timer.Elapsed >= deadline)
        {
            return [.. seed];
        }

        var solver = new SurfaceMiningCandidatePackingSolver(
            points,
            spacing,
            seed,
            maximumSuggestions,
            timer,
            deadline
        );
        solver.Search();
        return solver.best;
    }

    private bool IsExpired => timer.Elapsed >= deadline;

    private void BuildCompatibilityGraph(double spacing)
    {
        double spacingSquared = spacing * spacing;
        for (int left = 0; left < points.Count && !IsExpired; left++)
        {
            for (int right = left + 1; right < points.Count; right++)
            {
                if (DistanceSquared(points[left], points[right]) < spacingSquared)
                {
                    continue;
                }

                compatible[left, right] = true;
                compatible[right, left] = true;
                compatibilityDegrees[left]++;
                compatibilityDegrees[right]++;
            }
        }
    }

    private void Search()
    {
        if (IsExpired)
        {
            return;
        }

        var candidates = Enumerable
            .Range(0, points.Count)
            .OrderByDescending(index => compatibilityDegrees[index])
            .ThenBy(index => index)
            .ToList();
        Expand(candidates);
    }

    private void Expand(IReadOnlyList<int> candidates)
    {
        if (IsExpired || best.Count >= maximumSuggestions)
        {
            return;
        }

        ColoredCandidates colored = ColorSort(candidates);
        for (int index = colored.Vertices.Count - 1; index >= 0; index--)
        {
            if (IsExpired || current.Count + colored.ColorBounds[index] <= best.Count)
            {
                return;
            }

            int vertex = colored.Vertices[index];
            current.Add(vertex);
            List<int> next = CreateCompatiblePrefix(colored.Vertices, index, vertex);
            if (next.Count == 0)
            {
                SaveCurrentIfBetter();
            }
            else
            {
                Expand(next);
            }

            current.RemoveAt(current.Count - 1);
        }
    }

    private ColoredCandidates ColorSort(IReadOnlyList<int> candidates)
    {
        List<int> remaining = [.. candidates];
        List<int> ordered = new(candidates.Count);
        List<int> colorBounds = new(candidates.Count);
        int color = 0;
        while (remaining.Count > 0 && !IsExpired)
        {
            color++;
            List<int> colorClass = [];
            List<int> deferred = [];
            foreach (int vertex in remaining)
            {
                if (colorClass.All(other => !compatible[vertex, other]))
                {
                    colorClass.Add(vertex);
                    ordered.Add(vertex);
                    colorBounds.Add(color);
                }
                else
                {
                    deferred.Add(vertex);
                }
            }

            remaining = deferred;
        }

        return new ColoredCandidates(ordered, colorBounds);
    }

    private List<int> CreateCompatiblePrefix(List<int> ordered, int end, int vertex)
    {
        var result = new List<int>(end);
        for (int index = 0; index < end; index++)
        {
            if (compatible[vertex, ordered[index]])
            {
                result.Add(ordered[index]);
            }
        }

        return result;
    }

    private void SaveCurrentIfBetter()
    {
        if (current.Count > best.Count)
        {
            best = current.Select(index => points[index]).ToList();
        }
    }

    private static double DistanceSquared(Point2 first, Point2 second)
    {
        double deltaX = first.X - second.X;
        double deltaY = first.Y - second.Y;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private sealed record ColoredCandidates(List<int> Vertices, List<int> ColorBounds);
}
