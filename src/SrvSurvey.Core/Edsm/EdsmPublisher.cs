using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SrvSurvey.Core.Edsm;

public interface IEdsmPublisher : IDisposable
{
    Task<EdsmPublicationResult> ApplyAsync(
        EdsmPublicationUpdate update,
        CancellationToken cancellationToken = default);

    Task<EdsmPublicationResult> StopAsync(
        CancellationToken cancellationToken = default);

    void CancelPendingPublication();
}

/// <summary>
/// Sends opted-in, live journal messages to EDSM's authenticated Journal API.
/// Raw messages are retained only in a bounded in-memory queue and are filtered
/// against EDSM's current discard list before transmission.
/// </summary>
public sealed class EdsmPublisher : IEdsmPublisher
{
    public const string Endpoint = "https://www.edsm.net/api-journal-v1";
    public const string DiscardedEventsEndpoint =
        "https://www.edsm.net/api-journal-v1/discard";
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DiscardListRetryInterval =
        TimeSpan.FromMinutes(10);
    private const int MaximumEventsPerRequest = 128;
    private const int MaximumPendingEvents = 4096;
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const int MaximumResponseBytes = 1024 * 1024;
    private const int MaximumDiscardListBytes = 256 * 1024;

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string appVersion;
    private readonly TimeProvider timeProvider;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "SemaphoreSlim has no wait handle here and can still have shutdown waiters.")]
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly object stateSync = new();
    private readonly object stopSync = new();
    private readonly List<EdsmQueuedEvent> pending = [];
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly EdsmJournalContext journalContext = new();

    private EdsmSession? session;
    private HashSet<string>? discardedEvents;
    private Task<HashSet<string>>? discardListTask;
    private Task<EdsmPublicationResult>? activeSendTask;
    private Task<EdsmPublicationResult>? stopTask;
    private CancellationTokenSource? activeSendCancellation;
    private EdsmCredentials? authorizedCredentials;
    private long authorizationGeneration;
    private DateTimeOffset? nextSendAt;
    private DateTimeOffset? nextDiscardListAttemptAt;
    private int activeBatchCount;
    private int consecutiveTransientFailures;
    private bool publicationPaused;
    private volatile bool stopping;
    private volatile bool disposed;

    public EdsmPublisher(
        string appVersion,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        this.appVersion = appVersion;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (httpClient is null)
        {
            this.httpClient = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            })
            {
                Timeout = TimeSpan.FromSeconds(20),
            };
            this.httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"SrvSurvey/{appVersion}");
            ownsHttpClient = true;
        }
        else
        {
            this.httpClient = httpClient;
        }
    }

    public async Task<EdsmPublicationResult> ApplyAsync(
        EdsmPublicationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (stopping || disposed)
        {
            return EdsmPublicationResult.Empty;
        }

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (stopping || disposed)
            {
                return EdsmPublicationResult.Empty;
            }

            return await ApplyCoreAsync(update, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<EdsmPublicationResult> ApplyCoreAsync(
        EdsmPublicationUpdate update,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var sessionTransition = await EnsureSessionAsync(
                update.Options,
                update.JournalPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return sessionTransition;
        }

        var publicationAuthorized = PreparePublicationAuthorization(
            update.Options);
        HarvestDiscardList(warnings);
        if (publicationAuthorized)
        {
            EnsureDiscardListLoadStarted();
            HarvestDiscardList(warnings);
        }

        var queuedNames = CollectJournalEvents(
            update,
            publicationAuthorized,
            warnings);
        var completed = TakeCompletedSendResult();
        warnings.AddRange(completed.Warnings);
        var forceFlush = update.AllowPublishing
            && update.JournalEvents.Any(item =>
                item.EventName.Equals("Shutdown", StringComparison.OrdinalIgnoreCase));
        if (forceFlush || IsSendDue())
        {
            _ = TryStartBackgroundSend(forceFlush, out _);
        }

        return Combine(
            sessionTransition,
            new EdsmPublicationResult(
                queuedNames.Count,
                completed.AcceptedEventCount,
                GetPendingCount(),
                queuedNames.Distinct(StringComparer.Ordinal).ToArray(),
                warnings));
    }

    private async Task<EdsmPublicationResult> EnsureSessionAsync(
        EdsmPublicationOptions options,
        string? journalPath,
        CancellationToken cancellationToken)
    {
        var candidate = EdsmSession.Create(options, journalPath);
        if (candidate is null)
        {
            if (session is null)
            {
                return EdsmPublicationResult.Empty;
            }

            var finalizedInvalidSession = await FinalizeCurrentSessionAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            ResetSession(next: null);
            return finalizedInvalidSession;
        }

        if (session?.Matches(candidate) == true)
        {
            return EdsmPublicationResult.Empty;
        }

        var finalized = await FinalizeCurrentSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        ResetSession(candidate);
        return finalized;
    }

    private void ResetSession(EdsmSession? next)
    {
        ClearPublicationAuthorization();
        lock (stateSync)
        {
            pending.Clear();
            nextSendAt = null;
            consecutiveTransientFailures = 0;
            publicationPaused = false;
        }

        journalContext.Reset();
        session = next;
    }

    private bool PreparePublicationAuthorization(EdsmPublicationOptions options)
    {
        var credentials = session is { IsLive: true, IsBeta: false }
            ? EdsmSession.GetCredentials(
                options.EdsmCommanderName,
                options.ApiKey)
            : null;
        var canPrepare = credentials is not null;
        var normalized = credentials;
        CancellationTokenSource? cancellation;
        lock (stateSync)
        {
            if (authorizedCredentials == normalized)
            {
                return canPrepare && !publicationPaused;
            }

            authorizedCredentials = normalized;
            authorizationGeneration++;
            pending.Clear();
            nextSendAt = null;
            consecutiveTransientFailures = 0;
            publicationPaused = false;
            cancellation = activeSendCancellation;
        }

        cancellation?.Cancel();
        return canPrepare;
    }

    private List<string> CollectJournalEvents(
        EdsmPublicationUpdate update,
        bool publicationAuthorized,
        List<string> warnings)
    {
        var queuedNames = new List<string>();
        EdsmCredentials? credentials;
        long generation;
        lock (stateSync)
        {
            credentials = authorizedCredentials;
            generation = authorizationGeneration;
        }

        foreach (var journalEvent in update.JournalEvents)
        {
            try
            {
                var entry = JObject.Parse(journalEvent.RawJson);
                var identityMatches = JournalIdentityMatchesSession(entry);
                journalContext.Apply(entry);
                if (!publicationAuthorized
                    || !update.AllowPublishing
                    || credentials is null)
                {
                    continue;
                }

                if (!identityMatches)
                {
                    warnings.Add(
                        $"EDSM ignored {journalEvent.EventName} because its commander identity did not match the active journal session.");
                    continue;
                }

                if (journalContext.InMulticrew)
                {
                    continue;
                }

                var prepared = journalContext.AddTransientFields(entry);
                var queued = new EdsmQueuedEvent(
                    generation,
                    journalEvent.EventName,
                    prepared.ToString(Formatting.None));
                var dropped = Enqueue(queued);
                if (dropped > 0)
                {
                    warnings.Add(
                        $"EDSM discarded {dropped} oldest queued event(s) to keep its in-memory backlog bounded.");
                }

                queuedNames.Add(journalEvent.EventName);
            }
            catch (JsonException exception)
            {
                warnings.Add(
                    $"EDSM ignored {journalEvent.EventName}: {exception.Message}");
            }
        }

        return queuedNames;
    }

    private bool JournalIdentityMatchesSession(JObject entry)
    {
        if (session is null)
        {
            return false;
        }

        var eventName = entry.Value<string>("event");
        var commander = eventName switch
        {
            "Commander" => entry.Value<string>("Name"),
            "LoadGame" => entry.Value<string>("Commander"),
            _ => null,
        };
        var frontierId = eventName is "Commander" or "LoadGame"
            ? entry.Value<string>("FID")
            : null;
        if (!string.IsNullOrWhiteSpace(commander)
            && !string.Equals(
                commander.Trim(),
                session.ActiveCommanderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(frontierId)
            || string.Equals(
                frontierId.Trim(),
                session.FrontierId,
                StringComparison.OrdinalIgnoreCase);
    }

    private int Enqueue(EdsmQueuedEvent queued)
    {
        lock (stateSync)
        {
            pending.Add(queued);
            nextSendAt ??= timeProvider.GetUtcNow() + SendInterval;
            var dropped = Math.Max(0, pending.Count - MaximumPendingEvents);
            if (dropped > 0)
            {
                pending.RemoveRange(0, dropped);
            }

            return dropped;
        }
    }

    private void EnsureDiscardListLoadStarted()
    {
        lock (stateSync)
        {
            if (discardedEvents is not null || discardListTask is not null)
            {
                return;
            }

            if (nextDiscardListAttemptAt is { } retryAt
                && timeProvider.GetUtcNow() < retryAt)
            {
                return;
            }

            discardListTask = Task.Run(
                () => LoadDiscardedEventsAsync(lifetimeCancellation.Token),
                CancellationToken.None);
        }
    }

    private void HarvestDiscardList(List<string> warnings)
    {
        Task<HashSet<string>>? completed;
        lock (stateSync)
        {
            if (discardListTask?.IsCompleted != true)
            {
                return;
            }

            completed = discardListTask;
            discardListTask = null;
        }

        try
        {
            var result = completed.GetAwaiter().GetResult();
            lock (stateSync)
            {
                discardedEvents = result;
                nextDiscardListAttemptAt = null;
            }
        }
        catch (OperationCanceledException) when (
            lifetimeCancellation.IsCancellationRequested)
        {
            // Application shutdown owns this cancellation.
        }
        catch (Exception exception)
        {
            lock (stateSync)
            {
                nextDiscardListAttemptAt =
                    timeProvider.GetUtcNow() + DiscardListRetryInterval;
            }

            warnings.Add(
                "EDSM uploads are waiting because its current discarded-event list could not be loaded: "
                + exception.Message);
        }
    }

    private async Task<HashSet<string>> LoadDiscardedEventsAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
                DiscardedEventsEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await ReadBoundedTextAsync(
                response.Content,
                MaximumDiscardListBytes,
                "EDSM discarded-event response",
                cancellationToken)
            .ConfigureAwait(false);
        var events = JArray.Parse(body)
            .Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (events.Count == 0)
        {
            throw new InvalidDataException(
                "EDSM returned an empty discarded-event list.");
        }

        return events;
    }

    private bool IsSendDue()
    {
        lock (stateSync)
        {
            return activeSendTask is null
                && discardedEvents is not null
                && nextSendAt is { } deadline
                && timeProvider.GetUtcNow() >= deadline;
        }
    }

    private bool TryStartBackgroundSend(
        bool force,
        out Task<EdsmPublicationResult> sendTask)
    {
        lock (stateSync)
        {
            sendTask = activeSendTask!;
            if (disposed || activeSendTask is not null)
            {
                return sendTask is not null;
            }

            if (discardedEvents is null
                || (!force
                    && (nextSendAt is null
                        || timeProvider.GetUtcNow() < nextSendAt.Value)))
            {
                return false;
            }

            if (authorizedCredentials is null || session is null)
            {
                return false;
            }

            var generation = authorizationGeneration;
            var sendCredentials = authorizedCredentials;
            var sendSession = session;
            var sendDiscardedEvents = discardedEvents;
            activeSendCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
            var sendCancellation = activeSendCancellation;
            activeSendTask = Task.Run(
                () => SendPendingAsync(
                    sendSession,
                    sendCredentials,
                    sendDiscardedEvents,
                    generation,
                    sendCancellation.Token),
                CancellationToken.None);
            sendTask = activeSendTask;
            return true;
        }
    }

    private async Task<EdsmPublicationResult> SendPendingAsync(
        EdsmSession sendSession,
        EdsmCredentials credentials,
        HashSet<string> sendDiscardedEvents,
        long generation,
        CancellationToken cancellationToken)
    {
        var batch = TakeBatch(
            generation,
            sendDiscardedEvents,
            out var discardedForCredentials,
            out var discardedByEdsm);
        lock (stateSync)
        {
            nextSendAt = null;
            activeBatchCount = batch.Count;
        }

        var warnings = new List<string>();
        if (discardedForCredentials > 0)
        {
            warnings.Add(
                $"EDSM discarded {discardedForCredentials} queued event(s) after the commander credentials changed or were cleared.");
        }

        if (discardedByEdsm > 0)
        {
            warnings.Add(
                $"EDSM skipped {discardedByEdsm} event(s) that its current Journal API does not process.");
        }

        if (batch.Count == 0)
        {
            ScheduleNextAttempt(transientFailure: false);
            return CreateSendResult(0, warnings);
        }

        try
        {
            if (!IsPublicationAuthorized(generation, credentials))
            {
                return DiscardUnauthorizedBatch(batch, warnings);
            }

            var payloadBytes = await BuildBoundedPayloadAsync(
                    sendSession,
                    credentials,
                    batch,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
            if (payloadBytes is null)
            {
                ScheduleNextAttempt(transientFailure: false);
                return CreateSendResult(0, warnings);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new ByteArrayContent(payloadBytes),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(
                "application/x-www-form-urlencoded");
            if (!IsPublicationAuthorized(generation, credentials))
            {
                return DiscardUnauthorizedBatch(batch, warnings);
            }

            using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            return await InterpretResponseAsync(
                    response,
                    batch,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            if (!IsPublicationAuthorized(generation, credentials))
            {
                warnings.Add(
                    $"EDSM cancelled {batch.Count} event(s) after publication was disabled or its credentials changed.");
                return CreateSendResult(0, warnings);
            }

            Requeue(batch, warnings);
            throw;
        }
        catch (Exception exception)
        {
            Requeue(batch, warnings);
            ScheduleNextAttempt(transientFailure: true);
            warnings.Add(
                $"EDSM upload was deferred ({exception.GetType().Name}); {batch.Count} event(s) were retained in memory.");
            return CreateSendResult(0, warnings);
        }
        finally
        {
            lock (stateSync)
            {
                activeBatchCount = 0;
            }
        }
    }

    private List<EdsmQueuedEvent> TakeBatch(
        long generation,
        HashSet<string> sendDiscardedEvents,
        out int discardedForCredentials,
        out int discardedByEdsm)
    {
        lock (stateSync)
        {
            discardedForCredentials = pending.RemoveAll(item =>
                item.AuthorizationGeneration != generation);
            discardedByEdsm = pending.RemoveAll(item =>
                sendDiscardedEvents.Contains(item.EventName));
            var count = Math.Min(pending.Count, MaximumEventsPerRequest);
            var batch = pending.GetRange(0, count);
            pending.RemoveRange(0, count);
            return batch;
        }
    }

    private async Task<byte[]?> BuildBoundedPayloadAsync(
        EdsmSession sendSession,
        EdsmCredentials credentials,
        List<EdsmQueuedEvent> batch,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = new JArray(
                batch.Select(item => JToken.Parse(item.RawJson)))
                .ToString(Formatting.None);
            using var form = new FormUrlEncodedContent(
            [
                new("commanderName", credentials.CommanderName),
                new("apiKey", credentials.ApiKey),
                new("fromSoftware", "SrvSurvey"),
                new("fromSoftwareVersion", appVersion),
                new("fromGameVersion", sendSession.GameVersion),
                new("fromGameBuild", sendSession.GameBuild),
                new("message", message),
            ]);
            var bytes = await form.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length <= MaximumPayloadBytes)
            {
                lock (stateSync)
                {
                    activeBatchCount = batch.Count;
                }

                return bytes;
            }

            if (batch.Count == 1)
            {
                warnings.Add(
                    $"EDSM skipped one oversized {batch[0].EventName} event ({bytes.Length:N0} encoded bytes). No journal processing was affected.");
                batch.Clear();
                return null;
            }

            var splitAt = batch.Count / 2;
            var tail = batch.Skip(splitAt).ToArray();
            batch.RemoveRange(splitAt, batch.Count - splitAt);
            Requeue(tail, warnings);
        }
    }

    private async Task<EdsmPublicationResult> InterpretResponseAsync(
        HttpResponseMessage response,
        List<EdsmQueuedEvent> batch,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var rateLimitDelay = GetRateLimitDelay(response.Headers);
        if (IsTransient(response.StatusCode))
        {
            return DeferBatch(
                batch,
                warnings,
                $"EDSM upload was deferred after HTTP {(int)response.StatusCode} ({SafeStatusText(response.ReasonPhrase)}); {batch.Count} event(s) were retained in memory.",
                MaxDelay(rateLimitDelay, GetRetryAfter(response.Headers.RetryAfter)));
        }

        if (!response.IsSuccessStatusCode)
        {
            warnings.Add(
                $"EDSM rejected {batch.Count} event(s) with HTTP {(int)response.StatusCode} ({SafeStatusText(response.ReasonPhrase)}).");
            ScheduleNextAttempt(transientFailure: false, rateLimitDelay);
            return CreateSendResult(0, warnings);
        }

        var body = await ReadBoundedTextAsync(
                response.Content,
                MaximumResponseBytes,
                "EDSM Journal API response",
                cancellationToken)
            .ConfigureAwait(false);
        return ProcessResponseBody(body, batch, warnings, rateLimitDelay);
    }

    private EdsmPublicationResult ProcessResponseBody(
        string body,
        List<EdsmQueuedEvent> batch,
        List<string> warnings,
        TimeSpan? rateLimitDelay)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidDataException("EDSM returned an empty response.");
        }

        var result = JObject.Parse(body);
        var topStatus = result.Value<int?>("msgnum")
            ?? throw new InvalidDataException(
                "EDSM returned a response without a status code.");
        var topClass = topStatus / 100;
        var topMessage = SafeStatusText(result.Value<string>("msg"));
        if (topClass == 2)
        {
            warnings.Add(
                $"EDSM rejected a batch of {batch.Count} event(s) with API status {topStatus}: {topMessage}.");
            PausePublication();
            return CreateSendResult(0, warnings);
        }

        if (topClass == 5)
        {
            warnings.Add(
                $"EDSM saved {batch.Count} event(s) for later processing (API status {topStatus}: {topMessage}).");
            ScheduleNextAttempt(transientFailure: false, rateLimitDelay);
            return CreateSendResult(batch.Count, warnings);
        }

        if (topClass != 1)
        {
            warnings.Add(
                $"EDSM rejected a batch of {batch.Count} event(s) with unexpected API status {topStatus}: {topMessage}.");
            ScheduleNextAttempt(transientFailure: false, rateLimitDelay);
            return CreateSendResult(0, warnings);
        }

        if (result["events"] is not JArray responseEvents
            || responseEvents.Count != batch.Count)
        {
            throw new InvalidDataException(
                "EDSM returned an incomplete per-event response.");
        }

        var (accepted, retryable, failures) = ProcessEventResponses(
            responseEvents,
            batch);

        const int maximumReportedFailures = 10;
        warnings.AddRange(failures.Take(maximumReportedFailures));
        if (failures.Count > maximumReportedFailures)
        {
            warnings.Add(
                $"EDSM rejected {failures.Count - maximumReportedFailures} additional event(s).");
        }

        if (retryable.Count > 0)
        {
            Requeue(retryable, warnings);
            warnings.Add(
                $"EDSM deferred {retryable.Count} event(s) whose catalog items are not known yet; they were retained in memory.");
            ScheduleNextAttempt(transientFailure: true, rateLimitDelay);
        }
        else
        {
            ScheduleNextAttempt(transientFailure: false, rateLimitDelay);
        }

        return CreateSendResult(accepted, warnings);
    }

    private static (
        int Accepted,
        List<EdsmQueuedEvent> Retryable,
        List<string> Failures) ProcessEventResponses(
            JArray responseEvents,
            List<EdsmQueuedEvent> batch)
    {
        var accepted = 0;
        var retryable = new List<EdsmQueuedEvent>();
        var failures = new List<string>();
        for (var index = 0; index < responseEvents.Count; index++)
        {
            if (responseEvents[index] is not JObject eventResult
                || eventResult.Value<int?>("msgnum") is not { } status)
            {
                throw new InvalidDataException(
                    "EDSM returned an invalid per-event response.");
            }

            var statusClass = status / 100;
            if (statusClass is 1 or 5)
            {
                accepted++;
            }
            else if (status == 402)
            {
                retryable.Add(batch[index]);
            }
            else
            {
                failures.Add(
                    $"EDSM rejected {batch[index].EventName} with API status {status}: {SafeStatusText(eventResult.Value<string>("msg"))}.");
            }
        }

        return (accepted, retryable, failures);
    }

    private void PausePublication()
    {
        lock (stateSync)
        {
            publicationPaused = true;
            pending.Clear();
            nextSendAt = null;
        }
    }

    private EdsmPublicationResult DeferBatch(
        IReadOnlyCollection<EdsmQueuedEvent> batch,
        List<string> warnings,
        string warning,
        TimeSpan? retryAfter)
    {
        Requeue(batch, warnings);
        ScheduleNextAttempt(transientFailure: true, retryAfter);
        warnings.Add(warning);
        return CreateSendResult(0, warnings);
    }

    private EdsmPublicationResult DiscardUnauthorizedBatch(
        List<EdsmQueuedEvent> batch,
        List<string> warnings)
    {
        warnings.Add(
            $"EDSM discarded {batch.Count} event(s) after publication authorization changed.");
        return CreateSendResult(0, warnings);
    }

    private void Requeue(
        IReadOnlyCollection<EdsmQueuedEvent> batch,
        List<string> warnings)
    {
        lock (stateSync)
        {
            pending.InsertRange(0, batch);
            var dropped = Math.Max(0, pending.Count - MaximumPendingEvents);
            if (dropped > 0)
            {
                pending.RemoveRange(0, dropped);
                warnings.Add(
                    $"EDSM discarded {dropped} oldest queued event(s) to keep its in-memory backlog bounded.");
            }
        }
    }

    private void ScheduleNextAttempt(
        bool transientFailure,
        TimeSpan? minimumDelay = null)
    {
        lock (stateSync)
        {
            consecutiveTransientFailures = transientFailure
                ? consecutiveTransientFailures + 1
                : 0;
            if (pending.Count == 0)
            {
                nextSendAt = null;
                return;
            }

            var exponent = Math.Min(
                Math.Max(consecutiveTransientFailures - 1, 0),
                4);
            var delay = transientFailure
                ? TimeSpan.FromTicks(SendInterval.Ticks * (1L << exponent))
                : SendInterval;
            if (minimumDelay is { } requestedDelay && requestedDelay > delay)
            {
                delay = requestedDelay;
            }

            var candidate = timeProvider.GetUtcNow() + delay;
            if (nextSendAt is null || candidate > nextSendAt.Value)
            {
                nextSendAt = candidate;
            }
        }
    }

    private static TimeSpan? MaxDelay(TimeSpan? first, TimeSpan? second)
    {
        if (first is null)
        {
            return second;
        }

        return second is null || first >= second ? first : second;
    }

    private TimeSpan? GetRateLimitDelay(HttpResponseHeaders headers)
    {
        if (!TryGetHeaderInt64(headers, "X-Rate-Limit-Remaining", out var remaining)
            || remaining > 0
            || !TryGetHeaderInt64(headers, "X-Rate-Limit-Reset", out var reset))
        {
            return null;
        }

        DateTimeOffset resetAt;
        try
        {
            resetAt = DateTimeOffset.FromUnixTimeSeconds(reset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var delay = resetAt - timeProvider.GetUtcNow();
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private static bool TryGetHeaderInt64(
        HttpResponseHeaders headers,
        string name,
        out long value)
    {
        value = 0;
        return headers.TryGetValues(name, out var values)
            && long.TryParse(
                values.FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
    }

    private TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        if (retryAfter?.Date is not { } date)
        {
            return null;
        }

        var delay = date - timeProvider.GetUtcNow();
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private bool IsPublicationAuthorized(
        long generation,
        EdsmCredentials credentials)
    {
        lock (stateSync)
        {
            return !disposed
                && authorizationGeneration == generation
                && authorizedCredentials == credentials;
        }
    }

    private void ClearPublicationAuthorization()
    {
        CancellationTokenSource? cancellation;
        lock (stateSync)
        {
            authorizedCredentials = null;
            authorizationGeneration++;
            nextSendAt = null;
            pending.Clear();
            publicationPaused = false;
            cancellation = activeSendCancellation;
        }

        cancellation?.Cancel();
    }

    public void CancelPendingPublication()
    {
        if (!stopping && !disposed)
        {
            ClearPublicationAuthorization();
        }
    }

    private EdsmPublicationResult TakeCompletedSendResult()
    {
        Task<EdsmPublicationResult>? completed;
        CancellationTokenSource? completedCancellation;
        lock (stateSync)
        {
            if (activeSendTask?.IsCompleted != true)
            {
                return EdsmPublicationResult.Empty;
            }

            completed = activeSendTask;
            activeSendTask = null;
            completedCancellation = activeSendCancellation;
            activeSendCancellation = null;
        }

        try
        {
            return completed.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return EdsmPublicationResult.Empty;
        }
        finally
        {
            completedCancellation?.Dispose();
        }
    }

    public async Task<EdsmPublicationResult> FlushAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return stopping
                ? EdsmPublicationResult.Empty
                : await FlushCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async Task<EdsmPublicationResult> FlushCoreAsync(
        CancellationToken cancellationToken)
    {
        var result = await WaitForActiveSendAsync(cancellationToken)
            .ConfigureAwait(false);
        EnsureDiscardListLoadStarted();
        Task<HashSet<string>>? discardLoad;
        lock (stateSync)
        {
            discardLoad = discardListTask;
        }

        if (discardLoad is not null)
        {
            try
            {
                await discardLoad.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                // HarvestDiscardList translates the completed failure into a
                // bounded retry and a user-facing warning below.
            }

            var warnings = new List<string>();
            HarvestDiscardList(warnings);
            result = Combine(
                result,
                new EdsmPublicationResult(
                    0,
                    0,
                    GetPendingCount(),
                    [],
                    warnings));
        }

        if (TryStartBackgroundSend(force: true, out _))
        {
            var sent = await WaitForActiveSendAsync(cancellationToken)
                .ConfigureAwait(false);
            result = Combine(result, sent);
        }

        return result with { PendingEventCount = GetPendingCount() };
    }

    private async Task<EdsmPublicationResult> WaitForActiveSendAsync(
        CancellationToken cancellationToken)
    {
        var completed = TakeCompletedSendResult();
        Task<EdsmPublicationResult>? active;
        lock (stateSync)
        {
            active = activeSendTask;
        }

        if (active is null)
        {
            return completed;
        }

        var sent = await active.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? completedCancellation = null;
        lock (stateSync)
        {
            if (ReferenceEquals(activeSendTask, active))
            {
                activeSendTask = null;
                completedCancellation = activeSendCancellation;
                activeSendCancellation = null;
            }
        }

        completedCancellation?.Dispose();
        return Combine(completed, sent);
    }

    private async Task<EdsmPublicationResult> FinalizeCurrentSessionAsync(
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return EdsmPublicationResult.Empty;
        }

        var completed = await WaitForActiveSendAsync(cancellationToken)
            .ConfigureAwait(false);
        Task<HashSet<string>>? discardLoad;
        lock (stateSync)
        {
            discardLoad = discardListTask;
        }

        if (discardLoad is not null && GetPendingCount() > 0)
        {
            try
            {
                await discardLoad.WaitAsync(cancellationToken).ConfigureAwait(false);
                var warnings = new List<string>();
                HarvestDiscardList(warnings);
                completed = Combine(
                    completed,
                    new EdsmPublicationResult(0, 0, GetPendingCount(), [], warnings));
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                completed = Combine(
                    completed,
                    new EdsmPublicationResult(
                        0,
                        0,
                        GetPendingCount(),
                        [],
                        [$"EDSM could not load its discarded-event list before shutdown: {exception.Message}"]));
            }
        }

        if (TryStartBackgroundSend(force: true, out _))
        {
            var sent = await WaitForActiveSendAsync(cancellationToken)
                .ConfigureAwait(false);
            completed = Combine(completed, sent);
        }

        return completed;
    }

    public Task<EdsmPublicationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        Task<EdsmPublicationResult> sharedStop;
        lock (stopSync)
        {
            sharedStop = stopTask ??= StopCoreAsync();
        }

        return cancellationToken.CanBeCanceled
            ? sharedStop.WaitAsync(cancellationToken)
            : sharedStop;
    }

    private async Task<EdsmPublicationResult> StopCoreAsync()
    {
        stopping = true;
        await lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        using var shutdownCancellation = new CancellationTokenSource(
            ShutdownTimeout);
        try
        {
            return await FinalizeCurrentSessionAsync(shutdownCancellation.Token)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                disposed = true;
                ClearPublicationAuthorization();
                await lifetimeCancellation.CancelAsync().ConfigureAwait(false);
                DisposeActiveSendCancellation();
                if (ownsHttpClient)
                {
                    httpClient.Dispose();
                }

                lifetimeCancellation.Dispose();
            }
            finally
            {
                lifecycleGate.Release();
            }
        }
    }

    private void DisposeActiveSendCancellation()
    {
        CancellationTokenSource? cancellation;
        lock (stateSync)
        {
            cancellation = activeSendCancellation;
            activeSendCancellation = null;
        }

        cancellation?.Dispose();
    }

    public void Dispose()
    {
        using var shutdownCancellation = new CancellationTokenSource(
            ShutdownTimeout);
        try
        {
            StopAsync(shutdownCancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // The shared stop continues after the bounded synchronous wait.
        }
    }

    private EdsmPublicationResult CreateSendResult(
        int accepted,
        IReadOnlyList<string> warnings)
    {
        return new EdsmPublicationResult(
            0,
            accepted,
            GetQueuedCount(),
            [],
            warnings);
    }

    private int GetQueuedCount()
    {
        lock (stateSync)
        {
            return authorizedCredentials is null ? 0 : pending.Count;
        }
    }

    private int GetPendingCount()
    {
        lock (stateSync)
        {
            return authorizedCredentials is null
                ? 0
                : pending.Count + activeBatchCount;
        }
    }

    private static EdsmPublicationResult Combine(
        EdsmPublicationResult first,
        EdsmPublicationResult second)
    {
        return new EdsmPublicationResult(
            first.QueuedEventCount + second.QueuedEventCount,
            first.AcceptedEventCount + second.AcceptedEventCount,
            second.PendingEventCount,
            first.QueuedEventNames.Concat(second.QueuedEventNames).ToArray(),
            first.Warnings.Concat(second.Warnings).ToArray());
    }

    internal static bool IsBetaVersion(string? gameVersion)
    {
        return !string.IsNullOrWhiteSpace(gameVersion)
            && (gameVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                || gameVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsLiveVersion(string? gameVersion, bool isOdyssey)
    {
        if (isOdyssey)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return false;
        }

        var numeric = new string(gameVersion
            .TakeWhile(character => character is (>= '0' and <= '9') or '.')
            .ToArray());
        return Version.TryParse(numeric.TrimEnd('.'), out var version)
            && version.Major >= 4;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private static string SafeStatusText(string? value)
    {
        const int maximumLength = 300;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "no status text supplied";
        }

        var normalized = string.Join(
            " ",
            value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : $"{normalized[..maximumLength]}...";
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumBytes,
        string responseName,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"{responseName} exceeded {maximumBytes:N0} bytes.");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"{responseName} exceeded {maximumBytes:N0} bytes.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}
