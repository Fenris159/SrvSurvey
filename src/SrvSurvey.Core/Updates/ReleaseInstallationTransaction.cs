using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Updates;

public sealed record ReleaseInstallationPreparation(
    Guid RequestId,
    Version Version,
    string RuntimeIdentifier,
    string InstallationDirectory,
    string CandidateDirectory,
    string BackupDirectory,
    string FailedDirectory,
    string EntryPoint,
    string ManifestSha256,
    string InstallationFingerprint,
    IReadOnlyList<string> StartupArguments);

public enum ReleaseInstallationStatus
{
    Installed,
    RolledBack,
}

public sealed record ReleaseInstallationResult(
    ReleaseInstallationStatus Status,
    string InstallationDirectory,
    string? BackupDirectory,
    string? FailedDirectory,
    string? Error);

public interface IReleaseInstallationPreparer
{
    Task<ReleaseInstallationPreparation> PrepareAsync(
        Version version,
        string runtimeIdentifier,
        string readyDirectory,
        string manifestSha256,
        string installationDirectory,
        IReadOnlyList<string> startupArguments,
        CancellationToken cancellationToken = default);
}

internal enum ReleaseInstallationCheckpoint
{
    BeforeBackup,
    BackupMoved,
    CandidateActivated,
}

public sealed class ReleaseInstallationPreparer : IReleaseInstallationPreparer
{
    private const int MaximumInstallationFileCount = 16_384;
    private const long MaximumInstallationBytes = 4L * 1024 * 1024 * 1024;
    private readonly ReleasePackageStagingService stagingService;

    public ReleaseInstallationPreparer(
        ReleasePackageStagingService? stagingService = null)
    {
        this.stagingService = stagingService ?? new ReleasePackageStagingService();
    }

