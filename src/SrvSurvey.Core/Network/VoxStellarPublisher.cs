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

public sealed record VoxStellarPublicationResult(
    IReadOnlyList<string> QueuedEventNames,
    IReadOnlyList<string> Warnings)
{
    public static VoxStellarPublicationResult Empty { get; } = new([], []);
}

public interface IVoxStellarPublisher
{
    bool IsConfigured { get; }

    Task<VoxStellarPublicationResult> ApplyAsync(
        VoxStellarApplyRequest request,
        CancellationToken cancellationToken = default);

    void SetEnabled(bool enabled);
}

/// <summary>
/// Sends the exploration events accepted by EDMC-VoxStellar to VoxStellar's
/// signed journal webhook. Publication is memory-only and ordered; disabling
/// consent invalidates work that has not started sending.
/// </summary>
public sealed class VoxStellarPublisher : IVoxStellarPublisher, IDisposable
{
    private static readonly HashSet<string> AllowedEvents = new(
        StringComparer.Ordinal)
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

    private readonly object sync = new();
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly Uri endpoint;
    private readonly byte[] signingKey;
    private readonly ProductInfoHeaderValue userAgent;
    private readonly Action<string> log;
    private readonly Channel<QueuedUpload>? uploads;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly Task? workerTask;
    private bool enabled;
    private long consentGeneration;
    private bool disposed;

    public VoxStellarPublisher(
        string softwareVersion,
        string? sharedKey,
        HttpClient? client = null,
        Uri? endpoint = null,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(softwareVersion);
        this.client = client ?? CreateSharedClient();
        ownsClient = client is null;
        this.endpoint = endpoint ?? WellKnownUris.VoxStellarWebhook;
        signingKey = string.IsNullOrWhiteSpace(sharedKey)
            ? []
            : Encoding.UTF8.GetBytes(sharedKey.Trim());
        userAgent = new ProductInfoHeaderValue(
            "SrvSurvey-XP",
            NormalizeProductVersion(softwareVersion));
        this.log = log ?? (_ => { });

        if (IsConfigured)
        {
            uploads = Channel.CreateBounded<QueuedUpload>(
                new BoundedChannelOptions(4096)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                });
            workerTask = RunWorkerAsync();
        }
    }

    public bool IsConfigured => signingKey.Length > 0;

    public void SetEnabled(bool enabled)
    {
        lock (sync)
        {
            if (disposed || this.enabled == enabled)
            {
                return;
            }

            this.enabled = enabled;
            if (!enabled)
            {
                consentGeneration++;
            }
        }
    }

    public Task<VoxStellarPublicationResult> ApplyAsync(
        VoxStellarApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.JournalEvents);
        cancellationToken.ThrowIfCancellationRequested();
        SetEnabled(request.Enabled);

        if (!request.Enabled || !request.AllowPublishing)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        var matchingEvents = request.JournalEvents
            .Where(journalEvent => AllowedEvents.Contains(journalEvent.EventName))
            .ToArray();
        if (matchingEvents.Length == 0)
        {
            return Task.FromResult(VoxStellarPublicationResult.Empty);
        }

        if (!IsConfigured || uploads is null)
        {
            return Task.FromResult(new VoxStellarPublicationResult(
                [],
                ["VoxStellar sharing is enabled, but this build does not include the integration signing key."]));
        }

        var commanderName = request.CommanderName?.Trim();
        if (string.IsNullOrWhiteSpace(commanderName))
        {
            return Task.FromResult(new VoxStellarPublicationResult(
                [],
                ["VoxStellar did not queue exploration data because the active commander is unknown."]));
        }

        long generation;
        lock (sync)
        {
            if (disposed || !enabled)
            {
                return Task.FromResult(VoxStellarPublicationResult.Empty);
            }

            generation = consentGeneration;
        }

        var queued = new List<string>(matchingEvents.Length);
        var warnings = new List<string>();
        foreach (var journalEvent in matchingEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = SerializeBody(commanderName, journalEvent.Payload);
            if (uploads.Writer.TryWrite(new QueuedUpload(
                    generation,
                    journalEvent.EventName,
                    body)))
            {
                queued.Add(journalEvent.EventName);
            }
            else
            {
                warnings.Add(
                    $"VoxStellar could not queue {journalEvent.EventName} because its in-memory upload queue is full.");
            }
        }

        return Task.FromResult(new VoxStellarPublicationResult(queued, warnings));
    }

    private async Task RunWorkerAsync()
    {
        if (uploads is null)
        {
            return;
        }

        try
        {
            await foreach (var upload in uploads.Reader.ReadAllAsync(
                               lifetimeCancellation.Token))
            {
                try
                {
                    await SendAsync(upload, lifetimeCancellation.Token);
                }
                catch (OperationCanceledException)
                    when (lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException
                    || !lifetimeCancellation.IsCancellationRequested)
                {
                    WriteLog(
                        $"VoxStellar upload for {upload.EventName} failed: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
        }
    }

    private async Task SendAsync(
        QueuedUpload upload,
        CancellationToken cancellationToken)
    {
        var signature = Convert.ToHexString(
                HMACSHA256.HashData(signingKey, upload.Body))
            .ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(upload.Body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json");
        request.Headers.UserAgent.Add(userAgent);
        request.Headers.TryAddWithoutValidation("Signature", signature);
        request.Headers.ConnectionClose = true;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        Task<HttpResponseMessage> sendTask;
        lock (sync)
        {
            if (disposed
                || !enabled
                || consentGeneration != upload.ConsentGeneration)
            {
                return;
            }

            sendTask = client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }

        using var response = await sendTask;
        if (response.StatusCode == System.Net.HttpStatusCode.OK)
        {
            WriteLog($"VoxStellar accepted {upload.EventName}.");
        }
        else
        {
            WriteLog(
                $"VoxStellar rejected {upload.EventName} with HTTP {(int)response.StatusCode}.");
        }
    }

    private static byte[] SerializeBody(
        string commanderName,
        JsonElement payload)
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
        var normalized = value.Trim().Replace('+', '-');
        return string.Concat(normalized.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-'
                ? character
                : '-'));
    }

    private static HttpClient CreateSharedClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private void WriteLog(string message)
    {
        try
        {
            log(message);
        }
        catch
        {
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
            enabled = false;
            consentGeneration++;
        }

        uploads?.Writer.TryComplete();
        lifetimeCancellation.Cancel();
        try
        {
            workerTask?.Wait(
                TimeSpan.FromSeconds(2),
                CancellationToken.None);
        }
        catch (AggregateException exception)
            when (exception.InnerExceptions.All(inner =>
                inner is OperationCanceledException))
        {
        }

        lifetimeCancellation.Dispose();
        CryptographicOperations.ZeroMemory(signingKey);
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private sealed record QueuedUpload(
        long ConsentGeneration,
        string EventName,
        byte[] Body);
}
