namespace SrvSurvey.Core.Updates;

public sealed record ReleaseInstallationPlanCleanupResult(
    int DeletedPlans,
    int RetainedPlans,
    IReadOnlyList<string> Failures);

public sealed class ReleaseInstallationPlanCleaner
{
    public static readonly TimeSpan DefaultMinimumAge =
        ReleaseInstallationHistoryCleaner.DefaultMinimumAge;

    private readonly TimeProvider timeProvider;
    private readonly TimeSpan minimumAge;
    private readonly Action<string> deleteDirectory;

    public ReleaseInstallationPlanCleaner(
        TimeProvider? timeProvider = null,
        TimeSpan? minimumAge = null)
        : this(
            timeProvider,
            minimumAge,
            path => Directory.Delete(path, recursive: true))
    {
    }

    internal ReleaseInstallationPlanCleaner(
        TimeProvider? timeProvider,
        TimeSpan? minimumAge,
        Action<string> deleteDirectory)
    {
        var resolvedMinimumAge = minimumAge ?? DefaultMinimumAge;
        ArgumentOutOfRangeException.ThrowIfLessThan(
            resolvedMinimumAge,
            TimeSpan.Zero);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.minimumAge = resolvedMinimumAge;
        this.deleteDirectory = deleteDirectory
            ?? throw new ArgumentNullException(nameof(deleteDirectory));
    }

    public ReleaseInstallationPlanCleanupResult Clean(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(Path.Combine(
            dataDirectory,
            "updates",
            "install-plans"));
        var failures = new List<string>();
        var candidates = FindCandidates(root, failures);
        var deleted = 0;
        var retained = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if ((candidate.Attributes & FileAttributes.ReparsePoint) != 0
                    || timeProvider.GetUtcNow() - candidate.LastWriteTimeUtc
                        < minimumAge)
                {
                    retained++;
                    continue;
                }

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

        return new ReleaseInstallationPlanCleanupResult(
            deleted,
            retained,
            failures);
    }

    public Task<ReleaseInstallationPlanCleanupResult> CleanAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Clean(dataDirectory, cancellationToken),
            cancellationToken);
    }

    private static DirectoryInfo[] FindCandidates(
        string root,
        List<string> failures)
    {
        try
        {
            var rootDirectory = new DirectoryInfo(root);
            if (!rootDirectory.Exists)
            {
                return [];
            }

            if ((rootDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                failures.Add(
                    $"{root}: the installation-plan root is a reparse point.");
                return [];
            }

            return rootDirectory
                .EnumerateDirectories()
                .Where(directory =>
                    Guid.TryParseExact(directory.Name, "N", out var requestId)
                    && requestId != Guid.Empty)
                .OrderBy(directory => directory.LastWriteTimeUtc)
                .ThenBy(directory => directory.Name, StringComparer.Ordinal)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{root}: {exception.Message}");
            return [];
        }
    }
}
