namespace SrvSurvey.Core.Journeys;

/// <summary>Finds a deterministic order that satisfies both definite edges and each partial source's placement.</summary>
internal sealed class JourneyObservationSchedule
{
    private const int SearchBudget = 100_000;
    private readonly JourneySourcePlacement[] placements;
    private readonly string[] content;
    private readonly Dictionary<string, int> remaining;
    private readonly HashSet<int>[] children;
    private readonly int[] degree;
    private readonly SortedSet<int> ready;
    private readonly Comparison<int> compare;
    private readonly bool[] relevant;
    private readonly bool[] emitted;
    private int attempts;

    internal JourneyObservationSchedule(
        JourneyObservedRecord[] records,
        JourneyOrderWitnesses witnesses,
        Comparison<int> compare
    )
    {
        this.compare = compare;
        relevant = witnesses.SearchRelevantRecords();
        emitted = new bool[records.Length];
        placements = witnesses.Placements();
        content = records.Select(record => JourneyObservationOrder.ContentKey(record.Identity)).ToArray();
        remaining = content
            .GroupBy(key => key)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        children = records.Select(_ => new HashSet<int>()).ToArray();
        degree = new int[records.Length];
        ready = new SortedSet<int>(Comparer<int>.Create(compare));
        for (int child = 0; child < records.Length; child++)
        {
            foreach (int parent in witnesses.Predecessors(child).Where(parent => parent != child))
            {
                Link(parent, child);
            }
        }
        foreach (IGrouping<string, int> group in Enumerable.Range(0, records.Length).GroupBy(index => content[index]))
        {
            int[] occurrences = witnesses.OrderOccurrences(group);
            for (int index = 1; index < occurrences.Length; index++)
            {
                Link(occurrences[index - 1], occurrences[index]);
            }
        }
        for (int index = 0; index < degree.Length; index++)
        {
            if (degree[index] == 0)
            {
                ready.Add(index);
            }
        }
    }

    private void Link(int parent, int child)
    {
        if (children[parent].Add(child))
        {
            degree[child]++;
        }
    }

    internal IReadOnlyList<int> Sort()
    {
        var result = new List<int>(content.Length);
        List<SearchFrame> frames = [new()];
        var failed = new HashSet<string>(StringComparer.Ordinal);
        while (result.Count < content.Length)
        {
            SearchFrame frame = frames[^1];
            frame.StateKey ??= StateKey();
            if (!failed.Contains(frame.StateKey) && TryNext(frame, out int index, out int[] matches))
            {
                frame.Matches = matches;
                Advance(index, matches);
                result.Add(index);
                frames.Add(new());
            }
            else
            {
                failed.Add(frame.StateKey);
                if (result.Count == 0)
                {
                    throw Conflict();
                }
                frames.RemoveAt(frames.Count - 1);
                int previous = result[^1];
                result.RemoveAt(result.Count - 1);
                Undo(previous, frames[^1].Matches);
            }
        }
        return result;
    }

    private string StateKey() =>
        string.Join(',', Enumerable.Range(0, emitted.Length).Where(index => relevant[index] && emitted[index]))
        + ":"
        + string.Join(';', placements.Select(placement => placement.StateKey(relevant)));

    private bool TryNext(SearchFrame frame, out int index, out int[] matches)
    {
        foreach (int candidate in ready)
        {
            if (frame.LastTried is { } previous && compare(candidate, previous) <= 0)
            {
                continue;
            }
            frame.LastTried = candidate;
            if (++attempts > Math.Max((long)content.Length * 4, SearchBudget))
            {
                throw new IOException(
                    "Journal source ordering needs more evidence; Journey observations were retained for recovery."
                );
            }
            if (TryMatch(candidate, out matches))
            {
                index = candidate;
                return true;
            }
        }
        index = -1;
        matches = [];
        return false;
    }

    private bool TryMatch(int index, out int[] matches)
    {
        matches = new int[placements.Length];
        for (int witness = 0; witness < placements.Length; witness++)
        {
            if (!placements[witness].TryMatch(content[index], remaining[content[index]] - 1, out matches[witness]))
            {
                return false;
            }
        }
        return true;
    }

    private void Advance(int index, int[] matches)
    {
        ready.Remove(index);
        emitted[index] = true;
        remaining[content[index]]--;
        for (int witness = 0; witness < placements.Length; witness++)
        {
            placements[witness].Advance(content[index], matches[witness]);
        }
        foreach (int child in children[index])
        {
            if (--degree[child] == 0)
            {
                ready.Add(child);
            }
        }
    }

    private void Undo(int index, int[] matches)
    {
        foreach (int child in children[index])
        {
            if (degree[child]++ == 0)
            {
                ready.Remove(child);
            }
        }
        for (int witness = 0; witness < placements.Length; witness++)
        {
            placements[witness].Undo(content[index], matches[witness]);
        }
        remaining[content[index]]++;
        emitted[index] = false;
        ready.Add(index);
    }

    private static IOException Conflict() =>
        new("Journal copies contain conflicting record order; Journey observations were retained for recovery.");

    private sealed class SearchFrame
    {
        internal int? LastTried { get; set; }
        internal int[] Matches { get; set; } = [];
        internal string? StateKey { get; set; }
    }
}
