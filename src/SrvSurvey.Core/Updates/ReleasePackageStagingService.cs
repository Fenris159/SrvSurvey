using System.Buffers;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Updates;

public interface IReleasePackageStagingService
{
    Task<ReleasePackageStagingResult> StageAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        string dataDirectory,
        CancellationToken cancellationToken = default);

    Task<ReleasePackageStagingResult> VerifyReadyAsync(
        ReleaseVersion version,
        string runtimeIdentifier,
        string readyDirectory,
        string manifestSha256,
        CancellationToken cancellationToken = default);
}

public sealed record ReleasePackageStagingResult(
    string ReadyDirectory,
    string EntryPointPath,
    bool Reused,
    int FileCount,
    long ExpandedBytes,
    string ManifestSha256);

public sealed class ReleasePackageStagingService
    : IReleasePackageStagingService
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumFileCount = 4_096;
    private const int MaximumArchiveEntryCount = 8_192;
    private const long MaximumExpandedBytes = 1024L * 1024 * 1024;
    private const long MaximumSingleFileBytes = 512L * 1024 * 1024;
    private const string ManifestName = "release-package.json";
    private const string ProductName = "SrvSurvey.XP";
    private const string RuntimeWinX64 = "win-x64";
    private const string RuntimeLinuxX64 = "linux-x64";
    private const string ArchiveTypeZip = "zip";
    private const string ArchiveTypeTarGz = "tar.gz";
    private static readonly char[] InvalidPortableNameCharacters =
        ['<', '>', ':', '"', '|', '?', '*'];
    private static readonly SearchValues<char> InvalidPortableNameSearch =
        SearchValues.Create(InvalidPortableNameCharacters);

    public async Task<ReleasePackageStagingResult> StageAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(version, package, archivePath, dataDirectory);
        await VerifyArchiveAsync(package, archivePath, cancellationToken)
            .ConfigureAwait(false);
        var archive = await InspectArchiveAsync(
                version,
                package,
                archivePath,
                cancellationToken)
            .ConfigureAwait(false);
        var stageRoot = ResolveStageRoot(
            dataDirectory,
            version,
            package.RuntimeIdentifier);
        var readyDirectory = Path.Combine(stageRoot, "ready");
        if (await IsReadyAsync(
                readyDirectory,
                archive.Manifest,
                archive.ManifestBytes,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return CreateResult(
                readyDirectory,
                archive.Manifest,
                archive.ManifestBytes,
                reused: true);
        }

        Directory.CreateDirectory(stageRoot);
        var candidateDirectory = Path.Combine(
            stageRoot,
            $"candidate-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(candidateDirectory);
            await ExtractArchiveAsync(
                    archivePath,
                    package.ArchiveType,
                    candidateDirectory,
                    archive,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!await IsReadyAsync(
                    candidateDirectory,
                    archive.Manifest,
                    archive.ManifestBytes,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    "The extracted update candidate failed final verification.");
            }

            ActivateCandidate(stageRoot, candidateDirectory, readyDirectory);
            return CreateResult(
                readyDirectory,
                archive.Manifest,
                archive.ManifestBytes,
                reused: false);
        }
        finally
        {
            TryDeleteDirectory(candidateDirectory);
        }
    }

    public async Task<ReleasePackageStagingResult> VerifyReadyAsync(
        ReleaseVersion version,
        string runtimeIdentifier,
        string readyDirectory,
        string manifestSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(readyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestSha256);
        if (version.Build < 0
            || runtimeIdentifier is not (RuntimeWinX64 or RuntimeLinuxX64)
            || manifestSha256.Length != 64
            || manifestSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "The ready-package verification metadata is invalid.");
        }

        var manifestPath = Path.Combine(
            Path.GetFullPath(readyDirectory),
            ManifestName);
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The ready update has no bounded package manifest.");
        }

        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        var actualManifestHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!HashesMatch(actualManifestHash, manifestSha256))
        {
            throw new InvalidDataException(
                "The ready update manifest no longer matches the verified archive.");
        }

        var archiveType = runtimeIdentifier == RuntimeWinX64 ? ArchiveTypeZip : ArchiveTypeTarGz;
        var suffix = archiveType == ArchiveTypeZip ? ".zip" : ".tar.gz";
        var package = new CrossPlatformReleasePackage(
            runtimeIdentifier,
            $"SrvSurvey-XP-{version}-{runtimeIdentifier}{suffix}",
            archiveType,
            1,
            new string('0', 64),
            WellKnownUris.ExampleInvalidPackage);
        var manifest = ParseManifest(bytes, version, package);
        if (!await IsReadyAsync(
                readyDirectory,
                manifest,
                bytes,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "The ready update files no longer match their package manifest.");
        }

        return CreateResult(
            Path.GetFullPath(readyDirectory),
            manifest,
            bytes,
            reused: true);
    }

    private static async Task<InspectedArchive> InspectArchiveAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        CancellationToken cancellationToken)
    {
        return package.ArchiveType switch
        {
            ArchiveTypeZip => await InspectZipAsync(
                    version,
                    package,
                    archivePath,
                    cancellationToken)
                .ConfigureAwait(false),
            ArchiveTypeTarGz => await InspectTarAsync(
                    version,
                    package,
                    archivePath,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidDataException(
                $"Unsupported release archive type '{package.ArchiveType}'."),
        };
    }

    private static async Task<InspectedArchive> InspectZipAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var input = OpenRead(archivePath);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        if (zip.Entries.Count > MaximumArchiveEntryCount)
        {
            throw new InvalidDataException(
                "The update archive contains too many entries.");
        }

        var entries = new Dictionary<string, ArchiveEntryInfo>(StringComparer.Ordinal);
        byte[]? manifestBytes = null;
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectZipLink(entry);
            var isDirectory = entry.FullName.EndsWith('/');
            var path = NormalizeArchivePath(entry.FullName, isDirectory);
            if (isDirectory)
            {
                continue;
            }

            if (!entries.TryAdd(
                    path,
                    new ArchiveEntryInfo(entry.Length)))
            {
                throw new InvalidDataException(
                    $"The update archive contains duplicate file '{path}'.");
            }

            if (path == ManifestName)
            {
                await using var stream = await entry.OpenAsync(
                    cancellationToken);
                manifestBytes = await ReadBoundedAsync(
                        stream,
                        MaximumManifestBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var manifest = ParseManifest(manifestBytes, version, package);
        ValidateEntrySet(entries, manifest);
        return new InspectedArchive(manifest, manifestBytes!);
    }

    private static async Task<InspectedArchive> InspectTarAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var input = OpenRead(archivePath);
        await using var gzip = new GZipStream(
            input,
            CompressionMode.Decompress,
            leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        var entries = new Dictionary<string, ArchiveEntryInfo>(StringComparer.Ordinal);
        byte[]? manifestBytes = null;
        var count = 0;
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken)
            .ConfigureAwait(false) is { } entry)
        {
            count++;
            if (count > MaximumArchiveEntryCount)
            {
                throw new InvalidDataException(
                    "The update archive contains too many entries.");
            }

            manifestBytes = await CollectTarEntryAsync(
                    entry,
                    entries,
                    manifestBytes,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var manifest = ParseManifest(manifestBytes, version, package);
        ValidateEntrySet(entries, manifest);
        return new InspectedArchive(manifest, manifestBytes!);
    }

    private static async Task<byte[]?> CollectTarEntryAsync(
        TarEntry entry,
        Dictionary<string, ArchiveEntryInfo> entries,
        byte[]? manifestBytes,
        CancellationToken cancellationToken)
    {
        var isDirectory = entry.EntryType == TarEntryType.Directory;
        if (isDirectory && entry.Name is "." or "./")
        {
            return manifestBytes;
        }

        if (!isDirectory
            && entry.EntryType is not (
                TarEntryType.RegularFile or TarEntryType.V7RegularFile))
        {
            throw new InvalidDataException(
                $"The update archive contains unsupported entry '{entry.Name}'.");
        }

        var path = NormalizeArchivePath(entry.Name, isDirectory);
        if (isDirectory)
        {
            return manifestBytes;
        }

        if (!entries.TryAdd(path, new ArchiveEntryInfo(entry.Length)))
        {
            throw new InvalidDataException(
                $"The update archive contains duplicate file '{path}'.");
        }

        if (path != ManifestName)
        {
            return manifestBytes;
        }

        var stream = entry.DataStream
            ?? throw new InvalidDataException(
                "The update package manifest has no data stream.");
        return await ReadBoundedAsync(
                stream,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ReleasePackageManifest ParseManifest(
        byte[]? bytes,
        ReleaseVersion version,
        CrossPlatformReleasePackage package)
    {
        if (bytes is null or { Length: 0 })
        {
            throw new InvalidDataException(
                "The update archive has no release-package.json manifest.");
        }

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var manifestVersion = ValidateManifestHeader(root, version, package);
            var runtimeIdentifier = ReadString(root, "runtimeIdentifier");
            var entryPoint = ValidateEntryPoint(root, runtimeIdentifier);
            var (files, expandedBytes) = ParseManifestFiles(root, runtimeIdentifier);
            if (files.Count == 0 || !files.ContainsKey(entryPoint))
            {
                throw new InvalidDataException(
                    "The update package manifest does not contain its entry point.");
            }

            return new ReleasePackageManifest(
                manifestVersion,
                runtimeIdentifier,
                entryPoint,
                files,
                expandedBytes);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The update package manifest is not valid JSON.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "The update package manifest size total overflowed.",
                exception);
        }
    }

    private static ReleaseVersion ValidateManifestHeader(
        JsonElement root,
        ReleaseVersion version,
        CrossPlatformReleasePackage package)
    {
        if (root.ValueKind != JsonValueKind.Object
            || ReadInt32(root, "schemaVersion") != 1
            || !string.Equals(
                ReadString(root, "product"),
                ProductName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update package manifest has an incompatible schema or product.");
        }

        var versionText = ReadString(root, "version");
        if (!ReleaseVersion.TryParse(versionText, out var manifestVersion)
            || manifestVersion != version)
        {
            throw new InvalidDataException(
                "The update package manifest version does not match the release.");
        }

        var runtimeIdentifier = ReadString(root, "runtimeIdentifier");
        if (!string.Equals(
                runtimeIdentifier,
                package.RuntimeIdentifier,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The update package manifest runtime does not match the release.");
        }

        return manifestVersion;
    }

    private static string ValidateEntryPoint(
        JsonElement root,
        string runtimeIdentifier)
    {
        var expectedEntryPoint = runtimeIdentifier == RuntimeWinX64
            ? "SrvSurvey.Desktop.exe"
            : "SrvSurvey.Desktop";
        var entryPoint = ReadString(root, "entryPoint");
        if (!string.Equals(entryPoint, expectedEntryPoint, StringComparison.Ordinal)
            || NormalizeArchivePath(entryPoint, isDirectory: false) != entryPoint)
        {
            throw new InvalidDataException(
                "The update package manifest has an invalid entry point.");
        }

        return entryPoint;
    }

    private static (Dictionary<string, ReleasePackageManifestFile> Files, long ExpandedBytes)
        ParseManifestFiles(JsonElement root, string runtimeIdentifier)
    {
        if (!root.TryGetProperty("files", out var filesElement)
            || filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The update package manifest has no file array.");
        }

        var files = new Dictionary<string, ReleasePackageManifestFile>(
            StringComparer.Ordinal);
        var caseInsensitivePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        long expandedBytes = 0;
        foreach (var fileElement in filesElement.EnumerateArray())
        {
            expandedBytes = AddManifestFile(
                fileElement,
                files,
                caseInsensitivePaths,
                runtimeIdentifier,
                expandedBytes);
        }

        return (files, expandedBytes);
    }

    private static long AddManifestFile(
        JsonElement fileElement,
        Dictionary<string, ReleasePackageManifestFile> files,
        HashSet<string> caseInsensitivePaths,
        string runtimeIdentifier,
        long expandedBytes)
    {
        if (files.Count >= MaximumFileCount)
        {
            throw new InvalidDataException(
                "The update package manifest contains too many files.");
        }

        var path = ReadString(fileElement, "path");
        if (NormalizeArchivePath(path, isDirectory: false) != path
            || path == ManifestName)
        {
            throw new InvalidDataException(
                $"The update package manifest has invalid path '{path}'.");
        }

        var size = ReadInt64(fileElement, "size");
        if (size is < 0 or > MaximumSingleFileBytes)
        {
            throw new InvalidDataException(
                $"The update package file '{path}' has an invalid size.");
        }

        expandedBytes = checked(expandedBytes + size);
        if (expandedBytes > MaximumExpandedBytes)
        {
            throw new InvalidDataException(
                "The update package exceeds the supported expanded size.");
        }

        var sha256 = ReadString(fileElement, "sha256").ToLowerInvariant();
        if (sha256.Length != 64
            || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                $"The update package file '{path}' has an invalid SHA-256.");
        }

        if (!files.TryAdd(
                path,
                new ReleasePackageManifestFile(path, size, sha256))
            || (runtimeIdentifier == RuntimeWinX64
                && !caseInsensitivePaths.Add(path)))
        {
            throw new InvalidDataException(
                $"The update package manifest contains duplicate path '{path}'.");
        }

        return expandedBytes;
    }

    private static void ValidateEntrySet(
        IReadOnlyDictionary<string, ArchiveEntryInfo> entries,
        ReleasePackageManifest manifest)
    {
        if (entries.Count != manifest.Files.Count + 1
            || !entries.ContainsKey(ManifestName))
        {
            throw new InvalidDataException(
                "The update archive file set does not match its manifest.");
        }

        foreach (var file in manifest.Files.Values)
        {
            if (!entries.TryGetValue(file.Path, out var entry)
                || entry.Size != file.Size)
            {
                throw new InvalidDataException(
                    $"The update archive entry '{file.Path}' does not match its manifest size.");
            }
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string archiveType,
        string candidateDirectory,
        InspectedArchive archive,
        CancellationToken cancellationToken)
    {
        var manifestPath = ResolveDestination(candidateDirectory, ManifestName);
        await File.WriteAllBytesAsync(
                manifestPath,
                archive.ManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (archiveType == ArchiveTypeZip)
        {
            await ExtractZipAsync(
                    archivePath,
                    candidateDirectory,
                    archive.Manifest,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await ExtractTarAsync(
                    archivePath,
                    candidateDirectory,
                    archive.Manifest,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string candidateDirectory,
        ReleasePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var input = OpenRead(archivePath);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entries = zip.Entries
            .Where(entry => !entry.FullName.EndsWith('/'))
            .ToDictionary(
                entry => NormalizeArchivePath(entry.FullName, isDirectory: false),
                StringComparer.Ordinal);
        foreach (var file in manifest.Files.Values)
        {
            var entry = entries[file.Path];
            await using var source = await entry.OpenAsync(cancellationToken);
            await ExtractFileAsync(
                    source,
                    candidateDirectory,
                    file,
                    mode: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ExtractTarAsync(
        string archivePath,
        string candidateDirectory,
        ReleasePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var input = OpenRead(archivePath);
        await using var gzip = new GZipStream(
            input,
            CompressionMode.Decompress,
            leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        var extracted = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken)
            .ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not (
                    TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            var path = NormalizeArchivePath(entry.Name, isDirectory: false);
            if (path == ManifestName)
            {
                continue;
            }

            if (!manifest.Files.TryGetValue(path, out var file)
                || !extracted.Add(path))
            {
                throw new InvalidDataException(
                    $"The update archive contains unexpected file '{path}'.");
            }

            var source = entry.DataStream
                ?? throw new InvalidDataException(
                    $"The update archive entry '{path}' has no data stream.");
            await ExtractFileAsync(
                    source,
                    candidateDirectory,
                    file,
                    entry.Mode,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (extracted.Count != manifest.Files.Count)
        {
            throw new InvalidDataException(
                "The update archive did not extract every manifest file.");
        }
    }

    private static async Task ExtractFileAsync(
        Stream source,
        string candidateDirectory,
        ReleasePackageManifestFile file,
        UnixFileMode? mode,
        CancellationToken cancellationToken)
    {
        var destination = ResolveDestination(candidateDirectory, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > file.Size)
            {
                throw new InvalidDataException(
                    $"The update archive entry '{file.Path}' exceeded its manifest size.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        if (total != file.Size)
        {
            throw new InvalidDataException(
                $"The update archive entry '{file.Path}' ended before its manifest size.");
        }

        var actualHash = Convert.ToHexString(hash.GetHashAndReset());
        if (!HashesMatch(actualHash, file.Sha256))
        {
            throw new InvalidDataException(
                $"The update archive entry '{file.Path}' failed SHA-256 verification.");
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Close();
        if (!OperatingSystem.IsWindows() && mode.HasValue)
        {
            File.SetUnixFileMode(destination, SanitizeMode(mode.Value));
        }
    }

    private static async Task<bool> IsReadyAsync(
        string directory,
        ReleasePackageManifest expectedManifest,
        byte[] expectedManifestBytes,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            var manifest = await LoadReadyManifestAsync(
                    directory,
                    expectedManifest,
                    expectedManifestBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (manifest is null)
            {
                return false;
            }

            return await VerifyReadyContentAsync(
                    directory,
                    expectedManifest,
                    manifest,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or JsonException)
        {
            return false;
        }
    }

    private static async Task<ReleasePackageManifest?> LoadReadyManifestAsync(
        string directory,
        ReleasePackageManifest expectedManifest,
        byte[] expectedManifestBytes,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(directory, ManifestName);
        var manifestInfo = new FileInfo(manifestPath);
        if (!manifestInfo.Exists
            || manifestInfo.Length is <= 0 or > MaximumManifestBytes)
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        if (!bytes.AsSpan().SequenceEqual(expectedManifestBytes))
        {
            return null;
        }

        var package = new CrossPlatformReleasePackage(
            expectedManifest.RuntimeIdentifier,
            expectedManifest.RuntimeIdentifier == RuntimeWinX64 ? "unused.zip" : "unused.tar.gz",
            expectedManifest.RuntimeIdentifier == RuntimeWinX64 ? ArchiveTypeZip : ArchiveTypeTarGz,
            1,
            new string('0', 64),
            WellKnownUris.ExampleInvalidPackage);
        var manifest = ParseManifest(bytes, expectedManifest.Version, package);
        if (manifest.Files.Count != expectedManifest.Files.Count
            || manifest.ExpandedBytes != expectedManifest.ExpandedBytes)
        {
            return null;
        }

        return manifest;
    }

    private static async Task<bool> VerifyReadyContentAsync(
        string directory,
        ReleasePackageManifest expectedManifest,
        ReleasePackageManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!TryEnumerateReadyFiles(directory, out var actualFiles))
        {
            return false;
        }

        var expectedFiles = expectedManifest.Files.Keys
            .Append(ManifestName)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualFiles.SetEquals(expectedFiles))
        {
            return false;
        }

        foreach (var expected in expectedManifest.Files.Values)
        {
            if (!manifest.Files.TryGetValue(expected.Path, out var staged)
                || staged != expected)
            {
                return false;
            }

            if (!await FileMatchesExpectedAsync(
                    directory,
                    expected,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return false;
            }
        }

        var entryPoint = ResolveDestination(directory, expectedManifest.EntryPoint);
        return OperatingSystem.IsWindows()
            || expectedManifest.RuntimeIdentifier != RuntimeLinuxX64
            || (File.GetUnixFileMode(entryPoint) & UnixFileMode.UserExecute) != 0;
    }

    private static async Task<bool> FileMatchesExpectedAsync(
        string directory,
        ReleasePackageManifestFile expected,
        CancellationToken cancellationToken)
    {
        var path = ResolveDestination(directory, expected.Path);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size)
        {
            return false;
        }

        await using var stream = OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return HashesMatch(Convert.ToHexString(hash), expected.Sha256);
    }

    private static void ActivateCandidate(
        string stageRoot,
        string candidateDirectory,
        string readyDirectory)
    {
        if (!Directory.Exists(readyDirectory))
        {
            Directory.Move(candidateDirectory, readyDirectory);
            return;
        }

        var priorDirectory = Path.Combine(
            stageRoot,
            $"prior-{Guid.NewGuid():N}");
        Directory.Move(readyDirectory, priorDirectory);
        try
        {
            Directory.Move(candidateDirectory, readyDirectory);
        }
        catch
        {
            if (!Directory.Exists(readyDirectory)
                && Directory.Exists(priorDirectory))
            {
                Directory.Move(priorDirectory, readyDirectory);
            }

            throw;
        }

        TryDeleteDirectory(priorDirectory);
    }

    private static ReleasePackageStagingResult CreateResult(
        string readyDirectory,
        ReleasePackageManifest manifest,
        byte[] manifestBytes,
        bool reused)
    {
        return new ReleasePackageStagingResult(
            readyDirectory,
            ResolveDestination(readyDirectory, manifest.EntryPoint),
            reused,
            manifest.Files.Count,
            manifest.ExpandedBytes,
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
    }

    private static async Task VerifyArchiveAsync(
        CrossPlatformReleasePackage package,
        string archivePath,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length != package.Size)
        {
            throw new InvalidDataException(
                "The cached update archive size no longer matches the release index.");
        }

        await using var stream = OpenRead(archivePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        if (!HashesMatch(Convert.ToHexString(hash), package.Sha256))
        {
            throw new InvalidDataException(
                "The cached update archive SHA-256 no longer matches the release index.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    "The update package manifest exceeded the supported size.");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static string NormalizeArchivePath(string value, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.StartsWith('/'))
        {
            throw new InvalidDataException(
                $"The update archive contains invalid path '{value}'.");
        }

        while (value.StartsWith("./", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (isDirectory)
        {
            value = value.TrimEnd('/');
        }

        var segments = value.Split('/');
        if (segments.Length == 0
            || segments.Any(segment => string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.AsSpan().IndexOfAny(InvalidPortableNameSearch) >= 0
                || IsReservedPortableSegment(segment)))
        {
            throw new InvalidDataException(
                $"The update archive contains invalid path '{value}'.");
        }

        return string.Join('/', segments);
    }

    private static string ResolveDestination(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var destination = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!destination.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                $"The update file '{relativePath}' escaped the staging directory.");
        }

        return destination;
    }

    private static string ResolveStageRoot(
        string dataDirectory,
        ReleaseVersion version,
        string runtimeIdentifier)
    {
        var dataRoot = Path.GetFullPath(dataDirectory);
        var stageRoot = Path.GetFullPath(Path.Combine(
            dataRoot,
            "updates",
            "staged",
            version.ToString(),
            runtimeIdentifier));
        var prefix = Path.TrimEndingDirectorySeparator(dataRoot)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!stageRoot.StartsWith(prefix, comparison))
        {
            throw new InvalidDataException(
                "The update staging path escaped the application data directory.");
        }

        return stageRoot;
    }

    private static void RejectZipLink(ZipArchiveEntry entry)
    {
        const int UnixFileTypeMask = 0xF000;
        const int UnixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (unixMode == UnixSymbolicLink
            || (windowsAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"The update archive contains link '{entry.FullName}'.");
        }
    }

    private static bool TryEnumerateReadyFiles(
        string root,
        out HashSet<string> files)
    {
        files = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<DirectoryInfo>();
        var rootDirectory = new DirectoryInfo(root);
        if ((rootDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        pending.Push(rootDirectory);
        while (pending.TryPop(out var directory))
        {
            foreach (var child in directory.EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                pending.Push(child);
            }

            foreach (var file in directory.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                files.Add(Path.GetRelativePath(root, file.FullName)
                    .Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        return true;
    }

    private static bool IsReservedPortableSegment(string segment)
    {
        if (segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            return true;
        }

        var stem = segment.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
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

    private static bool HashesMatch(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static UnixFileMode SanitizeMode(UnixFileMode mode)
    {
        const UnixFileMode Allowed = UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        return mode & Allowed;
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"The update package manifest has invalid '{propertyName}'.");
        }

        return value;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt64(out var value))
        {
            throw new InvalidDataException(
                $"The update package manifest has invalid '{propertyName}'.");
        }

        return value;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"The update package manifest has invalid '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static void ValidateArguments(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string archivePath,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var archiveType = package.RuntimeIdentifier switch
        {
            RuntimeWinX64 => ArchiveTypeZip,
            RuntimeLinuxX64 => ArchiveTypeTarGz,
            _ => string.Empty,
        };
        var suffix = archiveType == ArchiveTypeZip ? ".zip" : ".tar.gz";
        var expectedArchiveName =
            $"SrvSurvey-XP-{version}-{package.RuntimeIdentifier}{suffix}";
        if (version.Build < 0
            || string.IsNullOrEmpty(archiveType)
            || !string.Equals(package.ArchiveType, archiveType, StringComparison.Ordinal)
            || !string.Equals(
                package.ArchiveName,
                expectedArchiveName,
                StringComparison.Ordinal)
            || package.Size <= 0
            || package.Sha256.Length != 64
            || package.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "The update staging request has incompatible metadata.");
        }
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
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; the next staging run retries removal.
        }
    }

    private sealed record InspectedArchive(
        ReleasePackageManifest Manifest,
        byte[] ManifestBytes);

    private sealed record ArchiveEntryInfo(long Size);

    private sealed record ReleasePackageManifest(
        ReleaseVersion Version,
        string RuntimeIdentifier,
        string EntryPoint,
        IReadOnlyDictionary<string, ReleasePackageManifestFile> Files,
        long ExpandedBytes);

    private sealed record ReleasePackageManifestFile(
        string Path,
        long Size,
        string Sha256);
}
