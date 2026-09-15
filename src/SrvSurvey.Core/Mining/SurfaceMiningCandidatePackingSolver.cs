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
    private readonly bool compatibilityGraphCompleted;
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
        compatibilityGraphCompleted = BuildCompatibilityGraph(spacing);
    }

    public static SearchResult FindMaximum(
        IReadOnlyList<Point2> points,
        double spacing,
        IReadOnlyList<Point2> seed,
        int maximumSuggestions,
        Stopwatch timer,
        TimeSpan deadline
    )
    {
        if (points.Count == 0 || seed.Count >= maximumSuggestions)
        {
            return new SearchResult([.. seed], Completed: true);
        }

        if (timer.Elapsed >= deadline)
        {
            return new SearchResult([.. seed], Completed: false);
        }

        var solver = new SurfaceMiningCandidatePackingSolver(
            points,
            spacing,
            seed,
            maximumSuggestions,
            timer,
            deadline
        );
        bool completed = solver.Search();
        return new SearchResult(solver.best, completed);
    }

    private bool IsExpired => timer.Elapsed >= deadline;

    private bool BuildCompatibilityGraph(double spacing)
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

        return !IsExpired;
    }

    private bool Search()
    {
        if (!compatibilityGraphCompleted || IsExpired)
        {
            return false;
        }

        var candidates = Enumerable
            .Range(0, points.Count)
            .OrderByDescending(index => compatibilityDegrees[index])
            .ThenBy(index => index)
            .ToList();
        return !IsExpired && Expand(candidates);
    }

    private bool Expand(IReadOnlyList<int> candidates)
    {
        if (best.Count >= maximumSuggestions)
        {
            return true;
        }

        if (IsExpired)
        {
            return false;
        }

        ColoredCandidates colored = ColorSort(candidates);
        if (!colored.Completed)
        {
            return false;
        }

        for (int index = colored.Vertices.Count - 1; index >= 0; index--)
        {
            if (IsExpired)
            {
                return false;
            }

            if (current.Count + colored.ColorBounds[index] <= best.Count)
            {
                return true;
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
                bool completed = Expand(next);
                current.RemoveAt(current.Count - 1);
                if (!completed)
                {
                    return false;
                }

                continue;
            }

            current.RemoveAt(current.Count - 1);
        }

        return true;
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

        return new ColoredCandidates(ordered, colorBounds, Completed: !IsExpired);
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

    internal readonly record struct SearchResult(List<Point2> Layout, bool Completed);

    private sealed record ColoredCandidates(List<int> Vertices, List<int> ColorBounds, bool Completed);
}
