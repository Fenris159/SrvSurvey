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
        if (journalEvent.EventName != "CodexEntry")
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(frontierIdFilter)
            && !string.Equals(
                session.FrontierId,
                frontierIdFilter,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(session.FrontierId))
        {
            warnings.Add("Skipped a Codex entry before the Frontier ID was known.");
            return 1;
        }

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
            return 1;
        }

        var discovery = new CommanderCodexDiscovery(
            entryId.Value,
            timestamp.Value,
            systemAddress.Value,
            GetInt32(root, "BodyID") ?? -1);
        AddPending(
            pending,
            new LedgerKey(
                session.FrontierId,
                session.CommanderName,
                0,
                null),
            discovery);
        if (session.StarPosition is { } position
            && GalacticRegionMap.Find(position) is { } region)
        {
            AddPending(
                pending,
                new LedgerKey(
                    session.FrontierId,
                    session.CommanderName,
                    region.Id,
                    region.Name),
                discovery);
        }

        return 1;
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
