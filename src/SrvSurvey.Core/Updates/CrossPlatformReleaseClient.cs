using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SrvSurvey.Core.Updates;

public interface ICrossPlatformReleaseClient
{
    Task<CrossPlatformRelease?> GetLatestAsync(
        string runtimeIdentifier,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default
    );
}

public sealed record CrossPlatformRelease(
    ReleaseVersion Version,
    Uri ReleaseUri,
    CrossPlatformReleasePackage Package,
    string ReleaseNotes = ""
);

public sealed record CrossPlatformReleasePackage(
    string RuntimeIdentifier,
    string ArchiveName,
    string ArchiveType,
    long Size,
    string Sha256,
    Uri DownloadUri
);

public sealed class CrossPlatformReleaseClient : ICrossPlatformReleaseClient
{
    private const int ReleasesPerPage = 100;
    private const int MaximumReleasePages = 5;
    private const int MaximumReleaseCount = ReleasesPerPage * MaximumReleasePages;
    private const int MaximumAssetCount = 64;
    private const int MaximumReleaseApiBytes = 2 * 1024 * 1024;
    private const int MaximumReleaseIndexBytes = 64 * 1024;
    private const long MaximumPackageBytes = 512L * 1024 * 1024;
    private const string ProductName = "SrvSurvey.XP";
    private const string ProductTagPrefix = "xp-v";
    private const string PackageNamePrefix = "SrvSurvey-XP";
    private const string ReleaseIndexName = "release-index.json";
    private const string WinX64RuntimeIdentifier = "win-x64";
    private const string LinuxX64RuntimeIdentifier = "linux-x64";
    public const string LinuxX64AppImageRuntimeIdentifier = "linux-x64-appimage";
    private static readonly Uri DefaultDevelopmentReleasesApiUri = new(
        "https://api.github.com/repos/Fenris159/SrvSurvey/releases?per_page=100"
    );
    private static readonly Uri DefaultStableReleasesApiUri = new(
        "https://api.github.com/repos/njthomson/SrvSurvey/releases?per_page=100"
    );
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client;
    private readonly Uri developmentReleasesApiUri;
    private readonly Uri stableReleasesApiUri;

    public CrossPlatformReleaseClient(
        HttpClient? client = null,
        Uri? developmentReleasesApiUri = null,
        Uri? stableReleasesApiUri = null
    )
    {
        this.client = client ?? SharedClient;
        this.developmentReleasesApiUri = developmentReleasesApiUri ?? DefaultDevelopmentReleasesApiUri;
        this.stableReleasesApiUri = stableReleasesApiUri ?? DefaultStableReleasesApiUri;
    }

