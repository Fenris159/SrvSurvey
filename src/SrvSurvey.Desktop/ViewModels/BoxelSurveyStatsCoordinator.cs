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
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "SemaphoreSlim does not allocate a wait handle here and may still have shutdown waiters.")]
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private string? frontierId;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "Every scheduled source is atomically cancelled and disposed by CancelScheduledFlush.")]
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

    public string? FrontierId
    {
        get
        {
            lock (gate)
            {
                return frontierId;
            }
        }
    }

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

    public bool TreatNavBeaconAsFullyScanned
    {
        get
        {
            lock (gate)
            {
                return state.TreatNavBeaconAsFullyScanned;
            }
        }
        set
        {
            lock (gate)
            {
                state.TreatNavBeaconAsFullyScanned = value;
            }
        }
    }

    public async Task SwitchCommanderAsync(
        string? nextFrontierId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var normalized = string.IsNullOrWhiteSpace(nextFrontierId)
                ? null
                : nextFrontierId.Trim();
            lock (gate)
            {
                if (string.Equals(frontierId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
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
        }
        finally
        {
            operationLock.Release();
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

    public async Task ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool bootstrapContextOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var raiseChanged = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
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
        }
        finally
        {
            operationLock.Release();
        }

        if (raiseChanged)
        {
            RaiseChanged();
        }
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

    public async Task IngestSnapshotAsync(
        SystemScanSnapshot snapshot,
        DateTimeOffset? visitedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var raiseChanged = false;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (BoxelAddress.TryParse(snapshot.SystemName, out var boxel)
                && boxel is not null)
            {
                await EnsurePrefixLoadedAsync(boxel.Prefix, cancellationToken)
                    .ConfigureAwait(false);
            }

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
        }
        finally
        {
            operationLock.Release();
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
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsurePrefixLoadedAsync(prefix, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                return state.TryGet(prefix, out var snapshot) ? snapshot : null;
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<BoxelSurveyBoxelDocument?> GetDocumentAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsurePrefixLoadedAsync(prefix, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                return state.TryCreateDocument(prefix, out var document)
                    ? document
                    : null;
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<BoxelSurveyBoxelSnapshot> RollupAsync(
        IEnumerable<string> prefixes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var list = prefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var prefix in list)
            {
                await EnsurePrefixLoadedAsync(prefix, cancellationToken)
                    .ConfigureAwait(false);
            }

            BoxelSurveyBoxelSnapshot rollup;
            lock (gate)
            {
                rollup = state.Rollup(list);
            }

            if (list.Length > MaximumRetainedDocuments)
            {
                EvictUnretained(cancellationToken);
            }

            return rollup;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<BoxelSurveyRebuildResult?> RebuildAsync(
        string journalDirectory,
        string? currentJournalPath = null,
        IProgress<BoxelSurveyRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            string commanderId;
            BoxelSurveyStatsState rebuiltState;
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(frontierId))
                {
                    return null;
                }

                commanderId = frontierId;
                rebuiltState = state.CreateWorkingCopy();
            }

            var service = new BoxelSurveyRebuildService(
                store.DataDirectory,
                journalDirectory);
            var result = await service.RebuildAsync(
                    commanderId,
                    rebuiltState,
                    currentJournalPath,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
            {
                if (!string.Equals(frontierId, commanderId, StringComparison.Ordinal))
                {
                    return null;
                }

                state.ReplaceWith(rebuiltState);
            }

            await FlushAsync(cancellationToken).ConfigureAwait(false);
            RaiseChanged();
            return result;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelScheduledFlush();
        string? commanderId;
        List<(string Prefix, BoxelSurveyBoxelDocument Document)> pending;
        int epoch;
        lock (gate)
        {
            commanderId = frontierId;
            if (string.IsNullOrWhiteSpace(commanderId))
            {
                state.ClearDirty();
                return;
            }

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
        try
        {
            await store.SaveBoxelsAsync(
                    commanderId,
                    pending.Select(item => item.Document).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            saved.AddRange(pending.Select(item => item.Prefix));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            PersistenceFailed?.Invoke(this, exception);
        }

        lock (gate)
        {
            if (state.Version != epoch
                || !string.Equals(frontierId, commanderId, StringComparison.Ordinal))
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
            retainPrefixes.UnionWith(
                prefixes
                    .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaximumRetainedDocuments));
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
            if (string.Equals(frontierId, commanderId, StringComparison.Ordinal)
                && !state.HasLoadedDocument(prefix))
            {
                state.ImportDocument(document);
            }
        }
    }

    private void EvictUnretained(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var keep = new HashSet<string>(retainPrefixes, StringComparer.Ordinal);
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

            foreach (var prefix in state.GetIndex()
                         .Select(entry => entry.Prefix)
                         .Where(prefix =>
                             !keep.Contains(prefix)
                             && state.HasLoadedDocument(prefix)))
            {
                state.UnloadDocument(prefix);
            }
        }
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

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref flushCancellation, cancellation);
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

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
