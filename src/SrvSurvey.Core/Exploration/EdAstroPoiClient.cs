using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Exploration;

public interface IExplorationPoiClient
{
    /// <summary>Returns a fresh local catalogue when available or performs one bounded quota-controlled refresh.</summary>
    Task<ExplorationPoiLoadResult> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

public sealed record ExplorationPoiLoadResult(
    ExplorationPoiCatalog Catalog,
    DateTimeOffset? FetchedAt,
    bool IsCached,
    string? Warning = null
);

public sealed class EdAstroPoiClient : IExplorationPoiClient, IDisposable
{
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient client;
    private readonly EdAstroTrafficBudget trafficBudget;
    private readonly string cachePath;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim gate = new(1, 1);
    private ExplorationPoiLoadResult? cached;
    private DateTimeOffset retryAt;

    /// <summary>Creates a catalogue reader with shared provider quota and a cache under the supplied directory.</summary>
    public EdAstroPoiClient(
        string cacheDirectory,
        HttpClient? client = null,
        TimeProvider? time = null,
        EdAstroTrafficBudget? trafficBudget = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        cachePath = Path.Combine(cacheDirectory, "edastro-gec.json");
        this.client = client ?? SharedClient;
        this.trafficBudget = trafficBudget ?? EdAstroTrafficBudget.Shared;
        this.time = time ?? TimeProvider.System;
    }

    /// <summary>Returns a fresh local catalogue when available or performs one bounded quota-controlled refresh.</summary>
    public async Task<ExplorationPoiLoadResult> GetAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default
    )
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached ??= await ReadCacheAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset now = time.GetUtcNow();
            if (now < retryAt || !forceRefresh && cached?.FetchedAt > now - TimeSpan.FromDays(7))
            {
                return cached
                    ?? new ExplorationPoiLoadResult(
                        ExplorationPoiCatalog.Empty,
                        null,
                        true,
                        "EDAstro refresh is cooling down."
                    );
            }

            if (!trafficBudget.TryAcquire(now, out DateTimeOffset availableAt))
            {
                retryAt = availableAt;
                return (cached ?? new ExplorationPoiLoadResult(ExplorationPoiCatalog.Empty, null, true)) with
                {
                    Warning = "EDAstro's shared request quota is cooling down.",
                };
            }
            retryAt = now + TimeSpan.FromMinutes(1);
            return await DownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Validates a bounded GEC response before replacing the cache and retains older data on failure.</summary>
    private async Task<ExplorationPoiLoadResult> DownloadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(WellKnownUris.EdastroPoiCatalog, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            ApplyRateLimit(response);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await BoundedHttpContent
                .ReadBytesAsync(response.Content, MaximumResponseBytes, "The EDAstro POI catalogue", timeout.Token)
                .ConfigureAwait(false);
            using var json = JsonDocument.Parse(bytes);
            var catalog = ExplorationPoiCatalog.Parse(json.RootElement);
            if (catalog.Items.Count == 0)
            {
                throw new InvalidDataException("The EDAstro catalogue contained no usable POIs.");
            }

            var result = new ExplorationPoiLoadResult(catalog, time.GetUtcNow(), false);
            await SaveCacheAsync(result.FetchedAt!.Value, bytes, cancellationToken).ConfigureAwait(false);
            cached = result with { IsCached = true };
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or JsonException
                        or OperationCanceledException
            )
        {
            return (cached ?? new ExplorationPoiLoadResult(ExplorationPoiCatalog.Empty, null, true)) with
            {
                Warning = "EDAstro is unavailable; retained the last catalogue. " + exception.Message,
            };
        }
    }

    /// <summary>Applies provider cooldown headers to both the catalogue reader and the shared request budget.</summary>
    private void ApplyRateLimit(HttpResponseMessage response)
    {
        DateTimeOffset cooldown = trafficBudget.Observe(response.Headers, time.GetUtcNow());
        if (cooldown > retryAt)
        {
            retryAt = cooldown;
        }
    }

    /// <summary>Restores a validated cached catalogue or ignores an unusable local snapshot.</summary>
    private async Task<ExplorationPoiLoadResult?> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(cachePath) || new FileInfo(cachePath).Length > MaximumResponseBytes)
            {
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(cachePath, cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(bytes);
            JsonElement root = json.RootElement;
            var catalog = ExplorationPoiCatalog.Parse(root.GetProperty("data"));
            if (catalog.Items.Count == 0)
            {
                return null;
            }
            return new ExplorationPoiLoadResult(catalog, root.GetProperty("fetchedAt").GetDateTimeOffset(), true);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or JsonException
                        or KeyNotFoundException
                        or FormatException
                        or InvalidOperationException
            )
        {
            return null;
        }
    }

    /// <summary>Atomically replaces the catalogue snapshot with its fetch timestamp and validated payload.</summary>
    private async Task SaveCacheAsync(DateTimeOffset fetchedAt, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var json = JsonDocument.Parse(bytes);
            byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new { fetchedAt, data = json.RootElement });
            await File.WriteAllBytesAsync(temporary, envelope, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, cachePath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Creates the shared catalogue connection with the application user agent.</summary>
    private static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-XP/2.1");
        return http;
    }

    /// <summary>Releases the reader's synchronization gate without disposing the shared HTTP connection.</summary>
    public void Dispose() => gate.Dispose();
}
