using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;

// Behavioral reference:
// https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/inara.py
// Copyright (c) EDCD, licensed under GNU GPL v2 or later.

namespace SrvSurvey.Core.Inara;

public interface IInaraPublisher : IDisposable
{
    Task<InaraPublicationResult> ApplyAsync(
        InaraPublicationUpdate update,
        CancellationToken cancellationToken = default);

    Task<InaraPublicationResult> StopAsync(
        CancellationToken cancellationToken = default);

    void CancelPendingPublication();
}

/// <summary>
/// Maps opted-in live journal activity to Inara events and batches API writes.
/// Mapping behavior follows EDMarketConnector's Inara integration and Inara's
/// published API documentation; raw journal events are never forwarded.
/// </summary>
public sealed class InaraPublisher : IInaraPublisher
{
    public const string Endpoint = "https://inara.cz/inapi/v1/";
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(35);
    private const int MaximumEventsPerRequest = 128;
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const int MaximumResponseBytes = 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string appVersion;
    private readonly TimeProvider timeProvider;
    private readonly InaraEventMapper mapper = new();
    private readonly InaraEventQueue queue = new();
    private readonly object sendStateSync = new();
    private readonly object stopSync = new();
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private JournalSessionState publicationState = new();
    private InaraSession? session;
    private Task<InaraPublicationResult>? activeSendTask;
    private Task<InaraPublicationResult>? stopTask;
    private CancellationTokenSource? activeSendCancellation;
    private string? authorizedApiKey;
    private long authorizationGeneration;
    private DateTimeOffset? nextSendAt;
    private int activeBatchCount;
    private int consecutiveTransientFailures;
    private volatile bool stopping;
    private volatile bool disposed;

