using System.Globalization;

namespace SrvSurvey.Core.Journeys;

internal static class JourneyObservationOrder
{
    internal static IReadOnlyList<JourneyObservedRecord> Sort(
        IReadOnlyList<JourneyObservedRecord> records,
        IReadOnlyDictionary<string, string> aliases
    )
    {
        var result = new List<JourneyObservedRecord>(records.Count);
        foreach (
            IGrouping<DateTimeOffset, JourneyObservedRecord> group in records
                .GroupBy(record => record.Timestamp)
                .OrderBy(group => group.Key)
        )
        {
            OrderGroup(group.ToArray(), aliases, result);
        }
        return result;
    }

    private static void OrderGroup(
        JourneyObservedRecord[] records,
        IReadOnlyDictionary<string, string> aliases,
        List<JourneyObservedRecord> result
    )
    {
        var indices = records
            .Select((record, index) => (record.Identity, index))
            .ToDictionary(pair => pair.Identity, pair => pair.index, StringComparer.Ordinal);
        int[] degree = new int[records.Length];
        var children = new List<int>[records.Length];
        for (int index = 0; index < records.Length; index++)
        {
            children[index] = [];
        }
        LinkPredecessors(records, aliases, indices, degree, children);
        var ready = new SortedSet<int>(Comparer<int>.Create((left, right) => Compare(records, left, right)));
        for (int index = 0; index < degree.Length; index++)
        {
            if (degree[index] == 0)
            {
                ready.Add(index);
            }
        }
        int written = 0;
        while (ready.Count > 0)
        {
            int index = ready.Min;
            ready.Remove(index);
            result.Add(records[index]);
            written++;
            foreach (int child in children[index])
            {
                if (--degree[child] == 0)
                {
                    ready.Add(child);
                }
            }
        }
        if (written != records.Length)
        {
            throw new IOException(
                "Journal copies contain conflicting record order; Journey observations were retained for recovery."
            );
        }
    }

    private static void LinkPredecessors(
        JourneyObservedRecord[] records,
        IReadOnlyDictionary<string, string> aliases,
        Dictionary<string, int> indices,
        int[] degree,
        List<int>[] children
    )
    {
        for (int index = 0; index < records.Length; index++)
        {
            foreach (
                string predecessor in records[index]
                    .PreviousIdentities.Select(id => aliases.GetValueOrDefault(id, id))
                    .Distinct(StringComparer.Ordinal)
            )
            {
                if (indices.TryGetValue(predecessor, out int parent) && parent != index)
                {
                    degree[index]++;
                    children[parent].Add(index);
                }
            }
        }
    }

    private static int Compare(JourneyObservedRecord[] records, int left, int right)
    {
        if (left == right)
        {
            return 0;
        }
        (string File, long Offset) a = Position(records[left].SourceRecordIdentity);
        (string File, long Offset) b = Position(records[right].SourceRecordIdentity);
        int file = string.Compare(a.File, b.File, StringComparison.Ordinal);
        if (file != 0)
        {
            return file;
        }
        int offset = a.Offset.CompareTo(b.Offset);
        return offset != 0 ? offset : left.CompareTo(right);
    }

    internal static (string File, long Offset) Position(string? identity)
    {
        int separator = identity?.LastIndexOf(':') ?? -1;
        return
            separator > 0
            && long.TryParse(
                identity.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long offset
            )
            ? (identity![..separator], offset)
            : (identity ?? string.Empty, long.MaxValue);
    }
}
