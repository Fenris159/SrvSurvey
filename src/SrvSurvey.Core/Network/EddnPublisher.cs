using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using System.Threading.Channels;

namespace SrvSurvey.Core.Network;

// EDDN uploader contract:
// https://github.com/EDCD/EDDN/blob/live/docs/Developers.md
// Message organization follows EDMarketConnector's plugins/eddn.py.

public sealed record EddnApplyRequest(
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    EliteStatus? Status,
    bool Enabled,
    bool UseTestSchemas,
    bool AllowPublishing,
    string? JournalDirectory = null,
    string? JournalPath = null,
    bool AllowSharedData = true);

public interface IEddnPublisher
{
    Task<EddnPublicationResult> ApplyAsync(
        EddnApplyRequest request,
        CancellationToken cancellationToken = default);

    void SetEnabled(bool enabled);

    void SetSuspended(bool suspended);
}

/// <summary>
/// Converts live Elite journal activity into schema-specific EDDN messages and
/// hands them to a single ordered persistence writer. Each message is committed
/// to the durable outbox before the outbox can send it, while journal projection
/// performs no queue-file I/O and awaits no network request.
/// </summary>
public sealed class EddnPublisher : IEddnPublisher, IDisposable
{
    private const string EventKey = "event";
    private const string FileheaderEvent = "Fileheader";
    private const string PartKey = "part";
    private const string CommanderEvent = "Commander";
    private const string LoadGameEvent = "LoadGame";
    private const string FsdJumpEvent = "FSDJump";
    private const string CarrierJumpEvent = "CarrierJump";
    private const string StartJumpEvent = "StartJump";
    private const string ApproachBodyEvent = "ApproachBody";
    private const string SupercruiseExitEvent = "SupercruiseExit";
    private const string LocationEvent = "Location";
    private const string HorizonsKey = "Horizons";
    private const string OdysseyKey = "Odyssey";
    private const string BodyNameKey = "BodyName";
    private const string BodyKey = "Body";
    private const string BodyIdKey = "BodyID";
    private const string BodyTypeKey = "BodyType";
    private const string PlanetBodyType = "Planet";
    private const string GameVersionKey = "gameversion";
    private const string BuildKey = "build";
    private const string NameKey = "Name";
    private const string CommanderNameKey = "Commander";

    private static readonly HashSet<string> JournalEvents = new(
        StringComparer.Ordinal)
    {
        "CodexEntry",
        "ApproachSettlement",
        "DockingGranted",
        "DockingDenied",
        "FSSAllBodiesFound",
        "FSSBodySignals",
        "FSSDiscoveryScan",
        "NavBeaconScan",
        "ScanBaryCentre",
        "Docked",
        "FSDJump",
        "CarrierJump",
        "Scan",
        "Location",
        "SAASignalsFound",
    };

    private readonly object sync = new();
    private readonly object companionTasksSync = new();
    private readonly EddnOutbox outbox;
    private readonly Channel<OutboxWriteCommand> outboxWrites;
    private readonly Task outboxWriterTask;
    private readonly Action<string> log;
    private readonly string softwareVersion;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly List<JObject> pendingSignals = [];
    private readonly Dictionary<string, string> stationSignatures = new(
        StringComparer.Ordinal);
    private readonly HashSet<Task> companionTasks = [];
    private UploadPayloadHeader? header;
    private EddnLocationContext? location;
    private EddnLocationContext? pendingSignalLocation;
    private string? currentJournalPath;
    private string? statusBodyName;
    private string? trackedBodyName;
    private int? trackedBodyId;
    private string? trackedBodyType;
    private bool? horizons;
    private bool? odyssey;
    private bool isCrewMember;
    private bool sharingEnabled;
    private bool publishingSuspended;
    private long sessionGeneration;
    private long consentGeneration;
    private int stagedWriteCount;
    private volatile bool acceptingWrites = true;
    private volatile bool disposing;
    private volatile bool disposed;

