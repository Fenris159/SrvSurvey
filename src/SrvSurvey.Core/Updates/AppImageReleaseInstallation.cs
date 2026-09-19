using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SrvSurvey.Core.Updates;

public interface IAppImageReleaseInstallationPreparer
{
    Task<ReleaseInstallationPreparation> PrepareAsync(
        ReleaseVersion version,
        string readyAppImagePath,
        string expectedSha256,
        string installationPath,
        IReadOnlyList<string> startupArguments,
        CancellationToken cancellationToken = default
    );

    Task AbortAsync(ReleaseInstallationPreparation preparation, CancellationToken cancellationToken = default);
}

public sealed class AppImageReleaseInstallationPreparer : IAppImageReleaseInstallationPreparer
{
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherExecute;

    public async Task<ReleaseInstallationPreparation> PrepareAsync(
        ReleaseVersion version,
        string readyAppImagePath,
        string expectedSha256,
        string installationPath,
        IReadOnlyList<string> startupArguments,
        CancellationToken cancellationToken = default
    )
    {
        EnsureLinux();
        ArgumentException.ThrowIfNullOrWhiteSpace(readyAppImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        ArgumentNullException.ThrowIfNull(startupArguments);
        ValidateSha256(expectedSha256);

        string readyPath = Path.GetFullPath(readyAppImagePath);
        string installedPath = ResolveInstallationPath(installationPath);
        ValidateRegularFile(readyPath, "The downloaded AppImage");
        ValidateRegularFile(installedPath, "The installed AppImage");
        if (PathsEqual(readyPath, installedPath))
        {
            throw new InvalidDataException("The downloaded and installed AppImages must be separate files.");
        }

        string parent =
            Path.GetDirectoryName(installedPath)
            ?? throw new InvalidDataException("The installed AppImage has no containing directory.");
        string fileName = Path.GetFileName(installedPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException("The installed AppImage file name is invalid.");
        }

        await VerifyAppImageAsync(readyPath, expectedSha256, cancellationToken).ConfigureAwait(false);
        string installationFingerprint = await ComputeFileFingerprintAsync(installedPath, cancellationToken)
            .ConfigureAwait(false);
        var requestId = Guid.NewGuid();
        string id = requestId.ToString("N");
        string candidatePath = Path.Combine(parent, $".{fileName}-update-{id}");
        string backupPath = Path.Combine(parent, $".{fileName}-backup-{id}");
        string failedPath = Path.Combine(parent, $".{fileName}-failed-{id}");
        EnsureMissing(candidatePath, backupPath, failedPath);

        try
        {
            await CopyFileAsync(readyPath, candidatePath, cancellationToken).ConfigureAwait(false);
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("AppImage updates are only supported on Linux.");
            }

            File.SetUnixFileMode(candidatePath, ExecutableMode);
            await VerifyAppImageAsync(candidatePath, expectedSha256, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFile(candidatePath);
            throw;
        }

        return new ReleaseInstallationPreparation(
            requestId,
            version,
            CrossPlatformReleaseClient.LinuxX64AppImageRuntimeIdentifier,
            parent,
            readyPath,
            candidatePath,
            backupPath,
            failedPath,
            fileName,
            expectedSha256.ToLowerInvariant(),
            installationFingerprint,
            RequiresElevation: false,
            startupArguments.ToArray(),
            ReleaseInstallationKind.AppImage
        );
    }

    public Task AbortAsync(ReleaseInstallationPreparation preparation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        cancellationToken.ThrowIfCancellationRequested();
        TryDeleteFile(preparation.CandidateDirectory);
        return Task.CompletedTask;
    }

    public static bool CanReplace(string installationPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            string fullPath = ResolveInstallationPath(installationPath);
            ValidateRegularFile(fullPath, "The installed AppImage");
            string parent =
                Path.GetDirectoryName(fullPath)
                ?? throw new InvalidDataException("The installed AppImage has no containing directory.");
            string probePath = Path.Combine(parent, $".srvsurvey-update-write-test-{Guid.NewGuid():N}");
            try
            {
                using var probe = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                probe.WriteByte(0);
                probe.Flush(flushToDisk: true);
            }
            finally
            {
                TryDeleteFile(probePath);
            }

            return true;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    public static string ResolveInstallationPath(string installationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        string fullPath = Path.GetFullPath(installationPath);
        var info = new FileInfo(fullPath);
        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException("The installed AppImage was not found.", fullPath);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return fullPath;
        }

        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not FileInfo targetFile || !targetFile.Exists)
        {
            throw new InvalidDataException("The installed AppImage symbolic link has no regular file target.");
        }

        return Path.GetFullPath(targetFile.FullName);
    }

