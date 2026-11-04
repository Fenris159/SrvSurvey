namespace SrvSurvey.Core.Journeys;

/// <summary>Matches one source's order as a subsequence without equating repeated occurrences in partial copies.</summary>
internal sealed class JourneySourcePlacement
{
    private readonly Dictionary<int, HashSet<int>> parents;
    private readonly Dictionary<string, int[]> occurrences;
    private readonly Dictionary<string, int> remaining;
    private readonly HashSet<int> matched = [];

    internal JourneySourcePlacement(JourneyObservedRecord[] records, Dictionary<int, HashSet<int>> parents)
    {
        this.parents = parents;
        occurrences = parents
            .Keys.GroupBy(index => JourneyObservationOrder.ContentKey(records[index].Identity))
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .OrderBy(index => JourneyObservationOrder.CanonicalOccurrence(records[index].Identity))
                        .ThenBy(index => index)
                        .ToArray(),
                StringComparer.Ordinal
            );
        remaining = occurrences.ToDictionary(pair => pair.Key, pair => pair.Value.Length, StringComparer.Ordinal);
        foreach (int[] indices in occurrences.Values)
        {
            for (int index = 1; index < indices.Length; index++)
            {
                parents[indices[index]].Add(indices[index - 1]);
            }
        }
    }

    internal bool TryMatch(string content, int availableAfter, out int match)
    {
        match = (occurrences.GetValueOrDefault(content) ?? []).FirstOrDefault(
            index => !matched.Contains(index) && parents[index].All(matched.Contains),
            -1
        );
        return match >= 0 || remaining.GetValueOrDefault(content) <= availableAfter;
    }

    internal void Advance(string content, int match)
    {
        if (match >= 0)
        {
            matched.Add(match);
            remaining[content]--;
        }
    }

    internal void Undo(string content, int match)
    {
        if (match >= 0)
        {
            matched.Remove(match);
            remaining[content]++;
        }
    }
}
