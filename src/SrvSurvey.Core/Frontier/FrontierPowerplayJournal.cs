using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Frontier;

/// <summary>Powerplay status from a timestamped journal event, including ranks beyond the legacy five ratings.</summary>
public sealed record FrontierPowerplaySnapshot(string Power, int? Rank, long? Merits, DateTimeOffset UpdatedAt);

public static class FrontierPowerplayJournal
{
    /// <summary>Reads only events attributed to the requested Frontier account from a remote daily journal.</summary>
    public static FrontierPowerplaySnapshot? Parse(
        string journal,
        string frontierId,
        FrontierPowerplaySnapshot? previous = null
    )
    {
        bool matches = false;
        FrontierPowerplaySnapshot? result = previous;
        bool changed = false;
        using var reader = new StringReader(journal);
        while (reader.ReadLine() is { } line)
        {
            if (!JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _) || entry is null)
            {
                continue;
            }

            if (entry.EventName == "Fileheader")
            {
                matches = false;
            }
            else if (entry.EventName is "Commander" or "LoadGame")
            {
                matches = string.Equals(Text(entry.Payload, "FID"), frontierId, StringComparison.OrdinalIgnoreCase);
            }
            else if (matches)
            {
                FrontierPowerplaySnapshot? updated = Apply(result, entry);
                changed |= !ReferenceEquals(result, updated);
                result = updated;
            }
        }

        return changed ? result : null;
    }

    /// <summary>Merges absolute rank/merit updates without letting older events or another pledge overwrite newer status.</summary>
    public static FrontierPowerplaySnapshot? Apply(FrontierPowerplaySnapshot? previous, JournalEventEnvelope entry)
    {
        if (entry.Timestamp is not { } timestamp || previous is not null && timestamp < previous.UpdatedAt)
        {
            return previous;
        }

        JsonElement payload = entry.Payload;
        if (entry.EventName == "PowerplayLeave")
        {
            return new FrontierPowerplaySnapshot("", null, null, timestamp);
        }

        string? power = entry.EventName == "PowerplayDefect" ? Text(payload, "ToPower") : Text(payload, "Power");
        if (
            entry.EventName
                is not ("Powerplay" or "PowerplayJoin" or "PowerplayDefect" or "PowerplayRank" or "PowerplayMerits")
            || power is null
        )
        {
            return previous;
        }

        int? rank = Number(payload, "Rank") is { } value && value <= int.MaxValue ? (int)value : null;
        long? merits = Number(payload, entry.EventName == "PowerplayMerits" ? "TotalMerits" : "Merits");
        if (
            entry.EventName == "PowerplayRank" && rank is null
            || entry.EventName == "PowerplayMerits" && merits is null
        )
        {
            return previous;
        }

        return Merge(previous, power, rank, merits, timestamp);
    }

    private static FrontierPowerplaySnapshot Merge(
        FrontierPowerplaySnapshot? previous,
        string power,
        int? rank,
        long? merits,
        DateTimeOffset timestamp
    )
    {
        bool samePower = string.Equals(previous?.Power, power, StringComparison.OrdinalIgnoreCase);
        return new FrontierPowerplaySnapshot(
            power,
            rank ?? (samePower ? previous?.Rank : null),
            merits ?? (samePower ? previous?.Merits : null),
            timestamp
        );
    }

    private static string? Text(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static long? Number(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
        && number >= 0
            ? number
            : null;
}