    public EddnPublisher(
        string softwareVersion,
        HttpClient? client = null,
        Uri? endpoint = null,
        string? outboxPath = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? utcNow = null,
        bool automaticProcessing = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.softwareVersion = softwareVersion.Trim();
        this.log = log ?? (_ => { });
        var transport = new EddnTransport(
            client,
            endpoint,
            $"SrvSurvey/{this.softwareVersion}");
        outbox = new EddnOutbox(
            outboxPath ?? Path.Combine(
                Path.GetTempPath(),
                "SrvSurvey",
                "eddn-outbox-v1.json"),
            transport,
            WriteLog,
            utcNow,
            automaticProcessing);
        outboxWrites = Channel.CreateBounded<OutboxWriteCommand>(
            new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        outboxWriterTask = RunOutboxWriterAsync();
    }

    public int PendingCount => outbox.pendingCount
        + Math.Max(0, Volatile.Read(ref stagedWriteCount));

    public void SetEnabled(bool enabled)
    {
        bool changed;
        lock (sync)
        {
            if (disposing || disposed)
            {
                return;
            }

            changed = sharingEnabled != enabled;
            sharingEnabled = enabled;
            if (changed && !enabled)
            {
                sessionGeneration++;
                consentGeneration++;
                ClearPendingSignals();
                stationSignatures.Clear();
            }
        }

        // EddnOutbox performs persistence and logging outside its own lock.
        // Calling it after releasing this lock prevents callback lock inversion.
        outbox.setEnabled(enabled, discardPendingWhenDisabled: !enabled);
    }

    public void SetSuspended(bool suspended)
    {
        lock (sync)
        {
            if (disposing || disposed || publishingSuspended == suspended)
            {
                return;
            }

            publishingSuspended = suspended;
            if (suspended)
            {
                sessionGeneration++;
                ClearPendingSignals();
                stationSignatures.Clear();
            }
        }

        // Suspension is operational, not consent: pending uploads remain on
        // disk and resume in order once commander attribution is unambiguous.
        outbox.setSuspended(suspended);
    }

    public Task<EddnPublicationResult> ApplyAsync(
        EddnApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        var journalEvents = request.JournalEvents;
        var status = request.Status;
        var enabled = request.Enabled;
        var useTestSchemas = request.UseTestSchemas;
        var allowPublishing = request.AllowPublishing;
        var journalDirectory = request.JournalDirectory;
        var journalPath = request.JournalPath;
        var allowSharedData = request.AllowSharedData;
        cancellationToken.ThrowIfCancellationRequested();
        if (disposing || disposed)
        {
            return Task.FromResult(EddnPublicationResult.Empty);
        }

        SetEnabled(enabled);
        var queued = new List<EddnPublishedEvent>();
        var warnings = new List<string>();
        bool suspended;
        lock (sync)
        {
            suspended = publishingSuspended;
        }

        if (enabled
            && allowPublishing
            && suspended
            && journalEvents.Count > 0)
        {
            warnings.Add(
                "EDDN sharing is paused while multiple Elite windows are active; pending uploads were preserved.");
        }

        foreach (var journalEvent in journalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryProcessJournalEvent(
                    journalEvent,
                    status,
                    enabled,
                    useTestSchemas,
                    allowPublishing,
                    journalDirectory,
                    journalPath,
                    allowSharedData,
                    queued,
                    warnings))
            {
                break;
            }
        }

        return Task.FromResult(new EddnPublicationResult(queued, warnings));
    }

