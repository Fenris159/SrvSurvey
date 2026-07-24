using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Journeys;

public sealed class JourneyService
{
    private readonly JourneyStore journeyStore;
    private readonly JourneyJournalHistoryReader historyReader;
    private readonly CommanderProfileStore profileStore;
    private readonly ExobiologyReferenceCatalog exobiologyCatalog;
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private JourneyJournalProcessor? activeProcessor;
    private bool activeIsOdyssey;

    public JourneyService(
        JourneyStore journeyStore,
        JourneyJournalHistoryReader historyReader,
        CommanderProfileStore profileStore,
        ExobiologyReferenceCatalog exobiologyCatalog)
    {
        this.journeyStore = journeyStore
            ?? throw new ArgumentNullException(nameof(journeyStore));
        this.historyReader = historyReader
            ?? throw new ArgumentNullException(nameof(historyReader));
        this.profileStore = profileStore
            ?? throw new ArgumentNullException(nameof(profileStore));
        this.exobiologyCatalog = exobiologyCatalog
            ?? throw new ArgumentNullException(nameof(exobiologyCatalog));
    }

    public JourneyDocument? ActiveJourney => activeProcessor?.Journey;

    public Task<JourneyJournalSystemSearchResult> FindLatestStartAsync(
        string frontierId,
        bool isOdyssey,
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        return historyReader.FindLatestFsdJumpAsync(
            frontierId,
            isOdyssey,
            systemAddress,
            cancellationToken);
    }

