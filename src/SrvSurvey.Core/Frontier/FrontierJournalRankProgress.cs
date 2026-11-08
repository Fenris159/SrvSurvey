using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Frontier;

/// <summary>Pairs journal percentages with their rank levels without mistaking CAPI rank ordinals for progress.</summary>
public sealed class FrontierJournalRankProgress
{
    private readonly Dictionary<string, Observation> observations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Accepts attributed rank observations and clears old percentages when a promotion changes the level.</summary>
    public bool Apply(JournalEventEnvelope entry)
    {
        if (entry.EventName is not ("Rank" or "Promotion" or "Progress") || entry.Timestamp is not { } at)
        {
            return false;
        }
        bool changed = false;
        foreach (JsonProperty property in entry.Payload.EnumerateObject())
        {
            string key = property.Name.ToLowerInvariant();
            if (
                key
                is not (
                    "combat"
                    or "trade"
                    or "explore"
                    or "soldier"
                    or "exobiologist"
                    or "cqc"
                    or "federation"
                    or "empire"
                )
            )
            {
                continue;
            }
            observations.TryGetValue(key, out Observation? previous);
            if (previous is not null && at < previous.At)
            {
                continue;
            }
            Observation? current = ReadObservation(entry.EventName, property.Value, previous, at);
            if (current is not null)
            {
                observations[key] = current;
                changed |= current != previous;
            }
        }
        return changed;
    }

    /// <summary>Keeps CAPI authoritative over older rank levels while filling its missing progress from journals.</summary>
    public FrontierRankSnapshot Project(FrontierRankSnapshot rank, DateTimeOffset fetchedAt)
    {
        if (!observations.TryGetValue(rank.Key, out Observation? observed) || observed.Level is not { } level)
        {
            return rank;
        }
        if (level != rank.Level)
        {
            if (observed.At < fetchedAt)
            {
                return rank;
            }
            rank = FrontierCapiSnapshotParser.CreateRank(rank.Key, level);
        }
        return rank with { ProgressPercent = observed.Percent };
    }

    private static Observation? ReadObservation(
        string eventName,
        JsonElement value,
        Observation? previous,
        DateTimeOffset at
    )
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }
        if (eventName == "Progress")
        {
            return value.TryGetDouble(out double percent) && double.IsFinite(percent)
                ? new(previous?.Level, Math.Clamp(percent, 0, 100), at)
                : null;
        }
        if (!value.TryGetInt32(out int level) || level < 0)
        {
            return null;
        }
        double? percentAtLevel = previous?.Level == level ? previous.Percent : null;
        return new(level, percentAtLevel, at);
    }

    private sealed record Observation(int? Level, double? Percent, DateTimeOffset At);
}
