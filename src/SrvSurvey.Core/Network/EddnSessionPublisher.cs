using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SrvSurvey.Core.Network;

// EDDN contract: https://github.com/EDCD/EDDN/blob/live/docs/Developers.md
// Event selection, companion validation, and signal batching follow the proven
// EDMarketConnector implementation in plugins/eddn.py.
/// <summary>
/// Publishes one immutable Commander journal session. The application publisher
/// replaces and disposes this module when journal series or Commander changes.
/// </summary>
internal sealed class EddnSessionPublisher : IDisposable
{
    private const string EventProperty = "event";
    private const string PlanetBodyType = "Planet";

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
    private readonly object enqueueSync = new();
    private readonly IEddnSessionSink sink;
    private readonly UploadPayloadHeader header;
    private readonly string? journalDirectory;
    private readonly Action<string> log;
    private readonly Func<string, JObject, CancellationToken,
        Task<EddnCompanionReadResult>> companionReader;
    private readonly CancellationTokenSource disposal = new();
    private CancellationTokenSource companionActivity = new();
    private readonly List<JObject> pendingSignals = [];
    private readonly Dictionary<string, string> stationSignatures = new(
        StringComparer.Ordinal);
    private readonly HashSet<Task> companionTasks = [];
    private EddnSignalBatchContext? pendingSignalContext;
    private EddnLocationContext? location;
    private string? statusBodyName;
    private string? trackedBodyName;
    private int? trackedBodyId;
    private string? trackedBodyType;
    private bool? horizons;
    private bool? odyssey;
    private bool isCrewMember;
    private bool sharingEnabled;
    private bool publishingSuspended;
    private int disposeStarted;
    private bool accepting = true;
    private bool disposed;
    private long sessionGeneration;

    internal EddnSessionPublisher(
        IEddnSessionSink sink,
        UploadPayloadHeader header,
        string? journalDirectory,
        Action<string>? log = null,
        Func<string, JObject, CancellationToken,
            Task<EddnCompanionReadResult>>? companionReader = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(header);
        if (string.IsNullOrWhiteSpace(header.uploaderID))
        {
            throw new ArgumentException(
                "An EDDN session requires a Commander name.",
                nameof(header));
        }

        this.sink = sink;
        this.header = header.clone();
        this.journalDirectory = journalDirectory;
        this.log = log ?? (_ => { });
        this.companionReader = companionReader
            ?? ((folder, journalEvent, cancellationToken) =>
                EddnCompanionFileReader.read(
                    folder,
                    journalEvent,
                    cancellationToken: cancellationToken));
    }

    internal string Commander => header.uploaderID;

    internal void SetEnabled(bool enabled)
    {
        var cancelCompanionReads = false;
        lock (sync)
        {
            if (disposed || sharingEnabled == enabled)
            {
                return;
            }

            sharingEnabled = enabled;
            sessionGeneration++;
            if (!enabled)
            {
                ClearTransientStateLocked();
                cancelCompanionReads = true;
            }
        }

        if (cancelCompanionReads)
        {
            ResetCompanionActivity();
        }
    }

    internal void SetSuspended(bool suspended)
    {
        var cancelCompanionReads = false;
        lock (sync)
        {
            if (disposed || publishingSuspended == suspended)
            {
                return;
            }

            publishingSuspended = suspended;
            sessionGeneration++;
            if (suspended)
            {
                ClearTransientStateLocked();
                cancelCompanionReads = true;
            }
        }

        if (cancelCompanionReads)
        {
            ResetCompanionActivity();
        }
    }

    internal EddnPublicationResult Apply(
        EddnApplyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var published = new List<EddnPublishedEvent>();
        var warnings = new List<string>();

        bool suspended;
        lock (sync)
        {
            suspended = publishingSuspended;
        }

        if (request.Enabled
            && request.AllowPublishing
            && suspended
            && request.JournalEvents.Count > 0)
        {
            warnings.Add(
                "EDDN sharing is paused while multiple Elite windows are active; pending uploads were preserved.");
        }

        foreach (var journalEvent in request.JournalEvents)
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

            if (!ProcessJournalEvent(
                    journalEvent.EventName,
                    raw,
                    request,
                    published,
                    warnings))
            {
                break;
            }
        }