    public Task<JourneyCatalogResult> LoadAllAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        return journeyStore.LoadAllAsync(frontierId, cancellationToken);
    }

    public Task<JourneyLoadResult> LoadAsync(
        string frontierId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        return journeyStore.LoadAsync(frontierId, fileName, cancellationToken);
    }

    public async Task<JourneyServiceResult> InitializeActiveAsync(
        string frontierId,
        bool isOdyssey,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = await profileStore.LoadAsync(
                    frontierId,
                    isOdyssey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile.Data is null)
            {
                activeProcessor = null;
                return new JourneyServiceResult(
                    null,
                    [profile.Error ?? "The commander profile could not be loaded."],
                    0);
            }

            var fileName = profile.Data?.ActiveJourneyFileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                activeProcessor = null;
                return JourneyServiceResult.Empty;
            }

            var load = await journeyStore.LoadAsync(
                    frontierId,
                    fileName,
                    cancellationToken)
                .ConfigureAwait(false);
            if (load.Journey is null)
            {
                activeProcessor = null;
                return new JourneyServiceResult(
                    null,
                    [load.Error ?? $"The active journey {fileName} was not found."],
                    0);
            }

            if (!load.Journey.IsActive)
            {
                activeProcessor = null;
                return new JourneyServiceResult(
                    load.Journey,
                    ["The commander profile points to a concluded journey."],
                    0);
            }

            return await CatchUpAndActivateAsync(
                    load.Journey,
                    isOdyssey,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> BeginAsync(
        JourneyBeginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartingEntry.Event.Timestamp is null)
        {
            throw new ArgumentException(
                "The starting FSD jump must have a timestamp.",
                nameof(request));
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is not null)
            {
                throw new InvalidOperationException(
                    "Conclude the active journey before beginning another one.");
            }

            var profile = await profileStore.LoadAsync(
                    request.FrontierId,
                    request.IsOdyssey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (profile.Data is null)
            {
                throw new InvalidDataException(
                    profile.Error ?? "The commander profile could not be loaded.");
            }

            if (!string.IsNullOrWhiteSpace(profile.Data.ActiveJourneyFileName))
            {
                throw new InvalidOperationException(
                    "Conclude the active journey before beginning another one.");
            }

            var journey = await journeyStore.CreateAsync(
                    new JourneyCreationRequest(
                        request.FrontierId,
                        request.CommanderName,
                        request.Name,
                        request.Description,
                        request.StartingEntry.JournalFileName,
                        request.StartingEntry.Event.Timestamp.Value),
                    cancellationToken)
                .ConfigureAwait(false);
            var result = await CatchUpAndActivateAsync(
                    journey,
                    request.IsOdyssey,
                    cancellationToken)
                .ConfigureAwait(false);
            await profileStore.SaveActiveJourneyAsync(
                    request.FrontierId,
                    request.CommanderName,
                    request.IsOdyssey,
                    result.Journey!.FileName,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyServiceResult> ApplyLiveAsync(
        IEnumerable<JournalEventEnvelope> journalEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return JourneyServiceResult.Empty;
            }

            var processed = 0;
            foreach (var journalEvent in journalEvents)
            {
                if (activeProcessor.Apply(journalEvent))
                {
                    processed++;
                }
            }

            if (processed > 0)
            {
                await journeyStore.SaveAsync(
                        activeProcessor.Journey,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new JourneyServiceResult(
                activeProcessor.Journey,
                [],
                processed);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyDocument> SaveAsync(
        JourneyDocument journey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (string.IsNullOrWhiteSpace(journey.Name))
        {
            throw new ArgumentException(
                "The journey name cannot be blank.",
                nameof(journey));
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = journey with
            {
                Name = journey.Name.Trim(),
                Description = journey.Description ?? string.Empty,
            };
            await journeyStore.SaveAsync(normalized, cancellationToken)
                .ConfigureAwait(false);
            if (activeProcessor?.Journey.FileName == normalized.FileName
                && activeProcessor.Journey.FrontierId == normalized.FrontierId)
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

    public async Task<bool> IncrementNoteCountAsync(
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return false;
            }

            var visits = activeProcessor.Journey.VisitedSystems.ToArray();
            var index = Array.FindLastIndex(
                visits,
                visit => visit.StarSystem.SystemAddress == systemAddress);
            if (index < 0)
            {
                return false;
            }

            var visit = visits[index];
            visits[index] = visit with
            {
                Counts = visit.Counts with
                {
                    Notes = checked(visit.Counts.Notes + 1),
                },
            };
            var updated = activeProcessor.Journey with
            {
                VisitedSystems = visits,
            };
            activeProcessor.UpdateJourney(updated);
            await journeyStore.SaveAsync(updated, cancellationToken)
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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journey);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reset = journey with
            {
                Watermark = journey.StartTime,
                VisitedSystems = [],
            };
            var read = await historyReader.ReadFromAsync(
                    reset.StartingJournal,
                    reset.FrontierId,
                    isOdyssey,
                    cancellationToken)
                .ConfigureAwait(false);
            var processor = new JourneyJournalProcessor(
                reset,
                exobiologyCatalog,
                isOdyssey);
            var events = journey.EndTime is { } endTime
                ? read.Events.Where(journalEvent =>
                    journalEvent.Timestamp is null
                    || journalEvent.Timestamp <= endTime)
                : read.Events;
            var replay = processor.ApplyCatchUp(events);
            await journeyStore.SaveAsync(replay.Journey, cancellationToken)
                .ConfigureAwait(false);

            if (activeProcessor?.Journey.FileName == journey.FileName
                && activeProcessor.Journey.FrontierId == journey.FrontierId)
            {
                activeProcessor = processor;
                activeIsOdyssey = isOdyssey;
            }

            return new JourneyServiceResult(
                replay.Journey,
                read.Errors,
                replay.ProcessedEventCount);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<JourneyDocument?> ConcludeActiveAsync(
        string commanderName,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (activeProcessor is null)
            {
                return null;
            }

            var concluded = activeProcessor.Journey with { EndTime = endTime };
            await journeyStore.SaveAsync(concluded, cancellationToken)
                .ConfigureAwait(false);
            await profileStore.SaveActiveJourneyAsync(
                    concluded.FrontierId,
                    commanderName,
                    activeIsOdyssey,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            activeProcessor = null;
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
        CancellationToken cancellationToken)
    {
        var read = await historyReader.ReadFromAsync(
                journey.StartingJournal,
                journey.FrontierId,
                isOdyssey,
                cancellationToken)
            .ConfigureAwait(false);
        var processor = new JourneyJournalProcessor(
            journey,
            exobiologyCatalog,
            isOdyssey);
        var replay = processor.ApplyCatchUp(read.Events);
        if (replay.ProcessedEventCount > 0)
        {
            await journeyStore.SaveAsync(replay.Journey, cancellationToken)
                .ConfigureAwait(false);
        }

        activeProcessor = processor;
        activeIsOdyssey = isOdyssey;
        return new JourneyServiceResult(
            replay.Journey,
            read.Errors,
            replay.ProcessedEventCount);
    }
}

public sealed record JourneyBeginRequest(
    string FrontierId,
    string CommanderName,
    bool IsOdyssey,
    string Name,
    string Description,
    JourneyJournalSystemEntry StartingEntry);

public sealed record JourneyServiceResult(
    JourneyDocument? Journey,
    IReadOnlyList<string> Errors,
    int ProcessedEventCount)
{
    public static JourneyServiceResult Empty { get; } = new(null, [], 0);
}
