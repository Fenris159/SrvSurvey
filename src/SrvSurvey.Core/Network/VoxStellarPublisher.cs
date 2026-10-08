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
/// signed journal webhook. Publication is memory-only and ordered; disabling
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
    private bool enabled;
    private long consentGeneration;
    private bool disposed;
    private bool stopping;

    public VoxStellarPublisher(
        string softwareVersion,
        string? sharedKey,
        HttpClient? client = null,
        Uri? endpoint = null,
        Action<string>? log = null,
        TimeProvider? timeProvider = null,
        TimeSpan? batchInterval = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.batchInterval = batchInterval ?? SendInterval;
        ArgumentOutOfRangeException.ThrowIfLessThan(this.batchInterval, TimeSpan.Zero);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.client = client ?? CreateSharedClient();
        ownsClient = client is null;
        this.endpoint = endpoint ?? WellKnownUris.VoxStellarWebhook;
        signingKey = string.IsNullOrWhiteSpace(sharedKey) ? [] : Encoding.UTF8.GetBytes(sharedKey.Trim());
        userAgent = new ProductInfoHeaderValue("SrvSurvey-XP", NormalizeProductVersion(softwareVersion));
        this.log = log ?? (_ => { });

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

    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (disposed || (stopping && enabled) || this.enabled == enabled)
            {
                return;
            }

            this.enabled = enabled;
            if (!enabled)
            {
                consentGeneration++;
                // Consent revocation also frees bounded queue capacity for a later opt-in.
                while (uploads?.Reader.TryRead(out _) == true)
                {
                    // Discard revoked uploads; the worker rechecks any event it already took.
                }
            }
        }
    }

    public Task<VoxStellarPublicationResult> ApplyAsync(
        VoxStellarApplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        cancellationToken.ThrowIfCancellationRequested();
        SetEnabled(request.Enabled);

        if (!request.Enabled || !request.AllowPublishing)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        JournalEventEnvelope[] matchingEvents = request
            .JournalEvents.Where(journalEvent => AllowedEvents.Contains(journalEvent.EventName))
            .ToArray();
        if (matchingEvents.Length == 0)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        if (!IsConfigured || uploads is null)
        {
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
        }

        return Task.FromResult(
            QueueEvents(uploads.Writer, generation, commanderName, matchingEvents, cancellationToken)
        );
    }

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
                if (writer.TryWrite(new QueuedUpload(generation, body)))
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
            warnings.Add($"VoxStellar could not queue {dropped} event(s) because its in-memory upload queue is full.");
        }

        return new VoxStellarPublicationResult(queued, warnings);
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

    private bool IsAuthorized(QueuedUpload upload)
    {
        lock (sync)
        {
            return !disposed && enabled && consentGeneration == upload.ConsentGeneration;
        }
    }

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

    private async Task<int?> SendAsync(QueuedUpload upload, CancellationToken cancellationToken)
    {
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
            if (disposed || !enabled || consentGeneration != upload.ConsentGeneration)
            {
                return null;
            }

            // Read the response before reusing the connection, under HttpClient's request timeout.
            sendTask = client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }

        using HttpResponseMessage response = await sendTask;
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

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed || stopping)
            {
                return;
            }
            stopping = true;
        }

        // Consent remains authoritative while a bounded shutdown flush drains queued work.
        uploads?.Writer.TryComplete();
        batchingCancellation.Cancel();
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

    private sealed record QueuedUpload(long ConsentGeneration, byte[] Body);
}
