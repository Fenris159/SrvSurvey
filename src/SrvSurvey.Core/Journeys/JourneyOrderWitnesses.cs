using System.Collections.Immutable;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

/// <summary>Retains what each source proves without identifying an ambiguous occurrence across different copies.</summary>
internal sealed class JourneyOrderWitnesses
{
    private readonly JourneyObservedRecord[] records;
    private readonly IReadOnlyDictionary<string, string> aliases;
    private readonly Dictionary<string, int> indices;
    private readonly Dictionary<string, int> multiplicities;
    private readonly IReadOnlyDictionary<string, int> occurrenceRanks;
    private readonly Dictionary<(string Witness, string Content), int> witnessedCounts = [];
    private readonly Dictionary<(string Witness, int Child), HashSet<int>> witnessedParents = [];

    internal JourneyOrderWitnesses(
        JourneyObservedRecord[] records,
        IReadOnlyDictionary<string, string> aliases,
        IReadOnlyDictionary<string, int> occurrenceRanks
    )
    {
        this.records = records;
        this.aliases = aliases;
        this.occurrenceRanks = occurrenceRanks;
        indices = records
            .Select((record, index) => (record.Identity, index))
            .ToDictionary(pair => pair.Identity, pair => pair.index, StringComparer.Ordinal);
        multiplicities = JourneyObservationOrder.Multiplicities(records);
        for (int index = 0; index < records.Length; index++)
        {
            LearnSource(index);
        }
    }

    private void LearnSource(int index)
    {
        JourneyObservedRecord record = records[index];
        foreach (
            (string witness, int count) in record.SourceWitnessOccurrences ?? ImmutableDictionary<string, int>.Empty
        )
        {
            LearnCount(witness, record.Identity, count);
        }
        foreach (JournalOrderEvidence edge in Evidence(record).Where(edge => edge.Witness is not null))
        {
            string witness = edge.Witness!;
            LearnCount(witness, record.Identity, edge.Occurrences);
            LearnCount(witness, edge.PreviousIdentity, edge.PreviousOccurrences);
            if (TryParent(edge, out int parent))
            {
                if (!witnessedParents.TryGetValue((witness, index), out HashSet<int>? parents))
                {
                    parents = [];
                    witnessedParents.Add((witness, index), parents);
                }
                parents.Add(parent);
            }
        }
    }

    private void LearnCount(string witness, string identity, int count)
    {
        (string, string) key = (witness, JourneyObservationOrder.ContentKey(identity));
        witnessedCounts[key] = Math.Max(witnessedCounts.GetValueOrDefault(key), count);
    }

    internal HashSet<int> Predecessors(int child)
    {
        var result = new HashSet<int>();
        JournalOrderEvidence[] evidence = Evidence(records[child]).ToArray();
        foreach (JournalOrderEvidence edge in evidence.Where(edge => edge.Witness is null))
        {
            if (
                edge.Occurrences >= Multiplicity(records[child].Identity)
                && edge.PreviousOccurrences >= Multiplicity(edge.PreviousIdentity)
                && TryParent(edge, out int parent)
            )
            {
                result.Add(parent);
            }
        }
        foreach (
            string witness in evidence.Select(edge => edge.Witness).OfType<string>().Distinct(StringComparer.Ordinal)
        )
        {
            AddUnambiguousAncestors(child, witness, result);
        }
        return result;
    }

    internal JourneySourcePlacement[] Placements()
    {
        return witnessedCounts
            .Keys.Select(key => key.Witness)
            .Distinct(StringComparer.Ordinal)
            .Select(CreatePlacement)
            .ToArray();
    }

    internal int[][] Components()
    {
        int[] roots = Enumerable.Range(0, records.Length).ToArray();
        for (int child = 0; child < records.Length; child++)
        {
            foreach (JournalOrderEvidence edge in Evidence(records[child]))
            {
                if (TryParent(edge, out int parent))
                {
                    Join(roots, child, parent);
                }
            }
        }
        foreach (
            IGrouping<string, int> group in Enumerable
                .Range(0, records.Length)
                .GroupBy(index => JourneyObservationOrder.ContentKey(records[index].Identity))
        )
        {
            int first = group.First();
            foreach (int index in group)
            {
                Join(roots, first, index);
            }
        }
        return Enumerable
            .Range(0, records.Length)
            .GroupBy(index => Root(roots, index))
            .Select(group => group.ToArray())
            .ToArray();
    }

