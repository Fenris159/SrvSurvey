using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleasePackageStagingServiceTests : IDisposable
{
    private static readonly Version Version = new(2, 0, 95, 23);
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-release-staging-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task StageAsyncVerifiesZipFilesAndReusesReadyCandidate()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop.exe"] = Encoding.UTF8.GetBytes("entry point"),
            ["runtimes/win-x64/native/library.dll"] = [1, 2, 3, 4],
            ["empty.txt"] = [],
        };
        var bundle = await CreateZipAsync(files);
        var service = new ReleasePackageStagingService();

        var first = await service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory);
        var second = await service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory);

        Assert.False(first.Reused);
        Assert.True(second.Reused);
        Assert.Equal(files.Count, first.FileCount);
        Assert.Equal(files.Sum(pair => pair.Value.LongLength), first.ExpandedBytes);
        Assert.Equal(files["SrvSurvey.Desktop.exe"],
            await File.ReadAllBytesAsync(first.EntryPointPath));
        Assert.Equal(files["runtimes/win-x64/native/library.dll"],
            await File.ReadAllBytesAsync(Path.Combine(
                first.ReadyDirectory,
                "runtimes",
                "win-x64",
                "native",
                "library.dll")));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("CON")]
    [InlineData("folder/trailing. ")]
    public async Task StageAsyncRejectsUnsafePortablePathsBeforeExtraction(
        string unsafePath)
    {
        var bundle = await CreateZipAsync(
            new Dictionary<string, byte[]>
            {
                ["SrvSurvey.Desktop.exe"] = [1],
            },
            extraEntry: unsafePath);
        var service = new ReleasePackageStagingService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory));

        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "escaped.txt")));
        Assert.False(Directory.Exists(GetReadyDirectory("win-x64")));
    }

    [Fact]
    public async Task StageAsyncRejectsPayloadHashMismatchWithoutReplacingReady()
    {
        var originalFiles = new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop.exe"] = Encoding.UTF8.GetBytes("known good"),
        };
        var valid = await CreateZipAsync(originalFiles, archiveName: "valid.zip");
        var service = new ReleasePackageStagingService();
        var staged = await service.StageAsync(
            Version,
            valid.Package,
            valid.ArchivePath,
            temporaryDirectory);
        var originalReady = await File.ReadAllBytesAsync(staged.EntryPointPath);
        var invalid = await CreateZipAsync(
            new Dictionary<string, byte[]>
            {
                ["SrvSurvey.Desktop.exe"] = Encoding.UTF8.GetBytes("corrupt"),
            },
            archiveName: "invalid.zip",
            manifestHashOverride: new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(
            Version,
            invalid.Package,
            invalid.ArchivePath,
            temporaryDirectory));

        Assert.Equal(originalReady, await File.ReadAllBytesAsync(staged.EntryPointPath));
    }

    [Fact]
    public async Task StageAsyncExtractsTarGzipPackage()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop"] = Encoding.UTF8.GetBytes("linux entry"),
            ["lib/library.so"] = [5, 4, 3, 2, 1],
        };
        var bundle = await CreateTarAsync(files);
        var service = new ReleasePackageStagingService();

        var result = await service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory);

        Assert.False(result.Reused);
        Assert.Equal(files["SrvSurvey.Desktop"],
            await File.ReadAllBytesAsync(result.EntryPointPath));
        Assert.Equal(files["lib/library.so"],
            await File.ReadAllBytesAsync(Path.Combine(
                result.ReadyDirectory,
                "lib",
                "library.so")));
    }

    [Fact]
    public async Task StageAsyncRejectsTarSymbolicLink()
    {
        var bundle = await CreateTarAsync(
            new Dictionary<string, byte[]>
            {
                ["SrvSurvey.Desktop"] = [1, 2, 3],
            },
            includeSymbolicLink: true);
        var service = new ReleasePackageStagingService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory));

        Assert.False(Directory.Exists(GetReadyDirectory("linux-x64")));
    }

    [Fact]
    public async Task StageAsyncRechecksOuterArchiveBeforeInspection()
    {
        var bundle = await CreateZipAsync(new Dictionary<string, byte[]>
        {
            ["SrvSurvey.Desktop.exe"] = [1, 2, 3],
        });
        await File.AppendAllTextAsync(bundle.ArchivePath, "drift");
        var service = new ReleasePackageStagingService();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.StageAsync(
            Version,
            bundle.Package,
            bundle.ArchivePath,
            temporaryDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private async Task<PackageBundle> CreateZipAsync(
        IReadOnlyDictionary<string, byte[]> files,
        string? extraEntry = null,
        string archiveName = "package.zip",
        string? manifestHashOverride = null)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var archivePath = Path.Combine(temporaryDirectory, archiveName);
        var manifest = CreateManifest(
            "win-x64",
            "SrvSurvey.Desktop.exe",
            files,
            manifestHashOverride);
        await using (var stream = new FileStream(
            archivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
                await using var output = entry.Open();
                await output.WriteAsync(file.Value);
            }

            var manifestEntry = archive.CreateEntry("release-package.json");
            await using (var output = manifestEntry.Open())
            {
                await output.WriteAsync(manifest);
            }

            if (extraEntry is not null)
            {
                var entry = archive.CreateEntry(extraEntry);
                await using var output = entry.Open();
                await output.WriteAsync(new byte[] { 9 });
            }
        }

        return await CreateBundleAsync(
            archivePath,
            "win-x64",
            "zip",
            "SrvSurvey-XP-2.0.95.23-win-x64.zip");
    }

    private async Task<PackageBundle> CreateTarAsync(
        IReadOnlyDictionary<string, byte[]> files,
        bool includeSymbolicLink = false)
    {
        Directory.CreateDirectory(temporaryDirectory);
        var archivePath = Path.Combine(temporaryDirectory, "package.tar.gz");
        var manifest = CreateManifest(
            "linux-x64",
            "SrvSurvey.Desktop",
            files);
        await using (var stream = new FileStream(
            archivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        await using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
        using (var writer = new TarWriter(gzip, leaveOpen: false))
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./"));
            foreach (var file in files)
            {
                using var data = new MemoryStream(file.Value, writable: false);
                var entry = new PaxTarEntry(TarEntryType.RegularFile, file.Key)
                {
                    DataStream = data,
                    Mode = file.Key == "SrvSurvey.Desktop"
                        ? UnixFileMode.UserRead
                            | UnixFileMode.UserWrite
                            | UnixFileMode.UserExecute
                        : UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                writer.WriteEntry(entry);
            }

            using (var data = new MemoryStream(manifest, writable: false))
            {
                writer.WriteEntry(new PaxTarEntry(
                    TarEntryType.RegularFile,
                    "./release-package.json")
                {
                    DataStream = data,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                });
            }

            if (includeSymbolicLink)
            {
                writer.WriteEntry(new PaxTarEntry(
                    TarEntryType.SymbolicLink,
                    "linked-entry")
                {
                    LinkName = "SrvSurvey.Desktop",
                });
            }
        }

        return await CreateBundleAsync(
            archivePath,
            "linux-x64",
            "tar.gz",
            "SrvSurvey-XP-2.0.95.23-linux-x64.tar.gz");
    }

    private static byte[] CreateManifest(
        string runtimeIdentifier,
        string entryPoint,
        IReadOnlyDictionary<string, byte[]> files,
        string? hashOverride = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            product = "SrvSurvey.XP",
            version = Version.ToString(),
            runtimeIdentifier,
            entryPoint,
            files = files.Select(file => new
            {
                path = file.Key,
                size = file.Value.LongLength,
                sha256 = hashOverride
                    ?? Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant(),
            }),
        });
    }

    private static async Task<PackageBundle> CreateBundleAsync(
        string archivePath,
        string runtimeIdentifier,
        string archiveType,
        string packageName)
    {
        var info = new FileInfo(archivePath);
        await using var stream = File.OpenRead(archivePath);
        var hash = await SHA256.HashDataAsync(stream);
        return new PackageBundle(
            archivePath,
            new CrossPlatformReleasePackage(
                runtimeIdentifier,
                packageName,
                archiveType,
                info.Length,
                Convert.ToHexString(hash).ToLowerInvariant(),
                new Uri("https://downloads.example.test/package")));
    }

    private string GetReadyDirectory(string runtimeIdentifier)
    {
        return Path.Combine(
            temporaryDirectory,
            "updates",
            "staged",
            Version.ToString(),
            runtimeIdentifier,
            "ready");
    }

    private sealed record PackageBundle(
        string ArchivePath,
        CrossPlatformReleasePackage Package);
}
