using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseInstallationHistoryCleanerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-update-cleaner-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CleanupRetainsNewestRecentProtectedAndUnrecognizedDirectories()
    {
        var installation = Path.Combine(root, "SrvSurvey-XP");
        Directory.CreateDirectory(installation);
        var oldBackup = CreateGenerated("backup", 1, Now.AddDays(-8));
        var protectedBackup = CreateGenerated("backup", 2, Now.AddDays(-7));
        var retainedBackup = CreateGenerated("backup", 3, Now.AddDays(-6));
        var newestBackup = CreateGenerated("backup", 4, Now.AddDays(-5));
        var oldUpdate = CreateGenerated("update", 5, Now.AddDays(-8));
        var recentUpdate = CreateGenerated("update", 6, Now.AddHours(-2));
        var newestUpdate = CreateGenerated("update", 7, Now.AddHours(-1));
        var malformed = Path.Combine(root, ".SrvSurvey-XP-backup-not-a-guid");
        var otherInstall = Path.Combine(
            root,
            $".OtherProduct-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(malformed);
        Directory.CreateDirectory(otherInstall);

        var cleaner = new ReleaseInstallationHistoryCleaner(
            new FixedTimeProvider(Now),
            retainedDirectoriesPerKind: 1,
            minimumAge: TimeSpan.FromHours(24));
        var result = cleaner.Clean(installation, [protectedBackup]);

        Assert.False(Directory.Exists(oldBackup));
        Assert.True(Directory.Exists(protectedBackup));
        Assert.False(Directory.Exists(retainedBackup));
        Assert.True(Directory.Exists(newestBackup));
        Assert.False(Directory.Exists(oldUpdate));
        Assert.True(Directory.Exists(recentUpdate));
        Assert.True(Directory.Exists(newestUpdate));
        Assert.True(Directory.Exists(malformed));
        Assert.True(Directory.Exists(otherInstall));
        Assert.Equal(2, result.DeletedBackupDirectories);
        Assert.Equal(1, result.DeletedUpdateDirectories);
        Assert.Equal(0, result.DeletedFailedDirectories);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void CleanupDoesNotTraverseOrMatchDirectoriesOutsideInstallationParent()
    {
        var parent = Path.Combine(root, "parent");
        var installation = Path.Combine(parent, "SrvSurvey-XP");
        Directory.CreateDirectory(installation);
        var outside = Path.Combine(
            root,
            $".SrvSurvey-XP-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        Directory.SetLastWriteTimeUtc(outside, Now.AddYears(-1).UtcDateTime);

        var result = new ReleaseInstallationHistoryCleaner(
            new FixedTimeProvider(Now),
            retainedDirectoriesPerKind: 0,
            minimumAge: TimeSpan.Zero)
            .Clean(installation);

        Assert.True(Directory.Exists(outside));
        Assert.Equal(0, result.DeletedDirectories);
    }

    [Fact]
    public void PackageCacheCleanupRetainsNewestAndRecentVersionDirectories()
    {
        var dataDirectory = Path.Combine(root, "data");
        var packages = Path.Combine(dataDirectory, "updates", "packages");
        var staged = Path.Combine(dataDirectory, "updates", "staged");
        var oldPackage = CreateVersionDirectory(
            packages,
            "2.1.3.0-rc.20",
            Now.AddDays(-8));
        var recentPackage = CreateVersionDirectory(
            packages,
            "2.1.3.0-rc.21",
            Now.AddHours(-2));
        var newestPackage = CreateVersionDirectory(
            packages,
            "2.1.3.0-rc.22",
            Now.AddHours(-1));
        var oldStaged = CreateVersionDirectory(
            staged,
            "2.1.3.0-rc.20",
            Now.AddDays(-8));
        var newestStaged = CreateVersionDirectory(
            staged,
            "2.1.3.0-rc.22",
            Now.AddDays(-2));
        var malformed = CreateVersionDirectory(
            packages,
            "not-a-version",
            Now.AddYears(-1));

        var result = new ReleasePackageCacheCleaner(
            new FixedTimeProvider(Now),
            retainedVersions: 1,
            minimumAge: TimeSpan.FromHours(24))
            .Clean(dataDirectory);

        Assert.False(Directory.Exists(oldPackage));
        Assert.True(Directory.Exists(recentPackage));
        Assert.True(Directory.Exists(newestPackage));
        Assert.False(Directory.Exists(oldStaged));
        Assert.True(Directory.Exists(newestStaged));
        Assert.True(Directory.Exists(malformed));
        Assert.Equal(1, result.DeletedPackageVersions);
        Assert.Equal(1, result.DeletedStagedVersions);
        Assert.Empty(result.Failures);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private string CreateGenerated(
        string kind,
        int seed,
        DateTimeOffset lastWriteTime)
    {
        var suffix = seed.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(root, $".SrvSurvey-XP-{kind}-{suffix}");
        Directory.CreateDirectory(path);
        Directory.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
        return path;
    }

    private static string CreateVersionDirectory(
        string root,
        string version,
        DateTimeOffset lastWriteTime)
    {
        var path = Path.Combine(root, version);
        Directory.CreateDirectory(path);
        Directory.SetLastWriteTimeUtc(path, lastWriteTime.UtcDateTime);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