    internal bool[] SearchRelevantRecords()
    {
        // Unique terminal records cannot unlock another placement. Their interleavings do not change feasibility.
        bool[] relevant = records.Select(record => Multiplicity(record.Identity) > 1).ToArray();
        foreach (JournalOrderEvidence edge in records.SelectMany(Evidence))
        {
            if (TryParent(edge, out int parent))
            {
                relevant[parent] = true;
            }
        }
        return relevant;
    }

    private static void Join(int[] roots, int left, int right) => roots[Root(roots, right)] = Root(roots, left);

    private static int Root(int[] roots, int index)
    {
        while (roots[index] != index)
        {
            roots[index] = roots[roots[index]];
            index = roots[index];
        }
        return index;
    }

    private JourneySourcePlacement CreatePlacement(string witness)
    {
        var parents = new Dictionary<int, HashSet<int>>();
        for (int index = 0; index < records.Length; index++)
        {
            HashSet<int>? preceding = witnessedParents.GetValueOrDefault((witness, index));
            if (records[index].SourceWitnessOccurrences?.ContainsKey(witness) == true || preceding is not null)
            {
                parents.TryAdd(index, []);
            }
            foreach (int parent in preceding ?? [])
            {
                parents[index].Add(parent);
                parents.TryAdd(parent, []);
            }
        }
        return new JourneySourcePlacement(records, parents, OrderOccurrences);
    }

    internal int[] OrderOccurrences(IEnumerable<int> occurrences) =>
        occurrences
            .OrderBy(OccurrenceRank)
            .ThenBy(index => JourneyObservationOrder.Position(records[index].SourceRecordIdentity).Offset)
            .ThenBy(index => index)
            .ToArray();

    private long OccurrenceRank(int index)
    {
        JourneyObservedRecord record = records[index];
        if (occurrenceRanks.TryGetValue(record.Identity, out int rank))
        {
            return rank;
        }
        return record.Identity.StartsWith(
            JourneyObservationOrder.ContentKey(record.Identity) + ":",
            StringComparison.Ordinal
        )
            ? JourneyObservationOrder.CanonicalOccurrence(record.Identity)
            : JourneyObservationOrder.Position(record.SourceRecordIdentity).Offset;
    }

    private void AddUnambiguousAncestors(int child, string witness, HashSet<int> result)
    {
        if (!IsUnambiguous(child, witness))
        {
            return;
        }
        var pending = new Stack<int>(witnessedParents.GetValueOrDefault((witness, child)) ?? []);
        var visited = new HashSet<int>();
        while (pending.TryPop(out int parent))
        {
            if (!visited.Add(parent))
            {
                continue;
            }
            if (parent == child)
            {
                throw new IOException(
                    "Journal copies contain conflicting record order; Journey observations were retained for recovery."
                );
            }
            if (IsUnambiguous(parent, witness))
            {
                result.Add(parent);
            }
            else
            {
                foreach (int ancestor in witnessedParents.GetValueOrDefault((witness, parent)) ?? [])
                {
                    pending.Push(ancestor);
                }
            }
        }
    }

    private bool IsUnambiguous(int index, string witness)
    {
        string content = JourneyObservationOrder.ContentKey(records[index].Identity);
        return witnessedCounts.GetValueOrDefault((witness, content)) >= multiplicities.GetValueOrDefault(content, 1);
    }

    private int Multiplicity(string identity) =>
        multiplicities.GetValueOrDefault(JourneyObservationOrder.ContentKey(identity), 1);

    private bool TryParent(JournalOrderEvidence edge, out int parent) =>
        indices.TryGetValue(aliases.GetValueOrDefault(edge.PreviousIdentity, edge.PreviousIdentity), out parent);

    private IEnumerable<JournalOrderEvidence> Evidence(JourneyObservedRecord record) =>
        record.OrderEvidence
        ?? record.PreviousIdentities.Select(previous => new JournalOrderEvidence(
            previous,
            Multiplicity(record.Identity),
            Multiplicity(previous)
        ));
}
