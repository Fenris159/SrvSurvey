using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using System.Threading.Channels;

namespace SrvSurvey.Core.Network;

// EDDN uploader contract:
// https://github.com/EDCD/EDDN/blob/live/docs/Developers.md
// Message organization follows EDMarketConnector's plugins/eddn.py.

public interface IEddnPublisher
{
    Task<EddnPublicationResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool enabled,
        bool useTestSchemas,
        bool allowPublishing,
        string? journalDirectory = null,
        string? journalPath = null,
        bool allowSharedData = true,
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
    private readonly EddnTransport transport;
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
        transport = new EddnTransport(
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
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool enabled,
        bool useTestSchemas,
        bool allowPublishing,
        string? journalDirectory = null,
        string? journalPath = null,
        bool allowSharedData = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
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
            JObject raw;
            try
            {
                raw = JObject.Parse(journalEvent.RawJson);
            }
            catch (JsonException exception)
            {
                warnings.Add(
                    $"EDDN skipped {journalEvent.EventName}: {exception.Message}");
                continue;
            }

            QueueCandidate? signalBatch = null;
            QueueCandidate? journalCandidate = null;
            CompanionCandidate? companionCandidate = null;
            string? skipReason = null;

            lock (sync)
            {
                if (disposed)
                {
                    break;
                }

                BeginJournalSessionIfNeeded(journalPath, raw);
                statusBodyName = allowSharedData
                    && !string.IsNullOrWhiteSpace(status?.BodyName)
                        ? status.BodyName
                        : null;
                UpdateHeader(raw);

                var eventName = journalEvent.EventName;
                var eventLocation = EddnMessageSanitizer.getLocation(raw);
                var suppressBatchForCrew = isCrewMember;
                if (eventName != "FSSSignalDiscovered"
                    && pendingSignals.Count > 0)
                {
                    var batchLocation = pendingSignalLocation;
                    if (batchLocation is null
                        && eventLocation is not null
                        && pendingSignals.All(signal =>
                            signal.Value<long?>("SystemAddress")
                                == eventLocation.systemAddress))
                    {
                        batchLocation = eventLocation;
                    }

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
                    else if (batchReason
                        != "no public signals remained after filtering")
                    {
                        warnings.Add(
                            "EDDN skipped FSSSignalDiscovered batch: "
                                + batchReason);
                    }

                    ClearPendingSignals();
                }

                if (eventLocation is not null)
                {
                    location = eventLocation;
                    ClearTrackedBody();
                }

                UpdateBodyContext(raw);
                UpdateExpansionFlags(raw);
                if (eventName == "JoinACrew")
                {
                    isCrewMember = true;
                }
                else if (eventName is "QuitACrew" or "LoadGame")
                {
                    isCrewMember = false;
                }

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
                }
                else if (enabled
                    && allowPublishing
                    && !publishingSuspended
                    && !isCrewMember
                    && HasUsableHeader())
                {
                    var context = CreateContext();
                    if (EddnMessageSanitizer.isCompanionEvent(eventName))
                    {
                        if (!allowSharedData)
                        {
                            skipReason =
                                "shared companion files are suppressed while multiple Elite instances are active";
                        }
                        else if (string.IsNullOrWhiteSpace(journalDirectory))
                        {
                            skipReason = "the journal directory was unavailable";
                        }
                        else
                        {
                            companionCandidate = new CompanionCandidate(
                                new JObject(raw),
                                context,
                                header!.clone(),
                                useTestSchemas,
                                sessionGeneration,
                                journalDirectory);
                        }
                    }
                    else if (JournalEvents.Contains(eventName))
                    {
                        if (EddnMessageSanitizer.tryBuildJournal(
                            raw,
                            context,
                            out var prepared,
                            out var reason))
                        {
                            journalCandidate = new QueueCandidate(
                                prepared!,
                                header!.clone(),
                                useTestSchemas,
                                sessionGeneration);
                        }
                        else
                        {
                            skipReason = reason;
                        }
                    }
                }
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
        }

        return Task.FromResult(new EddnPublicationResult(queued, warnings));
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
        var eventName = raw.Value<string>("event");
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
        if (!pathChanged && eventName != "Fileheader")
        {
            currentJournalPath ??= normalizedJournalPath;
            return;
        }

        var isContinuedPart = eventName == "Fileheader"
            && raw.Value<int?>("part") is > 1
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
        var eventName = raw.Value<string>("event");
        if (eventName == "Fileheader")
        {
            header = new UploadPayloadHeader(
                header?.uploaderID ?? string.Empty,
                raw.Value<string>("gameversion"),
                raw.Value<string>("build"),
                softwareVersion);
        }
        else if (eventName == "Commander")
        {
            var commander = raw.Value<string>("Name");
            if (!string.IsNullOrWhiteSpace(commander))
            {
                header = new UploadPayloadHeader(
                    commander,
                    header?.gameversion,
                    header?.gamebuild,
                    softwareVersion);
            }
        }
        else if (eventName == "LoadGame")
        {
            var commander = raw.Value<string>("Commander");
            if (!string.IsNullOrWhiteSpace(commander))
            {
                header = new UploadPayloadHeader(
                    commander,
                    header?.gameversion ?? raw.Value<string>("gameversion"),
                    header?.gamebuild ?? raw.Value<string>("build"),
                    softwareVersion);
            }
        }
    }

    private void UpdateExpansionFlags(JObject raw)
    {
        if (raw.Value<string>("event") is not ("Fileheader" or "LoadGame"))
        {
            return;
        }

        horizons = raw.Value<bool?>("Horizons") ?? horizons;
        odyssey = raw.Value<bool?>("Odyssey") ?? odyssey;
    }

    private void UpdateBodyContext(JObject raw)
    {
        var eventName = raw.Value<string>("event");
        if (eventName is "FSDJump" or "CarrierJump" or "StartJump")
        {
            ClearTrackedBody();
            return;
        }

        if (eventName is "ApproachBody" or "SupercruiseExit" or "Location")
        {
            var bodyName = raw.Value<string>("BodyName")
                ?? raw.Value<string>("Body");
            var bodyId = raw.Value<int?>("BodyID");
            if (!string.IsNullOrWhiteSpace(bodyName) && bodyId is >= 0)
            {
                trackedBodyName = bodyName;
                trackedBodyId = bodyId;
                trackedBodyType = raw.Value<string>("BodyType") ?? "Planet";
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

        var item = transport.prepare(
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

            var item = transport.prepare(
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
                           .ReadAllAsync()
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
