using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Storage;

public interface IVisitedStarsCacheService
{
    Task<VisitedStarsCacheSwapResult> SwapAsync(
        string systemName,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task<VisitedStarsCacheRestoreResult> RestoreAsync(
        string targetPath,
        CancellationToken cancellationToken = default);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The service is process-scoped and its gate may have in-flight waiters.")]
public sealed class VisitedStarsCacheService(
    HttpClient httpClient,
    string downloadDirectory,
    Func<bool>? isGameRunning = null)
    : IVisitedStarsCacheService
{
    public const string CacheFileName = "VisitedStarsCache.dat";
    public const string BackupFileName = "backup-VisitedStarsCache.dat";

    private const long MaximumDownloadBytes = 64L * 1024 * 1024;
    private static readonly Uri Endpoint = new(
        "https://edgalaxy.net/visitedstars",
        UriKind.Absolute);
    private readonly string downloadDirectory = Path.GetFullPath(
        downloadDirectory);
    private readonly Func<bool> isGameRunning = isGameRunning ?? (() => false);
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async Task<VisitedStarsCacheSwapResult> SwapAsync(
        string systemName,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        var target = ValidateTargetPath(targetPath);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureGameStopped();
            if (!File.Exists(target))
            {
                throw new FileNotFoundException(
                    "The current Elite visited-stars cache does not exist.",
                    target);
            }

            var download = await DownloadAsync(systemName.Trim(), cancellationToken)
                .ConfigureAwait(false);
            EnsureGameStopped();
            var backup = GetBackupPath(target);
            var originalHash = await ProfileInventory.ComputeSha256Async(
                    target,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsurePersistentBackupAsync(
                    target,
                    backup,
                    originalHash,
                    cancellationToken)
                .ConfigureAwait(false);

            var rollback = $"{target}.{Guid.NewGuid():N}.rollback";
            var activationStage = $"{target}.{Guid.NewGuid():N}.tmp";
            var activated = false;
            var retainRollback = false;
            try
            {
                await CopyAndVerifyAsync(
                        target,
                        rollback,
                        originalHash,
                        overwrite: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                await CopyAndVerifyAsync(
                        download.Path,
                        activationStage,
                        download.Sha256,
                        overwrite: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureGameStopped();
                File.Move(activationStage, target, true);
                activated = true;
                await VerifyHashAsync(target, download.Sha256, cancellationToken)
                    .ConfigureAwait(false);
                TryDeleteIfExists(rollback);
            }
            catch
            {
                if (activated)
                {
                    if (File.Exists(rollback))
                    {
                        try
                        {
                            File.Move(rollback, target, true);
                        }
                        catch
                        {
                            retainRollback = true;
                            throw;
                        }
                    }
                    else
                    {
                        DeleteIfExists(target);
                    }
                }

                throw;
            }
            finally
            {
                TryDeleteIfExists(activationStage);
                if (!retainRollback)
                {
                    TryDeleteIfExists(rollback);
                }
            }

            return new VisitedStarsCacheSwapResult(
                target,
                backup,
                download.Path,
                originalHash,
                download.Sha256);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<VisitedStarsCacheRestoreResult> RestoreAsync(
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var target = ValidateTargetPath(targetPath);
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureGameStopped();
            var backup = GetBackupPath(target);
            if (!File.Exists(backup))
            {
                throw new FileNotFoundException(
                    "No original visited-stars cache backup exists.",
                    backup);
            }

            var backupHash = await ReadOrRecordBackupHashAsync(
                    backup,
                    cancellationToken)
                .ConfigureAwait(false);
            var rollback = $"{target}.{Guid.NewGuid():N}.rollback";
            var activationStage = $"{target}.{Guid.NewGuid():N}.tmp";
            var activated = false;
            var retainRollback = false;
            try
            {
                if (File.Exists(target))
                {
                    var currentHash = await ProfileInventory.ComputeSha256Async(
                            target,
                            cancellationToken)
                        .ConfigureAwait(false);
                    await CopyAndVerifyAsync(
                            target,
                            rollback,
                            currentHash,
                            overwrite: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await CopyAndVerifyAsync(
                        backup,
                        activationStage,
                        backupHash,
                        overwrite: false,
                        cancellationToken)
                    .ConfigureAwait(false);
                EnsureGameStopped();
                File.Move(activationStage, target, true);
                activated = true;
                await VerifyHashAsync(target, backupHash, cancellationToken)
                    .ConfigureAwait(false);
                TryDeleteIfExists(rollback);
            }
            catch
            {
                if (activated)
                {
                    if (File.Exists(rollback))
                    {
                        try
                        {
                            File.Move(rollback, target, true);
                        }
                        catch
                        {
                            retainRollback = true;
                            throw;
                        }
                    }
                    else
                    {
                        DeleteIfExists(target);
                    }
                }

                throw;
            }
            finally
            {
                TryDeleteIfExists(activationStage);
                if (!retainRollback)
                {
                    TryDeleteIfExists(rollback);
                }
            }

            return new VisitedStarsCacheRestoreResult(target, backup, backupHash);
        }
        finally
        {
            operationLock.Release();
        }
    }

    public static string GetBackupPath(string targetPath)
    {
        var target = Path.GetFullPath(targetPath);
        return Path.Combine(
            Path.GetDirectoryName(target)
                ?? throw new InvalidDataException(
                    "The visited-stars cache path has no parent directory."),
            BackupFileName);
    }

    private async Task<DownloadedCache> DownloadAsync(
        string systemName,
        CancellationToken cancellationToken)
    {
        using var body = new StringContent(
            $"system={Uri.EscapeDataString(systemName)}",
            Encoding.ASCII,
            "application/x-www-form-urlencoded");
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = body,
        };
        using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (!IsExpectedDownload(response.Content.Headers))
        {
            throw new InvalidDataException(
                "EDGalaxy returned an unexpected response instead of a visited-stars cache.");
        }

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new InvalidDataException(
                "The downloaded visited-stars cache exceeded the safety limit.");
        }

        Directory.CreateDirectory(downloadDirectory);
        var finalPath = Path.Combine(
            downloadDirectory,
            CreateDownloadFileName(systemName));
        var temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                long length = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    length += read;
                    if (length > MaximumDownloadBytes)
                    {
                        throw new InvalidDataException(
                            "The downloaded visited-stars cache exceeded the safety limit.");
                    }

                    await output.WriteAsync(
                            buffer.AsMemory(0, read),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (new FileInfo(temporaryPath).Length == 0)
            {
                throw new InvalidDataException(
                    "EDGalaxy returned an empty visited-stars cache.");
            }

            var hash = await ProfileInventory.ComputeSha256Async(
                    temporaryPath,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, finalPath, true);
            await VerifyHashAsync(finalPath, hash, cancellationToken)
                .ConfigureAwait(false);
            return new DownloadedCache(finalPath, hash);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
        }
    }

    private static bool IsExpectedDownload(HttpContentHeaders headers)
    {
        if (string.Equals(
                headers.ContentType?.MediaType,
                "application/octet-stream",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            headers.ContentDisposition?.FileName?.Trim('"'),
            CacheFileName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsurePersistentBackupAsync(
        string target,
        string backup,
        string originalHash,
        CancellationToken cancellationToken)
    {
        if (File.Exists(backup))
        {
            _ = await ReadOrRecordBackupHashAsync(backup, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var stage = $"{backup}.{Guid.NewGuid():N}.tmp";
        try
        {
            await CopyAndVerifyAsync(
                    target,
                    stage,
                    originalHash,
                    overwrite: false,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(stage, backup, false);
            await WriteBackupHashAsync(backup, originalHash, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DeleteIfExists(stage);
        }
    }

    private static async Task<string> ReadOrRecordBackupHashAsync(
        string backup,
        CancellationToken cancellationToken)
    {
        var sidecar = GetHashSidecarPath(backup);
        if (File.Exists(sidecar))
        {
            var expected = (await File.ReadAllTextAsync(sidecar, cancellationToken))
                .Trim();
            if (expected.Length != 64
                || !expected.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "The visited-stars backup checksum record is malformed.");
            }

            await VerifyHashAsync(backup, expected.ToLowerInvariant(), cancellationToken)
                .ConfigureAwait(false);
            return expected.ToLowerInvariant();
        }

        var hash = await ProfileInventory.ComputeSha256Async(
                backup,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteBackupHashAsync(backup, hash, cancellationToken)
            .ConfigureAwait(false);
        return hash;
    }

    private static async Task WriteBackupHashAsync(
        string backup,
        string hash,
        CancellationToken cancellationToken)
    {
        var sidecar = GetHashSidecarPath(backup);
        var stage = $"{sidecar}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                    stage,
                    hash + Environment.NewLine,
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(stage, sidecar, true);
        }
        finally
        {
            DeleteIfExists(stage);
        }
    }

    private static async Task CopyAndVerifyAsync(
        string source,
        string destination,
        string expectedHash,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(
                         source,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(
                         destination,
                         overwrite ? FileMode.Create : FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await VerifyHashAsync(destination, expectedHash, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyHashAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var actualHash = await ProfileInventory.ComputeSha256Async(
                path,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new IOException(
                $"The visited-stars cache failed checksum verification: {path}");
        }
    }

    private static string ValidateTargetPath(string targetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var target = Path.GetFullPath(targetPath);
        if (!string.Equals(
                Path.GetFileName(target),
                CacheFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Choose the Elite {CacheFileName} file.",
                nameof(targetPath));
        }

        return target;
    }

    private void EnsureGameStopped()
    {
        if (isGameRunning())
        {
            throw new InvalidOperationException(
                "Close Elite Dangerous before changing VisitedStarsCache.dat.");
        }
    }

    private static string CreateDownloadFileName(string systemName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(systemName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();
        if (safe.Length == 0)
        {
            safe = "system";
        }

        if (safe.Length > 80)
        {
            safe = safe[..80];
        }

        var suffix = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(systemName)))[..12];
        return $"{safe}-{suffix}.dat";
    }

    private static string GetHashSidecarPath(string backup)
    {
        return backup + ".sha256";
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteIfExists(string path)
    {
        try
        {
            DeleteIfExists(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A verified target/backup or failed rollback is safer with a
            // redundant artifact than with cleanup replacing the real result.
        }
    }

    private sealed record DownloadedCache(string Path, string Sha256);
}

public sealed record VisitedStarsCacheSwapResult(
    string TargetPath,
    string BackupPath,
    string DownloadPath,
    string OriginalSha256,
    string ReplacementSha256);

public sealed record VisitedStarsCacheRestoreResult(
    string TargetPath,
    string BackupPath,
    string RestoredSha256);
