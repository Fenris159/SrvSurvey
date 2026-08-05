using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Quests;

public sealed class QuestCommanderContextTracker
{
    private static readonly HashSet<string> FactionEvents = new(
        ["Location", "FSDJump", "CarrierJump"],
        StringComparer.Ordinal);

    private readonly Dictionary<string, QuestFactionSnapshot> factions =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> priorJournalEvents =
        new(StringComparer.Ordinal);

    public void Apply(IEnumerable<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);

        foreach (var journalEvent in journalEvents)
        {
            Apply(journalEvent);
        }
    }

    public void Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);

        if (journalEvent.EventName is "Docked" or "FSDJump")
        {
            priorJournalEvents[journalEvent.EventName] =
                journalEvent.Payload.Clone();
        }

        if (FactionEvents.Contains(journalEvent.EventName))
        {
            UpdateFactions(journalEvent.Payload);
        }
    }

    public QuestCommanderContext CreateContext(
        string commanderName,
        EliteStatus? status)
    {
        var statusPayload = status is null
            ? (JsonElement?)null
            : JsonSerializer.SerializeToElement(status);
        var surface = status?.HasLatitudeLongitude == true
            ? new QuestSurfaceContext(
                status.Latitude,
                status.Longitude,
                decimal.ToDouble(status.PlanetRadius),
                status.NormalizedHeading)
            : null;

        return new QuestCommanderContext(
            commanderName,
            statusPayload,
            surface,
            new Dictionary<string, QuestFactionSnapshot>(
                factions,
                StringComparer.Ordinal),
            priorJournalEvents.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.Ordinal));
    }

    public void Reset()
    {
        factions.Clear();
        priorJournalEvents.Clear();
    }

    private void UpdateFactions(JsonElement payload)
    {
        if (!payload.TryGetProperty("Factions", out var factionArray)
            || factionArray.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var updated = new Dictionary<string, QuestFactionSnapshot>(
            StringComparer.Ordinal);
        foreach (var faction in factionArray.EnumerateArray())
        {
            if (faction.ValueKind != JsonValueKind.Object
                || !TryGetString(faction, "Name", out var name))
            {
                continue;
            }

            var activeStates = ReadStates(faction, "ActiveStates");
            if (activeStates is null)
            {
                activeStates = TryGetString(
                    faction,
                    "FactionState",
                    out var factionState)
                    ? [factionState]
                    : [];
            }

            updated[name] = new QuestFactionSnapshot(
                ReadDouble(faction, "MyReputation"),
                ReadDouble(faction, "Influence"),
                activeStates,
                ReadStates(faction, "PendingStates") ?? [],
                ReadStates(faction, "RecoveringStates") ?? []);
        }

        factions.Clear();
        foreach (var pair in updated)
        {
            factions.Add(pair.Key, pair.Value);
        }
    }

    private static string[]? ReadStates(
        JsonElement faction,
        string propertyName)
    {
        if (!faction.TryGetProperty(propertyName, out var states))
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
            .Select(state => TryGetString(state, "State", out var value)
                ? value
                : null)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();
    }

    private static bool TryGetString(
        JsonElement value,
        string propertyName,
        out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? string.Empty;
        return result.Length > 0;
    }

    private static double ReadDouble(
        JsonElement value,
        string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            && property.TryGetDouble(out var result)
                ? result
                : 0;
    }
}
