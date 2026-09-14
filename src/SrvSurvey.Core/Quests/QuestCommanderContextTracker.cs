using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Quests;

public sealed class QuestCommanderContextTracker
{
    private static readonly HashSet<string> FactionEvents = new(
        ["Location", "FSDJump", "CarrierJump"],
        StringComparer.Ordinal
    );

    private readonly Dictionary<string, QuestFactionSnapshot> factions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> priorJournalEvents = new(StringComparer.Ordinal);

    public void Apply(IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);

        foreach (JournalEventEnvelope journalEvent in journalEvents)
        {
            Apply(journalEvent);
        }
    }

    public void Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        if (journalEvent.EventName is "Docked" or "FSDJump")
        {
            priorJournalEvents[journalEvent.EventName] = journalEvent.Payload.Clone();
        }

        if (FactionEvents.Contains(journalEvent.EventName))
        {
            UpdateFactions(journalEvent.Payload);
        }
    }

    public QuestCommanderContext CreateContext(string commanderName, EliteStatus? status)
    {
        JsonElement? statusPayload = status is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(status);
        QuestSurfaceContext? surface =
            status?.HasLatitudeLongitude == true
                ? new QuestSurfaceContext(
                    status.Latitude,
                    status.Longitude,
                    decimal.ToDouble(status.PlanetRadius),
                    status.NormalizedHeading
                )
                : null;

        return new QuestCommanderContext(
            commanderName,
            statusPayload,
            surface,
            new Dictionary<string, QuestFactionSnapshot>(factions, StringComparer.Ordinal),
            priorJournalEvents.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal)
        );
    }

    public void Reset()
    {
        factions.Clear();
        priorJournalEvents.Clear();
    }

    private void UpdateFactions(JsonElement payload)
    {
        if (
            !payload.TryGetProperty("Factions", out JsonElement factionArray)
            || factionArray.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        var updated = new Dictionary<string, QuestFactionSnapshot>(StringComparer.Ordinal);
        foreach (JsonElement faction in factionArray.EnumerateArray())
        {
            if (faction.ValueKind != JsonValueKind.Object || !TryGetString(faction, "Name", out string? name))
            {
                continue;
            }

            string[]? activeStates =
                ReadStates(faction, "ActiveStates")
                ?? (TryGetString(faction, "FactionState", out string? factionState) ? [factionState] : []);
            updated[name] = new QuestFactionSnapshot(
                ReadDouble(faction, "MyReputation"),
                ReadDouble(faction, "Influence"),
                activeStates,
                ReadStates(faction, "PendingStates") ?? [],
                ReadStates(faction, "RecoveringStates") ?? []
            );
        }

        factions.Clear();
        foreach (KeyValuePair<string, QuestFactionSnapshot> pair in updated)
        {
            factions.Add(pair.Key, pair.Value);
        }
    }

    private static string[]? ReadStates(JsonElement faction, string propertyName)
    {
        if (!faction.TryGetProperty(propertyName, out JsonElement states))
        {
            return null;
        }

        if (states.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return states
            .EnumerateArray()
            .Where(state => state.ValueKind == JsonValueKind.Object)
            .Select(state => TryGetString(state, "State", out string? value) ? value : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static bool TryGetString(JsonElement value, string propertyName, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? string.Empty;
        return result.Length > 0;
    }

    private static double ReadDouble(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out JsonElement property) && property.TryGetDouble(out double result)
            ? result
            : 0;
    }
}
