using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>Attributes journal records using the identity active at each record, including mixed rotation batches.</summary>
public static class JournalCommanderEvents
{
    /// <summary>Yields only the requested account's records after applying each file and commander boundary.</summary>
    public static IEnumerable<JournalEventEnvelope> ForCommander(
        IEnumerable<JournalEventEnvelope> entries,
        string frontierId,
        string? initialFrontierId,
        string? initialCommanderName = null
    )
    {
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            yield break;
        }
        (string? FrontierId, string? Name) identity = (initialFrontierId, initialCommanderName);
        foreach (JournalEventEnvelope entry in entries)
        {
            if (entry.EventName == "Fileheader")
            {
                identity = (null, null);
                continue;
            }
            if (entry.EventName is "Commander" or "LoadGame")
            {
                identity = ReadIdentity(entry, identity);
            }
            if (string.Equals(identity.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase))
            {
                yield return entry;
            }
        }
    }

    /// <summary>A name-only change revokes the old account; unchanged names retain a confirmed identity.</summary>
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
        return name is not null && !string.Equals(name, previous.Name, StringComparison.OrdinalIgnoreCase)
            ? (null, name)
            : (previous.FrontierId, name ?? previous.Name);
    }

    private static string? Text(JsonElement payload, string key) =>
        payload.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
