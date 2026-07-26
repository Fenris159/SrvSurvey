using System.Net;

namespace SrvSurvey.Desktop.Platform;

public sealed class CodexImageCache : IDisposable
{
    public const long MaximumImageBytes = 30 * 1024 * 1024;

    private static readonly string[] KnownExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif"];

    private readonly Func<CodexImageLocations> locationProvider;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TimeSpan downloadTimeout;
    private bool disposed;

    public CodexImageCache(
        string cacheDirectory,
        HttpClient? httpClient = null,
        TimeSpan? downloadTimeout = null)
        : this(
            () => new CodexImageLocations(cacheDirectory, null),
            httpClient,
            downloadTimeout)
    {
    }

    public CodexImageCache(
        Func<CodexImageLocations> locationProvider,
        HttpClient? httpClient = null,
        TimeSpan? downloadTimeout = null)
    {
        this.locationProvider = locationProvider
            ?? throw new ArgumentNullException(nameof(locationProvider));
        _ = ResolveLocations();
        ownsHttpClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        this.downloadTimeout = downloadTimeout ?? TimeSpan.FromSeconds(30);
        if (this.downloadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(downloadTimeout),
                "The Codex image download timeout must be positive.");
        }
    }

    public async Task<CodexImageCacheResult> GetAsync(
        long entryId,
        string imageUrl,
        string? localImageName = null,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (entryId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entryId),
                "A positive Codex entry ID is required.");
        }

        var locations = ResolveLocations();
        if (!forceRefresh
            && ResolveLocalImage(
                locations.LocalFloraDirectory,
                localImageName) is { } localPath)
        {
            return new CodexImageCacheResult(
                localPath,
                true,
                true,
                null,
                true);
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return CodexImageCacheResult.Failed(
                string.Empty,
                "The Codex image URL is invalid.");
        }

        var path = Path.Combine(
            locations.CacheDirectory,
            entryId + GetExtension(uri));
        var cachedPath = !forceRefresh
            ? ResolveCachedImage(locations.CacheDirectory, entryId, path)
            : null;
        if (cachedPath is not null)
        {
            return new CodexImageCacheResult(cachedPath, true, true, null);
        }

        Directory.CreateDirectory(locations.CacheDirectory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var timeoutCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(downloadTimeout);
        var operationToken = timeoutCancellation.Token;
        try
        {
            using var response = await httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    operationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return CodexImageCacheResult.Failed(
                    path,
                    "No reference image is available from the source.");
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumImageBytes)
            {
                return CodexImageCacheResult.Failed(
                    path,
                    "The reference image exceeds the 30 MB safety limit.");
            }

            await using var source = await response.Content
                .ReadAsStreamAsync(operationToken)
                .ConfigureAwait(false);
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithLimitAsync(source, target, operationToken)
                    .ConfigureAwait(false);
                await target.FlushAsync(operationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, true);
            return new CodexImageCacheResult(path, false, true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CodexImageCacheResult.Failed(
                path,
                "The reference image download timed out.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            return CodexImageCacheResult.Failed(path, exception.Message);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<CodexImagePreDownloadResult> PreDownloadAsync(
        IEnumerable<CodexImageRequest> requests,
        IProgress<CodexImagePreDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var materialized = requests
            .Where(request => request.EntryId > 0
                && !string.IsNullOrWhiteSpace(request.ImageUrl))
            .GroupBy(request => request.EntryId)
            .Select(group => group.First())
            .ToArray();
        var downloaded = 0;
        var cached = 0;
        var local = 0;
        var failed = 0;
        for (var index = 0; index < materialized.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = materialized[index];
            var result = await GetAsync(
                    request.EntryId,
                    request.ImageUrl,
                    request.LocalImageName,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                failed++;
            }
            else if (result.IsLocal)
            {
                local++;
            }
            else if (result.IsFromCache)
            {
                cached++;
            }
            else
            {
                downloaded++;
            }

            progress?.Report(new CodexImagePreDownloadProgress(
                index + 1,
                materialized.Length,
                downloaded,
                cached,
                local,
                failed));
        }

        return new CodexImagePreDownloadResult(
            materialized.Length,
            downloaded,
            cached,
            local,
            failed);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream target,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > MaximumImageBytes)
            {
                throw new InvalidDataException(
                    "The reference image exceeds the 30 MB safety limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string GetExtension(Uri uri)
    {
        return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {
            ".jpeg" => ".jpg",
            ".jpg" => ".jpg",
            ".png" => ".png",
            ".webp" => ".webp",
            ".bmp" => ".bmp",
            ".gif" => ".gif",
            _ => ".jpg",
        };
    }

    private CodexImageLocations ResolveLocations()
    {
        var locations = locationProvider()
            ?? throw new InvalidOperationException(
                "The Codex image location provider returned no locations.");
        return new CodexImageLocations(
            Path.GetFullPath(
                string.IsNullOrWhiteSpace(locations.CacheDirectory)
                    ? throw new InvalidOperationException(
                        "A Codex image cache directory is required.")
                    : locations.CacheDirectory),
            string.IsNullOrWhiteSpace(locations.LocalFloraDirectory)
                ? null
                : Path.GetFullPath(locations.LocalFloraDirectory));
    }

    private static string? ResolveLocalImage(
        string? directory,
        string? imageName)
    {
        if (directory is null
            || string.IsNullOrWhiteSpace(imageName)
            || !string.Equals(
                Path.GetFileName(imageName),
                imageName,
                StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(directory, imageName + ".png");
        return File.Exists(path) ? path : null;
    }

    private static string? ResolveCachedImage(
        string directory,
        long entryId,
        string preferredPath)
    {
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        foreach (var extension in KnownExtensions)
        {
            var candidate = Path.Combine(directory, entryId + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed record CodexImageCacheResult(
    string Path,
    bool IsFromCache,
    bool IsSuccess,
    string? Error,
    bool IsLocal = false)
{
    public static CodexImageCacheResult Failed(string path, string error)
    {
        return new CodexImageCacheResult(path, false, false, error);
    }
}

public sealed record CodexImageLocations(
    string CacheDirectory,
    string? LocalFloraDirectory);

public sealed record CodexImageRequest(
    long EntryId,
    string ImageUrl,
    string? LocalImageName = null);

public sealed record CodexImagePreDownloadProgress(
    int Completed,
    int Total,
    int Downloaded,
    int Cached,
    int Local,
    int Failed);

public sealed record CodexImagePreDownloadResult(
    int Total,
    int Downloaded,
    int Cached,
    int Local,
    int Failed);
