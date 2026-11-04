using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyStore
{
    private sealed record RecordedProgress(DateTimeOffset From, bool Complete, IReadOnlySet<string> Records);

    private static RecordedProgress? ReadRecordedProgress(JsonObject root, DateTimeOffset watermark) =>
        root["journalRecords"] is JsonObject progress
        && GetDateTimeOffset(progress, "through") == watermark
        && GetDateTimeOffset(progress, "from") is { } from
        && from <= watermark
        && GetDateTimeOffset(root, "startTime") is { } start
        && from >= start
        && progress["complete"] is JsonValue flag
        && flag.TryGetValue<bool>(out bool complete)
        && ReadStringSet(progress, "records") is { } records
            ? new(from, complete, records)
            : null;

    private static bool? ReadBoundaryCompleteness(JsonObject root, DateTimeOffset watermark) =>
        root[WatermarkRecordsPropertyName] is JsonObject boundary
        && GetDateTimeOffset(boundary, "timestamp") == watermark
        && boundary["complete"] is JsonValue flag
        && flag.TryGetValue<bool>(out bool complete)
            ? complete
            : null;

    private static void WriteRecordedProgress(JsonObject root, JourneyDocument journey)
    {
        if (journey.RecordTrackingStart is { } from && journey.RecordedJournalRecords is { } records)
        {
            root["journalRecords"] = new JsonObject
            {
                ["from"] = from,
                ["through"] = journey.Watermark,
                ["complete"] = journey.RecordTrackingStartComplete,
                ["records"] = new JsonArray(records.Select(record => (JsonNode?)JsonValue.Create(record)).ToArray()),
            };
        }
        else
        {
            root.Remove("journalRecords");
        }
    }
}
