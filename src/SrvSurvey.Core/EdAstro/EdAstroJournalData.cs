using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.EdAstro;

/// <summary>Live journal evidence captured by the shared journal projection for one session.</summary>
public sealed record EdAstroApplyRequest(
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    string? CommanderName,
    string? JournalPath,
    bool IsOdyssey,
    bool AllowPublishing,
    bool IsLiveGame
);

/// <summary>Owns opt-in journal contribution, batching, delivery and traffic control.</summary>
public interface IEdAstroPublisher : IDisposable
{
    /// <summary>Maps new session events without blocking the journal projection on HTTP.</summary>
    void Apply(EdAstroApplyRequest request);

    /// <summary>Changes consent; disabling clears queued data and cancels outstanding requests.</summary>
    void SetEnabled(bool enabled);

    /// <summary>Pauses collection and delivery while shared journal attribution is ambiguous.</summary>
    void SetSuspended(bool suspended);
}

/// <summary>Preserves EDDiscovery's journal-object schema while limiting collection to disclosed events.</summary>
internal static class EdAstroJournalData
{
    internal static readonly HashSet<string> SupportedEvents = new(StringComparer.Ordinal)
    {
        "CarrierJumpRequest",
        "CarrierStats",
        "Docked",
        "ScanOrganic",
        "CodexEntry",
    };

    private static readonly HashSet<string> CarrierFields = new(StringComparer.Ordinal)
    {
        "timestamp",
        "event",
        "CarrierID",
        "Callsign",
        "Name",
        "DockingAccess",
        "AllowNotorious",
        "FuelLevel",
        "PendingDecommission",
        "Crew",
    };

    /// <summary>Copies public fields, excluding EDDiscovery extensions and private identifiers.</summary>
    internal static JsonObject? Prepare(JournalEventEnvelope entry)
    {
        if (!SupportedEvents.Contains(entry.EventName) || entry.Timestamp is null)
        {
            return null;
        }

        var result = new JsonObject();
        foreach (
            JsonProperty property in entry
                .Payload.EnumerateObject()
                .Where(property => IsPublicField(entry.EventName, property.Name))
        )
        {
            result[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        return result;
    }

    /// <summary>Limits each supported event to public fields covered by the sharing disclosure.</summary>
    private static bool IsPublicField(string eventName, string name) =>
        !name.StartsWith("EDD", StringComparison.OrdinalIgnoreCase)
        && name is not ("Commander" or "FID" or "Credits" or "Loan" or "Balance")
        && (eventName != "CarrierStats" || CarrierFields.Contains(name));
}
