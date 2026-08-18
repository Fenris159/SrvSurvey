using System.Threading.Channels;

namespace SrvSurvey.Core.Updates;

public sealed record ReleaseInstallationCleanupResult(
    int DeletedBackupDirectories,
    int DeletedUpdateDirectories,
    int DeletedFailedDirectories,
    int RetainedDirectories,
    IReadOnlyList<string> Failures)
{
    public int DeletedDirectories => DeletedBackupDirectories
        + DeletedUpdateDirectories
        + DeletedFailedDirectories;
}

public sealed record ReleasePackageCacheCleanupResult(
    int DeletedPackageVersions,
    int DeletedStagedVersions,
    int RetainedVersions,
    IReadOnlyList<string> Failures)
{
    public int DeletedVersions => checked(
        DeletedPackageVersions + DeletedStagedVersions);
}

public sealed class ReleaseInstallationHistoryCleaner
{
    public const int DefaultRetainedDirectoriesPerKind = 3;
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(24);

    private static readonly string[] Kinds = ["backup", "update", "failed"];
    private readonly TimeProvider timeProvider;
    private readonly int retainedDirectoriesPerKind;
    private readonly TimeSpan minimumAge;
    private readonly Action<string> deleteDirectory;

    public ReleaseInstallationHistoryCleaner(
        TimeProvider? timeProvider = null,
        int retainedDirectoriesPerKind = DefaultRetainedDirectoriesPerKind,
        TimeSpan? minimumAge = null)
        : this(
            timeProvider,
            retainedDirectoriesPerKind,
            minimumAge,
            path => Directory.Delete(path, recursive: true))
    {
    }

