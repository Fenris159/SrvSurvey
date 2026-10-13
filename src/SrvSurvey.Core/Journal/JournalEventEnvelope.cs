using System.Text.Json;

namespace SrvSurvey.Core.Journal;

public sealed record JournalEventEnvelope(
    string EventName,
    DateTimeOffset? Timestamp,
    string RawJson,
    JsonElement Payload
)
{
    /// <summary>Identifies the original file and byte position without conflating identical events at different positions.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string? SourceRecordIdentity { get; init; }

    public static bool TryParse(string line, out JournalEventEnvelope? journalEvent, out string? error)
    {
        journalEvent = null;
        error = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "The journal line is empty.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The journal line is not a JSON object.";
                return false;
            }

            if (
                !root.TryGetProperty("event", out JsonElement eventProperty)
                || eventProperty.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(eventProperty.GetString())
            )
            {
                error = "The journal line has no event name.";
                return false;
            }

            DateTimeOffset? timestamp = null;
            if (
                root.TryGetProperty("timestamp", out JsonElement timestampProperty)
                && timestampProperty.ValueKind == JsonValueKind.String
                && timestampProperty.TryGetDateTimeOffset(out DateTimeOffset parsedTimestamp)
            )
            {
                timestamp = parsedTimestamp;
            }

            journalEvent = new JournalEventEnvelope(eventProperty.GetString()!, timestamp, line, root.Clone());
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
