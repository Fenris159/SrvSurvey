namespace SrvSurvey.Core.Journal;

internal static class JournalSourceOrdering
{
    internal static void Attach(List<JournalEventEnvelope> events, int start, IReadOnlyDictionary<string, int> counts)
    {
        for (int index = start; index < events.Count; index++)
        {
            JournalEventEnvelope entry = events[index];
            int count = Count(entry.SourceContentIdentity, counts);
            IReadOnlyList<JournalOrderEvidence> evidence = entry.SourcePreviousContentIdentity is { } previous
                ? [new(previous, count, Count(previous, counts))]
                : [];
            // A non-null empty list explicitly identifies a source-chain root.
            events[index] = entry with
            {
                SourceOrderEvidence = evidence,
                SourceOccurrenceCount = count,
            };
        }
    }

    private static int Count(string? identity, IReadOnlyDictionary<string, int> counts)
    {
        int end = identity?.LastIndexOf(':') ?? -1;
        int start = end > 0 ? identity!.LastIndexOf(':', end - 1) : -1;
        return start >= 0 ? counts.GetValueOrDefault(identity![(start + 1)..end], 1) : 1;
    }
}
