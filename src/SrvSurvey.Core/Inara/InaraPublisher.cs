using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Inara;

public interface IInaraPublisher : IDisposable
{
    Task<InaraPublicationResult> ApplyAsync(
        InaraPublicationUpdate update,
        CancellationToken cancellationToken = default);

    Task<InaraPublicationResult> FlushAsync(
        InaraPublicationOptions options,
        CancellationToken cancellationToken = default);
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

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string appVersion;
    private readonly TimeProvider timeProvider;
    private readonly InaraEventMapper mapper = new();
    private readonly InaraEventQueue queue = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private DateTimeOffset? nextSendAt;
    private bool disposed;

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
        ObjectDisposedException.ThrowIf(disposed, this);

        var warnings = new List<string>();
        var options = update.Options;
        if (!CanPrepareUpload(
                options.Enabled,
                options.ApiKey,
                options.GameVersion,
                options.IsOdyssey))
        {
            queue.TakeAll();
            nextSendAt = null;
        }

        var context = new InaraContext(
            options.CommanderName,
            options.FrontierId,
            update.SystemName,
            update.StationName,
            update.AllowSharedData
                ? update.Status?.BodyName ?? update.BodyName
                : null,
            update.ShipType,
            update.ShipId,
            update.ShipName,
            update.ShipIdent,
            update.AllowSharedData ? update.Status?.InTaxi : null);

        var queuedNames = new List<string>();
        foreach (var journalEvent in update.JournalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = JObject.Parse(journalEvent.RawJson);
                if (CanPrepareUpload(
                        options.Enabled,
                        options.ApiKey,
                        options.GameVersion,
                        options.IsOdyssey))
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

                var canCollect = update.AllowPublishing
                    && CanUpload(
                        options.Enabled,
                        options.ApiKey,
                        options.GameVersion,
                        options.IsOdyssey,
                        mapper.InMulticrew);
                var mapped = mapper.Process(entry, context, canCollect);
                var credentials = GetCredentials(options);
                if (credentials is not null && mapped.Count > 0)
                {
                    queue.Enqueue(credentials, mapped);
                    queuedNames.AddRange(mapped.Select(item => item.Name));
                    nextSendAt ??= timeProvider.GetUtcNow() + SendInterval;
                }
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

        var forceFlush = update.AllowPublishing
            && update.JournalEvents.Any(item => item.EventName == "Shutdown");
        var due = nextSendAt is { } deadline
            && timeProvider.GetUtcNow() >= deadline;
        var sent = forceFlush || due
            ? await SendPendingAsync(options, cancellationToken).ConfigureAwait(false)
            : InaraPublicationResult.Empty;
        warnings.AddRange(sent.Warnings);

        return new InaraPublicationResult(
            queuedNames.Count,
            sent.AcceptedEventCount,
            queue.Count,
            queuedNames.Distinct(StringComparer.Ordinal).ToArray(),
            warnings);
    }

