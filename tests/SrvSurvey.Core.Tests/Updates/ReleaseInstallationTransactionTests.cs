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
        $"SrvSurvey-install-transaction-tests-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task PrepareAndApplySwapWholeInstallationAndKeepBackup()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        var preparer = new ReleaseInstallationPreparer();
        ReleaseInstallationPreparation preparation = await preparer.PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            ["--journal", "C:\\Elite Journals"]
        );

        Assert.Equal(
            fixture.OldEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(fixture.InstallationDirectory, "SrvSurvey.Desktop.exe"))
        );
        Assert.True(File.Exists(Path.Combine(preparation.CandidateDirectory, "release-package.json")));
        var transaction = new ReleaseInstallationTransaction();
        ReleaseInstallationResult result = await transaction.ApplyAsync(
            preparation,
            async (entryPoint, arguments, cancellationToken) =>
            {
                Assert.Equal(fixture.NewEntryPoint, await File.ReadAllBytesAsync(entryPoint, cancellationToken));
                Assert.Equal(["--journal", "C:\\Elite Journals"], arguments);
                return true;
            }
        );

        Assert.Equal(ReleaseInstallationStatus.Installed, result.Status);
        Assert.Equal(
            fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(fixture.InstallationDirectory, "SrvSurvey.Desktop.exe"))
        );
        Assert.False(File.Exists(Path.Combine(fixture.InstallationDirectory, "old-only.dll")));
        Assert.Equal(
            fixture.OldEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(preparation.BackupDirectory, "SrvSurvey.Desktop.exe"))
        );
        Assert.False(Directory.Exists(preparation.CandidateDirectory));
    }

    [Fact]
    public async Task AbortRemovesPreparedCandidateWithoutChangingInstallation()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        var preparer = new ReleaseInstallationPreparer();
        ReleaseInstallationPreparation preparation = await preparer.PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []
        );
        Assert.True(Directory.Exists(preparation.CandidateDirectory));

        await preparer.AbortAsync(preparation);

        Assert.False(Directory.Exists(preparation.CandidateDirectory));
        Assert.Equal(
            fixture.OldEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(fixture.InstallationDirectory, "SrvSurvey.Desktop.exe"))
        );
    }

    [Fact]
    public async Task FailedHealthConfirmationRestoresOldInstallationByteForByte()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        ReleaseInstallationPreparation preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []
        );
        IReadOnlyDictionary<string, byte[]> before = await SnapshotAsync(fixture.InstallationDirectory);

        ReleaseInstallationResult result = await new ReleaseInstallationTransaction().ApplyAsync(
            preparation,
            (_, _, _) => Task.FromResult(false)
        );

        Assert.Equal(ReleaseInstallationStatus.RolledBack, result.Status);
        AssertSnapshotsEqual(before, await SnapshotAsync(fixture.InstallationDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
        Assert.Equal(
            fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(preparation.FailedDirectory, "SrvSurvey.Desktop.exe"))
        );
    }

    [Fact]
    public async Task InstallationDriftAbortsBeforeAnyDirectoryMove()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        ReleaseInstallationPreparation preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []
        );
        string driftPath = Path.Combine(fixture.InstallationDirectory, "old-only.dll");
        await File.WriteAllTextAsync(driftPath, "changed after preparation");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseInstallationTransaction().ApplyAsync(preparation, (_, _, _) => Task.FromResult(true))
        );

        Assert.Equal("changed after preparation", await File.ReadAllTextAsync(driftPath));
        Assert.True(Directory.Exists(preparation.CandidateDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
    }

    [Fact]
    public async Task SwapFailureAfterActivationRestoresBackupAndPreservesCandidate()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        ReleaseInstallationPreparation preparation = await new ReleaseInstallationPreparer().PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []
        );
        IReadOnlyDictionary<string, byte[]> before = await SnapshotAsync(fixture.InstallationDirectory);
        var transaction = new ReleaseInstallationTransaction(
            stagingService: null,
            checkpoint: checkpoint =>
            {
                if (checkpoint == ReleaseInstallationCheckpoint.CandidateActivated)
                {
                    throw new IOException("injected post-activation failure");
                }
            }
        );

        await Assert.ThrowsAsync<IOException>(() =>
            transaction.ApplyAsync(preparation, (_, _, _) => Task.FromResult(true))
        );

        AssertSnapshotsEqual(before, await SnapshotAsync(fixture.InstallationDirectory));
        Assert.False(Directory.Exists(preparation.BackupDirectory));
        Assert.Equal(
            fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(preparation.FailedDirectory, "SrvSurvey.Desktop.exe"))
        );
    }

    [Fact]
    public async Task ReadyDriftIsRejectedBeforeCandidateCreation()
    {
        InstallationFixture fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.ReadyDirectory, "nested", "new.dll"), "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseInstallationPreparer().PrepareAsync(
                Version,
                "win-x64",
                fixture.ReadyDirectory,
                fixture.ManifestSha256,
                fixture.InstallationDirectory,
                []
            )
        );

        string parent = Directory.GetParent(fixture.InstallationDirectory)!.FullName;
        Assert.DoesNotContain(
            Directory.GetDirectories(parent),
            path => Path.GetFileName(path).Contains("-update-", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task AppImagePrepareAndApplyReplacesInstalledImageAndKeepsBackup()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-install");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "downloaded.AppImage");
        byte[] installed = CreateAppImage(0x11);
        byte[] downloaded = CreateAppImage(0x22);
        await File.WriteAllBytesAsync(installedPath, installed);
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                installedPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
        string sha256 = Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant();

        ReleaseInstallationPreparation preparation = await new AppImageReleaseInstallationPreparer().PrepareAsync(
            Version,
            downloadedPath,
            sha256,
            installedPath,
            ["--journal-directory", "/home/cmdr/journals"]
        );
        Assert.Equal(ReleaseInstallationKind.AppImage, preparation.Kind);
        Assert.True(File.Exists(preparation.CandidateDirectory));

        ReleaseInstallationResult result = await new ReleaseInstallationTransaction().ApplyAsync(
            preparation,
            async (entryPoint, arguments, cancellationToken) =>
            {
                Assert.Equal(installedPath, entryPoint);
                Assert.Equal(["--journal-directory", "/home/cmdr/journals"], arguments);
                Assert.Equal(downloaded, await File.ReadAllBytesAsync(entryPoint, cancellationToken));
                return true;
            }
        );

        Assert.Equal(ReleaseInstallationStatus.Installed, result.Status);
        Assert.Equal(downloaded, await File.ReadAllBytesAsync(installedPath));
        Assert.Equal(installed, await File.ReadAllBytesAsync(preparation.BackupDirectory));
        Assert.False(File.Exists(preparation.CandidateDirectory));
        if (OperatingSystem.IsLinux())
        {
            Assert.True((File.GetUnixFileMode(installedPath) & UnixFileMode.UserExecute) != 0);
        }
    }

    [Fact]
    public async Task FailedAppImageHealthConfirmationRestoresInstalledImage()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-rollback");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "rollback.AppImage");
        byte[] installed = CreateAppImage(0x33);
        byte[] downloaded = CreateAppImage(0x44);
        await File.WriteAllBytesAsync(installedPath, installed);
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        string sha256 = Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant();
        ReleaseInstallationPreparation preparation = await new AppImageReleaseInstallationPreparer().PrepareAsync(
            Version,
            downloadedPath,
            sha256,
            installedPath,
            []
        );

        ReleaseInstallationResult result = await new ReleaseInstallationTransaction().ApplyAsync(
            preparation,
            (_, _, _) => Task.FromResult(false)
        );

        Assert.Equal(ReleaseInstallationStatus.RolledBack, result.Status);
        Assert.Equal(installed, await File.ReadAllBytesAsync(installedPath));
        Assert.Equal(downloaded, await File.ReadAllBytesAsync(preparation.FailedDirectory));
        Assert.False(File.Exists(preparation.BackupDirectory));
    }

    [Fact]
    public async Task AppImageAbortRemovesCandidateWithoutChangingInstalledImage()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-abort");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "abort.AppImage");
        byte[] installed = CreateAppImage(0x51);
        byte[] downloaded = CreateAppImage(0x52);
        await File.WriteAllBytesAsync(installedPath, installed);
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        string sha256 = Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant();
        var preparer = new AppImageReleaseInstallationPreparer();
        ReleaseInstallationPreparation preparation = await preparer.PrepareAsync(
            Version,
            downloadedPath,
            sha256,
            installedPath,
            []
        );

        await preparer.AbortAsync(preparation);

        Assert.False(File.Exists(preparation.CandidateDirectory));
        Assert.Equal(installed, await File.ReadAllBytesAsync(installedPath));
    }

    [Fact]
    public async Task AppImagePreparationRejectsInvalidImageAndWrongChecksum()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-invalid");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "invalid.AppImage");
        await File.WriteAllBytesAsync(installedPath, CreateAppImage(0x61));
        byte[] invalid = Enumerable.Repeat((byte)0x62, 128).ToArray();
        await File.WriteAllBytesAsync(downloadedPath, invalid);
        var preparer = new AppImageReleaseInstallationPreparer();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            preparer.PrepareAsync(
                Version,
                downloadedPath,
                Convert.ToHexString(SHA256.HashData(invalid)).ToLowerInvariant(),
                installedPath,
                []
            )
        );

        byte[] valid = CreateAppImage(0x63);
        await File.WriteAllBytesAsync(downloadedPath, valid);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            preparer.PrepareAsync(Version, downloadedPath, new string('0', 64), installedPath, [])
        );
    }

    [Fact]
    public async Task InstalledAppImageDriftStopsReplacementBeforeMove()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-drift");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "drift.AppImage");
        byte[] downloaded = CreateAppImage(0x72);
        await File.WriteAllBytesAsync(installedPath, CreateAppImage(0x71));
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        ReleaseInstallationPreparation preparation = await new AppImageReleaseInstallationPreparer().PrepareAsync(
            Version,
            downloadedPath,
            Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant(),
            installedPath,
            []
        );
        await File.WriteAllBytesAsync(installedPath, CreateAppImage(0x73));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ReleaseInstallationTransaction().ApplyAsync(preparation, (_, _, _) => Task.FromResult(true))
        );

        Assert.True(File.Exists(preparation.CandidateDirectory));
        Assert.False(File.Exists(preparation.BackupDirectory));
    }

    [Fact]
    public async Task AppImageActivationFailureRestoresBackupAndPreservesFailedImage()
    {
        string parent = Path.Combine(temporaryDirectory, "appimage-activation-failure");
        Directory.CreateDirectory(parent);
        string installedPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "activation-failure.AppImage");
        byte[] installed = CreateAppImage(0x81);
        byte[] downloaded = CreateAppImage(0x82);
        await File.WriteAllBytesAsync(installedPath, installed);
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        ReleaseInstallationPreparation preparation = await new AppImageReleaseInstallationPreparer().PrepareAsync(
            Version,
            downloadedPath,
            Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant(),
            installedPath,
            []
        );
        var transaction = new ReleaseInstallationTransaction(
            stagingService: null,
            checkpoint: checkpoint =>
            {
                if (checkpoint == ReleaseInstallationCheckpoint.CandidateActivated)
                {
                    throw new IOException("injected AppImage activation failure");
                }
            }
        );

        await Assert.ThrowsAsync<IOException>(() =>
            transaction.ApplyAsync(preparation, (_, _, _) => Task.FromResult(true))
        );

        Assert.Equal(installed, await File.ReadAllBytesAsync(installedPath));
        Assert.Equal(downloaded, await File.ReadAllBytesAsync(preparation.FailedDirectory));
        Assert.False(File.Exists(preparation.BackupDirectory));
    }

    [Fact]
    public async Task AppImagePreparationResolvesStableSymbolicLinkToItsFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string parent = Path.Combine(temporaryDirectory, "appimage-link");
        Directory.CreateDirectory(parent);
        string targetPath = Path.Combine(parent, "SrvSurvey-XP-current.AppImage");
        string linkPath = Path.Combine(parent, "SrvSurvey.AppImage");
        string downloadedPath = Path.Combine(temporaryDirectory, "linked-update.AppImage");
        byte[] installed = CreateAppImage(0x55);
        byte[] downloaded = CreateAppImage(0x66);
        await File.WriteAllBytesAsync(targetPath, installed);
        File.CreateSymbolicLink(linkPath, targetPath);
        await File.WriteAllBytesAsync(downloadedPath, downloaded);
        string sha256 = Convert.ToHexString(SHA256.HashData(downloaded)).ToLowerInvariant();

        ReleaseInstallationPreparation preparation = await new AppImageReleaseInstallationPreparer().PrepareAsync(
            Version,
            downloadedPath,
            sha256,
            linkPath,
            []
        );

        Assert.Equal(parent, preparation.InstallationDirectory);
        Assert.Equal(Path.GetFileName(targetPath), preparation.EntryPoint);
        Assert.True(AppImageReleaseInstallationPreparer.CanReplace(linkPath));
        await new AppImageReleaseInstallationPreparer().AbortAsync(preparation);
    }

    [Fact]
    public async Task ProtectedWindowsInstallDefersCandidateCopyToHelper()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InstallationFixture fixture = await CreateFixtureAsync();
        var preparer = new ReleaseInstallationPreparer(
            stagingService: null,
            (_, _, _) => throw new UnauthorizedAccessException("protected installation parent")
        );

        ReleaseInstallationPreparation preparation = await preparer.PrepareAsync(
            Version,
            "win-x64",
            fixture.ReadyDirectory,
            fixture.ManifestSha256,
            fixture.InstallationDirectory,
            []
        );

        Assert.True(preparation.RequiresElevation);
        Assert.Equal(Path.GetFullPath(fixture.ReadyDirectory), preparation.ReadyDirectory);
        Assert.False(Directory.Exists(preparation.CandidateDirectory));

        ReleaseInstallationResult result = await new ReleaseInstallationTransaction().ApplyAsync(
            preparation,
            (_, _, _) => Task.FromResult(true)
        );

        Assert.Equal(ReleaseInstallationStatus.Installed, result.Status);
        Assert.Equal(
            fixture.NewEntryPoint,
            await File.ReadAllBytesAsync(Path.Combine(fixture.InstallationDirectory, "SrvSurvey.Desktop.exe"))
        );
        Assert.False(Directory.Exists(preparation.CandidateDirectory));
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
        string installationDirectory = Path.Combine(temporaryDirectory, "install-parent", "SrvSurvey");
        string readyDirectory = Path.Combine(temporaryDirectory, "ready");
        Directory.CreateDirectory(installationDirectory);
        Directory.CreateDirectory(Path.Combine(readyDirectory, "nested"));
        byte[] oldEntryPoint = Encoding.UTF8.GetBytes("old executable");
        byte[] newEntryPoint = Encoding.UTF8.GetBytes("new executable");
        await File.WriteAllBytesAsync(Path.Combine(installationDirectory, "SrvSurvey.Desktop.exe"), oldEntryPoint);
        await File.WriteAllTextAsync(Path.Combine(installationDirectory, "old-only.dll"), "old dependency");
        var newFiles = new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop.exe"] = newEntryPoint,
            ["nested/new.dll"] = Encoding.UTF8.GetBytes("new dependency"),
        };
        foreach (KeyValuePair<string, byte[]> file in newFiles)
        {
            string path = Path.Combine(readyDirectory, file.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.Value);
        }

        byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                schemaVersion = 1,
                product = "SrvSurvey.XP",
                version = Version.ToString(),
                runtimeIdentifier = "win-x64",
                entryPoint = "SrvSurvey.Desktop.exe",
                files = newFiles.Select(file => new
                {
                    path = file.Key,
                    size = file.Value.LongLength,
                    sha256 = Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(),
                }),
            }
        );
        await File.WriteAllBytesAsync(Path.Combine(readyDirectory, "release-package.json"), manifest);
        return new InstallationFixture(
            installationDirectory,
            readyDirectory,
            Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(),
            oldEntryPoint,
            newEntryPoint
        );
    }

    private static byte[] CreateAppImage(byte payload)
    {
        byte[] bytes = Enumerable.Repeat(payload, 128).ToArray();
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 2;
        bytes[5] = 1;
        bytes[8] = (byte)'A';
        bytes[9] = (byte)'I';
        bytes[10] = 2;
        bytes[18] = 62;
        bytes[19] = 0;
        return bytes;
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> SnapshotAsync(string directory)
    {
        var snapshot = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            snapshot[Path.GetRelativePath(directory, path)] = await File.ReadAllBytesAsync(path);
        }

        return snapshot;
    }

    private static void AssertSnapshotsEqual(
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> actual
    )
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (KeyValuePair<string, byte[]> pair in expected)
        {
            Assert.Equal(pair.Value, actual[pair.Key]);
        }
    }

    private sealed record InstallationFixture(
        string InstallationDirectory,
        string ReadyDirectory,
        string ManifestSha256,
        byte[] OldEntryPoint,
        byte[] NewEntryPoint
    );
}
