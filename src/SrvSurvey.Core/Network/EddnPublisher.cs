using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;

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
        string environment,
        bool allowPublishing,
        string? journalDirectory = null,
        string? journalPath = null,
        bool allowSharedData = true,
        CancellationToken cancellationToken = default);

    void SetEnabled(bool enabled);
}

/// <summary>
/// Converts live Elite journal activity into schema-specific EDDN messages and
/// commits those messages to a durable sender queue. No network request is
/// awaited by the journal projection path.
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
    private readonly Action<string> log;
    private readonly string softwareVersion;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly List<JObject> pendingSignals = [];
    private readonly Dictionary<string, string> stationSignatures = new(
        StringComparer.Ordinal);
    private readonly HashSet<Task> companionTasks = [];
    private UploadPayloadHeader? header;
    private EddnLocationContext? location;
    private string? currentJournalPath;
    private string? statusBodyName;
    private string? trackedBodyName;
    private int? trackedBodyId;
    private string? trackedBodyType;
    private bool? horizons;
    private bool? odyssey;
    private bool isCrewMember;
    private bool sharingEnabled;
    private long sessionGeneration;
    private volatile bool disposed;

    public EddnPublisher(
        string softwareVersion,
        HttpClient? client = null,
        IReadOnlyDictionary<string, Uri>? endpoints = null,
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
            endpoints,
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
    }

    public int PendingCount => outbox.pendingCount;

    public static string NormalizeEnvironment(string? value)
    {
        return EddnTransport.normalizeEnvironment(value);
    }

    public void SetEnabled(bool enabled)
    {
        bool changed;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            changed = sharingEnabled != enabled;
            sharingEnabled = enabled;
            if (changed && !enabled)
            {
                sessionGeneration++;
                pendingSignals.Clear();
                stationSignatures.Clear();
            }
        }

        // EddnOutbox performs persistence and logging outside its own lock.
        // Calling it after releasing this lock prevents callback lock inversion.
        outbox.setEnabled(enabled, discardPendingWhenDisabled: !enabled);
    }

    public Task<EddnPublicationResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        EliteStatus? status,
        bool enabled,
        string environment,
        bool allowPublishing,
        string? journalDirectory = null,
        string? journalPath = null,
        bool allowSharedData = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed)
        {
            return Task.FromResult(EddnPublicationResult.Empty);
        }

        SetEnabled(enabled);
        var queued = new List<EddnPublishedEvent>();
        var warnings = new List<string>();

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
                    var batchLocation = eventLocation ?? location;
                    if (EddnMessageSanitizer.tryBuildSignalBatch(
                        pendingSignals,
                        batchLocation,
                        horizons,
                        odyssey,
                        out var preparedBatch,
                        out var batchReason))
                    {
                        if (header is not null
                            && enabled
                            && allowPublishing
                            && !suppressBatchForCrew)
                        {
                            signalBatch = new QueueCandidate(
                                preparedBatch!,
                                header.clone(),
                                environment,
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

                    pendingSignals.Clear();
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
                        && !isCrewMember
                        && header is not null)
                    {
                        pendingSignals.Add(new JObject(raw));
                    }
                }
                else if (enabled
                    && allowPublishing
                    && !isCrewMember
                    && header is not null)
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
                                header.clone(),
                                environment,
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
                                header.clone(),
                                environment,
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

    public Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        return outbox.processDue(cancellationToken);
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
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            sharingEnabled = false;
            sessionGeneration++;
            pendingSignals.Clear();
            stationSignatures.Clear();
        }

        lifetimeCancellation.Cancel();
        outbox.Dispose();

        // Companion continuations can still observe cancellation and outbox
        // workers can still release their semaphore. The CTS is intentionally
        // left for GC so shutdown never disposes a primitive beneath a worker.
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

        sessionGeneration++;
        currentJournalPath = normalizedJournalPath ?? currentJournalPath;
        header = null;
        location = null;
        statusBodyName = null;
        ClearTrackedBody();
        horizons = null;
        odyssey = null;
        isCrewMember = false;
        pendingSignals.Clear();
        stationSignatures.Clear();
    }

    private void UpdateHeader(JObject raw)
    {
        var eventName = raw.Value<string>("event");
        if (eventName == "Fileheader")
        {
            header = new UploadPayloadHeader(
                string.Empty,
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
            candidate.Environment);
        if (outbox.enqueue(item))
        {
            queued.Add(new EddnPublishedEvent(
                candidate.Prepared.eventName,
                item.schemaRef,
                item.environment));
        }
        else if (IsCurrentSession(candidate.Generation))
        {
            warnings.Add(
                $"EDDN could not queue {candidate.Prepared.eventName} for upload.");
        }
    }

    private void StartCompanionRead(CompanionCandidate candidate)
    {
        var task = ProcessCompanionFileAsync(
            candidate,
            lifetimeCancellation.Token);
        lock (companionTasksSync)
        {
            companionTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed => CompleteCompanionTask(completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
                candidate.Environment);
            if (!outbox.enqueue(item))
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
        string Environment,
        long Generation);

    private sealed record CompanionCandidate(
        JObject JournalEvent,
        EddnMessageContext Context,
        UploadPayloadHeader Header,
        string Environment,
        long Generation,
        string JournalDirectory);
}

public sealed record EddnPublishedEvent(
    string EventName,
    string SchemaReference,
    string Environment);

public sealed record EddnPublicationResult(
    IReadOnlyList<EddnPublishedEvent> Published,
    IReadOnlyList<string> Warnings)
{
    public static EddnPublicationResult Empty { get; } = new([], []);
}