    internal static async Task VerifyAppImageAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken
    )
    {
        ValidateRegularFile(path, "The AppImage update candidate");
        ValidateSha256(expectedSha256);
        await using FileStream stream = OpenRead(path);
        byte[] header = new byte[20];
        int headerBytes = await stream
            .ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        if (
            headerBytes != header.Length
            || header[0] != 0x7f
            || header[1] != (byte)'E'
            || header[2] != (byte)'L'
            || header[3] != (byte)'F'
            || header[4] != 2
            || header[5] != 1
            || header[8] != (byte)'A'
            || header[9] != (byte)'I'
            || header[10] != 2
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18, 2)) != 62
        )
        {
            throw new InvalidDataException("The update candidate is not a 64-bit x86 type-2 AppImage.");
        }

        stream.Position = 0;
        string actualSha256 = Convert
            .ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The AppImage update candidate does not match its release checksum.");
        }
    }

    internal static async Task<string> ComputeFileFingerprintAsync(string path, CancellationToken cancellationToken)
    {
        EnsureLinux();
        ValidateRegularFile(path, "The installed AppImage");
        FileSnapshot before = GetSnapshot(path);
        await using FileStream stream = OpenRead(path);
        byte[] fileHash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        FileSnapshot after = GetSnapshot(path);
        if (before != after)
        {
            throw new InvalidDataException("The installed AppImage changed while it was being checked.");
        }

        using var fingerprint = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        fingerprint.AppendData(fileHash);
        Span<byte> metadata = stackalloc byte[sizeof(long) + sizeof(int)];
        BinaryPrimitives.WriteInt64LittleEndian(metadata, before.Length);
        BinaryPrimitives.WriteInt32LittleEndian(metadata[sizeof(long)..], before.UnixMode);
        fingerprint.AppendData(metadata);
        return Convert.ToHexString(fingerprint.GetHashAndReset()).ToLowerInvariant();
    }

    internal static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; retained files support recovery diagnostics.
        }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using FileStream input = OpenRead(source);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        await input.CopyToAsync(output, 128 * 1024, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
    }

    private static FileStream OpenRead(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );

    private static FileSnapshot GetSnapshot(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("AppImage updates are only supported on Linux.");
        }

        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException("The AppImage disappeared while it was being checked.", path);
        }

        return new FileSnapshot(info.Length, info.LastWriteTimeUtc.Ticks, (int)File.GetUnixFileMode(path));
    }

    private static void ValidateRegularFile(string path, string label)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{label} is missing or is a symbolic link: {path}");
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The AppImage release checksum is invalid.");
        }
    }

    private static void EnsureLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("AppImage updates are only supported on Linux.");
        }
    }

    private static void EnsureMissing(params string[] paths)
    {
        string? existing = paths.FirstOrDefault(path => File.Exists(path) || Directory.Exists(path));
        if (existing is not null)
        {
            throw new IOException($"The AppImage update transaction path already exists: {existing}");
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.Ordinal);

    private sealed record FileSnapshot(long Length, long LastWriteTicks, int UnixMode);
}