    public async Task<ReleaseInstallationPreparation> PrepareAsync(
        Version version,
        string runtimeIdentifier,
        string readyDirectory,
        string manifestSha256,
        string installationDirectory,
        IReadOnlyList<string> startupArguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(readyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentNullException.ThrowIfNull(startupArguments);
        var installationRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installationDirectory));
        var readyRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(readyDirectory));
        ValidateDistinctRoots(installationRoot, readyRoot);
        if (!Directory.Exists(installationRoot))
        {
            throw new DirectoryNotFoundException(
                $"The SrvSurvey installation was not found: {installationRoot}");
        }

        var parent = Directory.GetParent(installationRoot)?.FullName
            ?? throw new InvalidDataException(
                "The SrvSurvey installation cannot be a file-system root.");
        var installationName = Path.GetFileName(installationRoot);
        if (string.IsNullOrWhiteSpace(installationName))
        {
            throw new InvalidDataException(
                "The SrvSurvey installation directory name is invalid.");
        }

        var entryPoint = runtimeIdentifier switch
        {
            "win-x64" => "SrvSurvey.Desktop.exe",
            "linux-x64" => "SrvSurvey.Desktop",
            _ => throw new PlatformNotSupportedException(
                $"The runtime '{runtimeIdentifier}' has no install transaction."),
        };
        if (!File.Exists(Path.Combine(installationRoot, entryPoint)))
        {
            throw new InvalidDataException(
                "Automatic update requires a self-contained SrvSurvey installation.");
        }

        await stagingService.VerifyReadyAsync(
                version,
                runtimeIdentifier,
                readyRoot,
                manifestSha256,
                cancellationToken)
            .ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        var candidateDirectory = Path.Combine(
            parent,
            $".{installationName}-update-{requestId:N}");
        var backupDirectory = Path.Combine(
            parent,
            $".{installationName}-backup-{requestId:N}");
        var failedDirectory = Path.Combine(
            parent,
            $".{installationName}-failed-{requestId:N}");
        EnsureMissing(candidateDirectory, backupDirectory, failedDirectory);
        try
        {
            await CopyDirectoryAsync(
                    readyRoot,
                    candidateDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            await stagingService.VerifyReadyAsync(
                    version,
                    runtimeIdentifier,
                    candidateDirectory,
                    manifestSha256,
                    cancellationToken)
                .ConfigureAwait(false);
            var fingerprint = await ComputeDirectoryFingerprintAsync(
                    installationRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            return new ReleaseInstallationPreparation(
                requestId,
                version,
                runtimeIdentifier,
                installationRoot,
                candidateDirectory,
                backupDirectory,
                failedDirectory,
                entryPoint,
                manifestSha256.ToLowerInvariant(),
                fingerprint,
                startupArguments.ToArray());
        }
        catch
        {
            TryDeleteDirectory(candidateDirectory);
            throw;
        }
    }

    internal static async Task<string> ComputeDirectoryFingerprintAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var files = EnumerateFilesWithoutLinks(root);
        if (files.Count > MaximumInstallationFileCount)
        {
            throw new InvalidDataException(
                "The installation contains too many files to update safely.");
        }

        long totalBytes = 0;
        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = file.RefreshSnapshot();
            totalBytes = checked(totalBytes + before.Length);
            if (totalBytes > MaximumInstallationBytes)
            {
                throw new InvalidDataException(
                    "The installation is too large to update safely.");
            }

            await using var stream = OpenRead(file.FullPath);
            var fileHash = await SHA256.HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            var after = file.RefreshSnapshot();
            if (before != after)
            {
                throw new InvalidDataException(
                    $"Installation file changed while it was being checked: {file.RelativePath}");
            }

            AppendInt32(fingerprint, Encoding.UTF8.GetByteCount(file.RelativePath));
            fingerprint.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            AppendInt64(fingerprint, before.Length);
            AppendInt32(fingerprint, before.UnixMode);
            fingerprint.AppendData(fileHash);
        }

        AppendInt32(fingerprint, files.Count);
        return Convert.ToHexString(fingerprint.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var files = EnumerateFilesWithoutLinks(source);
        Directory.CreateDirectory(destination);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveChild(destination, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = OpenRead(file.FullPath);
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, 128 * 1024, cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            output.Close();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    target,
                    File.GetUnixFileMode(file.FullPath));
            }
        }
    }

    private static IReadOnlyList<FingerprintFile> EnumerateFilesWithoutLinks(
        string root)
    {
        var rootInfo = new DirectoryInfo(root);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "The update directory cannot be a symbolic link.");
        }

        var files = new List<FingerprintFile>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootInfo);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"The update directory contains link '{child.FullName}'.");
                }

                pending.Push(child);
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"The update directory contains link '{file.FullName}'.");
                }

                files.Add(new FingerprintFile(
                    file.FullName,
                    Path.GetRelativePath(root, file.FullName)
                        .Replace(Path.DirectorySeparatorChar, '/')));
            }
        }

        return files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateDistinctRoots(string installation, string ready)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var installationPrefix = installation + Path.DirectorySeparatorChar;
        var readyPrefix = ready + Path.DirectorySeparatorChar;
        if (string.Equals(installation, ready, comparison)
            || installation.StartsWith(readyPrefix, comparison)
            || ready.StartsWith(installationPrefix, comparison))
        {
            throw new InvalidDataException(
                "The staged update and current installation must be separate directories.");
        }
    }

    private static void EnsureMissing(params string[] paths)
    {
        var existing = paths.FirstOrDefault(path =>
            Directory.Exists(path) || File.Exists(path));
        if (existing is not null)
        {
            throw new IOException(
                $"The update transaction path already exists: {existing}");
        }
    }

    private static string ResolveChild(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var child = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(fullRoot)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!child.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException(
                $"Update file escaped its directory: {relativePath}");
        }

        return child;
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record FingerprintFile(string FullPath, string RelativePath)
    {
        public FileSnapshot RefreshSnapshot()
        {
            var info = new FileInfo(FullPath);
            info.Refresh();
            if (!info.Exists)
            {
                throw new InvalidDataException(
                    $"Update file disappeared while being checked: {RelativePath}");
            }

            var mode = OperatingSystem.IsWindows()
                ? 0
                : (int)File.GetUnixFileMode(FullPath);
            return new FileSnapshot(info.Length, info.LastWriteTimeUtc.Ticks, mode);
        }
    }

    private sealed record FileSnapshot(long Length, long LastWriteTicks, int UnixMode);
}

public sealed class ReleaseInstallationTransaction
{
    private readonly ReleasePackageStagingService stagingService;
    private readonly Action<ReleaseInstallationCheckpoint>? checkpoint;

    public ReleaseInstallationTransaction(
        ReleasePackageStagingService? stagingService = null)
        : this(stagingService, null)
    {
    }

    internal ReleaseInstallationTransaction(
        ReleasePackageStagingService? stagingService,
        Action<ReleaseInstallationCheckpoint>? checkpoint)
    {
        this.stagingService = stagingService ?? new ReleasePackageStagingService();
        this.checkpoint = checkpoint;
    }

