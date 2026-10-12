using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.EdAstro;

/// <summary>
/// Sends consented live journal data using EDDiscovery's EDAstro JSON envelope.
/// A bounded memory queue retains transient failures, sessions capture their own
/// expansion flag, and the provider's event list is intersected with disclosed
/// local types. No HTTP or per-event logging occurs on the journal thread.
/// </summary>
public sealed class EdAstroPublisher : IEdAstroPublisher
{
    /// <summary>Application identity sent in every journal envelope.</summary>
    public const string ApplicationName = "SrvSurvey-XP";

    /// <summary>EDDiscovery's maximum number of journal objects per POST.</summary>
    public const int MaximumBatchSize = 200;

    /// <summary>Minimum spacing between requests, matching EDDiscovery's ten-second pacing.</summary>
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(10);
    private const int MaximumQueueSize = 4096;
    private const int MaximumPayloadBytes = 1024 * 1024;
    private const int MaximumResponseBytes = 128 * 1024;
    private static readonly TimeSpan EventListLifetime = TimeSpan.FromDays(1);
    private static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(15);
    private readonly Lock sync = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly HttpClient client;
    private readonly EdAstroTrafficBudget trafficBudget;
    private readonly bool ownsClient;
    private readonly string version;
    private readonly Action<string> log;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task? worker;
    private readonly List<QueuedEvent> queue = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly Queue<string> seenOrder = new();
    private HashSet<string> acceptedEvents = new(StringComparer.Ordinal);
    private DateTimeOffset eventListExpiresAt;
    private DateTimeOffset nextRequestAt;
    private DateTimeOffset nextSummaryAt;
    private CancellationTokenSource? activeRequest;
    private string? lastProblem;
    private int completedCount;
    private int failureCount;
    private long generation;
    private bool enabled;
    private bool suspended;
    private bool disposed;

    /// <summary>Creates an initially disabled publisher; injected HTTP and time keep delivery testable.</summary>
    public EdAstroPublisher(
        string softwareVersion,
        HttpClient? client = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? utcNow = null,
        bool automaticProcessing = true,
        EdAstroTrafficBudget? trafficBudget = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        version = softwareVersion;
        this.client = client ?? new HttpClient();
        this.trafficBudget = trafficBudget ?? EdAstroTrafficBudget.Shared;
        ownsClient = client is null;
        this.log = log ?? (_ => { });
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        nextSummaryAt = this.utcNow() + LogInterval;
        if (automaticProcessing)
        {
            worker = Task.Run(RunWorkerAsync, CancellationToken.None);
        }
    }

