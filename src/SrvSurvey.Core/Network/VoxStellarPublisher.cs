using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Network;

public sealed class VoxStellarApplyRequest
{
    public required IReadOnlyList<JournalEventEnvelope> JournalEvents { get; init; }

    public string? CommanderName { get; init; }

    /// <summary>Identifies the journal commander independently of a display name.</summary>
    public string? FrontierId { get; init; }

    public bool Enabled { get; init; }

    public bool AllowPublishing { get; init; }
}

public sealed record VoxStellarPublicationResult(IReadOnlyList<string> QueuedEventNames, IReadOnlyList<string> Warnings)
{
    public static VoxStellarPublicationResult Empty { get; } = new([], []);
}

public interface IVoxStellarPublisher
{
    bool IsConfigured { get; }

    Task<VoxStellarPublicationResult> ApplyAsync(
        VoxStellarApplyRequest request,
        CancellationToken cancellationToken = default
    );

    void SetEnabled(bool enabled);
}

/// <summary>
/// Sends the exploration events accepted by EDMC-VoxStellar to VoxStellar's
/// signed journal webhook. Publication retains captured bodies locally; disabling
/// consent invalidates work that has not started sending. Bounded timed batches
/// share connections and produce one outcome summary without changing the wire format.
/// </summary>
public sealed class VoxStellarPublisher : IVoxStellarPublisher, IDisposable
{
    public static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(5);
    internal const int MaximumBatchSize = 100;
    private static readonly HashSet<string> AllowedEvents = new(StringComparer.Ordinal)
    {
        "Scan",
        "FSDTarget",
        "FSDJump",
        "FSSDiscoveryScan",
        "SAASignalsFound",
        "ScanOrganic",
        "ScanBaryCentre",
        "CodexEntry",
    };

    private readonly Lock sync = new();
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Uri endpoint;
    private readonly byte[] signingKey;
    private readonly ProductInfoHeaderValue userAgent;
    private readonly Action<string> log;
    private readonly Channel<QueuedUpload>? uploads;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly CancellationTokenSource batchingCancellation = new();
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan batchInterval;
    private readonly Task? workerTask;
    private readonly string? outboxDirectory;
    private readonly Dictionary<string, JournalUploadOutbox<StoredUpload>> outboxes = new(
        StringComparer.OrdinalIgnoreCase
    );
    private bool enabled;
    private bool consentInitialized;
    private long consentGeneration;
    private bool disposed;
    private bool stopping;
    private bool disposing;

    /// <summary>Creates an in-memory sender with injectable HTTP, timing, and diagnostics.</summary>
    public VoxStellarPublisher(
        string softwareVersion,
        string? sharedKey,
        HttpClient? client = null,
        Uri? endpoint = null,
        Action<string>? log = null,
        TimeProvider? timeProvider = null,
        TimeSpan? batchInterval = null
    )
        : this(
            softwareVersion,
            sharedKey,
            new VoxStellarPublisherOptions
            {
                Client = client,
                Endpoint = endpoint,
                Log = log,
                TimeProvider = timeProvider,
                BatchInterval = batchInterval,
            }
        ) { }

    /// <summary>Creates a sender with optional private durable storage while preserving the provider's signed webhook format.</summary>
    public VoxStellarPublisher(string softwareVersion, string? sharedKey, VoxStellarPublisherOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.batchInterval = options.BatchInterval ?? SendInterval;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.batchInterval, TimeSpan.Zero);
        this.timeProvider = options.TimeProvider ?? TimeProvider.System;
        this.client = options.Client ?? CreateSharedClient();
        ownsClient = options.Client is null;
        this.endpoint = options.Endpoint ?? WellKnownUris.VoxStellarWebhook;
        signingKey = string.IsNullOrWhiteSpace(sharedKey) ? [] : Encoding.UTF8.GetBytes(sharedKey.Trim());
        userAgent = new ProductInfoHeaderValue("SrvSurvey-XP", NormalizeProductVersion(softwareVersion));
        this.log = options.Log ?? (_ => { });
        outboxDirectory = options.OutboxDirectory;