    public async Task<CrossPlatformRelease?> GetLatestAsync(
        string runtimeIdentifier,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default
    )
    {
        ValidateRuntimeIdentifier(runtimeIdentifier);
        Uri releasesApiUri = channel == ReleaseChannel.Development ? developmentReleasesApiUri : stableReleasesApiUri;
        ReleaseCandidate? candidate = null;
        int releaseCount = 0;
        for (int page = 1; page <= MaximumReleasePages; page++)
        {
            Uri pageUri = ResolvePageUri(releasesApiUri, page);
            using HttpRequestMessage request = CreateGitHubRequest(pageUri);
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            byte[] bytes = await ReadBoundedAsync(response.Content, MaximumReleaseApiBytes, pageUri, cancellationToken)
                .ConfigureAwait(false);
            ReleasePage parsed = ParseReleasePage(bytes, channel);
            releaseCount += parsed.ReleaseCount;
            if (releaseCount > MaximumReleaseCount)
            {
                throw new InvalidDataException("The GitHub release feed contains too many releases.");
            }

            if (parsed.Latest is not null && (candidate is null || parsed.Latest.Version > candidate.Version))
            {
                candidate = parsed.Latest;
            }

            if (parsed.ReleaseCount < ReleasesPerPage)
            {
                break;
            }
        }

        if (candidate is null)
        {
            return null;
        }

        ReleaseAsset indexAsset = candidate.Assets.Single(asset =>
            string.Equals(asset.Name, ReleaseIndexName, StringComparison.Ordinal)
        );
        using var indexRequest = new HttpRequestMessage(HttpMethod.Get, indexAsset.DownloadUri);
        indexRequest.Headers.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        using HttpResponseMessage indexResponse = await client
            .SendAsync(indexRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        indexResponse.EnsureSuccessStatusCode();
        byte[] indexBytes = await ReadBoundedAsync(
                indexResponse.Content,
                MaximumReleaseIndexBytes,
                indexAsset.DownloadUri,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (indexBytes.LongLength != indexAsset.Size)
        {
            throw new InvalidDataException("The release index size does not match its GitHub asset metadata.");
        }

        CrossPlatformReleasePackage package = ParseReleaseIndex(
            indexBytes,
            candidate.Version,
            runtimeIdentifier,
            candidate.Assets
        );
        return new CrossPlatformRelease(candidate.Version, candidate.ReleaseUri, package, candidate.ReleaseNotes);
    }

    public static string ResolveCurrentRuntimeIdentifier()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException("Automatic updates currently require an x64 SrvSurvey package.");
        }

        if (OperatingSystem.IsWindows())
        {
            return WinX64RuntimeIdentifier;
        }

        if (OperatingSystem.IsLinux())
        {
            return string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPIMAGE"))
                ? LinuxX64RuntimeIdentifier
                : LinuxX64AppImageRuntimeIdentifier;
        }

        throw new PlatformNotSupportedException("Automatic updates are available only on Windows and Linux.");
    }

