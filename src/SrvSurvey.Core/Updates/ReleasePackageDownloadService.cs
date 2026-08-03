using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SrvSurvey.Core.Updates;

public interface IReleasePackageDownloadService
{
    Task<ReleasePackageDownloadResult> DownloadAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string dataDirectory,
        IProgress<ReleasePackageDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ReleasePackageDownloadProgress(
    long DownloadedBytes,
    long TotalBytes);

public sealed record ReleasePackageDownloadResult(
    string ArchivePath,
    bool Downloaded,
    long Size,
    string Sha256);

public sealed class ReleasePackageDownloadService
    : IReleasePackageDownloadService
{
    private const long MaximumPackageBytes = 512L * 1024 * 1024;
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client;

    public ReleasePackageDownloadService(HttpClient? client = null)
    {
        this.client = client ?? SharedClient;
    }

    public async Task<ReleasePackageDownloadResult> DownloadAsync(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string dataDirectory,
        IProgress<ReleasePackageDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(version, package, dataDirectory);
        var packageDirectory = ResolvePackageDirectory(
            dataDirectory,
            version,
            package.RuntimeIdentifier);
        Directory.CreateDirectory(packageDirectory);
        var archivePath = Path.Combine(packageDirectory, package.ArchiveName);
        if (await MatchesAsync(
                archivePath,
                package.Size,
                package.Sha256,
                cancellationToken)
            .ConfigureAwait(false))
        {
            progress?.Report(new ReleasePackageDownloadProgress(
                package.Size,
                package.Size));
            return new ReleasePackageDownloadResult(
                archivePath,
                false,
                package.Size,
                package.Sha256);
        }

        var partialPath = Path.Combine(
            packageDirectory,
            $".{package.ArchiveName}.{Guid.NewGuid():N}.partial");
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                package.DownloadUri);
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
            };
            request.Headers.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value != package.Size)
            {
                throw new InvalidDataException(
                    "The package response size does not match the release index.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                partialPath,
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
                var read = await input.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > package.Size || total > MaximumPackageBytes)
                {
                    throw new InvalidDataException(
                        "The package response exceeded the indexed size.");
                }

                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
                progress?.Report(new ReleasePackageDownloadProgress(
                    total,
                    package.Size));
            }

            if (total != package.Size)
            {
                throw new InvalidDataException(
                    "The package response ended before the indexed size was reached.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant();
            if (!HashesMatch(actualHash, package.Sha256))
            {
                throw new InvalidDataException(
                    "The package SHA-256 does not match the release index.");
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            output.Close();
            File.Move(partialPath, archivePath, overwrite: true);
            return new ReleasePackageDownloadResult(
                archivePath,
                true,
                package.Size,
                actualHash);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private static async Task<bool> MatchesAsync(
        string path,
        long expectedSize,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize)
        {
            return false;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return HashesMatch(Convert.ToHexString(hash), expectedHash);
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

    private static string ResolvePackageDirectory(
        string dataDirectory,
        ReleaseVersion version,
        string runtimeIdentifier)
    {
        var dataRoot = Path.GetFullPath(dataDirectory);
        var packageDirectory = Path.GetFullPath(Path.Combine(
            dataRoot,
            "updates",
            "packages",
            version.ToString(),
            runtimeIdentifier));
        var rootPrefix = Path.TrimEndingDirectorySeparator(dataRoot)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!packageDirectory.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException(
                "The update package path escaped the application data directory.");
        }

        return packageDirectory;
    }

    private static void Validate(
        ReleaseVersion version,
        CrossPlatformReleasePackage package,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (version.Build < 0 || version.Major < 0 || version.Minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        var expectedArchiveType = package.RuntimeIdentifier switch
        {
            "win-x64" => "zip",
            "linux-x64" => "tar.gz",
            _ => throw new PlatformNotSupportedException(
                $"The runtime '{package.RuntimeIdentifier}' has no update package."),
        };
        var suffix = expectedArchiveType == "zip" ? ".zip" : ".tar.gz";
        var expectedName = $"SrvSurvey-XP-{version}-{package.RuntimeIdentifier}{suffix}";
        if (!string.Equals(package.ArchiveType, expectedArchiveType, StringComparison.Ordinal)
            || !string.Equals(package.ArchiveName, expectedName, StringComparison.Ordinal)
            || !string.Equals(
                Path.GetFileName(package.ArchiveName),
                package.ArchiveName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The release package name or archive type is invalid.");
        }

        if (package.Size is <= 0 or > MaximumPackageBytes)
        {
            throw new InvalidDataException(
                "The release package size is outside the supported range.");
        }

        if (package.Sha256.Length != 64
            || package.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                "The release package SHA-256 is invalid.");
        }

        if (!package.DownloadUri.IsAbsoluteUri
            || package.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                "The release package download URI must use HTTPS.");
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        return client;
    }
}