    private bool TryProcessJournalEvent(
        JournalEventEnvelope journalEvent,
        EliteStatus? status,
        bool enabled,
        bool useTestSchemas,
        bool allowPublishing,
        string? journalDirectory,
        string? journalPath,
        bool allowSharedData,
        List<EddnPublishedEvent> queued,
        List<string> warnings)
    {
        JObject raw;
        try
        {
            raw = JObject.Parse(journalEvent.RawJson);
        }
        catch (JsonException exception)
        {
            warnings.Add(
                $"EDDN skipped {journalEvent.EventName}: {exception.Message}");
            return true;
        }

        QueueCandidate? signalBatch = null;
        QueueCandidate? journalCandidate = null;
        CompanionCandidate? companionCandidate = null;
        string? skipReason = null;
        var disposedDuringProcessing = false;

        lock (sync)
        {
            if (disposed)
            {
                disposedDuringProcessing = true;
            }
            else
            {
                BeginJournalSessionIfNeeded(journalPath, raw);
                statusBodyName = allowSharedData
                    && !string.IsNullOrWhiteSpace(status?.BodyName)
                        ? status.BodyName
                        : null;
                UpdateHeader(raw);

                var eventName = journalEvent.EventName;
                var eventLocation = EddnMessageSanitizer.getLocation(raw);
                signalBatch = TryFlushPendingSignals(
                    eventName,
                    eventLocation,
                    enabled,
                    useTestSchemas,
                    allowPublishing,
                    warnings);

                if (eventLocation is not null)
                {
                    location = eventLocation;
                    ClearTrackedBody();
                }

                UpdateBodyContext(raw);
                UpdateExpansionFlags(raw);
                UpdateCrewMembership(eventName);
                (journalCandidate, companionCandidate, skipReason) =
                    BuildPublicationCandidates(
                        eventName,
                        raw,
                        enabled,
                        useTestSchemas,
                        allowPublishing,
                        journalDirectory,
                        allowSharedData);
            }
        }

        if (disposedDuringProcessing)
        {
            return false;
        }

        if (signalBatch is not null)
        {
            TryQueue(signalBatch, queued, warnings);
        }

        if (journalCandidate is not null)
        {
            TryQueue(journalCandidate, queued, warnings);
        }

        if (companionCandidate is not null)
        {
            StartCompanionRead(companionCandidate);
        }
        else if (skipReason is not null)
        {
            warnings.Add(
                $"EDDN skipped {journalEvent.EventName}: {skipReason}.");
        }

        return true;
    }

    private QueueCandidate? TryFlushPendingSignals(
        string eventName,
        EddnLocationContext? eventLocation,
        bool enabled,
        bool useTestSchemas,
        bool allowPublishing,
        List<string> warnings)
    {
        if (eventName == "FSSSignalDiscovered" || pendingSignals.Count == 0)
        {
            return null;
        }

        var suppressBatchForCrew = isCrewMember;
        var batchLocation = pendingSignalLocation;
        if (batchLocation is null
            && eventLocation is not null
            && pendingSignals.All(signal =>
                signal.Value<long?>("SystemAddress")
                    == eventLocation.systemAddress))
        {
            batchLocation = eventLocation;
        }

        QueueCandidate? signalBatch = null;
        if (EddnMessageSanitizer.tryBuildSignalBatch(
            pendingSignals,
            batchLocation,
            horizons,
            odyssey,
            out var preparedBatch,
            out var batchReason))
        {
            if (HasUsableHeader()
                && enabled
                && allowPublishing
                && !publishingSuspended
                && !suppressBatchForCrew)
            {
                signalBatch = new QueueCandidate(
                    preparedBatch!,
                    header!.clone(),
                    useTestSchemas,
                    sessionGeneration);
            }
        }
        else if (batchReason != "no public signals remained after filtering")
        {
            warnings.Add(
                "EDDN skipped FSSSignalDiscovered batch: " + batchReason);
        }

        ClearPendingSignals();
        return signalBatch;
    }

    private void UpdateCrewMembership(string eventName)
    {
        if (eventName == "JoinACrew")
        {
            isCrewMember = true;
        }
        else if (eventName is "QuitACrew" or "LoadGame")
        {
            isCrewMember = false;
        }
    }

