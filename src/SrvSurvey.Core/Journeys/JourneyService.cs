using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Journeys;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The service is application-scoped and its gate may have in-flight waiters."
)]
public sealed partial class JourneyService
{
    private readonly JourneyStore journeyStore;
    private readonly JourneyJournalHistoryReader historyReader;
    private readonly CommanderProfileStore profileStore;
    private readonly ExobiologyReferenceCatalog exobiologyCatalog;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private JourneyJournalProcessor? activeProcessor;
    private JourneyDocument? pendingLiveProgress;
    private readonly List<JournalEventEnvelope> pendingJournalEvents = [];
    private JourneyJournalReadResult? pendingHistoryReplay;
    private bool activeIsOdyssey;
    private readonly Action<string>? log;

    public JourneyService(
        JourneyStore journeyStore,
        JourneyJournalHistoryReader historyReader,
        CommanderProfileStore profileStore,
        ExobiologyReferenceCatalog exobiologyCatalog,
        Action<string>? log = null
    )
    {
        this.journeyStore = journeyStore ?? throw new ArgumentNullException(nameof(journeyStore));
        this.historyReader = historyReader ?? throw new ArgumentNullException(nameof(historyReader));
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.exobiologyCatalog = exobiologyCatalog ?? throw new ArgumentNullException(nameof(exobiologyCatalog));
        this.log = log;
    }

    public JourneyDocument? ActiveJourney => activeProcessor?.Journey;

