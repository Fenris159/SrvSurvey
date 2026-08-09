using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Exobiology;

public sealed class CommanderCodexJournalTracker(
    CommanderCodexStore store,
    JournalSessionState? session = null,
    string? frontierIdFilter = null)
{
    private const string JournalRegionPrefix = "$Codex_RegionName_";
    private readonly CommanderCodexStore store = store
        ?? throw new ArgumentNullException(nameof(store));
    private readonly JournalSessionState session = session ?? new();

    public async Task<CommanderCodexJournalTrackResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var pending = new Dictionary<LedgerKey, List<CommanderCodexDiscovery>>();
        var warnings = new List<string>();
        var discoveryEventCount = 0;
        foreach (var journalEvent in journalEvents)
        {
            discoveryEventCount += CollectCodexDiscoveries(
                journalEvent,
                pending,
                warnings);
        }

        var (changedEntryCount, changedFileCount) = await PersistPendingAsync(
                pending,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);

        return new CommanderCodexJournalTrackResult(
            discoveryEventCount,
            changedEntryCount,
            changedFileCount,
            warnings);
    }

    private int CollectCodexDiscoveries(
        JournalEventEnvelope journalEvent,
        Dictionary<LedgerKey, List<CommanderCodexDiscovery>> pending,
        List<string> warnings)
    {
        session.Apply(journalEvent);
        if (!ShouldTrackCodexEntry(journalEvent))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(session.FrontierId))
        {
            warnings.Add("Skipped a Codex entry before the Frontier ID was known.");
            return 1;
        }

        if (!TryCreateDiscovery(journalEvent, warnings, out var discovery))
        {
            return 1;
        }

        QueueDiscovery(pending, discovery, journalEvent.Payload);
        return 1;
    }

    private bool ShouldTrackCodexEntry(JournalEventEnvelope journalEvent)
    {
        if (journalEvent.EventName != "CodexEntry")
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(frontierIdFilter)
            || string.Equals(
                session.FrontierId,
                frontierIdFilter,
                StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCreateDiscovery(
        JournalEventEnvelope journalEvent,
        List<string> warnings,
        out CommanderCodexDiscovery discovery)
    {
        discovery = null!;
        var root = journalEvent.Payload;
        var entryId = GetInt64(root, "EntryID");
        var systemAddress = GetInt64(root, "SystemAddress")
            ?? session.SystemAddress;
        var timestamp = journalEvent.Timestamp;
        if (entryId is not > 0
            || systemAddress is null
            || timestamp is null)
        {
            warnings.Add(
                "Skipped a Codex entry with missing ID, timestamp, or system address.");
            return false;
        }

        discovery = new CommanderCodexDiscovery(
            entryId.Value,
            timestamp.Value,
            systemAddress.Value,
            GetInt32(root, "BodyID") ?? -1);
        return true;
    }

    private void QueueDiscovery(
        Dictionary<LedgerKey, List<CommanderCodexDiscovery>> pending,
        CommanderCodexDiscovery discovery,
        JsonElement root)
    {
        AddPending(
            pending,
            new LedgerKey(
                session.FrontierId!,
                session.CommanderName,
                0,
                null),
            discovery);
        var region = session.StarPosition is { } position
            ? GalacticRegionMap.Find(position)
            : null;
        region ??= FindJournalRegion(GetString(root, "Region"));
        if (region is null)
        {
            return;
        }

        AddPending(
            pending,
            new LedgerKey(
                session.FrontierId!,
                session.CommanderName,
                region.Id,
                region.Name),
            discovery);
    }

    private static GalacticRegion? FindJournalRegion(string? journalName)
    {
        if (string.IsNullOrWhiteSpace(journalName))
        {
            return null;
        }

        if (journalName.StartsWith(JournalRegionPrefix, StringComparison.Ordinal)
            && journalName.EndsWith(';')
            && int.TryParse(
                journalName[JournalRegionPrefix.Length..^1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var regionId))
        {
            return GalacticRegionMap.Regions.FirstOrDefault(region =>
                region.Id == regionId);
        }

        return GalacticRegionMap.Regions.FirstOrDefault(region =>
            string.Equals(
                region.Name,
                journalName,
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(int ChangedEntryCount, int ChangedFileCount)> PersistPendingAsync(
        Dictionary<LedgerKey, List<CommanderCodexDiscovery>> pending,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var changedEntryCount = 0;
        var changedFileCount = 0;
        foreach (var group in pending)
        {
            var result = await store.TrackBatchAsync(
                    group.Key.FrontierId,
                    group.Key.CommanderName,
                    group.Value,
                    group.Key.RegionId,
                    group.Key.RegionName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                warnings.Add(
                    $"{Path.GetFileName(result.Path)}: {result.Error}");
                continue;
            }

            changedEntryCount += result.ChangedEntryCount;
            if (result.ChangedEntryCount > 0)
            {
                changedFileCount++;
            }
        }

        return (changedEntryCount, changedFileCount);
    }

    private static void AddPending(
        IDictionary<LedgerKey, List<CommanderCodexDiscovery>> pending,
        LedgerKey key,
        CommanderCodexDiscovery discovery)
    {
        if (!pending.TryGetValue(key, out var discoveries))
        {
            discoveries = [];
            pending[key] = discoveries;
        }

        discoveries.Add(discovery);
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                    ? number
                    : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(
                value.GetString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out number)
                    ? number
                    : null;
    }

    private sealed record LedgerKey(
        string FrontierId,
        string? CommanderName,
        int RegionId,
        string? RegionName);
}

public sealed record CommanderCodexJournalTrackResult(
    int DiscoveryEventCount,
    int ChangedEntryCount,
    int ChangedFileCount,
    IReadOnlyList<string> Warnings)
{
    public bool IsSuccess => Warnings.Count == 0;

    public bool HasChanges => ChangedEntryCount > 0;
}
