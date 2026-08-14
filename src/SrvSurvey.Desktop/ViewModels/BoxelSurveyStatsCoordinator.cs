using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSurveyStatsCoordinator : IDisposable
{
    public static readonly TimeSpan DefaultFlushDelay = TimeSpan.FromMilliseconds(500);
    private const int MaximumRetainedDocuments = 256;
    private const int RecentPrefixLimit = 20;

    private readonly BoxelSurveyStatsStore store;
    private readonly BoxelSurveyStatsState state = new();
    private readonly TimeSpan flushDelay;
    private readonly List<string> recents = [];
    private readonly HashSet<string> retainPrefixes = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private string? frontierId;
    private CancellationTokenSource? flushCancellation;
    private bool disposed;

    public BoxelSurveyStatsCoordinator(
        BoxelSurveyStatsStore store,
        TimeSpan? flushDelay = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.flushDelay = flushDelay ?? DefaultFlushDelay;
    }

    public event EventHandler? Changed;

    public event EventHandler<Exception>? PersistenceFailed;

    public BoxelSurveyStatsState State => state;

    public string? FrontierId => frontierId;

    public string StoreDataDirectory => store.DataDirectory;

    public IReadOnlyList<BoxelSurveyIndexEntry> Index
    {
        get
        {
            lock (gate)
            {
                return state.GetIndex();
            }
        }
    }

    public BoxelSurveyBoxelSnapshot? Current
    {
        get
        {
            lock (gate)
            {
                return state.Current;
            }
        }
    }

    public async Task SwitchCommanderAsync(
        string? nextFrontierId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var normalized = string.IsNullOrWhiteSpace(nextFrontierId)
            ? null
            : nextFrontierId.Trim();
        if (string.Equals(frontierId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await FlushAsync(cancellationToken).ConfigureAwait(false);
        BoxelSurveyStatsCatalog? catalog = null;
        if (normalized is not null)
        {
            catalog = await store.LoadCatalogAsync(normalized, cancellationToken)
                .ConfigureAwait(false);
        }

        lock (gate)
        {
            frontierId = normalized;
            recents.Clear();
            retainPrefixes.Clear();
            state.Reset(catalog);
        }

        RaiseChanged();
    }

    public Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CancellationToken cancellationToken = default)
    {
        return ApplyJournalEventsAsync(
            journalEvents,
            bootstrapContextOnly: false,
            cancellationToken);
    }

    public Task ApplyBootstrapContextAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        CancellationToken cancellationToken = default)
    {
        return ApplyJournalEventsAsync(
            journalEvents,
            bootstrapContextOnly: true,
            cancellationToken);
    }

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool bootstrapContextOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ObjectDisposedException.ThrowIf(disposed, this);
        var raiseChanged = false;
        foreach (var journalEvent in journalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bootstrapContextOnly
                && journalEvent.EventName is not ("Fileheader" or "LoadGame"))
            {
                continue;
            }

            await EnsureLoadedForEventAsync(journalEvent, cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                var version = state.Version;
                state.Apply(journalEvent);
                if (state.Version != version)
                {
                    RememberCurrentUnlocked();
                    ScheduleFlush();
                    raiseChanged = true;
                }
            }
        }

        if (raiseChanged)
        {
            RaiseChanged();
        }
    }

    public async Task IngestSnapshotAsync(
        SystemScanSnapshot snapshot,
        DateTimeOffset? visitedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (BoxelAddress.TryParse(snapshot.SystemName, out var boxel)
            && boxel is not null)
        {
            await EnsurePrefixLoadedAsync(boxel.Prefix, cancellationToken)
                .ConfigureAwait(false);
        }

        var raiseChanged = false;
        lock (gate)
        {
            var version = state.Version;
            if (state.IngestSnapshot(snapshot, visitedAt)
                && state.Version != version)
            {
                RememberCurrentUnlocked();
                ScheduleFlush();
                raiseChanged = true;
            }
        }

        if (raiseChanged)
        {
            RaiseChanged();
        }
    }

    public async Task<BoxelSurveyBoxelSnapshot?> GetAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        await EnsurePrefixLoadedAsync(prefix, cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            return state.TryGet(prefix, out var snapshot) ? snapshot : null;
        }
    }

    public async Task<BoxelSurveyBoxelSnapshot> RollupAsync(
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        var list = prefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var prefix in list)
        {
            await EnsurePrefixLoadedAsync(prefix, cancellationToken)
                .ConfigureAwait(false);
            if (list.Length > MaximumRetainedDocuments)
            {
                lock (gate)
                {
                    retainPrefixes.Add(prefix);
                }
            }
        }

        BoxelSurveyBoxelSnapshot rollup;
        lock (gate)
        {
            rollup = state.Rollup(list);
        }

        if (list.Length > MaximumRetainedDocuments)
        {
            await EvictUnretainedAsync(cancellationToken).ConfigureAwait(false);
        }

        return rollup;
    }

    public async Task<BoxelSurveyRebuildResult?> RebuildAsync(
        string journalDirectory,
        string? currentJournalPath = null,
        IProgress<BoxelSurveyRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            return null;
        }

        var service = new BoxelSurveyRebuildService(
            store.DataDirectory,
            journalDirectory);
        var result = await service.RebuildAsync(
                frontierId,
                state,
                currentJournalPath,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        RaiseChanged();
        return result;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelScheduledFlush();
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            lock (gate)
            {
                state.ClearDirty();
            }

            return;
        }

        List<(string Prefix, BoxelSurveyBoxelDocument Document)> pending;
        int epoch;
        lock (gate)
        {
            epoch = state.Version;
            pending = [];
            foreach (var prefix in state.GetDirtyPrefixes())
            {
                if (state.TryCreateDocument(prefix, out var document))
                {
                    pending.Add((prefix, document));
                }
            }
        }

        var saved = new List<string>();
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await store.SaveBoxelAsync(frontierId, item.Document, cancellationToken)
                    .ConfigureAwait(false);
                saved.Add(item.Prefix);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                PersistenceFailed?.Invoke(this, exception);
                break;
            }
        }

        lock (gate)
        {
            if (state.Version != epoch)
            {
                return;
            }

            foreach (var prefix in saved)
            {
                state.MarkClean(prefix);
            }
        }
    }

    public void SetRetainPrefixes(IEnumerable<string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        lock (gate)
        {
            retainPrefixes.Clear();
            foreach (var prefix in prefixes)
            {
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    retainPrefixes.Add(prefix);
                }
            }
        }
    }

    public IReadOnlyList<BoxelSurveyIndexEntry> RecentEntries(int count = RecentPrefixLimit)
    {
        IReadOnlyList<BoxelSurveyIndexEntry> index;
        lock (gate)
        {
            index = state.GetIndex();
        }

        var byPrefix = index.ToDictionary(
            entry => entry.Prefix,
            StringComparer.Ordinal);
        var recent = new List<BoxelSurveyIndexEntry>();
        lock (gate)
        {
            foreach (var prefix in recents)
            {
                if (byPrefix.TryGetValue(prefix, out var entry))
                {
                    recent.Add(entry);
                }

                if (recent.Count >= count)
                {
                    break;
                }
            }
        }

        if (recent.Count < count)
        {
            foreach (var entry in index
                         .OrderByDescending(entry => entry.LastVisited)
                         .ThenBy(entry => entry.Prefix, StringComparer.Ordinal))
            {
                if (recent.Exists(existing =>
                        string.Equals(
                            existing.Prefix,
                            entry.Prefix,
                            StringComparison.Ordinal)))
                {
                    continue;
                }

                recent.Add(entry);
                if (recent.Count >= count)
                {
                    break;
                }
            }
        }

        return recent;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelScheduledFlush();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            FlushAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (
            exception is OperationCanceledException
                or IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or AggregateException)
        {
            // Shutdown must not throw or wait indefinitely on file I/O.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelScheduledFlush();
        try
        {
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            PersistenceFailed?.Invoke(this, exception);
        }
    }

    private async Task EnsureLoadedForEventAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken)
    {
        if (journalEvent.EventName is not ("FSDJump" or "Location" or "CarrierJump"))
        {
            return;
        }

        var name = GetString(journalEvent.Payload, "StarSystem")
            ?? GetString(journalEvent.Payload, "SystemName");
        if (BoxelAddress.TryParse(name, out var boxel) && boxel is not null)
        {
            await EnsurePrefixLoadedAsync(boxel.Prefix, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task EnsurePrefixLoadedAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        string? commanderId;
        lock (gate)
        {
            commanderId = frontierId;
            if (state.HasLoadedDocument(prefix) || string.IsNullOrWhiteSpace(commanderId))
            {
                return;
            }
        }

        var document = await store.LoadBoxelAsync(commanderId, prefix, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return;
        }

        lock (gate)
        {
            if (!state.HasLoadedDocument(prefix))
            {
                state.ImportDocument(document);
            }
        }
    }

    private Task EvictUnretainedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> keep;
        IReadOnlyList<BoxelSurveyIndexEntry> index;
        lock (gate)
        {
            keep = new HashSet<string>(retainPrefixes, StringComparer.Ordinal);
            if (state.Current?.Prefix is { } current)
            {
                keep.Add(current);
            }

            foreach (var prefix in recents.Take(RecentPrefixLimit))
            {
                keep.Add(prefix);
            }

            foreach (var prefix in state.GetDirtyPrefixes())
            {
                keep.Add(prefix);
            }

            index = state.GetIndex();
        }

        foreach (var entry in index)
        {
            lock (gate)
            {
                if (keep.Contains(entry.Prefix) || !state.HasLoadedDocument(entry.Prefix))
                {
                    continue;
                }

                state.UnloadDocument(entry.Prefix);
            }
        }

        return Task.CompletedTask;
    }

    private void RememberCurrentUnlocked()
    {
        var prefix = state.Current?.Prefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        recents.RemoveAll(existing =>
            string.Equals(existing, prefix, StringComparison.Ordinal));
        recents.Insert(0, prefix);
        if (recents.Count > RecentPrefixLimit)
        {
            recents.RemoveRange(RecentPrefixLimit, recents.Count - RecentPrefixLimit);
        }
    }

    private void ScheduleFlush()
    {
        if (string.IsNullOrWhiteSpace(frontierId) || state.GetDirtyPrefixes().Count == 0)
        {
            return;
        }

        CancelScheduledFlush();
        var cancellation = new CancellationTokenSource();
        flushCancellation = cancellation;
        _ = FlushAfterDelayAsync(cancellation.Token);
    }

    private async Task FlushAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(flushDelay, cancellationToken).ConfigureAwait(false);
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer mutation replaced this flush, or the coordinator is disposing.
        }
    }

    private void CancelScheduledFlush()
    {
        var scheduled = Interlocked.Exchange(ref flushCancellation, null);
        if (scheduled is null)
        {
            return;
        }

        scheduled.Cancel();
        scheduled.Dispose();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static string? GetString(System.Text.Json.JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
}