    public Task<InaraPublicationResult> FlushAsync(
        InaraPublicationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(disposed, this);
        return SendPendingAsync(options, cancellationToken);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        sendLock.Dispose();
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    internal static bool CanPrepareUpload(
        bool enabled,
        string? apiKey,
        string? gameVersion,
        bool isOdyssey)
    {
        return enabled
            && !string.IsNullOrWhiteSpace(apiKey)
            && IsLiveVersion(gameVersion, isOdyssey)
            && !IsBetaVersion(gameVersion);
    }

    internal static bool CanUpload(
        bool enabled,
        string? apiKey,
        string? gameVersion,
        bool isOdyssey,
        bool inMulticrew)
    {
        return CanPrepareUpload(enabled, apiKey, gameVersion, isOdyssey)
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

    private async Task<InaraPublicationResult> SendPendingAsync(
        InaraPublicationOptions options,
        CancellationToken cancellationToken)
    {
        if (!await sendLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return InaraPublicationResult.Empty;
        }

        try
        {
            var pending = queue.TakeAll();
            nextSendAt = null;
            if (pending.Count == 0)
            {
                return InaraPublicationResult.Empty;
            }

            if (!CanPrepareUpload(
                    options.Enabled,
                    options.ApiKey,
                    options.GameVersion,
                    options.IsOdyssey))
            {
                return InaraPublicationResult.Empty;
            }

            var accepted = 0;
            var warnings = new List<string>();
            foreach (var group in pending.GroupBy(item => item.Credentials))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = group.ToList();
                if (string.Equals(
                        options.CommanderName,
                        group.Key.Commander,
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        options.ApiKey?.Trim(),
                        group.Key.ApiKey,
                        StringComparison.Ordinal))
                {
                    warnings.Add(
                        $"Inara discarded {batch.Count} queued event(s) after the commander API key changed.");
                    continue;
                }

                try
                {
                    var payload = InaraPayloadBuilder.Build(
                        appVersion,
                        group.Key,
                        batch.Select(item => item.Event).ToArray(),
                        options.DeveloperTestMode);
                    using var content = new StringContent(
                        payload.ToString(Formatting.None),
                        Encoding.UTF8,
                        "application/json");
                    using var response = await httpClient.PostAsync(
                            Endpoint,
                            content,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (IsTransient(response.StatusCode))
                    {
                        Requeue(batch);
                        warnings.Add(
                            $"Inara upload was deferred after HTTP {(int)response.StatusCode}; {batch.Count} event(s) were retained.");
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        warnings.Add(
                            $"Inara rejected {batch.Count} event(s) with HTTP {(int)response.StatusCode}.");
                        continue;
                    }

                    var body = await response.Content
                        .ReadAsStringAsync(cancellationToken)
                        .ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(body))
                    {
                        Requeue(batch);
                        warnings.Add(
                            $"Inara returned an empty response; {batch.Count} event(s) were retained.");
                        continue;
                    }

                    var result = JObject.Parse(body);
                    var headerStatus = result
                        .SelectToken("header.eventStatus")
                        ?.Value<int?>();
                    var responseEvents = result["events"] as JArray;
                    var responseIsComplete = headerStatus is not null
                        && responseEvents?.Count == batch.Count
                        && responseEvents.OfType<JObject>().All(eventResult =>
                            eventResult["eventStatus"] is not null);
                    if (!responseIsComplete)
                    {
                        Requeue(batch);
                        warnings.Add(
                            $"Inara returned an incomplete response; {batch.Count} event(s) were retained.");
                        continue;
                    }

                    if (headerStatus >= 400)
                    {
                        warnings.Add(
                            $"Inara rejected a batch of {batch.Count} event(s) with API status {headerStatus}.");
                        continue;
                    }

                    var failed = responseEvents!
                        .OfType<JObject>()
                        .Select((eventResult, index) => new
                        {
                            Index = index,
                            Status = eventResult.Value<int?>("eventStatus"),
                        })
                        .Where(item => item.Status >= 400)
                        .ToArray();
                    accepted += batch.Count - failed.Length;
                    if (failed.Length > 0)
                    {
                        var names = failed
                            .Where(item => item.Index < batch.Count)
                            .Select(item => batch[item.Index].Event.Name)
                            .Distinct(StringComparer.Ordinal);
                        warnings.Add(
                            $"Inara rejected {failed.Length} event(s): {string.Join(", ", names)}.");
                    }
                }
                catch (Exception exception) when (
                    exception is HttpRequestException
                        or TaskCanceledException
                        or JsonException)
                {
                    Requeue(batch);
                    warnings.Add(
                        $"Inara upload was deferred ({exception.GetType().Name}); {batch.Count} event(s) were retained.");
                }
            }

            return new InaraPublicationResult(
                0,
                accepted,
                queue.Count,
                [],
                warnings);
        }
        finally
        {
            sendLock.Release();
        }
    }

    private void Requeue(IReadOnlyCollection<InaraQueuedEvent> batch)
    {
        queue.Requeue(batch);
        nextSendAt ??= timeProvider.GetUtcNow() + SendInterval;
    }

    private static InaraCredentials? GetCredentials(
        InaraPublicationOptions options)
    {
        var commander = options.CommanderName?.Trim();
        var apiKey = options.ApiKey?.Trim();
        if (string.IsNullOrWhiteSpace(commander)
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new InaraCredentials(
            commander,
            options.FrontierId?.Trim() ?? string.Empty,
            apiKey);
    }

    private static async Task<JObject> AddSidecarDataAsync(
        JObject entry,
        JournalEventEnvelope journalEvent,
        CargoSnapshot? cargo,
        string? journalPath,
        bool allowSharedData,
        ICollection<string> warnings,
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
        return statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }
}

public sealed record InaraPublicationOptions(
    bool Enabled,
    bool DeveloperTestMode,
    string? ApiKey,
    string? CommanderName,
    string? FrontierId,
    string? GameVersion,
    bool IsOdyssey);

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