    internal ReleaseInstallationHistoryCleaner(
        TimeProvider? timeProvider,
        int retainedDirectoriesPerKind,
        TimeSpan? minimumAge,
        Action<string> deleteDirectory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedDirectoriesPerKind);
        var resolvedMinimumAge = minimumAge ?? DefaultMinimumAge;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            resolvedMinimumAge,
            TimeSpan.Zero);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.retainedDirectoriesPerKind = retainedDirectoriesPerKind;
        this.minimumAge = resolvedMinimumAge;
        this.deleteDirectory = deleteDirectory
            ?? throw new ArgumentNullException(nameof(deleteDirectory));
    }

    public ReleaseInstallationCleanupResult Clean(
        string installationDirectory,
        IEnumerable<string>? protectedDirectories = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        var installation = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installationDirectory));
        var parent = Directory.GetParent(installation)?.FullName
            ?? throw new InvalidDataException(
                "The SrvSurvey installation cannot be a file-system root.");
        var installationName = Path.GetFileName(installation);
        if (string.IsNullOrWhiteSpace(installationName))
        {
            throw new InvalidDataException(
                "The SrvSurvey installation directory name is invalid.");
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var protectedPaths = new HashSet<string>(comparer);
        if (protectedDirectories is not null)
        {
            foreach (var path in protectedDirectories
                .Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                protectedPaths.Add(Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(path)));
            }
        }

        var deleted = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["backup"] = 0,
            ["update"] = 0,
            ["failed"] = 0,
        };
        var retained = 0;
        var failures = new List<string>();
        foreach (var kind in Kinds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = FindCandidates(
                parent,
                installationName,
                kind,
                protectedPaths,
                failures);
            retained += Math.Min(
                retainedDirectoriesPerKind,
                candidates.Length);
            foreach (var candidate in candidates.Skip(retainedDirectoriesPerKind))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (timeProvider.GetUtcNow() - candidate.LastWriteTimeUtc
                    < minimumAge)
                {
                    retained++;
                    continue;
                }

                try
                {
                    deleteDirectory(candidate.FullName);
                    deleted[kind]++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    retained++;
                    failures.Add($"{candidate.FullName}: {exception.Message}");
                }
            }
        }

        return new ReleaseInstallationCleanupResult(
            deleted["backup"],
            deleted["update"],
            deleted["failed"],
            retained,
            failures);
    }

    public Task<ReleaseInstallationCleanupResult> CleanAsync(
        string installationDirectory,
        IEnumerable<string>? protectedDirectories = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Clean(
                installationDirectory,
                protectedDirectories,
                cancellationToken),
            cancellationToken);
    }

    private static DirectoryInfo[] FindCandidates(
        string parent,
        string installationName,
        string kind,
        HashSet<string> protectedPaths,
        List<string> failures)
    {
        var prefix = $".{installationName}-{kind}-";
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        try
        {
            return new DirectoryInfo(parent)
                .EnumerateDirectories()
                .Where(directory =>
                    directory.Name.StartsWith(prefix, comparison)
                    && IsGeneratedSuffix(directory.Name[prefix.Length..])
                    && !protectedPaths.Contains(Path.TrimEndingDirectorySeparator(
                        Path.GetFullPath(directory.FullName)))
                    && (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ThenByDescending(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{parent}: {exception.Message}");
            return [];
        }
    }

    private static bool IsGeneratedSuffix(string value)
    {
        return value.Length == 32
            && value.All(Uri.IsHexDigit)
            && Guid.TryParseExact(value, "N", out _);
    }
}

public sealed class ReleasePackageCacheCleaner
{
    private readonly TimeProvider timeProvider;
    private readonly int retainedVersions;
    private readonly TimeSpan minimumAge;
    private readonly Action<string> deleteDirectory;

    public ReleasePackageCacheCleaner(
        TimeProvider? timeProvider = null,
        int retainedVersions =
            ReleaseInstallationHistoryCleaner.DefaultRetainedDirectoriesPerKind,
        TimeSpan? minimumAge = null)
        : this(
            timeProvider,
            retainedVersions,
            minimumAge,
            path => Directory.Delete(path, recursive: true))
    {
    }

    internal ReleasePackageCacheCleaner(
        TimeProvider? timeProvider,
        int retainedVersions,
        TimeSpan? minimumAge,
        Action<string> deleteDirectory)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedVersions);
        var resolvedMinimumAge = minimumAge
            ?? ReleaseInstallationHistoryCleaner.DefaultMinimumAge;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            resolvedMinimumAge,
            TimeSpan.Zero);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.retainedVersions = retainedVersions;
        this.minimumAge = resolvedMinimumAge;
        this.deleteDirectory = deleteDirectory
            ?? throw new ArgumentNullException(nameof(deleteDirectory));
    }

    public ReleasePackageCacheCleanupResult Clean(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var updatesRoot = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "updates"));
        var failures = new List<string>();
        var packageResult = CleanVersionRoot(
            Path.Combine(updatesRoot, "packages"),
            failures,
            cancellationToken);
        var stagedResult = CleanVersionRoot(
            Path.Combine(updatesRoot, "staged"),
            failures,
            cancellationToken);
        return new ReleasePackageCacheCleanupResult(
            packageResult.Deleted,
            stagedResult.Deleted,
            packageResult.Retained + stagedResult.Retained,
            failures);
    }

    public Task<ReleasePackageCacheCleanupResult> CleanAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Clean(dataDirectory, cancellationToken),
            cancellationToken);
    }

    private (int Deleted, int Retained) CleanVersionRoot(
        string root,
        List<string> failures,
        CancellationToken cancellationToken)
    {
        DirectoryInfo[] candidates;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates = new DirectoryInfo(root)
                .EnumerateDirectories()
                .Where(directory =>
                    ReleaseVersion.TryParse(directory.Name, out _)
                    && (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ThenByDescending(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return (0, 0);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{root}: {exception.Message}");
            return (0, 0);
        }

        var deleted = 0;
        var retained = Math.Min(retainedVersions, candidates.Length);
        foreach (var candidate in candidates.Skip(retainedVersions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeProvider.GetUtcNow() - candidate.LastWriteTimeUtc < minimumAge)
            {
                retained++;
                continue;
            }

            try
            {
                deleteDirectory(candidate.FullName);
                deleted++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                retained++;
                failures.Add($"{candidate.FullName}: {exception.Message}");
            }
        }

        return (deleted, retained);
    }
}

public sealed class ReleaseUpdateHistoryCleanupCoordinator
{
    private readonly Channel<bool> gate = CreateGate();

    public async Task<ReleaseInstallationCleanupResult> CleanInstallationAsync(
        ReleaseInstallationHistoryCleaner cleaner,
        string installationDirectory,
        IEnumerable<string>? protectedDirectories = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        _ = await gate.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await cleaner.CleanAsync(
                    installationDirectory,
                    protectedDirectories,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseGate();
        }
    }

    public async Task<ReleasePackageCacheCleanupResult> CleanPackageCacheAsync(
        ReleasePackageCacheCleaner cleaner,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cleaner);
        _ = await gate.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await cleaner.CleanAsync(dataDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseGate();
        }
    }

    private static Channel<bool> CreateGate()
    {
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        if (!channel.Writer.TryWrite(true))
        {
            throw new InvalidOperationException(
                "Could not initialize the release-history cleanup gate.");
        }

        return channel;
    }

    private void ReleaseGate()
    {
        if (!gate.Writer.TryWrite(true))
        {
            throw new InvalidOperationException(
                "The release-history cleanup gate was released more than once.");
        }
    }
}
