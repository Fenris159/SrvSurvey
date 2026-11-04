using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Journeys;

public sealed partial class JourneyStore
{
    private const string JournalTimelinePropertyName = "journalTimeline";

    private static JourneyObservationHistory? ReadObservationHistory(JsonObject root, string path)
    {
        if (root[JournalTimelinePropertyName] is null)
        {
            return null;
        }
        if (
            root[JournalTimelinePropertyName] is not JsonObject history
            || GetInt32(history, "version") != 1
            || GetDateTimeOffset(history, "through") is not { } through
            || GetString(history, "projection") is not { } signature
            || history["baseline"] is not JsonObject baseline
            || GetDateTimeOffset(baseline, "watermark") is not { } watermark
            || baseline["originKnown"] is not JsonValue origin
            || !origin.TryGetValue<bool>(out bool originKnown)
            || baseline["odyssey"] is not JsonValue mode
            || !mode.TryGetValue<bool>(out bool odyssey)
            || ReadStringSet(baseline, "knownRecords") is not { } known
            || baseline["visits"] is not JsonArray visits
        )
        {
            throw new InvalidDataException($"The journey {path} has an unsupported or invalid journalTimeline.");
        }
        JourneySystemVisit[] savedVisits = visits
            .Select(
                (node, index) =>
                    node is JsonObject visit
                        ? ParseVisit(path, index, visit)
                        : throw new InvalidDataException($"The journey {path} has an invalid timeline baseline visit.")
            )
            .ToArray();
        return new(
            new(watermark, savedVisits, known, ReadObservations(baseline, "context", path), originKnown, odyssey),
            ReadObservations(history, "records", path),
            through,
            signature
        );
    }

    private static List<JourneyObservedRecord> ReadObservations(JsonObject root, string property, string path)
    {
        if (root[property] is not JsonArray array)
        {
            throw new InvalidDataException($"The journey {path} has invalid timeline {property}.");
        }
        var records = new List<JourneyObservedRecord>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in array)
        {
            if (
                node is not JsonObject row
                || string.IsNullOrWhiteSpace(GetString(row, "identity"))
                || GetDateTimeOffset(row, "timestamp") is not { } timestamp
                || ReadStringSet(row, "previous") is not { } previous
                || row["record"] is not JsonObject payload
            )
            {
                throw new InvalidDataException($"The journey {path} has an invalid timeline observation.");
            }
            string identity = GetString(row, "identity")!;
            string json = payload.ToJsonString();
            if (
                !identities.Add(identity)
                || !JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _)
                || entry?.Timestamp != timestamp
            )
            {
                throw new InvalidDataException($"The journey {path} has a duplicate or invalid timeline observation.");
            }
            records.Add(new(identity, timestamp, json, GetString(row, "source"), previous));
        }
        return records;
    }

    private static void WriteObservationHistory(JsonObject root, JourneyDocument journey)
    {
        if (journey.ObservationHistory is not { } history)
        {
            root.Remove(JournalTimelinePropertyName);
            return;
        }
        JourneyProgressBaseline baseline = history.Baseline;
        var visits = new JsonArray();
        foreach (JourneySystemVisit visit in baseline.VisitedSystems)
        {
            var row = new JsonObject();
            WriteVisit(row, visit);
            visits.Add(row);
        }
        var savedBaseline = new JsonObject
        {
            ["watermark"] = baseline.Watermark,
            ["originKnown"] = baseline.IsOriginKnown,
            ["odyssey"] = baseline.IsOdyssey,
            ["visits"] = visits,
            ["context"] = WriteObservations(baseline.Context),
        };
        WriteSet(savedBaseline, "knownRecords", baseline.KnownRecords);
        root[JournalTimelinePropertyName] = new JsonObject
        {
            ["version"] = 1,
            ["through"] = history.Through,
            ["projection"] = history.ProjectionSignature,
            ["baseline"] = savedBaseline,
            ["records"] = WriteObservations(history.Records),
        };
    }

    private static JsonArray WriteObservations(IReadOnlyList<JourneyObservedRecord> records)
    {
        var array = new JsonArray();
        foreach (JourneyObservedRecord record in records)
        {
            var row = new JsonObject
            {
                ["identity"] = record.Identity,
                ["timestamp"] = record.Timestamp,
                ["source"] = record.SourceRecordIdentity,
                ["record"] = JsonNode.Parse(record.RawJson),
            };
            WriteSet(row, "previous", record.PreviousIdentities);
            array.Add(row);
        }
        return array;
    }
}