        return new EddnPublicationResult(published, warnings);
    }

    internal async Task WaitForCompanionReadsAsync(
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
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        SignalBatch? batch;
        lock (enqueueSync)
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                batch = TakeSignalBatchLocked();
                disposed = true;
                accepting = false;
                stationSignatures.Clear();
            }

            disposal.Cancel();
        }

        Task[] tasks;
        lock (companionTasksSync)
        {
            tasks = companionTasks.ToArray();
        }

        try
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            WriteLog(
                "EDDN companion-file processing stopped during session shutdown: "
                    + exception.GetBaseException().Message);
        }

        if (batch is not null)
        {
            PublishSignalBatch(batch, null, null, allowDisposedBatch: true);
        }

        disposal.Dispose();
        lock (companionTasksSync)
        {
            companionActivity.Dispose();
        }
    }

    private bool ProcessJournalEvent(
        string eventName,
        JObject raw,
        EddnApplyRequest request,
        List<EddnPublishedEvent> published,
        List<string> warnings)
    {
        if (!MatchesCapturedCommander(eventName, raw, warnings))
        {
            return false;
        }

        var captured = CaptureEventState(eventName, raw, request);
        if (captured is null)
        {
            return false;
        }

        if (captured.SignalBatch is not null)
        {
            PublishSignalBatch(captured.SignalBatch, published, warnings);
        }

        if (eventName == "FSSSignalDiscovered")
        {
            BufferSignal(
                raw,
                captured.Context,
                request.AllowPublishing,
                captured.SessionGeneration);
            return true;
        }

        if (captured.SuppressForCrew
            || !CanPublishNow(
                request.AllowPublishing,
                captured.SessionGeneration,
                out var ingestionGeneration))
        {
            return true;
        }

        if (EddnMessageSanitizer.isCompanionEvent(eventName))
        {
            return ProcessCompanionEvent(
                eventName,
                raw,
                request,
                captured,
                ingestionGeneration,
                warnings);
        }

        if (JournalEvents.Contains(eventName))
        {
            PublishJournalEvent(
                eventName,
                raw,
                captured,
                ingestionGeneration,
                published,
                warnings);
        }

        return true;
    }

    private bool MatchesCapturedCommander(
        string eventName,
        JObject raw,
        List<string> warnings)
    {
        if (eventName != "LoadGame")
        {
            return true;
        }

        var eventCommander = raw.Value<string>("Commander");
        if (string.IsNullOrWhiteSpace(eventCommander)
            || eventCommander.Equals(
                header.uploaderID,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        StopForCommanderChange(eventCommander);
        warnings.Add(
            $"EDDN stopped the captured '{header.uploaderID}' session because LoadGame identified Commander '{eventCommander}'.");
        return false;
    }

    private CapturedEventState? CaptureEventState(
        string eventName,
        JObject raw,
        EddnApplyRequest request)
    {
        lock (sync)
        {
            if (disposed || !accepting)
            {
                return null;
            }

            var signalBatch = eventName == "FSSSignalDiscovered"
                ? null
                : TakeSignalBatchLocked();
            var eventLocation = EddnMessageSanitizer.getLocation(raw);
            if (eventLocation is not null)
            {
                location = eventLocation;
                ClearTrackedBodyLocked();
            }

            statusBodyName = request.AllowSharedData
                && !string.IsNullOrWhiteSpace(request.Status?.BodyName)
                    ? request.Status.BodyName
                    : null;
            UpdateBodyContextLocked(raw);
            UpdateExpansionFlagsLocked(raw);
            UpdateCrewMembershipLocked(eventName);
            return new CapturedEventState(
                signalBatch,
                CreateContextLocked(),
                isCrewMember,
                sessionGeneration);
        }
    }

    private bool ProcessCompanionEvent(
        string eventName,
        JObject raw,
        EddnApplyRequest request,
        CapturedEventState captured,
        long ingestionGeneration,
        List<string> warnings)
    {
        if (!request.AllowSharedData)
        {
            warnings.Add(
                $"EDDN skipped {eventName}: shared companion files are suppressed while multiple Elite instances are active.");
            return true;
        }

        var directory = string.IsNullOrWhiteSpace(request.JournalDirectory)
            ? journalDirectory
            : request.JournalDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            warnings.Add(
                $"EDDN skipped {eventName}: the journal directory was unavailable.");
            return true;
        }

        StartCompanionRead(new CompanionCandidate(
            new JObject(raw),
            captured.Context,
            captured.SessionGeneration,
            ingestionGeneration,
            directory));
        return true;
    }

    private void PublishJournalEvent(
        string eventName,
        JObject raw,
        CapturedEventState captured,
        long ingestionGeneration,
        List<EddnPublishedEvent> published,
        List<string> warnings)
    {
        if (!EddnMessageSanitizer.tryBuildJournal(
                raw,
                captured.Context,
                out var prepared,
                out var reason))
        {
            warnings.Add($"EDDN skipped {eventName}: {reason}.");
            return;
        }

        if (TryEnqueue(
                prepared!,
                captured.SessionGeneration,
                ingestionGeneration))
        {
            published.Add(new EddnPublishedEvent(
                prepared!.eventName,
                EddnTransport.NormalizeSchemaReference(prepared.schemaRef),
                UsesTestSchemas: EddnTransport.TestSchemasEnabled));
        }
        else if (IsCurrentSession(captured.SessionGeneration))
        {
            warnings.Add($"EDDN could not queue {eventName} for upload.");
        }
    }

    private void BufferSignal(
        JObject raw,
        EddnMessageContext context,
        bool allowPublishing,
        long currentSessionGeneration)
    {
        if (!CanPublishNow(
                allowPublishing,
                currentSessionGeneration,
                out var ingestionGeneration))
        {
            ClearSignals();
            return;
        }

        lock (sync)
        {
            if (!IsCurrentSessionLocked(currentSessionGeneration))
            {
                return;
            }

            pendingSignalContext ??= new EddnSignalBatchContext(
                context.location,
                context.horizons,
                context.odyssey,
                currentSessionGeneration,
                ingestionGeneration);
            if (pendingSignalContext.SessionGeneration
                    != currentSessionGeneration
                || pendingSignalContext.IngestionGeneration
                    != ingestionGeneration)
            {
                pendingSignals.Clear();
                pendingSignalContext = new EddnSignalBatchContext(
                    context.location,
                    context.horizons,
                    context.odyssey,
                    currentSessionGeneration,
                    ingestionGeneration);
            }

            pendingSignals.Add(new JObject(raw));
        }
    }

    private void PublishSignalBatch(
        SignalBatch batch,
        List<EddnPublishedEvent>? published,
        List<string>? warnings,
        bool allowDisposedBatch = false)
    {
        if (!EddnMessageSanitizer.tryBuildSignalBatch(
                batch.Signals,
                batch.Context.Location,
                batch.Context.Horizons,
                batch.Context.Odyssey,
                out var prepared,
                out var reason))
        {
            if (reason != "no public signals remained after filtering")
            {
                var warning =
                    "EDDN skipped FSSSignalDiscovered batch: " + reason;
                warnings?.Add(warning);
                if (warnings is null)
                {
                    WriteLog(warning);
                }
            }

            return;
        }

        if (TryEnqueue(
                prepared!,
                batch.Context.SessionGeneration,
                batch.Context.IngestionGeneration,
                allowDisposedBatch))
        {
            published?.Add(new EddnPublishedEvent(
                prepared!.eventName,
                EddnTransport.NormalizeSchemaReference(prepared.schemaRef),
                UsesTestSchemas: EddnTransport.TestSchemasEnabled));
        }
        else if (!allowDisposedBatch
            && IsCurrentSession(batch.Context.SessionGeneration))
        {
            const string warning =
                "EDDN could not queue FSSSignalDiscovered for upload.";
            warnings?.Add(warning);
            if (warnings is null)
            {
                WriteLog(warning);
            }
        }
    }

    private bool CanPublishNow(
        bool allowPublishing,
        long expectedSessionGeneration,
        out long ingestionGeneration)
    {
        lock (sync)
        {
            if (!allowPublishing
                || !sharingEnabled
                || publishingSuspended
                || isCrewMember
                || !IsCurrentSessionLocked(expectedSessionGeneration))
            {
                ingestionGeneration = default;
                return false;
            }
        }

        return sink.TryBeginIngestion(out ingestionGeneration);
    }

    private bool TryEnqueue(
        EddnPreparedMessage prepared,
        long expectedSessionGeneration,
        long expectedIngestionGeneration,
        bool allowDisposedBatch = false,
        Action? rejected = null)
    {
        lock (enqueueSync)
        {
            lock (sync)
            {
                if (!allowDisposedBatch
                    && !IsCurrentSessionLocked(expectedSessionGeneration))
                {
                    return false;
                }
            }

            return sink.TryEnqueue(
                prepared,
                header,
                expectedIngestionGeneration,
                prepared.eventName,
                rejected);
        }
    }

    private void StartCompanionRead(CompanionCandidate candidate)
    {
        lock (companionTasksSync)
        {
            if (disposal.IsCancellationRequested
                || !IsCurrentSession(candidate.SessionGeneration))
            {
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                disposal.Token,
                companionActivity.Token);
            var task = ProcessCompanionFileWithCancellationAsync(
                candidate,
                cancellation);
            companionTasks.Add(task);
            _ = task.ContinueWith(
                CompleteCompanionTask,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task ProcessCompanionFileWithCancellationAsync(
        CompanionCandidate candidate,
        CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            await ProcessCompanionFileAsync(candidate, cancellation.Token)
                .ConfigureAwait(false);
        }
    }

    private void ResetCompanionActivity()
    {
        CancellationTokenSource previous;
        lock (companionTasksSync)
        {
            if (disposal.IsCancellationRequested)
            {
                return;
            }

            previous = companionActivity;
            companionActivity = new CancellationTokenSource();
        }

        try
        {
            previous.Cancel();
        }
        finally
        {
            previous.Dispose();
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
        var eventName = candidate.JournalEvent.Value<string>(EventProperty)
            ?? "companion file";
        try
        {
            var read = await companionReader(
                    candidate.JournalDirectory,
                    candidate.JournalEvent,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!CanUseCompanionRead(
                    read,
                    candidate.SessionGeneration,
                    eventName,
                    cancellationToken))
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

            QueueCompanionMessage(prepared!, candidate, eventName);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Session replacement and application shutdown intentionally cancel
            // reads so a shared companion file cannot cross Commander sessions.
        }
        catch (Exception exception) when (IsExpectedCompanionException(exception))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                WriteLog($"EDDN skipped {eventName}: {exception.Message}");
            }
        }
    }

    private bool CanUseCompanionRead(
        EddnCompanionReadResult read,
        long expectedSessionGeneration,
        string eventName,
        CancellationToken cancellationToken)
    {
        if (!read.isSuccess)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                WriteLog($"EDDN skipped {eventName}: {read.error}");
            }

            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return IsCurrentSession(expectedSessionGeneration);
    }

    private void QueueCompanionMessage(
        EddnPreparedMessage prepared,
        CompanionCandidate candidate,
        string eventName)
    {
        var signature = GetCompanionSignature(prepared);
        if (signature is not null && !ReserveSignature(signature.Value))
        {
            return;
        }

        var queued = TryEnqueue(
            prepared,
            candidate.SessionGeneration,
            candidate.IngestionGeneration,
            rejected: signature is null
                ? null
                : () => ReleaseSignature(signature.Value));
        if (queued)
        {
            return;
        }

        if (signature is not null)
        {
            ReleaseSignature(signature.Value);
        }

        if (IsCurrentSession(candidate.SessionGeneration))
        {
            WriteLog($"EDDN could not queue {eventName} for upload.");
        }
    }

    private static bool IsExpectedCompanionException(Exception exception)
    {
        return exception is IOException
            or JsonException
            or UnauthorizedAccessException
            or InvalidDataException;
    }

    private static (string Key, string Value)? GetCompanionSignature(
        EddnPreparedMessage prepared)
    {
        if (prepared.eventName == "NavRoute")
        {
            return null;
        }

        var marketId = prepared.message.Value<long?>("marketId")
            ?? prepared.message.Value<long?>("MarketID")
            ?? 0;
        var comparable = new JObject(prepared.message);
        comparable.Remove("timestamp");
        return (
            prepared.schemaRef + ":" + marketId,
            comparable.ToString(Formatting.None));
    }

    private bool ReserveSignature((string Key, string Value) signature)
    {
        lock (sync)
        {
            if (disposed || !accepting)
            {
                return false;
            }

            if (stationSignatures.GetValueOrDefault(signature.Key)
                == signature.Value)
            {
                return false;
            }

            stationSignatures[signature.Key] = signature.Value;
            return true;
        }
    }

    private void ReleaseSignature((string Key, string Value) signature)
    {
        lock (sync)
        {
            if (stationSignatures.GetValueOrDefault(signature.Key)
                == signature.Value)
            {
                stationSignatures.Remove(signature.Key);
            }
        }
    }

    private void StopForCommanderChange(string eventCommander)
    {
        lock (enqueueSync)
        {
            lock (sync)
            {
                if (disposed || !accepting)
                {
                    return;
                }

                accepting = false;
                sessionGeneration++;
                ClearTransientStateLocked();
            }

            disposal.Cancel();
        }

        WriteLog(
            $"EDDN stopped session '{header.uploaderID}' after LoadGame identified Commander '{eventCommander}'; "
                + "a new journal session must capture the new Commander before uploads resume.");
    }

    private SignalBatch? TakeSignalBatchLocked()
    {
        if (pendingSignals.Count == 0 || pendingSignalContext is null)
        {
            return null;
        }

        var batch = new SignalBatch(
            pendingSignals.Select(signal => new JObject(signal)).ToArray(),
            pendingSignalContext);
        pendingSignals.Clear();
        pendingSignalContext = null;
        return batch;
    }

    private void ClearSignals()
    {
        lock (sync)
        {
            pendingSignals.Clear();
            pendingSignalContext = null;
        }
    }

    private void ClearTransientStateLocked()
    {
        pendingSignals.Clear();
        pendingSignalContext = null;
        stationSignatures.Clear();
    }

    private void UpdateCrewMembershipLocked(string eventName)
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

    private void UpdateExpansionFlagsLocked(JObject raw)
    {
        if (raw.Value<string>(EventProperty) == "Fileheader")
        {
            horizons = null;
            odyssey = null;
            return;
        }

        if (raw.Value<string>(EventProperty) != "LoadGame")
        {
            return;
        }

        horizons = raw.Value<bool?>("Horizons");
        odyssey = raw.Value<bool?>("Odyssey");
    }

    private void UpdateBodyContextLocked(JObject raw)
    {
        var eventName = raw.Value<string>(EventProperty);
        if (eventName is "FSDJump" or "CarrierJump" or "StartJump")
        {
            ClearTrackedBodyLocked();
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
                trackedBodyType = raw.Value<string>("BodyType")
                    ?? PlanetBodyType;
            }
        }
    }

    private void ClearTrackedBodyLocked()
    {
        trackedBodyName = null;
        trackedBodyId = null;
        trackedBodyType = null;
    }

    private EddnMessageContext CreateContextLocked()
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
            && accepting
            && sharingEnabled
            && !publishingSuspended
            && sessionGeneration == generation;
    }

    private void WriteLog(string message)
    {
        try
        {
            log(message);
        }
        catch
        {
            // Diagnostics must not interrupt journal processing.
        }
    }

    private sealed record EddnSignalBatchContext(
        EddnLocationContext? Location,
        bool? Horizons,
        bool? Odyssey,
        long SessionGeneration,
        long IngestionGeneration);

    private sealed record CapturedEventState(
        SignalBatch? SignalBatch,
        EddnMessageContext Context,
        bool SuppressForCrew,
        long SessionGeneration);

    private sealed record SignalBatch(
        IReadOnlyList<JObject> Signals,
        EddnSignalBatchContext Context);

    private sealed record CompanionCandidate(
        JObject JournalEvent,
        EddnMessageContext Context,
        long SessionGeneration,
        long IngestionGeneration,
        string JournalDirectory);
}
