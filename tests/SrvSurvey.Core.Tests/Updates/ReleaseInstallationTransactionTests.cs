using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseInstallationTransactionTests : IDisposable
{
    private static readonly Version Version = new(2, 0, 95, 23);
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-install-transaction-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task PrepareAndApplySwapWholeInstallationAndKeepBackup()
    {
        var fixture = await CreateFixtureAsync();
        var preparer = new ReleaseInstallationPreparer();
        var preparation = await preparer.PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            ["--journal", "C:\\Elite Journals"]);

        Assert.Equal(
            fixture.OldEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(
                fixture.InstallationDirectory,
                "SrvSurvey.Desktop.exe")));
        Assert.True(File.Exists(Path.Combine(
            preparation.CandidateDirectory,
            "release-package.json")));
        var transaction = new ReleaseInstallationTransaction();
        var result = await transaction.ApplyAsync(
            preparation,
            async (entryPoint, arguments, _) =>
            {
                Assert.Equal(fixture.NewEntryPoint,
                    await File.ReadAllBytesAsync(entryPoint));
                Assert.Equal(["--journal", "C:\\Elite Journals"], arguments);
                return true;
            });

        Assert.Equal(ReleaseInstallationStatus.Installed, result.Status);
        Assert.Equal(fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(
                fixture.InstallationDirectory,
                "SrvSurvey.Desktop.exe")));
        Assert.False(File.Exists(Path.Combine(
            fixture.InstallationDirectory,
            "old-only.dll")));
        Assert.Equal(fixture.OldEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(
                preparation.BackupDirectory,
                "SrvSurvey.Desktop.exe")));
        Assert.False(Directory.Exists(preparation.CandidateDirectory));
    }

    [Fact]
    public async Task FailedHealthConfirmationRestoresOldInstallationByteForByte()
    {
        var fixture = await CreateFixtureAsync();
        var preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []);
        var before = await SnapshotAsync(fixture.InstallationDirectory);

        var result = await new ReleaseInstallationTransaction().ApplyAsync(
            preparation,
            (_, _, _) => Task.FromResult(false));

        Assert.Equal(ReleaseInstallationStatus.RolledBack, result.Status);
        AssertSnapshotsEqual(
            before,
            await SnapshotAsync(fixture.InstallationDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
        Assert.Equal(fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(
                preparation.FailedDirectory,
                "SrvSurvey.Desktop.exe")));
    }

    [Fact]
    public async Task InstallationDriftAbortsBeforeAnyDirectoryMove()
    {
        var fixture = await CreateFixtureAsync();
        var preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []);
        var driftPath = Path.Combine(fixture.InstallationDirectory, "old-only.dll");
        await File.WriteAllTextAsync(driftPath, "changed after preparation");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseInstallationTransaction().ApplyAsync(
                preparation,
                (_, _, _) => Task.FromResult(true)));

        Assert.Equal("changed after preparation", await File.ReadAllTextAsync(driftPath));
        Assert.True(Directory.Exists(preparation.CandidateDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
    }

    [Fact]
    public async Task SwapFailureAfterActivationRestoresBackupAndPreservesCandidate()
    {
        var fixture = await CreateFixtureAsync();
        var preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []);
        var before = await SnapshotAsync(fixture.InstallationDirectory);
        var transaction = new ReleaseInstallationTransaction(
            stagingService: null,
            checkpoint: checkpoint =>
            {
                if (checkpoint == ReleaseInstallationCheckpoint.CandidateActivated)
                {
                    throw new IOException("injected post-activation failure");
                }
            });

        await Assert.ThrowsAsync<IOException>(() => transaction.ApplyAsync(
            preparation,
            (_, _, _) => Task.FromResult(true)));

        AssertSnapshotsEqual(
            before,
            await SnapshotAsync(fixture.InstallationDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
        Assert.Equal(fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(
                preparation.FailedDirectory,
                "SrvSurvey.Desktop.exe")));
    }

    [Fact]
    public async Task ReadyDriftIsRejectedBeforeCandidateCreation()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(
            Path.Combine(fixture.ReadyDirectory, "nested", "new.dll"),
            "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseInstallationPreparer().PrepareAsync(
                Version,
                "win-x64",
                fixture.ReadyDirectory,
                fixture.ManifestSha256,
                fixture.InstallationDirectory,
                []));

        var parent = Directory.GetParent(fixture.InstallationDirectory)!.FullName;
        Assert.DoesNotContain(
            Directory.GetDirectories(parent),
            path => Path.GetFileName(path).Contains("-update-", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task<InstallationFixture> CreateFixtureAsync()
    {
        var installationDirectory = Path.Combine(
            temporaryDirectory,
            "install-parent",
            "SrvSurvey");
        var readyDirectory = Path.Combine(temporaryDirectory, "ready");
        Directory.CreateDirectory(installationDirectory);
        Directory.CreateDirectory(Path.Combine(readyDirectory, "nested"));
        var oldEntryPoint = Encoding.UTF8.GetBytes("old executable");
        var newEntryPoint = Encoding.UTF8.GetBytes("new executable");
        await File.WriteAllBytesAsync(
            Path.Combine(installationDirectory, "SrvSurvey.Desktop.exe"),
            oldEntryPoint);
        await File.WriteAllTextAsync(
            Path.Combine(installationDirectory, "old-only.dll"),
            "old dependency");
        var newFiles = new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop.exe"] = newEntryPoint,
            ["nested/new.dll"] = Encoding.UTF8.GetBytes("new dependency"),
        };
        foreach (var file in newFiles)
        {
            var path = Path.Combine(
                readyDirectory,
                file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.Value);
        }

        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            product = "SrvSurvey.Avalonia",
            version = Version.ToString(),
            runtimeIdentifier = "win-x64",
            entryPoint = "SrvSurvey.Desktop.exe",
            files = newFiles.Select(file => new
            {
                path = file.Key,
                size = file.Value.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(),
            }),
        });
        await File.WriteAllBytesAsync(
            Path.Combine(readyDirectory, "release-package.json"),
            manifest);
        return new InstallationFixture(
            installationDirectory,
            readyDirectory,
            Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(),
            oldEntryPoint,
            newEntryPoint);
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> SnapshotAsync(
        string directory)
    {
        var snapshot = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(
            directory,
            "*",
            SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(directory, path)] =
                await File.ReadAllBytesAsync(path);
        }

        return snapshot;
    }

    private static void AssertSnapshotsEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    private sealed record InstallationFixture(
        string InstallationDirectory,
        string ReadyDirectory,
        string ManifestSha256,
        byte[] OldEntryPoint,
        byte[] NewEntryPoint);
}
