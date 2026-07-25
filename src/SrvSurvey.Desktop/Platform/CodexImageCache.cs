using System.Net;

namespace SrvSurvey.Desktop.Platform;

public sealed class CodexImageCache : IDisposable
{
    public const long MaximumImageBytes = 30 * 1024 * 1024;

    private readonly string cacheDirectory;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TimeSpan downloadTimeout;
    private bool disposed;

    public CodexImageCache(
        string cacheDirectory,
        HttpClient? httpClient = null,
        TimeSpan? downloadTimeout = null)
    {
        this.cacheDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(cacheDirectory)
                ? throw new ArgumentException(
                    "A Codex image cache directory is required.",
                    nameof(cacheDirectory))
                : cacheDirectory);
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

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return CodexImageCacheResult.Failed(
                string.Empty,
                "The Codex image URL is invalid.");
        }

        var path = Path.Combine(
            cacheDirectory,
            entryId + GetExtension(uri));
        if (!forceRefresh && File.Exists(path))
        {
            return new CodexImageCacheResult(path, true, true, null);
        }

        Directory.CreateDirectory(cacheDirectory);
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
}

public sealed record CodexImageCacheResult(
    string Path,
    bool IsFromCache,
    bool IsSuccess,
    string? Error)
{
    public static CodexImageCacheResult Failed(string path, string error)
    {
        return new CodexImageCacheResult(path, false, false, error);
    }
}
