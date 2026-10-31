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
            // Initialization belongs to the requested profile even when loading or replay fails.
            activeProcessor = null;
            RecoveryReference = null;
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
            JourneyServiceResult result = await CatchUpAndActivateAsync(journey, request.IsOdyssey, cancellationToken)
                .ConfigureAwait(false);
            bool selected = await profileStore
                .TryReplaceActiveJourneyAsync(
                    request.FrontierId,
                    request.IsOdyssey,
                    expectedReference,
                    result.Journey!.FileName,
                    cancellationToken,
                    request.CommanderName
                )
                .ConfigureAwait(false);
            if (!selected)
            {
                return SelectionChanged() with
                {
                    Errors =
                    [
                        "The journey was saved, but the active journey selection changed. Refresh Journeys to load the latest selection.",
                    ],
                };
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

            int processed = journalEvents.Count(journalEvent => activeProcessor.Apply(journalEvent));

            if (processed > 0)
            {
                await journeyStore.SaveAsync(activeProcessor.Journey, cancellationToken).ConfigureAwait(false);
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
            }

            return normalized;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<bool> IncrementNoteCountAsync(long systemAddress, CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return false;
            }

            JourneySystemVisit? current = activeProcessor.Journey.CurrentSystem;
            if (current is null || current.StarSystem.SystemAddress != systemAddress)
            {
                return false;
            }

            JourneySystemVisit[] visits = activeProcessor.Journey.VisitedSystems.ToArray();
            int index = Array.LastIndexOf(visits, current);
            JourneySystemVisit visit = visits[index];
            visits[index] = visit with { Counts = visit.Counts with { Notes = checked(visit.Counts.Notes + 1) } };
            JourneyDocument updated = activeProcessor.Journey with { VisitedSystems = visits };
            activeProcessor.UpdateJourney(updated);
            await journeyStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
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
            JourneyDocument reset = journey with { Watermark = journey.StartTime, VisitedSystems = [] };
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

            JourneyDocument concluded = activeProcessor.Journey with { EndTime = endTime };
            await journeyStore.SaveAsync(concluded, cancellationToken).ConfigureAwait(false);
            activeProcessor = null;
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
        (JourneyJournalProcessor processor, bool rebuilt) = CreateCatchUpProcessor(journey, read, isOdyssey);
        int processed = read.Events.Count(entry => entry.Timestamp > journey.Watermark);
        if (rebuilt || processed > 0)
        {
            await journeyStore.SaveAsync(processor.Journey, cancellationToken).ConfigureAwait(false);
        }

        activeProcessor = processor;
        activeIsOdyssey = isOdyssey;
        return new JourneyServiceResult(processor.Journey, read.Errors, processed);
    }

    private (JourneyJournalProcessor Processor, bool Rebuilt) CreateCatchUpProcessor(
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
                    VisitedSystems = [],
                },
                exobiologyCatalog,
                isOdyssey
            );
            rebuilt.ApplyCatchUp(read.Events);
            if (CoversSavedActivity(journey, rebuilt.Journey))
            {
                var notes = journey.VisitedSystems.ToDictionary(
                    visit => (visit.StarSystem.SystemAddress, visit.Arrived),
                    visit => visit.Counts.Notes
                );
                rebuilt.UpdateJourney(
                    rebuilt.Journey with
                    {
                        VisitedSystems = rebuilt
                            .Journey.VisitedSystems.Select(visit =>
                                visit with
                                {
                                    Counts = visit.Counts with
                                    {
                                        Notes = notes.GetValueOrDefault(
                                            (visit.StarSystem.SystemAddress, visit.Arrived)
                                        ),
                                    },
                                }
                            )
                            .ToArray(),
                    }
                );
                return (rebuilt, true);
            }
        }
        // Missing history must not erase activity that was already saved. Continue from the watermark instead.
        var incremental = new JourneyJournalProcessor(journey, exobiologyCatalog, isOdyssey);
        incremental.ApplyCatchUp(read.Events);
        return (incremental, false);
    }

    private static bool CoversSavedActivity(JourneyDocument saved, JourneyDocument rebuilt)
    {
        var visits = rebuilt.VisitedSystems.ToDictionary(visit => (visit.StarSystem.SystemAddress, visit.Arrived));
        return rebuilt.Watermark >= saved.Watermark
            && saved.VisitedSystems.All(visit =>
                visits.TryGetValue((visit.StarSystem.SystemAddress, visit.Arrived), out JourneySystemVisit? replacement)
                && CoversVisit(visit, replacement)
            );
    }

    private static bool CoversVisit(JourneySystemVisit saved, JourneySystemVisit rebuilt) =>
        CoversCounts(saved.Counts, rebuilt.Counts)
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
