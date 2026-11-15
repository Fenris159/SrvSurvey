using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>Attributes journal records using the identity active at each record, including mixed rotation batches.</summary>
public static class JournalCommanderEvents
{
    private const string CommanderEvent = "Commander";

    /// <summary>A fresh header revokes the preceding account until an explicit identity establishes the next owner.</summary>
    public static (string? FrontierId, string? Name) ReadFinalIdentity(
        IEnumerable<JournalEventEnvelope> entries,
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
            else if (entry.EventName is CommanderEvent or "LoadGame")
            {
                identity = ReadIdentity(entry, identity);
            }
        }
        return identity;
    }

    /// <summary>Yields only the requested account's records after applying each file and commander boundary.</summary>
    public static IEnumerable<JournalEventEnvelope> ForCommander(
        IEnumerable<JournalEventEnvelope> entries,
        string frontierId,
        string? initialFrontierId,
        string? initialCommanderName = null,
        bool excludeLegacy = false,
        bool? initialIsLive = null
    )
    {
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            yield break;
        }
        (string? FrontierId, string? Name) identity = (initialFrontierId, initialCommanderName);
        bool? isLive = initialIsLive;
        foreach (JournalEventEnvelope entry in entries)
        {
            if (entry.EventName == "Fileheader")
            {
                identity = (null, null);
                isLive = JournalGalaxy.IsLive(entry.Payload);
                continue;
            }
            if (entry.EventName is CommanderEvent or "LoadGame")
            {
                identity = ReadIdentity(entry, identity);
            }
            if (
                string.Equals(identity.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase)
                && (!excludeLegacy || isLive != false)
            )
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
        string? name = Text(entry.Payload, entry.EventName == CommanderEvent ? "Name" : CommanderEvent);
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
