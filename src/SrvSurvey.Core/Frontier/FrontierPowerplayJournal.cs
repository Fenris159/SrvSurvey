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
        FrontierPowerplaySnapshot? result = Apply(previous, ReadEntries(journal), frontierId, initialFrontierId: null);
        return ReferenceEquals(previous, result) ? null : result;
    }

    /// <summary>Attributes each event using its identity boundary and the commander active before the batch.</summary>
    public static FrontierPowerplaySnapshot? Apply(
        FrontierPowerplaySnapshot? previous,
        IEnumerable<JournalEventEnvelope> entries,
        string frontierId,
        string? initialFrontierId
    )
    {
        bool matches = string.Equals(initialFrontierId, frontierId, StringComparison.OrdinalIgnoreCase);
        FrontierPowerplaySnapshot? result = previous;
        foreach (JournalEventEnvelope entry in entries)
        {
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
                result = Apply(result, entry);
            }
        }

        return result;
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
            || power.Length == 0 && entry.EventName != "Powerplay"
        )
        {
            return previous;
        }

        int? rank = Number(payload, "Rank") is { } value && value <= int.MaxValue ? (int)value : null;
        long? merits = Number(payload, entry.EventName == "PowerplayMerits" ? "TotalMerits" : "Merits");
        if (!HasRequiredPowerplayValue(entry.EventName, rank, merits))
        {
            return previous;
        }

        return entry.EventName is "PowerplayJoin" or "PowerplayDefect"
            ? new FrontierPowerplaySnapshot(power, rank, merits, timestamp)
            : Merge(previous, power, rank, merits, timestamp);
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

    private static IEnumerable<JournalEventEnvelope> ReadEntries(string journal)
    {
        using var reader = new StringReader(journal);
        while (reader.ReadLine() is { } line)
        {
            if (JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? entry, out _) && entry is not null)
            {
                yield return entry;
            }
        }
    }

    private static bool HasRequiredPowerplayValue(string eventName, int? rank, long? merits) =>
        eventName switch
        {
            "PowerplayRank" => rank is not null,
            "PowerplayMerits" => merits is not null,
            _ => true,
        };

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
