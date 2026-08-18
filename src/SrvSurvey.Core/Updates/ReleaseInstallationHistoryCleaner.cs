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

    public ReleaseInstallationHistoryCleaner(
        TimeProvider? timeProvider = null,
        int retainedDirectoriesPerKind = DefaultRetainedDirectoriesPerKind,
        TimeSpan? minimumAge = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedDirectoriesPerKind);
        var resolvedMinimumAge = minimumAge ?? DefaultMinimumAge;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            resolvedMinimumAge,
            TimeSpan.Zero);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.retainedDirectoriesPerKind = retainedDirectoriesPerKind;
        this.minimumAge = resolvedMinimumAge;
    }

    public ReleaseInstallationCleanupResult Clean(
        string installationDirectory,
        IEnumerable<string>? protectedDirectories = null)
    {
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
            var candidates = FindCandidates(
                parent,
                installationName,
                kind,
                protectedPaths,
                failures);
            retained += Math.Min(
                retainedDirectoriesPerKind,
                candidates.Count);
            foreach (var candidate in candidates.Skip(retainedDirectoriesPerKind))
            {
                if (timeProvider.GetUtcNow() - candidate.LastWriteTimeUtc
                    < minimumAge)
                {
                    retained++;
                    continue;
                }

                try
                {
                    Directory.Delete(candidate.FullName, recursive: true);
                    deleted[kind]++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
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

    private static IReadOnlyList<DirectoryInfo> FindCandidates(
        string parent,
        string installationName,
        string kind,
        IReadOnlySet<string> protectedPaths,
        ICollection<string> failures)
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

    public ReleasePackageCacheCleaner(
        TimeProvider? timeProvider = null,
        int retainedVersions =
            ReleaseInstallationHistoryCleaner.DefaultRetainedDirectoriesPerKind,
        TimeSpan? minimumAge = null)
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
    }

    public ReleasePackageCacheCleanupResult Clean(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var updatesRoot = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "updates"));
        var failures = new List<string>();
        var packageResult = CleanVersionRoot(
            Path.Combine(updatesRoot, "packages"),
            failures);
        var stagedResult = CleanVersionRoot(
            Path.Combine(updatesRoot, "staged"),
            failures);
        return new ReleasePackageCacheCleanupResult(
            packageResult.Deleted,
            stagedResult.Deleted,
            packageResult.Retained + stagedResult.Retained,
            failures);
    }

    private (int Deleted, int Retained) CleanVersionRoot(
        string root,
        ICollection<string> failures)
    {
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        DirectoryInfo[] candidates;
        try
        {
            candidates = new DirectoryInfo(root)
                .EnumerateDirectories()
                .Where(directory =>
                    ReleaseVersion.TryParse(directory.Name, out _)
                    && (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ThenByDescending(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
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
            if (timeProvider.GetUtcNow() - candidate.LastWriteTimeUtc < minimumAge)
            {
                retained++;
                continue;
            }

            try
            {
                Directory.Delete(candidate.FullName, recursive: true);
                deleted++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{candidate.FullName}: {exception.Message}");
            }
        }

        return (deleted, retained);
    }
}