    private (QueueCandidate? Journal, CompanionCandidate? Companion, string? SkipReason)
        BuildPublicationCandidates(
            string eventName,
            JObject raw,
            bool enabled,
            bool useTestSchemas,
            bool allowPublishing,
            string? journalDirectory,
            bool allowSharedData)
    {
        if (eventName == "FSSSignalDiscovered")
        {
            if (enabled
                && allowPublishing
                && !publishingSuspended
                && !isCrewMember
                && HasUsableHeader())
            {
                pendingSignalLocation ??= location;
                pendingSignals.Add(new JObject(raw));
            }

            return (null, null, null);
        }

        if (!enabled
            || !allowPublishing
            || publishingSuspended
            || isCrewMember
            || !HasUsableHeader())
        {
            return (null, null, null);
        }

        var context = CreateContext();
        if (EddnMessageSanitizer.isCompanionEvent(eventName))
        {
            if (!allowSharedData)
            {
                return (null, null,
                    "shared companion files are suppressed while multiple Elite instances are active");
            }

            if (string.IsNullOrWhiteSpace(journalDirectory))
            {
                return (null, null, "the journal directory was unavailable");
            }

            return (null, new CompanionCandidate(
                new JObject(raw),
                context,
                header!.clone(),
                useTestSchemas,
                sessionGeneration,
                journalDirectory), null);
        }

        if (!JournalEvents.Contains(eventName))
        {
            return (null, null, null);
        }

        if (EddnMessageSanitizer.tryBuildJournal(
            raw,
            context,
            out var prepared,
            out var reason))
        {
            return (new QueueCandidate(
                prepared!,
                header!.clone(),
                useTestSchemas,
                sessionGeneration), null, null);
        }

        return (null, null, reason);
    }

    public async Task ProcessPendingAsync(
        CancellationToken cancellationToken = default)
    {
        await FlushOutboxWritesAsync(cancellationToken).ConfigureAwait(false);
        await outbox.processDue(cancellationToken).ConfigureAwait(false);
    }

    public async Task WaitForCompanionReadsAsync(
        CancellationToken cancellationToken = default)
    {
        Task[] tasks;
        lock (companionTasksSync)
        {
            tasks = companionTasks.ToArray();
        }

        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Companion tasks stage durable outbox writes. Waiting for the reads
        // alone can observe the same item once in the outbox and once in the
        // staged-write count while the ordered writer finishes its command.
        await FlushOutboxWritesAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Task[] companionReads;
        lock (companionTasksSync)
        {
            lock (sync)
            {
                if (disposing || disposed)
                {
                    return;
                }

                disposing = true;
                acceptingWrites = false;
            }

            companionReads = companionTasks.ToArray();
        }

        lifetimeCancellation.Cancel();
        try
        {
            Task.WhenAll(companionReads).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteLog(
                "EDDN companion-file processing stopped during shutdown: "
                    + exception.GetBaseException().Message);
        }

        outboxWrites.Writer.TryComplete();
        try
        {
            outboxWriterTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteLog(
                "EDDN queue writer stopped during shutdown: "
                    + exception.GetBaseException().Message);
        }

        lock (sync)
        {
            disposed = true;
            sharingEnabled = false;
            sessionGeneration++;
            ClearPendingSignals();
            stationSignatures.Clear();
        }

        outbox.Dispose();
        lifetimeCancellation.Dispose();
    }