    private static ReleasePage ParseReleasePage(byte[] bytes, ReleaseChannel channel)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The GitHub releases response is not an array.");
            }

            ReleaseCandidate? latest = null;
            int count = 0;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                count++;
                if (count > ReleasesPerPage)
                {
                    throw new InvalidDataException("A GitHub release page contains too many releases.");
                }

                ReleaseCandidate? candidate = ParseReleaseCandidate(element, channel);
                if (candidate is not null && (latest is null || candidate.Version > latest.Version))
                {
                    latest = candidate;
                }
            }

            return new ReleasePage(latest, count);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The GitHub releases response is not valid JSON.", exception);
        }
    }

    private static ReleaseCandidate? ParseReleaseCandidate(JsonElement element, ReleaseChannel channel)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        bool isDraft = ReadBoolean(element, "draft");
        bool isPrerelease = ReadBoolean(element, "prerelease");
        string tag = ReadRequiredString(element, "tag_name");
        if (
            isDraft
            || (channel == ReleaseChannel.Stable && isPrerelease)
            || !tag.StartsWith(ProductTagPrefix, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        string versionText = tag[ProductTagPrefix.Length..];
        if (!ReleaseVersion.TryParse(versionText, out ReleaseVersion version) || version.IsPrerelease != isPrerelease)
        {
            return null;
        }

        Uri releaseUri = ReadRequiredHttpsUri(element, "html_url");
        string releaseNotes = ReadOptionalString(element, "body");
        if (
            !element.TryGetProperty("assets", out JsonElement assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array
        )
        {
            throw new InvalidDataException($"Release {version} has no GitHub asset array.");
        }

        var assets = new List<ReleaseAsset>();
        foreach (JsonElement assetElement in assetsElement.EnumerateArray())
        {
            if (assets.Count >= MaximumAssetCount)
            {
                throw new InvalidDataException($"Release {version} has too many GitHub assets.");
            }

            assets.Add(
                new ReleaseAsset(
                    ReadRequiredString(assetElement, "name"),
                    ReadPositiveInt64(assetElement, "size"),
                    ReadRequiredHttpsUri(assetElement, "browser_download_url")
                )
            );
        }

        int indexCount = assets.Count(asset => string.Equals(asset.Name, ReleaseIndexName, StringComparison.Ordinal));
        if (indexCount == 0)
        {
            return null;
        }

        if (indexCount != 1)
        {
            throw new InvalidDataException($"Release {version} has duplicate release index assets.");
        }

        return new ReleaseCandidate(version, releaseUri, assets, GitHubReleaseNotes.ExtractChanges(releaseNotes));
    }

    private static CrossPlatformReleasePackage ParseReleaseIndex(
        byte[] bytes,
        ReleaseVersion expectedVersion,
        string runtimeIdentifier,
        IReadOnlyList<ReleaseAsset> assets
    )
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The cross-platform release index is not an object.");
            }

            int schemaVersion = ReadRequiredInt32(root, "schemaVersion");
            if (
                schemaVersion is not (1 or 2)
                || !string.Equals(ReadRequiredString(root, "product"), ProductName, StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException(
                    "The cross-platform release index has an incompatible schema or product."
                );
            }

            string versionText = ReadRequiredString(root, "version");
            if (
                !ReleaseVersion.TryParse(versionText, out ReleaseVersion indexVersion)
                || indexVersion != expectedVersion
            )
            {
                throw new InvalidDataException("The release index version does not match the GitHub tag.");
            }

            if (
                !root.TryGetProperty("packages", out JsonElement packagesElement)
                || packagesElement.ValueKind != JsonValueKind.Array
            )
            {
                throw new InvalidDataException("The release index has no package array.");
            }

            JsonElement[] packages = packagesElement.EnumerateArray().ToArray();
            int expectedPackageCount = schemaVersion == 1 ? 2 : 3;
            if (packages.Length != expectedPackageCount)
            {
                throw new InvalidDataException(
                    $"Release-index schema {schemaVersion} must contain exactly {expectedPackageCount} packages."
                );
            }

            CrossPlatformReleasePackage windows = ParseIndexedPackage(
                packages,
                expectedVersion,
                WinX64RuntimeIdentifier,
                "zip",
                assets
            );
            CrossPlatformReleasePackage linux = ParseIndexedPackage(
                packages,
                expectedVersion,
                LinuxX64RuntimeIdentifier,
                "tar.gz",
                assets
            );
            CrossPlatformReleasePackage? appImage =
                schemaVersion == 2
                    ? ParseIndexedPackage(
                        packages,
                        expectedVersion,
                        LinuxX64AppImageRuntimeIdentifier,
                        "appimage",
                        assets
                    )
                    : null;
            return runtimeIdentifier switch
            {
                WinX64RuntimeIdentifier => windows,
                LinuxX64RuntimeIdentifier => linux,
                LinuxX64AppImageRuntimeIdentifier => appImage
                    ?? throw new InvalidDataException("The release does not contain an indexed AppImage update."),
                _ => throw new PlatformNotSupportedException(
                    $"The runtime '{runtimeIdentifier}' has no SrvSurvey update package."
                ),
            };
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The cross-platform release index is not valid JSON.", exception);
        }
    }

    private static CrossPlatformReleasePackage ParseIndexedPackage(
        IReadOnlyList<JsonElement> packages,
        ReleaseVersion version,
        string runtimeIdentifier,
        string archiveType,
        IReadOnlyList<ReleaseAsset> assets
    )
    {
        JsonElement[] matching = packages
            .Where(package =>
                string.Equals(
                    ReadRequiredString(package, "runtimeIdentifier"),
                    runtimeIdentifier,
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        string expectedName = archiveType switch
        {
            "zip" => $"{PackageNamePrefix}-{version}-{runtimeIdentifier}.zip",
            "tar.gz" => $"{PackageNamePrefix}-{version}-{runtimeIdentifier}.tar.gz",
            "appimage" => $"{PackageNamePrefix}-{version}-x86_64.AppImage",
            _ => throw new InvalidDataException($"The release index has an unsupported package type '{archiveType}'."),
        };
        if (
            matching.Length != 1
            || !string.Equals(ReadRequiredString(matching[0], "archive"), expectedName, StringComparison.Ordinal)
            || !string.Equals(ReadRequiredString(matching[0], "archiveType"), archiveType, StringComparison.Ordinal)
        )
        {
            throw new InvalidDataException($"The release index has an invalid {runtimeIdentifier} package contract.");
        }

        JsonElement selected = matching[0];
        string archiveName = ReadRequiredString(selected, "archive");
        long size = ReadPositiveInt64(selected, "size");
        if (size > MaximumPackageBytes)
        {
            throw new InvalidDataException($"The {runtimeIdentifier} package exceeds the supported size.");
        }

        string sha256 = ReadRequiredString(selected, "sha256").ToLowerInvariant();
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"The {runtimeIdentifier} package has an invalid SHA-256 value.");
        }

        ReleaseAsset[] matchingAssets = assets
            .Where(asset => string.Equals(asset.Name, archiveName, StringComparison.Ordinal))
            .ToArray();
        if (matchingAssets.Length != 1 || matchingAssets[0].Size != size)
        {
            throw new InvalidDataException(
                $"The {runtimeIdentifier} package does not match its GitHub asset metadata."
            );
        }

        return new CrossPlatformReleasePackage(
            runtimeIdentifier,
            archiveName,
            archiveType,
            size,
            sha256,
            matchingAssets[0].DownloadUri
        );
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        Uri uri,
        CancellationToken cancellationToken
    )
    {
        long? contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException($"The update response exceeded {maximumBytes:N0} bytes: {uri}");
        }

        await using Stream input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"The update response exceeded {maximumBytes:N0} bytes: {uri}");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static HttpRequestMessage CreateGitHubRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static Uri ResolvePageUri(Uri baseUri, int page)
    {
        if (page == 1)
        {
            return baseUri;
        }

        string separator = string.IsNullOrEmpty(baseUri.Query) ? "?" : "&";
        return new Uri($"{baseUri.AbsoluteUri}{separator}page={page}");
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
        )
        {
            throw new InvalidDataException($"The GitHub release has an invalid '{propertyName}' value.");
        }

        return property.GetBoolean();
    }

    private static int ReadRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || !property.TryGetInt32(out int value))
        {
            throw new InvalidDataException($"The update metadata has an invalid '{propertyName}' value.");
        }

        return value;
    }

    private static long ReadPositiveInt64(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || !property.TryGetInt64(out long value)
            || value <= 0
        )
        {
            throw new InvalidDataException($"The update metadata has an invalid '{propertyName}' value.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString())
        )
        {
            throw new InvalidDataException($"The update metadata has an invalid '{propertyName}' value.");
        }

        return property.GetString()!;
    }

    private static string ReadOptionalString(JsonElement element, string propertyName)
    {
        if (
            !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        )
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : string.Empty;
    }

    private static Uri ReadRequiredHttpsUri(JsonElement element, string propertyName)
    {
        string value = ReadRequiredString(element, propertyName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"The update metadata has an invalid '{propertyName}' URI.");
        }

        return uri;
    }

    private static void ValidateRuntimeIdentifier(string runtimeIdentifier)
    {
        if (
            runtimeIdentifier
            is not (WinX64RuntimeIdentifier or LinuxX64RuntimeIdentifier or LinuxX64AppImageRuntimeIdentifier)
        )
        {
            throw new PlatformNotSupportedException(
                $"The runtime '{runtimeIdentifier}' has no SrvSurvey update package."
            );
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        return client;
    }

    private sealed record ReleasePage(ReleaseCandidate? Latest, int ReleaseCount);

    private sealed record ReleaseCandidate(
        ReleaseVersion Version,
        Uri ReleaseUri,
        IReadOnlyList<ReleaseAsset> Assets,
        string ReleaseNotes
    );

    private sealed record ReleaseAsset(string Name, long Size, Uri DownloadUri);
}