    public Task<JourneyJournalSystemSearchResult> FindLatestStartAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        return historyReader.FindLatestFsdJumpAsync(frontierId, isOdyssey, systemAddress, cancellationToken);
    }

    public async Task<JourneyCatalogResult> LoadAllAsync(
        string frontierId,
        CancellationToken cancellationToken = default
    )
    {
        JourneyCatalogResult catalog = await journeyStore
            .LoadAllAsync(frontierId, cancellationToken)
            .ConfigureAwait(false);
        var journeys = new List<JourneyDocument>();
        var errors = new List<string>(catalog.Errors);
        foreach (JourneyDocument journey in catalog.Journeys)
        {
            JourneyLoadResult upgraded = await journeyStore
                .UpgradeAsync(frontierId, journey.FileName, cancellationToken)
                .ConfigureAwait(false);
            if (upgraded.Journey is not null)
            {
                journeys.Add(upgraded.Journey);
            }
            if (upgraded.Error is not null)
            {
                errors.Add(upgraded.Error);
            }
        }
        return new(journeys, errors);
    }

    public Task<JourneyJournalSystemVisitsResult> FindStartVisitsAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        DateOnly? onOrAfter = null,
        CancellationToken cancellationToken = default
    ) => historyReader.FindArrivalsAsync(frontierId, isOdyssey, systemAddress, onOrAfter, cancellationToken);

    public Task<JourneyJournalSystemVisitsResult> FindStartVisitsAsync(
        string frontierId,
        bool isOdyssey,
        string systemName,
        DateOnly? onOrAfter = null,
        CancellationToken cancellationToken = default
    ) => historyReader.FindArrivalsAsync(frontierId, isOdyssey, systemName, onOrAfter, cancellationToken);

    public Task<JourneyJournalSystemSearchResult> FindCurrentStartAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default
    ) => historyReader.FindCurrentLocationAsync(frontierId, isOdyssey, systemAddress, cancellationToken);

    public Task<JourneyLoadResult> LoadAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        return journeyStore.LoadAsync(frontierId, fileName, cancellationToken);
    }

    public async Task<JourneyServiceResult> InitializeActiveAsync(
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ParkPendingProgress();
            // Initialization belongs to the requested profile even when loading or replay fails.
            activeProcessor = null;
            pendingLiveProgress = null;
            pendingJournalEvents.Clear();
            pendingHistoryReplay = null;
            RecoveryReference = null;
            await DrainRetainedProgressAsync(frontierId, cancellationToken).ConfigureAwait(false);
            CommanderProfileLoadResult profile = await profileStore
                .LoadAsync(frontierId, isOdyssey, cancellationToken)
                .ConfigureAwait(false);
            if (profile.Data is not { } data)
            {
                return new(null, [profile.Error ?? "The commander profile could not be loaded."], 0);
            }
            return await ResolveActiveJourneyAsync(frontierId, isOdyssey, data, replay: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> BeginAsync(
        JourneyBeginRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartingEntry.Event.Timestamp is null)
        {
            throw new ArgumentException("The starting location must have a timestamp.", nameof(request));
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CommanderProfileLoadResult profile = await profileStore
                .LoadAsync(request.FrontierId, request.IsOdyssey, cancellationToken)
                .ConfigureAwait(false);
            if (profile.Data is not { } data)
            {
                throw new InvalidDataException(profile.Error ?? "The commander profile could not be loaded.");
            }
            LogState("begin", request.FrontierId, request.IsOdyssey, data.ActiveJourneyFileName);
            JourneyServiceResult active = await ResolveActiveJourneyAsync(
                    request.FrontierId,
                    request.IsOdyssey,
                    data,
                    replay: false,
                    cancellationToken
                )
                .ConfigureAwait(false);
            JourneyCatalogResult catalog = await journeyStore
                .LoadAllAsync(request.FrontierId, cancellationToken)
                .ConfigureAwait(false);
            JourneyDocument? blockingJourney = catalog.Journeys.FirstOrDefault(journey => journey.IsActive);
            if (blockingJourney is not null)
            {
                throw new InvalidOperationException(
                    $"Conclude the active journey before beginning another one. Active journey: {blockingJourney.Name} ({blockingJourney.CommanderName}). Resume it from the Journeys list if it is not currently recording."
                );
            }

            string? expectedReference = GetResolvedReference(data, active);

            JourneyDocument journey = await journeyStore
                .CreateAsync(
                    new JourneyCreationRequest(
                        request.FrontierId,
                        request.CommanderName,
                        request.Name,
                        request.Description,
                        request.StartingEntry.JournalFileName,
                        request.StartingEntry.Event.Timestamp.Value
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            bool selected = await profileStore
                .TryReplaceActiveJourneyAsync(
                    request.FrontierId,
                    request.IsOdyssey,
                    expectedReference,
                    journey.FileName,
                    cancellationToken,
                    request.CommanderName
                )
                .ConfigureAwait(false);
            if (!selected)
            {
                return SelectionChanged();
            }
            JourneyServiceResult result = await ResumeSavedJourneyAsync(
                    journey,
                    request.IsOdyssey,
                    replay: true,
                    upgradeWarning: null,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (
                !await profileStore
                    .TryReplaceActiveJourneyAsync(
                        request.FrontierId,
                        request.IsOdyssey,
                        journey.FileName,
                        journey.FileName,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                return SelectionChanged();
            }
            RecoveryReference = null;
            return result with { Errors = [.. active.Errors, .. catalog.Errors, .. result.Errors] };
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> ApplyLiveAsync(
        IEnumerable<JournalEventEnvelope> journalEvents,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return JourneyServiceResult.Empty;
            }

            JourneyObservedBatch batch = activeProcessor.ApplyLiveBatch(journalEvents);
            int processed = batch.Processed;
            pendingJournalEvents.AddRange(batch.Observations);
            if (batch.Observations.Count > 0)
            {
                pendingLiveProgress = activeProcessor.Journey;
            }
            if (HasPendingProgress(activeProcessor.Journey))
            {
                await UpdateStoredJourneyLockedAsync(
                        activeProcessor.Journey,
                        static latest => latest,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            return new JourneyServiceResult(activeProcessor.Journey, [], processed);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyDocument> SaveAsync(JourneyDocument journey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (string.IsNullOrWhiteSpace(journey.Name))
        {
            throw new ArgumentException("The journey name cannot be blank.", nameof(journey));
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JourneyDocument normalized = journey with
            {
                Name = journey.Name.Trim(),
                Description = journey.Description ?? string.Empty,
            };
            normalized = await journeyStore.SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            if (
                activeProcessor?.Journey.FileName == normalized.FileName
                && activeProcessor.Journey.FrontierId == normalized.FrontierId
            )
            {
                activeProcessor.UpdateJourney(normalized);
                pendingLiveProgress = null;
                pendingJournalEvents.Clear();
                pendingHistoryReplay = null;
            }

            return normalized;
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <summary>Edits metadata against the latest stored or active journey while serialized with live projection.</summary>
    public Task<JourneyDocument> SaveMetadataAsync(
        JourneyDocument journey,
        string name,
        string description,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return UpdateStoredJourneyAsync(
            journey,
            latest => latest with { Name = name.Trim(), Description = description ?? string.Empty },
            cancellationToken
        );
    }

    /// <summary>Records a saved note against its selected visit, including historical and completed journeys.</summary>
    public Task<JourneyDocument> RecordNoteAsync(
        JourneyDocument journey,
        long systemAddress,
        DateTimeOffset arrived,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        return UpdateStoredJourneyAsync(
            journey,
            latest =>
            {
                JourneySystemVisit[] visits = latest.VisitedSystems.ToArray();
                int index = Array.FindIndex(
                    visits,
                    visit => visit.StarSystem.SystemAddress == systemAddress && visit.Arrived == arrived
                );
                if (index < 0)
                {
                    throw new InvalidOperationException(
                        "The selected Journey visit is no longer available; its note count was not updated."
                    );
                }
                JourneySystemVisit visit = visits[index];
                visits[index] = visit with { Counts = visit.Counts with { Notes = checked(visit.Counts.Notes + 1) } };
                return latest with { VisitedSystems = visits };
            },
            cancellationToken
        );
    }

    public async Task<JourneyDocument> LoadLatestAsync(
        JourneyDocument journey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadLatestJourneyAsync(journey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<JourneyDocument> ReadLatestJourneyAsync(
        JourneyDocument expected,
        CancellationToken cancellationToken
    )
    {
        if (
            activeProcessor?.Journey is { } active
            && active.FileName == expected.FileName
            && active.FrontierId == expected.FrontierId
        )
        {
            return active;
        }
        JourneyLoadResult load = await journeyStore
            .LoadAsync(expected.FrontierId, expected.FileName, cancellationToken)
            .ConfigureAwait(false);
        return load.Journey ?? throw new IOException(load.Error ?? "The Journey could not be loaded for editing.");
    }

    private async Task<JourneyDocument> UpdateStoredJourneyAsync(
        JourneyDocument expected,
        Func<JourneyDocument, JourneyDocument> update,
        CancellationToken cancellationToken
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await UpdateStoredJourneyLockedAsync(expected, update, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    private bool HasPendingProgress(JourneyDocument journey) =>
        pendingLiveProgress is { } pending
        && pending.FrontierId == journey.FrontierId
        && pending.FileName == journey.FileName;

    private JourneyDocument ReconcilePendingProgress(JourneyDocument stored)
    {
        if (HasPendingProgress(stored) && activeProcessor is not null)
        {
            return MergePendingProgress(
                stored,
                new(activeProcessor, activeIsOdyssey, pendingJournalEvents, pendingHistoryReplay)
            );
        }
        return retainedProgress.TryGetValue((stored.FrontierId, stored.FileName), out PendingJourney? retained)
            ? MergePendingProgress(stored, retained)
            : stored;
    }

    private JourneyDocument MergePendingProgress(JourneyDocument stored, PendingJourney pending)
    {
        JourneyJournalProcessor merged = pending.History is { } replay
            ? CreateCatchUpProcessor(stored, replay, pending.IsOdyssey).Processor
            : pending.Processor.CreateMergeProcessor(stored);
        merged.MergePendingRecords(pending.Events);
        return merged.Journey;
    }

    private async Task<JourneyDocument> UpdateStoredJourneyLockedAsync(
        JourneyDocument expected,
        Func<JourneyDocument, JourneyDocument> update,
        CancellationToken cancellationToken
    )
    {
        JourneyDocument saved = await journeyStore
            .UpdateAsync(
                expected.FrontierId,
                expected.FileName,
                stored => update(ReconcilePendingProgress(stored)),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (
            activeProcessor?.Journey.FileName == saved.FileName
            && activeProcessor.Journey.FrontierId == saved.FrontierId
        )
        {
            activeProcessor.UpdateJourney(saved);
            pendingLiveProgress = null;
            pendingJournalEvents.Clear();
            pendingHistoryReplay = null;
        }
        retainedProgress.Remove((saved.FrontierId, saved.FileName));
        return saved;
    }

    public Task<bool> IncrementNoteCountAsync(long systemAddress, CancellationToken cancellationToken = default) =>
        IncrementNoteCountAsync(systemAddress, null, cancellationToken);

    public async Task<bool> IncrementNoteCountAsync(
        long systemAddress,
        JourneyDocument? expectedJourney,
        CancellationToken cancellationToken = default
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (
                activeProcessor is null
                || (
                    expectedJourney is not null
                    && (
                        activeProcessor.Journey.FrontierId != expectedJourney.FrontierId
                        || activeProcessor.Journey.FileName != expectedJourney.FileName
                    )
                )
            )
            {
                return false;
            }

            JourneySystemVisit? current = activeProcessor.Journey.CurrentSystem;
            if (current is null || current.StarSystem.SystemAddress != systemAddress)
            {
                return false;
            }

            await UpdateStoredJourneyLockedAsync(
                    activeProcessor.Journey,
                    latest =>
                    {
                        JourneySystemVisit[] visits = latest.VisitedSystems.ToArray();
                        int index = Array.FindIndex(
                            visits,
                            visit =>
                                visit.StarSystem.SystemAddress == current.StarSystem.SystemAddress
                                && visit.Arrived == current.Arrived
                        );
                        if (index < 0)
                        {
                            throw new InvalidOperationException(
                                "The active Journey visit is no longer available for its note count."
                            );
                        }
                        JourneySystemVisit visit = visits[index];
                        visits[index] = visit with
                        {
                            Counts = visit.Counts with { Notes = checked(visit.Counts.Notes + 1) },
                        };
                        return latest with { VisitedSystems = visits };
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> ReprocessAsync(
        JourneyDocument journey,
        bool isOdyssey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journey);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JourneyDocument reset = journey with
            {
                Watermark = journey.StartTime,
                WatermarkRecords = null,
                WatermarkRecordsComplete = null,
                RecordTrackingStart = null,
                RecordedJournalRecords = null,
                ObservationHistory = null,
                RecordTrackingStartComplete = false,
                VisitedSystems = [],
            };
            JourneyJournalReadResult read = await historyReader
                .ReadFromAsync(reset.StartingJournal, reset.FrontierId, isOdyssey, reset.StartTime, cancellationToken)
                .ConfigureAwait(false);
            if (!read.IsComplete)
            {
                return new JourneyServiceResult(journey, read.Errors, 0);
            }
            var processor = new JourneyJournalProcessor(reset, exobiologyCatalog, isOdyssey);
            IEnumerable<JournalEventEnvelope> events = journey.EndTime is { } endTime
                ? read.Events.Where(journalEvent => journalEvent.Timestamp is null || journalEvent.Timestamp <= endTime)
                : read.Events;
            JourneyReplaySummary replay = processor.ApplyCatchUp(events);
            JourneyDocument saved = await journeyStore
                .SaveAsync(replay.Journey, cancellationToken)
                .ConfigureAwait(false);
            processor.UpdateJourney(saved);

            if (
                activeProcessor?.Journey.FileName == journey.FileName
                && activeProcessor.Journey.FrontierId == journey.FrontierId
            )
            {
                activeProcessor = processor;
                pendingLiveProgress = null;
                pendingJournalEvents.Clear();
                pendingHistoryReplay = null;
                activeIsOdyssey = isOdyssey;
            }

            return new JourneyServiceResult(saved, read.Errors, replay.ProcessedEventCount);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyDocument?> ConcludeActiveAsync(
        string commanderName,
        DateTimeOffset endTime,
        JourneyDocument? expectedJourney = null,
        CancellationToken cancellationToken = default
    )
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return null;
            }

            if (
                expectedJourney is not null
                && (
                    !string.Equals(
                        activeProcessor.Journey.FrontierId,
                        expectedJourney.FrontierId,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || activeProcessor.Journey.FileName != expectedJourney.FileName
                )
            )
            {
                return null;
            }

            JourneyDocument concluded = await UpdateStoredJourneyLockedAsync(
                    activeProcessor.Journey,
                    latest => latest with { EndTime = endTime },
                    cancellationToken
                )
                .ConfigureAwait(false);
            activeProcessor = null;
            pendingLiveProgress = null;
            pendingJournalEvents.Clear();
            pendingHistoryReplay = null;
            await profileStore
                .SaveActiveJourneyAsync(concluded.FrontierId, commanderName, activeIsOdyssey, null, cancellationToken)
                .ConfigureAwait(false);
            return concluded;
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<JourneyServiceResult> CatchUpAndActivateAsync(
        JourneyDocument journey,
        bool isOdyssey,
        CancellationToken cancellationToken
    )
    {
        JourneyJournalReadResult read = await historyReader
            .ReadFromAsync(journey.StartingJournal, journey.FrontierId, isOdyssey, journey.StartTime, cancellationToken)
            .ConfigureAwait(false);
        (JourneyJournalProcessor processor, bool rebuilt, int processed) = CreateCatchUpProcessor(
            journey,
            read,
            isOdyssey
        );
        activeProcessor = processor;
        activeIsOdyssey = isOdyssey;
        if (rebuilt || processed > 0 || processor.HasRecordMetadataChanges)
        {
            // Keep the read and its projection until the leased merge succeeds.
            // A failed catch-up save must not discard learned boundary identities.
            pendingLiveProgress = processor.Journey;
            pendingHistoryReplay = read;
            await UpdateStoredJourneyLockedAsync(processor.Journey, static latest => latest, cancellationToken)
                .ConfigureAwait(false);
        }
        return new JourneyServiceResult(processor.Journey, read.Errors, processed);
    }

    private (JourneyJournalProcessor Processor, bool Rebuilt, int Processed) CreateCatchUpProcessor(
        JourneyDocument journey,
        JourneyJournalReadResult read,
        bool isOdyssey
    )
    {
        if (read.IsComplete)
        {
            var rebuilt = new JourneyJournalProcessor(
                journey with
                {
                    Watermark = journey.StartTime,
                    WatermarkRecords = null,
                    WatermarkRecordsComplete = null,
                    RecordTrackingStart = null,
                    RecordedJournalRecords = null,
                    ObservationHistory = null,
                    RecordTrackingStartComplete = false,
                    VisitedSystems = [],
                },
                exobiologyCatalog,
                isOdyssey
            );
            rebuilt.ApplyCatchUp(read.Events);
            rebuilt.MergeRetainedOrdering(journey);
            if (CoversSavedActivity(journey, rebuilt.Journey))
            {
                rebuilt.UpdateJourney(
                    rebuilt.Journey with
                    {
                        VisitedSystems = JourneyJournalProcessor.RestoreNotes(
                            journey.VisitedSystems,
                            rebuilt.Journey.VisitedSystems
                        ),
                    }
                );
                return (rebuilt, true, read.Events.Count(entry => entry.Timestamp > journey.Watermark));
            }
        }
        // Missing history must not erase activity that was already saved. Continue from the watermark instead.
        var incremental = new JourneyJournalProcessor(journey, exobiologyCatalog, isOdyssey);
        JourneyReplaySummary replay = incremental.ApplyCatchUp(read.Events);
        return (incremental, false, replay.ProcessedEventCount);
    }

    private static bool CoversSavedActivity(JourneyDocument saved, JourneyDocument rebuilt) =>
        rebuilt.Watermark >= saved.Watermark
        && (
            (JourneyProgressSignature.IsTrusted(saved) && saved.ObservationHistory!.Baseline.IsOriginKnown)
            || CoversVisitedActivity(saved, rebuilt)
        )
        && JourneyJournalProcessor.CoversRecordedProgress(saved, rebuilt);

    private static bool CoversVisitedActivity(JourneyDocument saved, JourneyDocument rebuilt)
    {
        var visits = rebuilt.VisitedSystems.ToDictionary(visit => (visit.StarSystem.SystemAddress, visit.Arrived));
        return saved.VisitedSystems.All(visit =>
            visits.TryGetValue((visit.StarSystem.SystemAddress, visit.Arrived), out JourneySystemVisit? replacement)
            && CoversVisit(visit, replacement)
        );
    }

    private static bool CoversVisit(JourneySystemVisit saved, JourneySystemVisit rebuilt) =>
        (saved.Departed is null || (rebuilt.Departed is { } departed && departed <= saved.Departed))
        && CoversCounts(saved.Counts, rebuilt.Counts)
        && CoversDictionary(saved.LandedOn, rebuilt.LandedOn)
        && CoversDictionary(saved.SubCategories, rebuilt.SubCategories)
        && CoversDictionary(saved.SurfaceSignals, rebuilt.SurfaceSignals)
        && CoversDictionary(saved.FssSignals, rebuilt.FssSignals)
        && CoversSet(saved.CodexScanned, rebuilt.CodexScanned)
        && CoversSet(saved.BodiesScanned, rebuilt.BodiesScanned)
        && CoversSet(saved.CodexNew, rebuilt.CodexNew);

    private static bool CoversDictionary(
        IReadOnlyDictionary<string, int>? saved,
        IReadOnlyDictionary<string, int>? rebuilt
    ) => saved is null || saved.All(pair => rebuilt?.GetValueOrDefault(pair.Key) >= pair.Value);

    private static bool CoversSet<T>(IReadOnlySet<T>? saved, IReadOnlySet<T>? rebuilt) =>
        saved is null || (rebuilt is not null && saved.IsSubsetOf(rebuilt));

    private static bool CoversCounts(JourneyCounts saved, JourneyCounts rebuilt) =>
        rebuilt.BodyScans >= saved.BodyScans
        && rebuilt.DetailedSurfaceScans >= saved.DetailedSurfaceScans
        && rebuilt.NewCodexEntries >= saved.NewCodexEntries
        && rebuilt.Organisms >= saved.Organisms
        && rebuilt.Touchdowns >= saved.Touchdowns
        && rebuilt.BodyCount >= saved.BodyCount
        && rebuilt.Screenshots >= saved.Screenshots
        && rebuilt.ExobiologyRewards >= saved.ExobiologyRewards
        && rebuilt.ExplorationRewards >= saved.ExplorationRewards
        && rebuilt.Stars >= saved.Stars;
}

public sealed record JourneyBeginRequest(
    string FrontierId,
    string CommanderName,
    bool IsOdyssey,
    string Name,
    string Description,
    JourneyJournalSystemEntry StartingEntry
);

public sealed record JourneyServiceResult(
    JourneyDocument? Journey,
    IReadOnlyList<string> Errors,
    int ProcessedEventCount
)
{
    public static JourneyServiceResult Empty { get; } = new(null, [], 0);
    public JourneyRecoveryReference? Recovery { get; init; }
}

public sealed record JourneyRecoveryReference(string FrontierId, bool IsOdyssey, string FileName);