        if (IsConfigured)
        {
            uploads = Channel.CreateBounded<QueuedUpload>(
                new BoundedChannelOptions(4096)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                }
            );
            workerTask = Task.Run(RunWorkerAsync, lifetimeCancellation.Token);
        }
    }

    public bool IsConfigured => signingKey.Length > 0;

    /// <summary>Changes publication consent and invalidates queued work and retained reports on explicit opt-out.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (disposed || (stopping && enabled) || (consentInitialized && this.enabled == enabled))
            {
                return;
            }

            consentInitialized = true;
            this.enabled = enabled;
            if (enabled)
            {
                foreach (JournalUploadOutbox<StoredUpload> outbox in outboxes.Values)
                {
                    outbox.AcceptConsent();
                }
            }
            if (!enabled)
            {
                consentGeneration++;
                foreach (JournalUploadOutbox<StoredUpload> outbox in outboxes.Values)
                {
                    outbox.ClearAll();
                }
                if (outboxes.Count == 0 && outboxDirectory is not null)
                {
                    using var unbound = new JournalUploadOutbox<StoredUpload>(
                        outboxDirectory,
                        "voxstellar",
                        item => item.Identity,
                        ValidateStoredUpload,
                        WriteLog
                    );
                    unbound.ClearAll();
                }
                // Consent revocation also frees bounded queue capacity for a later opt-in.
                while (uploads?.Reader.TryRead(out _) == true)
                {
                    // Discard revoked uploads; the worker rechecks any event it already took.
                }
            }
        }
    }

    /// <summary>Recovers eligible captured reports and queues opted-in live events for the attributable commander.</summary>
    public Task<VoxStellarPublicationResult> ApplyAsync(
        VoxStellarApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        cancellationToken.ThrowIfCancellationRequested();
        SetEnabled(request.Enabled);

        if (!request.Enabled)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        JournalEventEnvelope[] matchingEvents = request
            .JournalEvents.TakeWhile(journalEvent => MatchesCommander(journalEvent, request))
            .Where(journalEvent => AllowedEvents.Contains(journalEvent.EventName))
            .ToArray();
        if (matchingEvents.Length == 0 && outboxDirectory is null)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        if (!IsConfigured || uploads is null)
        {
            if (!request.AllowPublishing)
            {
                return Task.FromResult(VoxStellarPublicationResult.Empty);
            }
            return Task.FromResult(
                new VoxStellarPublicationResult(
                    [],
                    ["VoxStellar sharing is enabled, but this build does not include the integration signing key."]
                )
            );
        }

        string? commanderName = request.CommanderName?.Trim();
        if (string.IsNullOrWhiteSpace(commanderName))
        {
            return Task.FromResult(
                new VoxStellarPublicationResult(
                    [],
                    ["VoxStellar did not queue exploration data because the active commander is unknown."]
                )
            );
        }

        long generation;
        lock (sync)
        {
            if (disposed || stopping || !enabled)
            {
                return Task.FromResult(VoxStellarPublicationResult.Empty);
            }

            generation = consentGeneration;
            if (!RestoreUploads(commanderName))
            {
                return Task.FromResult(
                    new VoxStellarPublicationResult(
                        [],
                        ["VoxStellar delivery is paused because its retained queue is unavailable."]
                    )
                );
            }
        }
        if (!request.AllowPublishing)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        return Task.FromResult(
            QueueEvents(uploads.Writer, generation, commanderName, matchingEvents, cancellationToken)
        );
    }

    /// <summary>Stops at a foreign commander boundary so subsequent records cannot inherit the requested commander's name.</summary>
    private static bool MatchesCommander(JournalEventEnvelope entry, VoxStellarApplyRequest request)
    {
        if (entry.EventName is not ("Commander" or "LoadGame"))
        {
            return true;
        }
        string nameKey = entry.EventName == "Commander" ? "Name" : "Commander";
        return MatchesField(entry, nameKey, request.CommanderName) && MatchesField(entry, "FID", request.FrontierId);
    }

    /// <summary>Rejects mismatching or malformed declared identities without requiring optional fields in older records.</summary>
    private static bool MatchesField(JournalEventEnvelope entry, string key, string? expected) =>
        !entry.Payload.TryGetProperty(key, out JsonElement actual)
        || (
            actual.ValueKind == JsonValueKind.String
            && (
                string.IsNullOrWhiteSpace(expected)
                || string.Equals(actual.GetString()?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase)
            )
        );

    /// <summary>Persists immutable webhook bodies before queueing them and reports failures without stopping journal processing.</summary>
    private VoxStellarPublicationResult QueueEvents(
        ChannelWriter<QueuedUpload> writer,
        long generation,
        string commanderName,
        JournalEventEnvelope[] matchingEvents,
        CancellationToken cancellationToken
    )
    {
        var queued = new List<string>(matchingEvents.Length);
        var warnings = new List<string>();
        int dropped = 0;
        foreach (JournalEventEnvelope? journalEvent in matchingEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] body = SerializeBody(commanderName, journalEvent.Payload);
            lock (sync)
            {
                if (disposed || stopping || !enabled || consentGeneration != generation)
                {
                    break;
                }
                string identity = JournalUploadIdentity.Capture(journalEvent);
                JournalUploadOutbox<StoredUpload>? outbox = outboxes.GetValueOrDefault(commanderName);
                try
                {
                    if (
                        outbox is not null
                        && outbox.Enqueue([new StoredUpload(identity, commanderName, body)]).Count == 0
                    )
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    dropped++;
                    continue;
                }
                if (writer.TryWrite(new QueuedUpload(generation, body, commanderName, identity)))
                {
                    queued.Add(journalEvent.EventName);
                }
                else
                {
                    dropped++;
                }
            }
        }
        if (dropped > 0)
        {
            warnings.Add(
                $"VoxStellar could not queue {dropped} event(s) because its in-memory queue or durable outbox was unavailable."
            );
        }

        return new VoxStellarPublicationResult(queued, warnings);
    }

    /// <summary>Recovers signed-webhook bodies only for their captured commander and keeps the signing key in memory.</summary>
    private bool RestoreUploads(string commander)
    {
        if (outboxDirectory is null)
        {
            return true;
        }
        if (!outboxes.TryGetValue(commander, out JournalUploadOutbox<StoredUpload>? outbox))
        {
            outbox = new JournalUploadOutbox<StoredUpload>(
                outboxDirectory,
                "voxstellar",
                item => item.Identity,
                ValidateStoredUpload,
                WriteLog
            );
            outboxes.Add(commander, outbox);
        }
        if (!outbox.TryOpen(commander.ToUpperInvariant(), out IReadOnlyList<StoredUpload>? recovered))
        {
            return false;
        }
        if (
            recovered is not null
            && recovered.Any(item =>
                !uploads!.Writer.TryWrite(new QueuedUpload(consentGeneration, item.Body, commander, item.Identity))
            )
        )
        {
            WriteLog("VoxStellar retained more reports than fit in its active queue; remaining reports stay on disk.");
        }
        return true;
    }

    /// <summary>Validates retained webhook identity and supported journal content before signing recovered evidence.</summary>
    private static bool ValidateStoredUpload(StoredUpload item)
    {
        if (
            string.IsNullOrWhiteSpace(item.Identity)
            || string.IsNullOrWhiteSpace(item.Commander)
            || item.Body is null
            || item.Body.Length > 1024 * 1024
        )
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(item.Body);
            return document.RootElement.GetProperty("commander").GetString() == item.Commander
                && AllowedEvents.Contains(
                    document.RootElement.GetProperty("data").GetProperty("event").GetString() ?? ""
                );
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Retains transient outcomes with a cooldown while allowing final acknowledgements to retire the exact payload.</summary>
    private void RecordOutcome(QueuedUpload upload, int? status, TimeSpan? retryAfter = null)
    {
        lock (sync)
        {
            if (disposed || consentGeneration != upload.ConsentGeneration)
            {
                return;
            }
            JournalUploadOutbox<StoredUpload>? outbox = outboxes.GetValueOrDefault(upload.Commander);
            bool transient = status is null or 408 or 429 or >= 500;
            if (!transient)
            {
                outbox?.Complete([upload.Identity]);
                return;
            }
            TimeSpan retry =
                retryAfter ?? TimeSpan.FromSeconds(Math.Min(300, 10 * (1 << Math.Min(upload.Attempts, 5))));
            DateTimeOffset deadline = timeProvider.GetUtcNow() + retry;
            outbox?.DeferUntil(deadline);
            if (!stopping && IsAuthorized(upload))
            {
                uploads?.Writer.TryWrite(upload with { Attempts = upload.Attempts + 1, NotBefore = deadline });
            }
        }
    }

    private async Task RunWorkerAsync()
    {
        if (uploads is null)
        {
            return;
        }

        try
        {
            await foreach (QueuedUpload first in uploads.Reader.ReadAllAsync(lifetimeCancellation.Token))
            {
                if (!IsAuthorized(first))
                {
                    continue;
                }
                try
                {
                    await Task.Delay(batchInterval, timeProvider, batchingCancellation.Token);
                }
                catch (OperationCanceledException) when (batchingCancellation.IsCancellationRequested)
                {
                    // Closing the queue flushes buffered events without waiting another interval.
                }

                List<QueuedUpload> batch = TakeAuthorizedBatch(first, uploads.Reader);
                if (batch.Count > 0)
                {
                    await SendBatchAsync(batch, lifetimeCancellation.Token);
                }
            }
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
            // Cancellation is the expected worker result during shutdown.
        }
    }

    private List<QueuedUpload> TakeAuthorizedBatch(QueuedUpload first, ChannelReader<QueuedUpload> reader)
    {
        var batch = new List<QueuedUpload>(MaximumBatchSize);
        if (IsAuthorized(first))
        {
            batch.Add(first);
        }
        while (batch.Count < MaximumBatchSize && reader.TryRead(out QueuedUpload? next))
        {
            if (IsAuthorized(next))
            {
                batch.Add(next);
            }
        }
        return batch;
    }

    /// <summary>Checks the captured consent generation before work may access transport or retained queues.</summary>
    private bool IsAuthorized(QueuedUpload upload)
    {
        lock (sync)
        {
            return !disposed
                && enabled
                && consentGeneration == upload.ConsentGeneration
                && (outboxes.GetValueOrDefault(upload.Commander)?.HasConsent ?? true);
        }
    }

    /// <summary>Sends captured webhook bodies and checkpoints final outcomes while retaining retryable failures.</summary>
    private async Task SendBatchAsync(IReadOnlyList<QueuedUpload> batch, CancellationToken cancellationToken)
    {
        int accepted = 0;
        int rejected = 0;
        int failed = 0;
        var details = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            foreach (QueuedUpload upload in batch)
            {
                try
                {
                    int? status = await SendAsync(upload, cancellationToken);
                    if (status == 200)
                    {
                        accepted++;
                    }
                    else if (status is { } code)
                    {
                        rejected++;
                        AddOutcome(details, $"HTTP {code}");
                    }
                }
                catch (Exception exception)
                    when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    failed++;
                    try
                    {
                        RecordOutcome(upload, null);
                    }
                    catch (Exception storageError) when (storageError is IOException or UnauthorizedAccessException)
                    {
                        WriteLog("VoxStellar retained queue could not checkpoint a retry: " + storageError.Message);
                    }
                    AddOutcome(details, exception.GetType().Name);
                }
            }
        }
        finally
        {
            if (accepted + rejected + failed > 0)
            {
                string detailText =
                    details.Count == 0
                        ? string.Empty
                        : " " + string.Join(", ", details.Select(pair => $"{pair.Key}: {pair.Value}")) + ".";
                WriteLog(
                    $"VoxStellar upload batch: {accepted} accepted, {rejected} rejected, {failed} failed." + detailText
                );
            }
        }
    }

    private static void AddOutcome(Dictionary<string, int> outcomes, string outcome)
    {
        outcomes.TryGetValue(outcome, out int count);
        outcomes[outcome] = count + 1;
    }

    /// <summary>Signs one captured body and uses successful HTTP headers as acknowledgement without reading an unused response body.</summary>
    private async Task<int?> SendAsync(QueuedUpload upload, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline;
        lock (sync)
        {
            deadline = outboxes.GetValueOrDefault(upload.Commander)?.NotBefore ?? upload.NotBefore;
        }
        TimeSpan wait = deadline - timeProvider.GetUtcNow();
        if (wait > TimeSpan.Zero)
        {
            if (stopping)
            {
                return null;
            }
            try
            {
                await Task.Delay(wait, timeProvider, batchingCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (batchingCancellation.IsCancellationRequested)
            {
                return null;
            }
        }
        string signature = Convert.ToHexString(HMACSHA256.HashData(signingKey, upload.Body)).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(upload.Body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.UserAgent.Add(userAgent);
        request.Headers.TryAddWithoutValidation("Signature", signature);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        Task<HttpResponseMessage> sendTask;
        lock (sync)
        {
            if (!IsAuthorized(upload))
            {
                return null;
            }

            // This webhook acknowledges through status alone; an unused body failure must not replay accepted data.
            sendTask = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        using HttpResponseMessage response = await sendTask;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            retryAfter = date - timeProvider.GetUtcNow();
        }
        if (retryAfter <= TimeSpan.Zero)
        {
            retryAfter = null;
        }
        RecordOutcome(upload, (int)response.StatusCode, retryAfter);
        return (int)response.StatusCode;
    }

    private static byte[] SerializeBody(string commanderName, JsonElement payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("commander", commanderName);
            writer.WritePropertyName("data");
            payload.WriteTo(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static string NormalizeProductVersion(string value)
    {
        string normalized = value.Trim().Replace('+', '-');
        return string.Concat(
            normalized.Select(character => char.IsLetterOrDigit(character) || character is '.' or '-' ? character : '-')
        );
    }

    private static HttpClient CreateSharedClient() =>
        new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 };

    private void WriteLog(string message)
    {
        try
        {
            log(message);
        }
        catch (Exception)
        {
            // Logging is best-effort; reporting this failure through the same sink would recurse.
        }
    }

    /// <summary>
    /// Stops accepting events and waits for queued, currently eligible uploads to finish.
    /// Reports in a configured outbox survive for the next session. Cancellation stops only
    /// the wait; callers must still dispose the publisher to release its resources.
    /// </summary>
    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (!disposed)
            {
                StopAcceptingWork();
            }
            return (workerTask ?? Task.CompletedTask).WaitAsync(cancellationToken);
        }
    }

    /// <summary>Closes the producer side once and releases any batching delay so the worker can drain accepted uploads.</summary>
    private void StopAcceptingWork()
    {
        if (stopping)
        {
            return;
        }
        stopping = true;
        uploads?.Writer.TryComplete();
        batchingCancellation.Cancel();
    }

    /// <summary>Stops batching and releases queue leases after bounded worker shutdown without discarding pending reports.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed || disposing)
            {
                return;
            }
            disposing = true;
            StopAcceptingWork();
        }

        // Consent remains authoritative while a bounded shutdown flush drains queued work.
        WaitForWorker();
        lock (sync)
        {
            disposed = true;
            enabled = false;
            consentGeneration++;
        }
        lifetimeCancellation.Cancel();
        WaitForWorker();
        batchingCancellation.Dispose();
        lifetimeCancellation.Dispose();
        foreach (JournalUploadOutbox<StoredUpload> outbox in outboxes.Values)
        {
            outbox.Dispose();
        }
        CryptographicOperations.ZeroMemory(signingKey);
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private void WaitForWorker()
    {
        try
        {
            workerTask?.Wait(TimeSpan.FromSeconds(2), CancellationToken.None);
        }
        catch (AggregateException exception)
            when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
            // Cancellation is the expected worker result during disposal.
        }
    }

    /// <summary>Retains immutable webhook data without credentials or signatures.</summary>
    private sealed record StoredUpload(string Identity, string Commander, byte[] Body);

    /// <summary>Associates a captured upload with consent and bounded retry scheduling.</summary>
    private sealed record QueuedUpload(long ConsentGeneration, byte[] Body, string Commander, string Identity)
    {
        /// <summary>Counts transient failures for bounded retry backoff.</summary>
        public int Attempts { get; init; }

        /// <summary>Defers the captured body until its provider cooldown expires.</summary>
        public DateTimeOffset NotBefore { get; init; }
    }
}
