using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Journal;

internal static class JournalSourceOrdering
{
    internal static void Attach(
        List<JournalEventEnvelope> events,
        int start,
        IReadOnlyDictionary<string, int> counts,
        string? witness = null
    )
    {
        witness ??= CreateWitness(events, start);
        for (int index = start; index < events.Count; index++)
        {
            JournalEventEnvelope entry = events[index];
            int count = Count(entry.SourceContentIdentity, counts);
            IReadOnlyList<JournalOrderEvidence> evidence = entry.SourcePreviousContentIdentity is { } previous
                ? [new(previous, count, Count(previous, counts)) { Witness = witness }]
                : [];
            // A non-null empty list explicitly identifies a source-chain root.
            events[index] = entry with
            {
                SourceOrderEvidence = evidence,
                SourceOccurrenceCount = count,
                SourceWitnessOccurrences = ImmutableDictionary<string, int>.Empty.Add(witness, count),
            };
        }
    }

    private static string CreateWitness(List<JournalEventEnvelope> events, int start)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string identity in events.Skip(start).Select(entry => entry.SourceContentIdentity ?? entry.RawJson))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(identity));
            hash.AppendData("\n"u8);
        }
        return "history:" + Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int Count(string? identity, IReadOnlyDictionary<string, int> counts)
    {
        int end = identity?.LastIndexOf(':') ?? -1;
        int start = end > 0 ? identity!.LastIndexOf(':', end - 1) : -1;
        return start >= 0 ? counts.GetValueOrDefault(identity![(start + 1)..end], 1) : 1;
    }
}