    /// <summary>Number of journal events retained locally, including the active batch.</summary>
    public int PendingCount
    {
        get
        {
            lock (sync)
            {
                return queue.Count;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (disposed || this.enabled == enabled)
            {
                return;
            }
            this.enabled = enabled;
            generation++;
            if (!enabled)
            {
                queue.Clear();
                seen.Clear();
                seenOrder.Clear();
                activeRequest?.Cancel();
            }
        }
    }

    /// <inheritdoc />
    public void SetSuspended(bool suspended)
    {
        lock (sync)
        {
            if (disposed || this.suspended == suspended)
            {
                return;
            }
            this.suspended = suspended;
            generation++;
            if (suspended)
            {
                activeRequest?.Cancel();
            }
        }
    }

    /// <inheritdoc />
    public void Apply(EdAstroApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        lock (sync)
        {
            if (
                disposed
                || !enabled
                || suspended
                || !request.AllowPublishing
                || !request.IsLiveGame
                || string.IsNullOrWhiteSpace(request.CommanderName)
            )
            {
                return;
            }
            string session = request.CommanderName + "\n" + request.JournalPath;
            foreach (JournalEventEnvelope entry in request.JournalEvents)
            {
                if (IsOtherCommander(entry, request.CommanderName))
                {
                    break;
                }
                QueueEvent(entry, session, request.IsOdyssey);
            }
        }
    }

    /// <summary>Detects a LoadGame boundary that belongs to another commander before collecting subsequent events.</summary>
    private static bool IsOtherCommander(JournalEventEnvelope entry, string commander) =>
        entry.EventName == "LoadGame"
        && entry.Payload.TryGetProperty("Commander", out JsonElement value)
        && !string.Equals(value.GetString(), commander, StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies a disclosed event into the bounded queue and suppresses repeated journal evidence.</summary>
    private void QueueEvent(JournalEventEnvelope entry, string session, bool odyssey)
    {
        JsonObject? data = EdAstroJournalData.Prepare(entry);
        if (data is null)
        {
            return;
        }
        string json = data.ToJsonString();
        if (
            Encoding.UTF8.GetByteCount(json) > MaximumPayloadBytes / 2
            || queue.Count >= MaximumQueueSize
            || queue.Sum(item => Encoding.UTF8.GetByteCount(item.Json)) + Encoding.UTF8.GetByteCount(json)
                > 4 * MaximumPayloadBytes
        )
        {
            ReportProblem("EDAstro's bounded local queue is full or an event is too large; new data was skipped.");
            return;
        }
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session + "\n" + json)));
        if (!seen.Add(identity))
        {
            return;
        }
        seenOrder.Enqueue(identity);
        while (seenOrder.Count > MaximumQueueSize)
        {
            seen.Remove(seenOrder.Dequeue());
        }
        queue.Add(new QueuedEvent(session, odyssey, json, utcNow()));
    }

    /// <summary>Processes at most one paced request; used by the worker and deterministic delivery tests.</summary>
    internal async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
        }
        try
        {
            if (!await sending.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        try
        {
            await ProcessRequestAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sending.Release();
        }
    }

    /// <summary>Owns one cancellation-scoped request and retains queued reports when delivery fails.</summary>
    private async Task ProcessRequestAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource cancellation;
        long capturedGeneration;
        bool refresh;
        lock (sync)
        {
            WriteActivitySummary();
            queue.RemoveAll(item => utcNow() - item.QueuedAt > TimeSpan.FromDays(1));
            if (!CanRequest())
            {
                return;
            }
            capturedGeneration = generation;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            cancellation.CancelAfter(TimeSpan.FromSeconds(25));
            activeRequest = cancellation;
            refresh = utcNow() >= eventListExpiresAt;

            nextRequestAt = utcNow() + SendInterval;
        }
        using (cancellation)
        {
            try
            {
                if (refresh)
                {
                    await RefreshAcceptedEventsAsync(capturedGeneration, cancellation.Token).ConfigureAwait(false);
                }
                else
                {
                    await SendBatchAsync(capturedGeneration, cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                lock (sync)
                {
                    if (IsCurrent(capturedGeneration))
                    {
                        DeferFailure("EDAstro request timed out; queued data will retry.");
                    }
                }
            }
            catch (Exception exception)
                when (exception
                        is HttpRequestException
                            or IOException
                            or JsonException
                            or InvalidDataException
                            or FormatException
                            or KeyNotFoundException
                            or InvalidOperationException
                )
            {
                lock (sync)
                {
                    if (IsCurrent(capturedGeneration))
                    {
                        DeferFailure("EDAstro delivery is unavailable; queued data will retry.");
                    }
                }
            }
            finally
            {
                lock (sync)
                {
                    activeRequest = null;
                    nextRequestAt = Max(nextRequestAt, utcNow() + SendInterval);
                }
            }
        }
    }

    /// <summary>Reserves shared provider quota only when consent, attribution, pending data and local pacing permit a request.</summary>
    private bool CanRequest()
    {
        DateTimeOffset now = utcNow();
        if (disposed || !enabled || suspended || queue.Count == 0 || now < nextRequestAt)
        {
            return false;
        }
        if (trafficBudget.TryAcquire(now, out DateTimeOffset retryAt))
        {
            return true;
        }
        nextRequestAt = Max(nextRequestAt, retryAt);
        return false;
    }

    /// <summary>Checks that a request still belongs to the current consent and attribution generation.</summary>
    private bool IsCurrent(long captured) => !disposed && enabled && !suspended && generation == captured;

    /// <summary>Refreshes the provider policy and discards reports outside the disclosed accepted-event intersection.</summary>
    private async Task RefreshAcceptedEventsAsync(long captured, CancellationToken token)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, WellKnownUris.EdastroAcceptedEvents);
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        ApplyTrafficHeaders(response);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await BoundedHttpContent
            .ReadBytesAsync(response.Content, MaximumResponseBytes, "EDAstro accepted events", token)
            .ConfigureAwait(false);
        string[] names =
            JsonSerializer.Deserialize<string[]>(bytes) ?? throw new InvalidDataException("Missing event list.");
        var accepted = names.Where(EdAstroJournalData.SupportedEvents.Contains).ToHashSet(StringComparer.Ordinal);
        lock (sync)
        {
            if (!IsCurrent(captured))
            {
                return;
            }
            acceptedEvents = accepted;
            eventListExpiresAt = utcNow() + EventListLifetime;
            failureCount = 0;
            queue.RemoveAll(item => !accepted.Contains(GetEventName(item.Json)));
            lastProblem = null;
        }
    }

    /// <summary>Serializes one captured session and expansion mode into the EDDiscovery-compatible journal envelope.</summary>
    private async Task SendBatchAsync(long captured, CancellationToken token)
    {
        QueuedEvent[] batch;
        string payload;
        lock (sync)
        {
            if (!IsCurrent(captured))
            {
                return;
            }
            queue.RemoveAll(item => !acceptedEvents.Contains(GetEventName(item.Json)));
            batch = queue.Count == 0 ? [] : TakeBatch();
            if (batch.Length == 0)
            {
                return;
            }
            var body = new JsonArray(
                new JsonObject
                {
                    ["appName"] = ApplicationName,
                    ["appVersion"] = version,
                    ["odyssey"] = batch[0].Odyssey,
                }
            );
            foreach (QueuedEvent item in batch)
            {
                body.Add(JsonNode.Parse(item.Json));
            }
            payload = body.ToJsonString();
        }
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, WellKnownUris.EdastroJournalUpload);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
            .ConfigureAwait(false);
        ApplyTrafficHeaders(response);
        byte[] bytes = await BoundedHttpContent
            .ReadBytesAsync(response.Content, MaximumResponseBytes, "EDAstro upload result", token)
            .ConfigureAwait(false);
        lock (sync)
        {
            if (!IsCurrent(captured))
            {
                return;
            }
            HandleOutcome(response.StatusCode, bytes, batch);
        }
    }

    /// <summary>Selects an ordered batch within event and byte limits without mixing captured sessions.</summary>
    private QueuedEvent[] TakeBatch()
    {
        QueuedEvent first = queue[0];
        var result = new List<QueuedEvent>();
        int size = 256;
        foreach (QueuedEvent item in queue)
        {
            size += Encoding.UTF8.GetByteCount(item.Json) + 1;
            if (
                item.Session != first.Session
                || item.Odyssey != first.Odyssey
                || size > MaximumPayloadBytes
                || result.Count == MaximumBatchSize
            )
            {
                break;
            }
            result.Add(item);
        }
        return result.ToArray();
    }

    /// <summary>Distinguishes accepted, unwanted, transiently deferred and rejected batches using the provider JSON result.</summary>
    private void HandleOutcome(HttpStatusCode status, byte[] bytes, QueuedEvent[] batch)
    {
        if ((int)status >= 500 || status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
        {
            DeferFailure("EDAstro deferred a journal batch; queued data will retry.");
            return;
        }
        int apiStatus = (int)status;
        if (status == HttpStatusCode.OK)
        {
            using var document = JsonDocument.Parse(bytes);
            if (!TryReadApiStatus(document.RootElement, out apiStatus))
            {
                DeferFailure("EDAstro delivery is unavailable; queued data will retry.");
                return;
            }
        }
        if (status != HttpStatusCode.OK || apiStatus is not (200 or 401))
        {
            DeferFailure(
                "EDAstro rejected a journal batch; sharing is paused for fifteen minutes. Check the application identity and provider support."
            );
            nextRequestAt = utcNow() + TimeSpan.FromMinutes(15);
            return;
        }
        queue.RemoveRange(0, batch.Length);
        completedCount += apiStatus == 200 ? batch.Length : 0;
        failureCount = 0;
        lastProblem = null;
    }

    /// <summary>Reads a numeric or textual result code without throwing on malformed provider status values.</summary>
    private static bool TryReadApiStatus(JsonElement root, out int status)
    {
        status = 0;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out JsonElement value))
        {
            return false;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out status),
            JsonValueKind.String => int.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out status
            ),
            _ => false,
        };
    }

    /// <summary>Reads the event discriminator from a previously validated queued journal object.</summary>
    private static string GetEventName(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("event").GetString()!;
    }

    /// <summary>Creates an unauthenticated provider request identified by the SrvSurvey-XP user agent.</summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, Uri endpoint)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(ApplicationName, version));
        return request;
    }

    /// <summary>Extends local and shared request cooldowns using the provider response headers.</summary>
    private void ApplyTrafficHeaders(HttpResponseMessage response)
    {
        DateTimeOffset cooldown = trafficBudget.Observe(response.Headers, utcNow());
        lock (sync)
        {
            nextRequestAt = Max(nextRequestAt, cooldown);
        }
    }

    /// <summary>Returns the later deadline so a shorter delay cannot weaken an existing cooldown.</summary>
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    /// <summary>Retains pending reports while increasing the retry delay and consolidating the failure diagnostic.</summary>
    private void DeferFailure(string message)
    {
        failureCount = Math.Min(failureCount + 1, 6);
        nextRequestAt = Max(
            nextRequestAt,
            utcNow() + TimeSpan.FromSeconds(Math.Min(900, 30 * Math.Pow(2, failureCount - 1)))
        );
        ReportProblem(message);
    }

    /// <summary>Logs a changed delivery problem once instead of repeating it on every journal tick.</summary>
    private void ReportProblem(string message)
    {
        if (lastProblem == message)
        {
            return;
        }
        lastProblem = message;
        WriteLog(message);
    }

    /// <summary>Emits and resets a periodic aggregate of delivered journal reports.</summary>
    private void WriteActivitySummary(bool force = false)
    {
        if (!force && utcNow() < nextSummaryAt)
        {
            return;
        }
        if (completedCount > 0)
        {
            WriteLog($"EDAstro submitted {completedCount:N0} journal events in the previous activity window.");
        }
        completedCount = 0;
        nextSummaryAt = utcNow() + LogInterval;
    }

    /// <summary>Keeps a failed diagnostic sink from interrupting journal contribution.</summary>
    private void WriteLog(string message)
    {
        try
        {
            log(message);
        }
        catch
        { /* Diagnostics must never interrupt journal contribution. */
        }
    }

    /// <summary>Runs paced background delivery until application shutdown cancels the worker.</summary>
    private async Task RunWorkerAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), lifetime.Token).ConfigureAwait(false);
                try
                {
                    await ProcessPendingAsync(lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    lock (sync)
                    {
                        DeferFailure("EDAstro delivery encountered an unexpected failure; queued data will retry.");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        { /* Normal shutdown. */
        }
    }

    /// <summary>Cancels delivery, discards the memory queue, and emits one final activity summary.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            generation++;
            queue.Clear();
            activeRequest?.Cancel();
            WriteActivitySummary(force: true);
        }
        lifetime.Cancel();
        worker?.GetAwaiter().GetResult();
        sending.Wait(CancellationToken.None);
        lock (sync)
        {
            activeRequest?.Dispose();
            activeRequest = null;
        }
        sending.Release();
        sending.Dispose();
        lifetime.Dispose();
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private sealed record QueuedEvent(string Session, bool Odyssey, string Json, DateTimeOffset QueuedAt);
}
