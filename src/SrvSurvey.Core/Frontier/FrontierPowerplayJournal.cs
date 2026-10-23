using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Frontier;

/// <summary>Powerplay status from a timestamped journal event, including ranks beyond the legacy five ratings.</summary>
public sealed record FrontierPowerplaySnapshot(string Power, int? Rank, long? Merits, DateTimeOffset UpdatedAt);

public static class FrontierPowerplayJournal
{
    private const string JoinEvent = "PowerplayJoin";
    private const string DefectEvent = "PowerplayDefect";
    private const string MeritsEvent = "PowerplayMerits";

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

    /// <summary>Retains account-attributed Powerplay records while discarding unrelated daily travel events.</summary>
    public static IReadOnlyList<JournalEventEnvelope> ReadPowerplayEvents(string journal, string frontierId) =>
        ForCommander(ReadEntries(journal), frontierId, null, null).ToArray();

    /// <summary>Checks whether collected records define status without any earlier membership history.</summary>
    public static bool HasRecoveryBaseline(IEnumerable<JournalEventEnvelope> entries)
    {
        FrontierPowerplaySnapshot? status = null;
        bool recovered = false;
        foreach (JournalEventEnvelope entry in entries)
        {
            FrontierPowerplaySnapshot? updated = Apply(status, entry);
            if (
                !ReferenceEquals(updated, status)
                && (
                    entry.EventName is JoinEvent or DefectEvent or "PowerplayLeave"
                    || updated is { Power.Length: 0 } or { Rank: not null, Merits: not null }
                )
            )
            {
                recovered = true;
            }
            status = updated;
        }

        return recovered;
    }

    /// <summary>Attributes each event using its identity boundary and the commander active before the batch.</summary>
    public static FrontierPowerplaySnapshot? Apply(
        FrontierPowerplaySnapshot? previous,
        IEnumerable<JournalEventEnvelope> entries,
        string frontierId,
        string? initialFrontierId,
        string? initialCommanderName = null
    )
    {
        FrontierPowerplaySnapshot? result = previous;
        foreach (
            JournalEventEnvelope entry in ForCommander(entries, frontierId, initialFrontierId, initialCommanderName)
        )
        {
            result = Apply(result, entry);
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

        string? power = entry.EventName == DefectEvent ? Text(payload, "ToPower") : Text(payload, "Power");
        if (
            entry.EventName is not ("Powerplay" or JoinEvent or DefectEvent or "PowerplayRank" or MeritsEvent)
            || power is null
            || power.Length == 0 && entry.EventName != "Powerplay"
        )
        {
            return previous;
        }

        int? rank = Number(payload, "Rank") is { } value && value <= int.MaxValue ? (int)value : null;
        long? merits = Number(payload, entry.EventName == MeritsEvent ? "TotalMerits" : "Merits");
        if (!HasRequiredPowerplayValue(entry.EventName, rank, merits))
        {
            return previous;
        }

        return entry.EventName is JoinEvent or DefectEvent
            ? new FrontierPowerplaySnapshot(power, rank, merits, timestamp)
            : Merge(previous, power, rank, merits, timestamp);
    }

    /// <summary>Carries forward missing values only within the same pledged power.</summary>
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

    /// <summary>Tracks file and commander boundaries before yielding only the selected account's Powerplay events.</summary>
    private static IEnumerable<JournalEventEnvelope> ForCommander(
        IEnumerable<JournalEventEnvelope> entries,
        string frontierId,
        string? initialFrontierId,
        string? initialCommanderName
    )
    {
        (string? FrontierId, string? Name) identity = (initialFrontierId, initialCommanderName);
        foreach (JournalEventEnvelope entry in entries)
        {
            if (entry.EventName == "Fileheader")
            {
                identity = (null, null);
            }
            else if (entry.EventName is "Commander" or "LoadGame")
            {
                identity = ReadIdentity(entry, identity);
            }
            else if (
                string.Equals(identity.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase)
                && entry.EventName
                    is "Powerplay"
                        or JoinEvent
                        or DefectEvent
                        or "PowerplayLeave"
                        or "PowerplayRank"
                        or MeritsEvent
            )
            {
                yield return entry;
            }
        }
    }

    /// <summary>Retains a confirmed FID for the same commander and clears it when a name-only boundary changes identity.</summary>
    private static (string? FrontierId, string? Name) ReadIdentity(
        JournalEventEnvelope entry,
        (string? FrontierId, string? Name) previous
    )
    {
        string? name = Text(entry.Payload, entry.EventName == "Commander" ? "Name" : "Commander");
        if (entry.Payload.TryGetProperty("FID", out _))
        {
            string? frontierId = Text(entry.Payload, "FID");
            if (name is null && string.Equals(frontierId, previous.FrontierId, StringComparison.OrdinalIgnoreCase))
            {
                name = previous.Name;
            }
            return (frontierId, name);
        }
        if (name is not null && !string.Equals(name, previous.Name, StringComparison.OrdinalIgnoreCase))
        {
            return (null, name);
        }
        return (previous.FrontierId, name ?? previous.Name);
    }

    /// <summary>Parses daily journal lines and skips malformed records without retaining the full journal.</summary>
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

    /// <summary>Requires absolute numeric values for rank and merit deltas while allowing unknown membership fields.</summary>
    private static bool HasRequiredPowerplayValue(string eventName, int? rank, long? merits) =>
        eventName switch
        {
            "PowerplayRank" => rank is not null,
            MeritsEvent => merits is not null,
            _ => true,
        };

    /// <summary>Reads a trimmed string field while preserving the distinction between missing and empty values.</summary>
    private static string? Text(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    /// <summary>Reads nonnegative integral journal values and rejects malformed or negative numbers.</summary>
    private static long? Number(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
        && number >= 0
            ? number
            : null;
}