    private void BeginJournalSessionIfNeeded(string? journalPath, JObject raw)
    {
        var eventName = raw.Value<string>(EventKey);
        var normalizedJournalPath = string.IsNullOrWhiteSpace(journalPath)
            ? null
            : Path.GetFullPath(journalPath);
        var pathChanged = normalizedJournalPath is not null
            && currentJournalPath is not null
            && !string.Equals(
                normalizedJournalPath,
                currentJournalPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        if (!pathChanged && eventName != FileheaderEvent)
        {
            currentJournalPath ??= normalizedJournalPath;
            return;
        }

        var isContinuedPart = eventName == FileheaderEvent
            && raw.Value<int?>(PartKey) is > 1
            && HasUsableHeader()
            && (!pathChanged
                || IsSameJournalSeries(
                    currentJournalPath,
                    normalizedJournalPath));
        if (isContinuedPart)
        {
            currentJournalPath = normalizedJournalPath ?? currentJournalPath;
            return;
        }

        sessionGeneration++;
        currentJournalPath = normalizedJournalPath ?? currentJournalPath;
        header = null;
        location = null;
        statusBodyName = null;
        ClearTrackedBody();
        horizons = null;
        odyssey = null;
        isCrewMember = false;
        ClearPendingSignals();
        stationSignatures.Clear();
    }

    private void UpdateHeader(JObject raw)
    {
        var eventName = raw.Value<string>(EventKey);
        if (eventName == FileheaderEvent)
        {
            header = new UploadPayloadHeader(
                header?.uploaderID ?? string.Empty,
                raw.Value<string>(GameVersionKey),
                raw.Value<string>(BuildKey),
                softwareVersion);
        }
        else if (eventName == CommanderEvent)
        {
            var commander = raw.Value<string>(NameKey);
            if (!string.IsNullOrWhiteSpace(commander))
            {
                header = new UploadPayloadHeader(
                    commander,
                    header?.gameversion,
                    header?.gamebuild,
                    softwareVersion);
            }
        }
        else if (eventName == LoadGameEvent)
        {
            var commander = raw.Value<string>(CommanderNameKey);
            if (!string.IsNullOrWhiteSpace(commander))
            {
                header = new UploadPayloadHeader(
                    commander,
                    header?.gameversion ?? raw.Value<string>(GameVersionKey),
                    header?.gamebuild ?? raw.Value<string>(BuildKey),
                    softwareVersion);
            }
        }
    }

    private void UpdateExpansionFlags(JObject raw)
    {
        if (raw.Value<string>(EventKey) is not (FileheaderEvent or LoadGameEvent))
        {
            return;
        }

        horizons = raw.Value<bool?>(HorizonsKey) ?? horizons;
        odyssey = raw.Value<bool?>(OdysseyKey) ?? odyssey;
    }

    private void UpdateBodyContext(JObject raw)
    {
        var eventName = raw.Value<string>(EventKey);
        if (eventName is FsdJumpEvent or CarrierJumpEvent or StartJumpEvent)
        {
            ClearTrackedBody();
            return;
        }

        if (eventName is ApproachBodyEvent or SupercruiseExitEvent or LocationEvent)
        {
            var bodyName = raw.Value<string>(BodyNameKey)
                ?? raw.Value<string>(BodyKey);
            var bodyId = raw.Value<int?>(BodyIdKey);
            if (!string.IsNullOrWhiteSpace(bodyName) && bodyId is >= 0)
            {
                trackedBodyName = bodyName;
                trackedBodyId = bodyId;
                trackedBodyType = raw.Value<string>(BodyTypeKey) ?? PlanetBodyType;
            }
        }
    }

    private void ClearTrackedBody()
    {
        trackedBodyName = null;
        trackedBodyId = null;
        trackedBodyType = null;
    }

    private bool HasUsableHeader()
    {
        return header is not null
            && !string.IsNullOrWhiteSpace(header.uploaderID);
    }

    private static bool IsSameJournalSeries(
        string? currentPath,
        string? nextPath)
    {
        var currentSeries = GetJournalSeriesPath(currentPath);
        var nextSeries = GetJournalSeriesPath(nextPath);
        return currentSeries is not null
            && nextSeries is not null
            && string.Equals(
                currentSeries,
                nextSeries,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }

    private static string? GetJournalSeriesPath(string? journalPath)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
        {
            return null;
        }

        var filenameParts = Path.GetFileName(journalPath).Split('.');
        if (filenameParts.Length < 4
            || !int.TryParse(filenameParts[^2], out _))
        {
            return null;
        }

        var seriesName = string.Join('.', filenameParts[..^2])
            + "."
            + filenameParts[^1];
        return Path.Combine(
            Path.GetDirectoryName(journalPath) ?? string.Empty,
            seriesName);
    }

    private EddnMessageContext CreateContext()
    {
        return new EddnMessageContext(
            location,
            horizons,
            odyssey,
            statusBodyName,
            trackedBodyName,
            trackedBodyId,
            trackedBodyType);
    }

    private void TryQueue(
        QueueCandidate candidate,
        List<EddnPublishedEvent> queued,
        List<string> warnings)
    {
        if (!IsCurrentSession(candidate.Generation))
        {
            return;
        }

        var item = EddnTransport.prepare(
            candidate.Prepared.message,
            candidate.Prepared.schemaRef,
            candidate.Header,
            candidate.UseTestSchemas);
        if (TryStageOutboxWrite(
            item,
            candidate.Generation,
            candidate.Prepared.eventName))
        {
            queued.Add(new EddnPublishedEvent(
                candidate.Prepared.eventName,
                item.schemaRef,
                item.useTestSchemas));
        }
        else if (IsCurrentSession(candidate.Generation))
        {
            warnings.Add(
                $"EDDN could not queue {candidate.Prepared.eventName} for upload.");
        }
    }

    private void StartCompanionRead(CompanionCandidate candidate)
    {
        lock (companionTasksSync)
        {
            CancellationToken cancellationToken;
            lock (sync)
            {
                if (disposing || disposed)
                {
                    return;
                }

                cancellationToken = lifetimeCancellation.Token;
            }

            var task = ProcessCompanionFileAsync(
                candidate,
                cancellationToken);
            companionTasks.Add(task);
            _ = task.ContinueWith(
                completed => CompleteCompanionTask(completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void CompleteCompanionTask(Task task)
    {
        lock (companionTasksSync)
        {
            companionTasks.Remove(task);
        }

        if (task.IsFaulted)
        {
            WriteLog(
                "EDDN companion-file processing failed safely: "
                    + task.Exception?.GetBaseException().Message);
        }
    }

    private async Task ProcessCompanionFileAsync(
        CompanionCandidate candidate,
        CancellationToken cancellationToken)
    {
        var eventName = candidate.JournalEvent.Value<string>("event")
            ?? "companion file";
        try
        {
            var read = await EddnCompanionFileReader.read(
                candidate.JournalDirectory,
                candidate.JournalEvent,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!read.isSuccess)
            {
                if (IsCurrentSession(candidate.Generation))
                {
                    WriteLog($"EDDN skipped {eventName}: {read.error}");
                }

                return;
            }

            if (!IsCurrentSession(candidate.Generation))
            {
                return;
            }

            if (!EddnMessageSanitizer.tryBuildCompanion(
                read.content!,
                candidate.Context,
                out var prepared,
                out var reason))
            {
                WriteLog($"EDDN skipped {eventName}: {reason}");
                return;
            }

            if (!TryReserveStationSignature(
                prepared!,
                candidate.Generation,
                out var signatureKey,
                out var signature))
            {
                return;
            }

            var item = EddnTransport.prepare(
                prepared!.message,
                prepared.schemaRef,
                candidate.Header,
                candidate.UseTestSchemas);
            if (!TryStageOutboxWrite(
                item,
                candidate.Generation,
                eventName,
                () => ReleaseStationSignature(signatureKey, signature)))
            {
                ReleaseStationSignature(signatureKey, signature);
                if (IsCurrentSession(candidate.Generation))
                {
                    WriteLog($"EDDN could not queue {eventName} for upload.");
                }
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Session replacement, opt-out, and application shutdown are all
            // expected cancellation paths.
        }
        catch (Exception exception) when (
            exception is IOException
                or JsonException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            if (IsCurrentSession(candidate.Generation))
            {
                WriteLog($"EDDN skipped {eventName}: {exception.Message}");
            }
        }
    }

    private bool TryReserveStationSignature(
        EddnPreparedMessage prepared,
        long generation,
        out string? key,
        out string? signature)
    {
        key = null;
        signature = null;
        lock (sync)
        {
            if (!IsCurrentSessionLocked(generation))
            {
                return false;
            }

            if (prepared.eventName == "NavRoute")
            {
                return true;
            }

            var marketId = prepared.message.Value<long?>("marketId")
                ?? prepared.message.Value<long?>("MarketID")
                ?? 0;
            key = prepared.schemaRef + ":" + marketId;
            var comparable = new JObject(prepared.message);
            comparable.Remove("timestamp");
            signature = comparable.ToString(Formatting.None);
            if (stationSignatures.GetValueOrDefault(key) == signature)
            {
                return false;
            }

            stationSignatures[key] = signature;
            return true;
        }
    }

    private void ReleaseStationSignature(string? key, string? signature)
    {
        if (key is null || signature is null)
        {
            return;
        }

        lock (sync)
        {
            if (stationSignatures.GetValueOrDefault(key) == signature)
            {
                stationSignatures.Remove(key);
            }
        }
    }

    private bool TryStageOutboxWrite(
        EddnQueuedMessage item,
        long generation,
        string eventName,
        Action? rejected = null)
    {
        long acceptedConsentGeneration;
        lock (sync)
        {
            if (!acceptingWrites || !IsCurrentSessionLocked(generation))
            {
                return false;
            }

            acceptedConsentGeneration = consentGeneration;
        }

        Interlocked.Increment(ref stagedWriteCount);
        if (outboxWrites.Writer.TryWrite(new PersistOutboxWrite(
            item,
            acceptedConsentGeneration,
            eventName,
            rejected)))
        {
            return true;
        }

        Interlocked.Decrement(ref stagedWriteCount);
        return false;
    }

    private async Task RunOutboxWriterAsync()
    {
        await foreach (var command in outboxWrites.Reader
                           .ReadAllAsync(CancellationToken.None)
                           .ConfigureAwait(false))
        {
            if (command is FlushOutboxWrites flush)
            {
                flush.Completion.TrySetResult();
                continue;
            }

            var write = (PersistOutboxWrite)command;
            var persisted = false;
            try
            {
                persisted = IsConsentCurrent(write.ConsentGeneration)
                    && outbox.enqueue(
                        write.Item,
                        allowWhileSuspended: true);
                if (!persisted)
                {
                    write.Rejected?.Invoke();
                    if (IsConsentCurrent(write.ConsentGeneration))
                    {
                        WriteLog(
                            $"EDDN could not persist {write.EventName} for upload.");
                    }
                }
            }
            catch (Exception exception)
            {
                write.Rejected?.Invoke();
                WriteLog(
                    $"EDDN could not persist {write.EventName} for upload: "
                        + exception.Message);
            }
            finally
            {
                Interlocked.Decrement(ref stagedWriteCount);
            }
        }
    }

    private async Task FlushOutboxWritesAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await outboxWrites.Writer.WriteAsync(
                new FlushOutboxWrites(completion),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            await outboxWriterTask.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await completion.Task.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsConsentCurrent(long generation)
    {
        lock (sync)
        {
            return !disposed
                && sharingEnabled
                && consentGeneration == generation;
        }
    }

    private void ClearPendingSignals()
    {
        pendingSignals.Clear();
        pendingSignalLocation = null;
    }

    private bool IsCurrentSession(long generation)
    {
        lock (sync)
        {
            return IsCurrentSessionLocked(generation);
        }
    }

    private bool IsCurrentSessionLocked(long generation)
    {
        return !disposed
            && sharingEnabled
            && !publishingSuspended
            && sessionGeneration == generation;
    }

    private void WriteLog(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            log(message);
        }
        catch
        {
            // Diagnostics must never stop journal processing or the outbox.
        }
    }

    private sealed record QueueCandidate(
        EddnPreparedMessage Prepared,
        UploadPayloadHeader Header,
        bool UseTestSchemas,
        long Generation);

    private sealed record CompanionCandidate(
        JObject JournalEvent,
        EddnMessageContext Context,
        UploadPayloadHeader Header,
        bool UseTestSchemas,
        long Generation,
        string JournalDirectory);

    private abstract record OutboxWriteCommand;

    private sealed record PersistOutboxWrite(
        EddnQueuedMessage Item,
        long ConsentGeneration,
        string EventName,
        Action? Rejected) : OutboxWriteCommand;

    private sealed record FlushOutboxWrites(
        TaskCompletionSource Completion) : OutboxWriteCommand;
}

public sealed record EddnPublishedEvent(
    string EventName,
    string SchemaReference,
    bool UsesTestSchemas);

public sealed record EddnPublicationResult(
    IReadOnlyList<EddnPublishedEvent> Published,
    IReadOnlyList<string> Warnings)
{
    public static EddnPublicationResult Empty { get; } = new([], []);
}