    public InaraPublisher(
        string appVersion,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        this.appVersion = appVersion;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        if (httpClient is null)
        {
            this.httpClient = new HttpClient
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

    public async Task<InaraPublicationResult> ApplyAsync(
        InaraPublicationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (stopping || disposed)
        {
            return InaraPublicationResult.Empty;
        }

        var warnings = new List<string>();
        var options = update.Options;
        var sessionTransition = await EnsureSessionAsync(
                options,
                update.JournalPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return sessionTransition;
        }

        var publicationAuthorized = PreparePublicationAuthorization(options);
        var queuedNames = await CollectAllJournalEventsAsync(
                update,
                options,
                publicationAuthorized,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);

        var completed = TakeCompletedSendResult();
        warnings.AddRange(completed.Warnings);
        MaybeStartBackgroundSend(update);
        var current = new InaraPublicationResult(
            queuedNames.Count,
            completed.AcceptedEventCount,
            GetPendingCount(),
            queuedNames.Distinct(StringComparer.Ordinal).ToArray(),
            warnings);
        return Combine(sessionTransition, current);
    }

    private bool PreparePublicationAuthorization(InaraPublicationOptions options)
    {
        var publicationAuthorized = UpdatePublicationAuthorization(
            options,
            out var authorizationChanged);
        if (!publicationAuthorized || authorizationChanged)
        {
            queue.TakeAll();
            lock (sendStateSync)
            {
                nextSendAt = null;
            }
        }

        return publicationAuthorized;
    }

    private async Task<InaraPublicationResult> EnsureSessionAsync(
        InaraPublicationOptions options,
        string? journalPath,
        CancellationToken cancellationToken)
    {
        var candidate = InaraSession.Create(options, journalPath);
        if (candidate is null)
        {
            if (session is null)
            {
                return InaraPublicationResult.Empty;
            }

            var finalizedInvalidSession = await FinalizeCurrentSessionAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            ResetSession(next: null);
            return finalizedInvalidSession;
        }

        if (session?.Matches(candidate) == true)
        {
            return InaraPublicationResult.Empty;
        }

        var finalized = await FinalizeCurrentSessionAsync(cancellationToken)
            .ConfigureAwait(false);
        ResetSession(candidate);
        return finalized;
    }

    private void ResetSession(InaraSession? next)
    {
        ClearPublicationAuthorization();
        queue.TakeAll();
        publicationState = new JournalSessionState();
        mapper.Reset();
        session = next;
    }

    private async Task<List<string>> CollectAllJournalEventsAsync(
        InaraPublicationUpdate update,
        InaraPublicationOptions options,
        bool publicationAuthorized,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var queuedNames = new List<string>();
        foreach (var journalEvent in update.JournalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CollectJournalEventAsync(
                    update,
                    options,
                    journalEvent,
                    publicationAuthorized,
                    queuedNames,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return queuedNames;
    }

    private void MaybeStartBackgroundSend(InaraPublicationUpdate update)
    {
        var forceFlush = update.AllowPublishing
            && update.JournalEvents.Any(item => item.EventName == "Shutdown");
        if (forceFlush || IsSendDue())
        {
            _ = TryStartBackgroundSend(
                force: forceFlush,
                out _);
        }
    }

    private async Task CollectJournalEventAsync(
        InaraPublicationUpdate update,
        InaraPublicationOptions options,
        JournalEventEnvelope journalEvent,
        bool publicationAuthorized,
        List<string> queuedNames,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = JObject.Parse(journalEvent.RawJson);
            if (!JournalIdentityMatchesSession(entry))
            {
                warnings.Add(
                    $"Inara ignored {journalEvent.EventName} because its commander identity did not match the active journal session.");
                return;
            }

            if (publicationAuthorized)
            {
                entry = await AddSidecarDataAsync(
                        entry,
                        journalEvent,
                        update.Cargo,
                        update.JournalPath,
                        update.AllowSharedData,
                        warnings,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            ApplyPublicationState(journalEvent, entry);
            QueueMappedEvents(
                update,
                options,
                entry,
                queuedNames,
                warnings);
        }
        catch (JsonException exception)
        {
            warnings.Add(
                $"Inara ignored {journalEvent.EventName}: {exception.Message}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"Inara could not prepare {journalEvent.EventName}: {exception.Message}");
        }
    }

    private void QueueMappedEvents(
        InaraPublicationUpdate update,
        InaraPublicationOptions options,
        JObject entry,
        List<string> queuedNames,
        List<string> warnings)
    {
        var context = CreateContext(update);
        var currentSession = session;
        var canCollect = update.AllowPublishing
            && currentSession is not null
            && CanUpload(
                options.ApiKey,
                currentSession.IsLive,
                currentSession.IsBeta,
                mapper.InMulticrew);
        var mapped = mapper.Process(entry, context, canCollect);
        var credentials = currentSession?.GetCredentials(options.ApiKey);
        if (credentials is null || mapped.Count == 0)
        {
            return;
        }

        var dropped = queue.Enqueue(credentials.ApiKey, mapped);
        if (dropped > 0)
        {
            warnings.Add(
                $"Inara discarded {dropped} oldest queued event(s) to keep its local backlog bounded.");
        }

        queuedNames.AddRange(mapped.Select(item => item.Name));
        lock (sendStateSync)
        {
            nextSendAt ??= timeProvider.GetUtcNow() + SendInterval;
        }
    }

    public async Task<InaraPublicationResult> FlushAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        var completed = TakeCompletedSendResult();
        Task<InaraPublicationResult>? active;
        lock (sendStateSync)
        {
            active = activeSendTask;
        }

        if (active is null)
        {
            if (!TryStartBackgroundSend(
                force: true,
                out var started))
            {
                return completed;
            }

            active = started;
        }

        var sent = await active.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? completedCancellation = null;
        lock (sendStateSync)
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

    public Task<InaraPublicationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        lock (stopSync)
        {
            return stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    public void CancelPendingPublication()
    {
        if (stopping || disposed)
        {
            return;
        }

        ClearPublicationAuthorization();
        queue.TakeAll();
    }

    private async Task<InaraPublicationResult> StopCoreAsync(
        CancellationToken cancellationToken)
    {
        stopping = true;
        try
        {
            return await FinalizeCurrentSessionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            disposed = true;
            ClearPublicationAuthorization();
            queue.TakeAll();
            lifetimeCancellation.Cancel();
            if (ownsHttpClient)
            {
                httpClient.Dispose();
            }

            lifetimeCancellation.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // A caller explicitly cancelled graceful shutdown.
        }
    }

    internal static bool CanPrepareUpload(
        string? apiKey,
        bool isLive,
        bool isBeta)
    {
        return !string.IsNullOrWhiteSpace(apiKey)
            && isLive
            && !isBeta;
    }

    internal static bool CanUpload(
        string? apiKey,
        bool isLive,
        bool isBeta,
        bool inMulticrew)
    {
        return CanPrepareUpload(apiKey, isLive, isBeta)
            && !inMulticrew;
    }

    internal static bool IsBetaVersion(string? gameVersion)
    {
        return !string.IsNullOrWhiteSpace(gameVersion)
            && (gameVersion.Contains("beta", StringComparison.OrdinalIgnoreCase)
                || gameVersion.Contains(
                    "alpha",
                    StringComparison.OrdinalIgnoreCase));
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
            .TakeWhile(character => char.IsDigit(character) || character == '.')
            .ToArray());
        return Version.TryParse(numeric.TrimEnd('.'), out var version)
            && version.Major >= 4;
    }

    private bool JournalIdentityMatchesSession(JObject entry)
    {
        if (session is null)
        {
            return false;
        }

        var eventName = entry.Value<string>("event");
        var commander = eventName == "Commander"
            ? entry.Value<string>("Name")
            : eventName == "LoadGame"
                ? entry.Value<string>("Commander")
                : null;
        var frontierId = eventName is "Commander" or "LoadGame"
            ? entry.Value<string>("FID")
            : null;
        if (!string.IsNullOrWhiteSpace(commander)
            && !string.Equals(
                commander.Trim(),
                session.Commander,
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

    private void ApplyPublicationState(
        JournalEventEnvelope journalEvent,
        JObject entry)
    {
        var eventName = journalEvent.EventName;
        var multicrewToken = entry["Multicrew"];
        var journalSaysMulticrew = multicrewToken?.Type == JTokenType.Boolean
            && multicrewToken.Value<bool>();
        var entersOrContinuesMulticrew = mapper.InMulticrew
            || eventName is "JoinACrew" or "ChangeCrewRole"
            || journalSaysMulticrew;
        if (entersOrContinuesMulticrew
            && eventName is not "QuitACrew" and not "LoadGame" and not "Fileheader")
        {
            return;
        }

        publicationState.Apply(journalEvent);
    }

    private InaraContext CreateContext(InaraPublicationUpdate? update)
    {
        var useSharedFallback = update is not null
            && update.AllowSharedData
            && update.JournalEvents.Count == 1;
        return new InaraContext(
            session?.Commander,
            session?.FrontierId,
            publicationState.SystemName,
            publicationState.StationName,
            publicationState.BodyName ?? (useSharedFallback
                ? update!.Status?.BodyName ?? update.BodyName
                : null),
            publicationState.ShipType,
            publicationState.ShipId,
            publicationState.ShipName,
            publicationState.ShipIdent,
            useSharedFallback ? update!.Status?.InTaxi : null);
    }

    private bool IsSendDue()
    {
        lock (sendStateSync)
        {
            return activeSendTask is null
                && nextSendAt is { } deadline
                && timeProvider.GetUtcNow() >= deadline;
        }
    }

    private bool UpdatePublicationAuthorization(
        InaraPublicationOptions options,
        out bool authorizationChanged)
    {
        var canPrepare = session is not null
            && CanPrepareUpload(
                options.ApiKey,
                session.IsLive,
                session.IsBeta);
        var normalizedApiKey = canPrepare ? options.ApiKey!.Trim() : null;
        CancellationTokenSource? cancellation;
        lock (sendStateSync)
        {
            if (string.Equals(
                    authorizedApiKey,
                    normalizedApiKey,
                    StringComparison.Ordinal))
            {
                authorizationChanged = false;
                return canPrepare;
            }

            authorizationChanged = true;
            authorizedApiKey = normalizedApiKey;
            authorizationGeneration++;
            cancellation = activeSendCancellation;
            if (!canPrepare)
            {
                nextSendAt = null;
            }
        }

        cancellation?.Cancel();
        return canPrepare;
    }

    private void ClearPublicationAuthorization()
    {
        CancellationTokenSource? cancellation;
        lock (sendStateSync)
        {
            authorizedApiKey = null;
            authorizationGeneration++;
            nextSendAt = null;
            cancellation = activeSendCancellation;
        }

        cancellation?.Cancel();
    }

    private bool IsPublicationAuthorized(long generation)
    {
        lock (sendStateSync)
        {
            return !disposed
                && authorizedApiKey is not null
                && authorizationGeneration == generation;
        }
    }

    private bool TryStartBackgroundSend(
        bool force,
        out Task<InaraPublicationResult> sendTask)
    {
        lock (sendStateSync)
        {
            sendTask = activeSendTask!;
            if (disposed || activeSendTask is not null)
            {
                return sendTask is not null;
            }

            if (!force
                && (nextSendAt is null
                    || timeProvider.GetUtcNow() < nextSendAt.Value))
            {
                return false;
            }

            if (authorizedApiKey is not { } apiKey)
            {
                return false;
            }

            var generation = authorizationGeneration;
            activeSendCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    lifetimeCancellation.Token);
            var sendCancellation = activeSendCancellation;
            activeSendTask = Task.Run(
                () => SendPendingAsync(
                    apiKey,
                    generation,
                    sendCancellation.Token),
                CancellationToken.None);
            sendTask = activeSendTask;
            return true;
        }
    }

    private InaraPublicationResult TakeCompletedSendResult()
    {
        Task<InaraPublicationResult>? completed;
        CancellationTokenSource? completedCancellation;
        lock (sendStateSync)
        {
            if (activeSendTask?.IsCompleted != true)
            {
                return InaraPublicationResult.Empty;
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
            return InaraPublicationResult.Empty;
        }
        finally
        {
            completedCancellation?.Dispose();
        }
    }

    private async Task<InaraPublicationResult> WaitForActiveSendAsync(
        CancellationToken cancellationToken)
    {
        var completed = TakeCompletedSendResult();
        Task<InaraPublicationResult>? active;
        lock (sendStateSync)
        {
            active = activeSendTask;
        }

        if (active is null)
        {
            return completed;
        }

        var sent = await active.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? completedCancellation = null;
        lock (sendStateSync)
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

    private async Task<InaraPublicationResult> FinalizeCurrentSessionAsync(
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            return InaraPublicationResult.Empty;
        }

        var completed = await WaitForActiveSendAsync(cancellationToken)
            .ConfigureAwait(false);
        string? apiKey;
        lock (sendStateSync)
        {
            apiKey = authorizedApiKey;
        }

        var credentials = apiKey is null
            ? null
            : session.GetCredentials(apiKey);
        if (credentials is not null
            && CanUpload(
                credentials.ApiKey,
                session.IsLive,
                session.IsBeta,
                mapper.InMulticrew))
        {
            var finalEvents = mapper.Process(
                new JObject
                {
                    ["timestamp"] = timeProvider.GetUtcNow().ToString("O"),
                    ["event"] = "Shutdown",
                },
                CreateContext(update: null),
                collectEvents: true);
            if (finalEvents.Count > 0)
            {
                queue.Enqueue(credentials.ApiKey, finalEvents);
            }
        }

        var flushed = await FlushAsync(cancellationToken).ConfigureAwait(false);
        return Combine(completed, flushed);
    }

    private static InaraPublicationResult Combine(
        InaraPublicationResult first,
        InaraPublicationResult second)
    {
        return new InaraPublicationResult(
            first.QueuedEventCount + second.QueuedEventCount,
            first.AcceptedEventCount + second.AcceptedEventCount,
            second.PendingEventCount,
            first.QueuedEventNames.Concat(second.QueuedEventNames).ToArray(),
            first.Warnings.Concat(second.Warnings).ToArray());
    }

    private async Task<InaraPublicationResult> SendPendingAsync(
        string apiKey,
        long generation,
        CancellationToken cancellationToken)
    {
        var batch = queue.TakeBatch(
            apiKey,
            MaximumEventsPerRequest,
            out var discarded);
        lock (sendStateSync)
        {
            nextSendAt = null;
            activeBatchCount = batch.Count;
        }

        if (batch.Count == 0)
        {
            return discarded == 0
                ? InaraPublicationResult.Empty
                : CreateSendResult(
                    0,
                    [$"Inara discarded {discarded} queued event(s) after the commander API key changed or was cleared."]);
        }

        var warnings = new List<string>();
        if (discarded > 0)
        {
            warnings.Add(
                $"Inara discarded {discarded} queued event(s) after the commander API key changed or was cleared.");
        }
        try
        {
            return await SendBatchAsync(
                    apiKey,
                    generation,
                    batch,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            cancellationToken.IsCancellationRequested)
        {
            return HandleSendCancellation(generation, batch, warnings, exception);
        }
        catch (Exception exception)
        {
            return HandleSendException(batch, warnings, exception);
        }
        finally
        {
            lock (sendStateSync)
            {
                activeBatchCount = 0;
            }
        }
    }

    private async Task<InaraPublicationResult> SendBatchAsync(
        string apiKey,
        long generation,
        List<InaraQueuedEvent> batch,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!IsPublicationAuthorized(generation))
        {
            return DiscardUnauthorizedBatch(batch, warnings);
        }

        if (session is null
            || !CanPrepareUpload(
                apiKey,
                session.IsLive,
                session.IsBeta))
        {
            return InaraPublicationResult.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                apiKey,
                batch[0].ApiKey,
                StringComparison.Ordinal))
        {
            warnings.Add(
                $"Inara discarded {batch.Count} queued event(s) after the commander API key changed.");
            ScheduleNextAttempt(transientFailure: false);
            return CreateSendResult(0, warnings);
        }

        var credentials = session.GetCredentials(apiKey);
        if (credentials is null)
        {
            return InaraPublicationResult.Empty;
        }

        var payloadBytes = BuildBoundedPayload(
            credentials,
            batch,
            warnings);
        if (payloadBytes is null)
        {
            ScheduleNextAttempt(transientFailure: false);
            return CreateSendResult(0, warnings);
        }

        return await SendPayloadAsync(
                generation,
                batch,
                payloadBytes,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InaraPublicationResult> SendPayloadAsync(
        long generation,
        List<InaraQueuedEvent> batch,
        byte[] payloadBytes,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new ByteArrayContent(payloadBytes),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8",
            };
        if (!IsPublicationAuthorized(generation))
        {
            return DiscardUnauthorizedBatch(batch, warnings);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        return await InterpretSendResponseAsync(response, batch, warnings, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<InaraPublicationResult> InterpretSendResponseAsync(
        HttpResponseMessage response,
        List<InaraQueuedEvent> batch,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (IsTransient(response.StatusCode))
        {
            return DeferBatch(
                batch,
                warnings,
                $"Inara upload was deferred after HTTP {(int)response.StatusCode} ({SafeStatusText(response.ReasonPhrase)}); {batch.Count} event(s) were retained.",
                GetRetryAfter(response.Headers.RetryAfter));
        }

        if (!IsSuccess((int)response.StatusCode))
        {
            warnings.Add(
                $"Inara rejected {batch.Count} event(s) with HTTP {(int)response.StatusCode} ({SafeStatusText(response.ReasonPhrase)}).");
            ScheduleNextAttempt(transientFailure: false);
            return CreateSendResult(0, warnings);
        }

        var body = await ReadBoundedTextAsync(response.Content, cancellationToken)
            .ConfigureAwait(false);
        return ProcessInaraResponseBody(body, batch, warnings);
    }

    private InaraPublicationResult DiscardUnauthorizedBatch(
        List<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        warnings.Add(
            $"Inara discarded {batch.Count} event(s) after publication authorization changed.");
        return CreateSendResult(0, warnings);
    }

    private InaraPublicationResult HandleSendCancellation(
        long generation,
        List<InaraQueuedEvent> batch,
        List<string> warnings,
        OperationCanceledException exception)
    {
        if (!IsPublicationAuthorized(generation))
        {
            warnings.Add(
                $"Inara cancelled {batch.Count} event(s) after publication was disabled or its authorization changed.");
            return CreateSendResult(0, warnings);
        }

        Requeue(batch, warnings);
        throw exception;
    }

    private InaraPublicationResult HandleSendException(
        List<InaraQueuedEvent> batch,
        List<string> warnings,
        Exception exception)
    {
        Requeue(batch, warnings);
        ScheduleNextAttempt(transientFailure: true);
        warnings.Add(
            $"Inara upload was deferred ({exception.GetType().Name}); {batch.Count} event(s) were retained.");
        return CreateSendResult(0, warnings);
    }

    private byte[]? BuildBoundedPayload(
        InaraCredentials credentials,
        List<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        while (true)
        {
            var payload = InaraPayloadBuilder.Build(
                appVersion,
                credentials,
                batch.Select(item => item.Event).ToArray());
            var payloadBytes = Encoding.UTF8.GetBytes(
                payload.ToString(Formatting.None));
            if (payloadBytes.Length <= MaximumPayloadBytes)
            {
                lock (sendStateSync)
                {
                    activeBatchCount = batch.Count;
                }

                return payloadBytes;
            }

            if (batch.Count == 1)
            {
                warnings.Add(
                    $"Inara skipped one oversized event ({payloadBytes.Length:N0} bytes). No journal processing was affected.");
                batch.Clear();
                lock (sendStateSync)
                {
                    activeBatchCount = 0;
                }

                return null;
            }

            var splitAt = batch.Count / 2;
            var tail = batch.Skip(splitAt).ToArray();
            batch.RemoveRange(splitAt, batch.Count - splitAt);
            Requeue(tail, warnings);
        }
    }

    private void Requeue(
        IReadOnlyCollection<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        var dropped = queue.Requeue(batch);
        if (dropped > 0)
        {
            warnings.Add(
                $"Inara discarded {dropped} oldest queued event(s) to keep its local backlog bounded.");
        }
    }

    private void ScheduleNextAttempt(
        bool transientFailure,
        TimeSpan? retryAfter = null)
    {
        lock (sendStateSync)
        {
            if (transientFailure)
            {
                consecutiveTransientFailures++;
            }
            else
            {
                consecutiveTransientFailures = 0;
            }

            if (queue.Count == 0)
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
            if (retryAfter is { } requestedDelay && requestedDelay > delay)
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

    private InaraPublicationResult DeferBatch(
        IReadOnlyList<InaraQueuedEvent> batch,
        List<string> warnings,
        string warning,
        TimeSpan? retryAfter = null)
    {
        Requeue(batch, warnings);
        ScheduleNextAttempt(transientFailure: true, retryAfter);
        warnings.Add(warning);
        return CreateSendResult(0, warnings);
    }

    private InaraPublicationResult ProcessInaraResponseBody(
        string? body,
        List<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidDataException("Inara returned an empty response.");
        }

        var result = JObject.Parse(body);
        var headerStatus = result
            .SelectToken("header.eventStatus")
            ?.Value<int?>();
        var responseEvents = result["events"] as JArray;
        var responseIsComplete = headerStatus is not null
            && responseEvents?.Count == batch.Count
            && responseEvents.All(
                eventResult => eventResult is JObject responseEvent
                    && responseEvent["eventStatus"] is not null);
        if (!responseIsComplete)
        {
            throw new InvalidDataException(
                "Inara returned an incomplete response.");
        }

        if (!IsSuccess(headerStatus!.Value))
        {
            return HandleUnsuccessfulHeader(
                headerStatus.Value,
                result.SelectToken("header.eventStatusText")?.Value<string>(),
                batch,
                warnings);
        }

        return ApplyEventStatuses(responseEvents!, batch, warnings);
    }

    private InaraPublicationResult HandleUnsuccessfulHeader(
        int headerStatus,
        string? headerStatusText,
        List<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        var detail = SafeStatusText(headerStatusText);
        if (IsTransient(headerStatus))
        {
            Requeue(batch, warnings);
            ScheduleNextAttempt(transientFailure: true);
            warnings.Add(
                $"Inara deferred {batch.Count} event(s) with API status {headerStatus}: {detail}.");
        }
        else
        {
            warnings.Add(
                $"Inara rejected a batch of {batch.Count} event(s) with API status {headerStatus}: {detail}.");
            ScheduleNextAttempt(transientFailure: false);
        }

        return CreateSendResult(0, warnings);
    }

    private InaraPublicationResult ApplyEventStatuses(
        JArray responseEvents,
        List<InaraQueuedEvent> batch,
        List<string> warnings)
    {
        var statuses = responseEvents
            .Cast<JObject>()
            .Select((eventResult, index) => new
            {
                Index = index,
                Status = eventResult.Value<int>("eventStatus"),
                Text = SafeStatusText(
                    eventResult.Value<string>("eventStatusText")),
            })
            .ToArray();
        var transient = statuses
            .Where(item => IsTransient(item.Status))
            .Select(item => batch[item.Index])
            .ToArray();
        var rejected = statuses
            .Where(item => !IsSuccess(item.Status)
                && !IsTransient(item.Status))
            .ToArray();
        if (transient.Length > 0)
        {
            Requeue(transient, warnings);
            ScheduleNextAttempt(transientFailure: true);
            warnings.Add(
                $"Inara deferred {transient.Length} event(s); they were retained for retry.");
        }
        else
        {
            ScheduleNextAttempt(transientFailure: false);
        }

        if (rejected.Length > 0)
        {
            const int maximumReportedFailures = 10;
            foreach (var item in rejected.Take(maximumReportedFailures))
            {
                warnings.Add(
                    $"Inara rejected {batch[item.Index].Event.Name} with API status {item.Status}: {item.Text}.");
            }

            if (rejected.Length > maximumReportedFailures)
            {
                warnings.Add(
                    $"Inara rejected {rejected.Length - maximumReportedFailures} additional event(s).");
            }
        }

        var accepted = statuses.Count(item => IsSuccess(item.Status));
        return CreateSendResult(accepted, warnings);
    }

    private InaraPublicationResult CreateSendResult(
        int accepted,
        IReadOnlyList<string> warnings)
    {
        return new InaraPublicationResult(
            0,
            accepted,
            queue.Count,
            [],
            warnings);
    }

    private int GetPendingCount()
    {
        lock (sendStateSync)
        {
            return authorizedApiKey is null
                ? 0
                : queue.Count + activeBatchCount;
        }
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

    private static async Task<JObject> AddSidecarDataAsync(
        JObject entry,
        JournalEventEnvelope journalEvent,
        CargoSnapshot? cargo,
        string? journalPath,
        bool allowSharedData,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var needsCargo = journalEvent.EventName == "Cargo"
            && string.Equals(
                entry.Value<string>("Vessel"),
                "Ship",
                StringComparison.OrdinalIgnoreCase)
            && entry["Inventory"] is not JArray;
        var lockerSections = new[]
        {
            "Items",
            "Components",
            "Data",
            "Consumables",
        };
        var needsLocker = journalEvent.EventName == "ShipLocker"
            && lockerSections.Any(section => entry[section] is not JArray);
        if (!allowSharedData && (needsCargo || needsLocker))
        {
            return entry;
        }

        if (needsCargo
            && cargo is not null
            && string.Equals(
                cargo.Vessel,
                "Ship",
                StringComparison.OrdinalIgnoreCase))
        {
            var augmented = (JObject)entry.DeepClone();
            augmented["Inventory"] = new JArray(cargo.Inventory.Select(item =>
                new JObject
                {
                    ["Name"] = item.Name,
                    ["Name_Localised"] = item.LocalizedName,
                    ["Count"] = item.Count,
                    ["Stolen"] = item.Stolen,
                }));
            return augmented;
        }

        if (!needsLocker || string.IsNullOrWhiteSpace(journalPath))
        {
            return entry;
        }

        var directory = Path.GetDirectoryName(journalPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return entry;
        }

        var path = Path.Combine(directory, "ShipLocker.json");
        if (!File.Exists(path))
        {
            return entry;
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = await reader.ReadToEndAsync(cancellationToken)
                .ConfigureAwait(false);
            var sidecar = JObject.Parse(content);
            sidecar["event"] = journalEvent.EventName;
            sidecar["timestamp"] = entry["timestamp"]?.DeepClone();
            return sidecar;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            warnings.Add(
                $"Inara could not read ShipLocker.json: {exception.Message}");
            return entry;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        return IsTransient((int)statusCode);
    }

    private static bool IsTransient(int statusCode)
    {
        return statusCode is 408 or 429 || statusCode >= 500;
    }

    private static bool IsSuccess(int statusCode)
    {
        return statusCode is >= 200 and <= 299;
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                $"Inara response exceeded {MaximumResponseBytes:N0} bytes.");
        }

        await using var input = await content.ReadAsStreamAsync(
                cancellationToken)
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

            if (output.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    $"Inara response exceeded {MaximumResponseBytes:N0} bytes.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }
}

public sealed record InaraPublicationOptions(
    string? ApiKey,
    string? CommanderName,
    string? FrontierId,
    string? GameVersion,
    bool IsOdyssey)
{
    public override string ToString() =>
        $"InaraPublicationOptions {{ HasApiKey = {!string.IsNullOrWhiteSpace(ApiKey)}, CommanderName = {CommanderName}, FrontierId = {FrontierId}, GameVersion = {GameVersion}, IsOdyssey = {IsOdyssey} }}";
}

public sealed record InaraPublicationUpdate(
    IReadOnlyList<JournalEventEnvelope> JournalEvents,
    EliteStatus? Status,
    CargoSnapshot? Cargo,
    string? JournalPath,
    bool AllowPublishing,
    bool AllowSharedData,
    string? SystemName,
    string? StationName,
    string? BodyName,
    string? ShipType,
    long? ShipId,
    string? ShipName,
    string? ShipIdent,
    InaraPublicationOptions Options);

public sealed record InaraPublicationResult(
    int QueuedEventCount,
    int AcceptedEventCount,
    int PendingEventCount,
    IReadOnlyList<string> QueuedEventNames,
    IReadOnlyList<string> Warnings)
{
    public static InaraPublicationResult Empty { get; } = new(
        0,
        0,
        0,
        [],
        []);
}