    public async Task<ReleaseInstallationResult> ApplyAsync(
        ReleaseInstallationPreparation preparation,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<bool>>
            launchAndConfirm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(launchAndConfirm);
        ValidatePreparationPaths(preparation);
        await stagingService.VerifyReadyAsync(
                preparation.Version,
                preparation.RuntimeIdentifier,
                preparation.CandidateDirectory,
                preparation.ManifestSha256,
                cancellationToken)
            .ConfigureAwait(false);
        var fingerprint = await ReleaseInstallationPreparer
            .ComputeDirectoryFingerprintAsync(
                preparation.InstallationDirectory,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                fingerprint,
                preparation.InstallationFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The installation changed after update preparation; no files were replaced.");
        }

        checkpoint?.Invoke(ReleaseInstallationCheckpoint.BeforeBackup);
        Directory.Move(
            preparation.InstallationDirectory,
            preparation.BackupDirectory);
        var candidateActivated = false;
        try
        {
            checkpoint?.Invoke(ReleaseInstallationCheckpoint.BackupMoved);
            Directory.Move(
                preparation.CandidateDirectory,
                preparation.InstallationDirectory);
            candidateActivated = true;
            checkpoint?.Invoke(ReleaseInstallationCheckpoint.CandidateActivated);
        }
        catch
        {
            RestoreBackup(preparation, candidateActivated);
            throw;
        }

        string? launchError = null;
        var healthy = false;
        try
        {
            healthy = await launchAndConfirm(
                    Path.Combine(
                        preparation.InstallationDirectory,
                        preparation.EntryPoint),
                    preparation.StartupArguments,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or TaskCanceledException)
        {
            launchError = exception.Message;
        }

        if (healthy)
        {
            return new ReleaseInstallationResult(
                ReleaseInstallationStatus.Installed,
                preparation.InstallationDirectory,
                preparation.BackupDirectory,
                null,
                null);
        }

        RestoreBackup(preparation, candidateActivated: true);
        return new ReleaseInstallationResult(
            ReleaseInstallationStatus.RolledBack,
            preparation.InstallationDirectory,
            null,
            preparation.FailedDirectory,
            launchError ?? "The replacement process did not confirm healthy startup.");
    }

    private static void RestoreBackup(
        ReleaseInstallationPreparation preparation,
        bool candidateActivated)
    {
        if (candidateActivated && Directory.Exists(preparation.InstallationDirectory))
        {
            if (Directory.Exists(preparation.FailedDirectory)
                || File.Exists(preparation.FailedDirectory))
            {
                throw new IOException(
                    "The failed-update preservation directory already exists.");
            }

            Directory.Move(
                preparation.InstallationDirectory,
                preparation.FailedDirectory);
        }

        if (!Directory.Exists(preparation.InstallationDirectory)
            && Directory.Exists(preparation.BackupDirectory))
        {
            Directory.Move(
                preparation.BackupDirectory,
                preparation.InstallationDirectory);
        }
    }

    private static void ValidatePreparationPaths(
        ReleaseInstallationPreparation preparation)
    {
        var expectedEntryPoint = preparation.RuntimeIdentifier == "win-x64"
            ? "SrvSurvey.Desktop.exe"
            : "SrvSurvey.Desktop";
        if (preparation.RequestId == Guid.Empty
            || preparation.Version.Build < 0
            || preparation.RuntimeIdentifier is not ("win-x64" or "linux-x64")
            || preparation.ManifestSha256.Length != 64
            || preparation.ManifestSha256.Any(character => !Uri.IsHexDigit(character))
            || preparation.InstallationFingerprint.Length != 64
            || preparation.InstallationFingerprint.Any(character =>
                !Uri.IsHexDigit(character))
            || !string.Equals(
                preparation.EntryPoint,
                expectedEntryPoint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update installation preparation is invalid.");
        }

        var installation = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(preparation.InstallationDirectory));
        var parent = Directory.GetParent(installation)?.FullName
            ?? throw new InvalidDataException(
                "The installation cannot be a file-system root.");
        var name = Path.GetFileName(installation);
        var id = preparation.RequestId.ToString("N");
        var expectedCandidate = Path.Combine(parent, $".{name}-update-{id}");
        var expectedBackup = Path.Combine(parent, $".{name}-backup-{id}");
        var expectedFailed = Path.Combine(parent, $".{name}-failed-{id}");
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetFullPath(preparation.CandidateDirectory),
                expectedCandidate,
                comparison)
            || !string.Equals(
                Path.GetFullPath(preparation.BackupDirectory),
                expectedBackup,
                comparison)
            || !string.Equals(
                Path.GetFullPath(preparation.FailedDirectory),
                expectedFailed,
                comparison)
            || Directory.Exists(expectedBackup)
            || File.Exists(expectedBackup)
            || Directory.Exists(expectedFailed)
            || File.Exists(expectedFailed))
        {
            throw new InvalidDataException(
                "The update transaction paths are invalid or already occupied.");
        }
    }
}
