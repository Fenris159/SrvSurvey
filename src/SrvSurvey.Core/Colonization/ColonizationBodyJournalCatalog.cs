using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

/// <summary>Retains local scans and the upstream completion gates independently of external exploration data.</summary>
public sealed class ColonizationBodyJournalCatalog(long systemAddress, string systemName)
{
    private readonly Dictionary<int, JournalEventEnvelope> scans = [];
    private readonly Dictionary<int, int> lastScanJump = [];
    private int jumpCount;
    private readonly HashSet<string> observedSourceRecords = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HashSet<string>> signalFeatures = [];
    private int? allBodiesCount;
    private int? discoveryCount;
    private double discoveryProgress;
    private bool localFssComplete;

    public int ExpectedBodyCount => allBodiesCount ?? discoveryCount ?? 0;
    public int ScannedBodyCount => lastScanJump.Count;
    public bool CanUpload =>
        localFssComplete
        && (allBodiesCount.HasValue || discoveryProgress >= 1)
        && ExpectedBodyCount > 0
        && ScannedBodyCount >= ExpectedBodyCount;

    /// <summary>Only the events upstream uses to advance its updater request a catalog check.</summary>
    public bool Apply(IEnumerable<JournalEventEnvelope> entries, bool knownLocalFssComplete = false)
    {
        localFssComplete |= knownLocalFssComplete;
        bool triggered = false;
        foreach (JournalEventEnvelope entry in entries)
        {
            if (entry.EventName == "FSDJump")
            {
                AdvanceJumpWindow(entry);
                continue;
            }
            JsonElement root = entry.Payload;
            if (
                !root.TryGetProperty("SystemAddress", out JsonElement address)
                || address.ValueKind != JsonValueKind.Number
                || !address.TryGetInt64(out long value)
                || value != systemAddress
            )
            {
                continue;
            }

            triggered |= IsCheckTrigger(entry.EventName);
            if (!RememberSourceRecord(entry))
            {
                continue;
            }
            switch (entry.EventName)
            {
                case "FSSDiscoveryScan":
                    discoveryCount = (int?)ColonizationBodyCatalog.Number(root, "BodyCount") ?? discoveryCount;
                    discoveryProgress = ColonizationBodyCatalog.Number(root, "Progress") ?? 0;
                    break;
                case "FSSAllBodiesFound":
                    allBodiesCount = (int?)ColonizationBodyCatalog.Number(root, "Count") ?? allBodiesCount;
                    localFssComplete = true;
                    break;
                case "Scan":
                case "ScanBaryCentre":
                    ReadScan(entry);
                    break;
                case "FSSBodySignals":
                case "SAASignalsFound":
                    ReadSignals(root);
                    break;
            }
        }
        return triggered;
    }

    public IReadOnlyList<ColonizationSystemBody> CreateBodies() =>
        ColonizationBodyCatalog.FromJournal(
            scans.Where(pair => lastScanJump.ContainsKey(pair.Key)).Select(pair => pair.Value),
            systemName,
            signalFeatures
        );

    private bool RememberSourceRecord(JournalEventEnvelope entry)
    {
        string? identity = entry.SourceContentIdentity ?? entry.SourceRecordIdentity;
        return identity is null || observedSourceRecords.Add(identity);
    }

    private static bool IsCheckTrigger(string eventName) =>
        eventName is "FSSDiscoveryScan" or "FSSAllBodiesFound" or "NavBeaconScan";

    private void ReadScan(JournalEventEnvelope entry)
    {
        JsonElement root = entry.Payload;
        if (
            root.TryGetProperty("BodyID", out JsonElement id)
            && id.ValueKind == JsonValueKind.Number
            && id.TryGetInt32(out int number)
        )
        {
            scans[number] = scans.TryGetValue(number, out JournalEventEnvelope? prior)
                ? PreserveDetailedScan(prior, entry)
                : entry;
            lastScanJump[number] = jumpCount;
        }
    }

    private void AdvanceJumpWindow(JournalEventEnvelope entry)
    {
        if (!RememberSourceRecord(entry))
        {
            return;
        }
        jumpCount++;
        foreach (KeyValuePair<int, int> scan in lastScanJump.ToArray())
        {
            if (scan.Value < jumpCount - 10)
            {
                lastScanJump.Remove(scan.Key);
            }
        }
    }

    private static JournalEventEnvelope PreserveDetailedScan(JournalEventEnvelope prior, JournalEventEnvelope current)
    {
        var fields = prior
            .Payload.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        foreach (JsonProperty property in current.Payload.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Null)
            {
                fields[property.Name] = property.Value;
            }
        }
        JsonElement payload = JsonSerializer.SerializeToElement(fields);
        return current with { RawJson = payload.GetRawText(), Payload = payload };
    }

    private void ReadSignals(JsonElement root)
    {
        if (
            !root.TryGetProperty("BodyID", out JsonElement id)
            || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt32(out int number)
            || !root.TryGetProperty("Signals", out JsonElement signals)
            || signals.ValueKind != JsonValueKind.Array
        )
        {
            return;
        }

        HashSet<string> features = signalFeatures.GetValueOrDefault(number) ?? [];
        foreach (JsonElement signal in signals.EnumerateArray())
        {
            if (ColonizationBodyCatalog.Number(signal, "Count") is not > 0)
            {
                continue;
            }

            string? type = ColonizationBodyCatalog.Text(signal, "Type");
            if (type?.Contains("Biological", StringComparison.OrdinalIgnoreCase) == true)
            {
                features.Add("bio");
            }

            if (type?.Contains("Geological", StringComparison.OrdinalIgnoreCase) == true)
            {
                features.Add("geo");
            }
        }
        signalFeatures[number] = features;
    }
}
