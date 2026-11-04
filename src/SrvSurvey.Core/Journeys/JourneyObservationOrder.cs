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
        var occurrenceRanks = aliases
            .GroupBy(pair => pair.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Min(pair => CanonicalOccurrence(pair.Key)),
                StringComparer.Ordinal
            );
        foreach (
            IGrouping<DateTimeOffset, JourneyObservedRecord> group in records
                .GroupBy(record => record.Timestamp)
                .OrderBy(group => group.Key)
        )
        {
            OrderGroup(group.ToArray(), aliases, occurrenceRanks, result);
        }
        return result;
    }

    private static void OrderGroup(
        JourneyObservedRecord[] records,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, int> occurrenceRanks,
        List<JourneyObservedRecord> result
    )
    {
        var witnesses = new JourneyOrderWitnesses(records, aliases, occurrenceRanks);
        var heads = new PriorityQueue<Queue<int>, int>(
            Comparer<int>.Create((left, right) => Compare(records, left, right))
        );
        foreach (int[] component in witnesses.Components())
        {
            JourneyObservedRecord[] connected = component.Select(index => records[index]).ToArray();
            var schedule = new JourneyObservationSchedule(
                connected,
                new JourneyOrderWitnesses(connected, aliases, occurrenceRanks),
                (left, right) => Compare(connected, left, right)
            );
            var ordered = new Queue<int>(schedule.Sort().Select(index => component[index]));
            heads.Enqueue(ordered, ordered.Peek());
        }
        while (heads.TryDequeue(out Queue<int>? ordered, out _))
        {
            result.Add(records[ordered.Dequeue()]);
            if (ordered.Count > 0)
            {
                heads.Enqueue(ordered, ordered.Peek());
            }
        }
    }

    internal static Dictionary<string, int> Multiplicities(IReadOnlyList<JourneyObservedRecord> records)
    {
        var counts = records
            .GroupBy(record => ContentKey(record.Identity))
            .ToDictionary(
                group => group.Key,
                group => group.Select(record => record.Identity).Distinct(StringComparer.Ordinal).Count(),
                StringComparer.Ordinal
            );
        foreach (JourneyObservedRecord record in records)
        {
            string key = ContentKey(record.Identity);
            int observed = record.SourceOccurrenceCount ?? CanonicalOccurrence(record.Identity);
            int witnessed = record.SourceWitnessOccurrences?.Values.DefaultIfEmpty(1).Max() ?? 1;
            counts[key] = Math.Max(counts[key], Math.Max(observed, witnessed));
            foreach (SrvSurvey.Core.Journal.JournalOrderEvidence edge in record.OrderEvidence ?? [])
            {
                counts[key] = Math.Max(counts[key], edge.Occurrences);
                string parent = ContentKey(edge.PreviousIdentity);
                counts[parent] = Math.Max(counts.GetValueOrDefault(parent, 1), edge.PreviousOccurrences);
            }
        }
        return counts;
    }

    internal static string ContentKey(string identity)
    {
        int last = identity.LastIndexOf(':');
        int prior = last > 0 ? identity.LastIndexOf(':', last - 1) : -1;
        if (prior < 0)
        {
            return identity;
        }
        if (
            last - prior - 1 == 64
            && int.TryParse(identity.AsSpan(last + 1), NumberStyles.None, CultureInfo.InvariantCulture, out _)
        )
        {
            return identity[..last];
        }
        return identity.Length - last - 1 == 64 ? identity[..prior] + ":" + identity[(last + 1)..] : identity;
    }

    internal static int CanonicalOccurrence(string identity)
    {
        string key = ContentKey(identity);
        return
            identity.StartsWith(key + ":", StringComparison.Ordinal)
            && int.TryParse(
                identity.AsSpan(key.Length + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int occurrence
            )
            && occurrence < int.MaxValue
            ? occurrence + 1
            : 1;
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
