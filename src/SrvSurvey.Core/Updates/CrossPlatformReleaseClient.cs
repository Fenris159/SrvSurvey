using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SrvSurvey.Core.Updates;

public interface ICrossPlatformReleaseClient
{
    Task<CrossPlatformRelease?> GetLatestAsync(
        string runtimeIdentifier,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default);
}

public sealed record CrossPlatformRelease(
    ReleaseVersion Version,
    Uri ReleaseUri,
    CrossPlatformReleasePackage Package,
    string ReleaseNotes = "");

public sealed record CrossPlatformReleasePackage(
    string RuntimeIdentifier,
    string ArchiveName,
    string ArchiveType,
    long Size,
    string Sha256,
    Uri DownloadUri);

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
    private static readonly Uri DefaultDevelopmentReleasesApiUri = new(
        "https://api.github.com/repos/Fenris159/SrvSurvey/releases?per_page=100");
    private static readonly Uri DefaultStableReleasesApiUri = new(
        "https://api.github.com/repos/njthomson/SrvSurvey/releases?per_page=100");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient client;
    private readonly Uri developmentReleasesApiUri;
    private readonly Uri stableReleasesApiUri;

    public CrossPlatformReleaseClient(
        HttpClient? client = null,
        Uri? developmentReleasesApiUri = null,
        Uri? stableReleasesApiUri = null)
    {
        this.client = client ?? SharedClient;
        this.developmentReleasesApiUri = developmentReleasesApiUri
            ?? DefaultDevelopmentReleasesApiUri;
        this.stableReleasesApiUri = stableReleasesApiUri
            ?? DefaultStableReleasesApiUri;
    }

    public async Task<CrossPlatformRelease?> GetLatestAsync(
        string runtimeIdentifier,
        ReleaseChannel channel,
        CancellationToken cancellationToken = default)
    {
        ValidateRuntimeIdentifier(runtimeIdentifier);
        var releasesApiUri = channel == ReleaseChannel.Development
            ? developmentReleasesApiUri
            : stableReleasesApiUri;
        ReleaseCandidate? candidate = null;
        var releaseCount = 0;
        for (var page = 1; page <= MaximumReleasePages; page++)
        {
            var pageUri = ResolvePageUri(releasesApiUri, page);
            using var request = CreateGitHubRequest(pageUri);
            using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await ReadBoundedAsync(
                    response.Content,
                    MaximumReleaseApiBytes,
                    pageUri,
                    cancellationToken)
                .ConfigureAwait(false);
            var parsed = ParseReleasePage(bytes, channel);
            releaseCount += parsed.ReleaseCount;
            if (releaseCount > MaximumReleaseCount)
            {
                throw new InvalidDataException(
                    "The GitHub release feed contains too many releases.");
            }

            if (parsed.Latest is not null
                && (candidate is null
                    || parsed.Latest.Version > candidate.Version))
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

        var indexAsset = candidate.Assets.Single(asset =>
            string.Equals(
                asset.Name,
                ReleaseIndexName,
                StringComparison.Ordinal));
        using var indexRequest = new HttpRequestMessage(
            HttpMethod.Get,
            indexAsset.DownloadUri);
        indexRequest.Headers.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        using var indexResponse = await client.SendAsync(
                indexRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        indexResponse.EnsureSuccessStatusCode();
        var indexBytes = await ReadBoundedAsync(
                indexResponse.Content,
                MaximumReleaseIndexBytes,
                indexAsset.DownloadUri,
                cancellationToken)
            .ConfigureAwait(false);
        if (indexBytes.LongLength != indexAsset.Size)
        {
            throw new InvalidDataException(
                "The release index size does not match its GitHub asset metadata.");
        }

        var package = ParseReleaseIndex(
            indexBytes,
            candidate.Version,
            runtimeIdentifier,
            candidate.Assets);
        return new CrossPlatformRelease(
            candidate.Version,
            candidate.ReleaseUri,
            package,
            candidate.ReleaseNotes);
    }

    public static string ResolveCurrentRuntimeIdentifier()
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "Automatic updates currently require an x64 SrvSurvey package.");
        }

        if (OperatingSystem.IsWindows())
        {
            return WinX64RuntimeIdentifier;
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux-x64";
        }

        throw new PlatformNotSupportedException(
            "Automatic updates are available only on Windows and Linux.");
    }

    private static ReleasePage ParseReleasePage(
        byte[] bytes,
        ReleaseChannel channel)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The GitHub releases response is not an array.");
            }

            ReleaseCandidate? latest = null;
            var count = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                count++;
                if (count > ReleasesPerPage)
                {
                    throw new InvalidDataException(
                        "A GitHub release page contains too many releases.");
                }

                var candidate = ParseReleaseCandidate(element, channel);
                if (candidate is not null
                    && (latest is null || candidate.Version > latest.Version))
                {
                    latest = candidate;
                }
            }

            return new ReleasePage(latest, count);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The GitHub releases response is not valid JSON.",
                exception);
        }
    }

    private static ReleaseCandidate? ParseReleaseCandidate(
        JsonElement element,
        ReleaseChannel channel)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var isDraft = ReadBoolean(element, "draft");
        var isPrerelease = ReadBoolean(element, "prerelease");
        var tag = ReadRequiredString(element, "tag_name");
        if (isDraft
            || (channel == ReleaseChannel.Stable && isPrerelease)
            || !tag.StartsWith(ProductTagPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var versionText = tag[ProductTagPrefix.Length..];
        if (!ReleaseVersion.TryParse(versionText, out var version)
            || version.IsPrerelease != isPrerelease)
        {
            return null;
        }

        var releaseUri = ReadRequiredHttpsUri(element, "html_url");
        var releaseNotes = ReadOptionalString(element, "body");
        if (!element.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Release {version} has no GitHub asset array.");
        }

        var assets = new List<ReleaseAsset>();
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            if (assets.Count >= MaximumAssetCount)
            {
                throw new InvalidDataException(
                    $"Release {version} has too many GitHub assets.");
            }

            assets.Add(new ReleaseAsset(
                ReadRequiredString(assetElement, "name"),
                ReadPositiveInt64(assetElement, "size"),
                ReadRequiredHttpsUri(assetElement, "browser_download_url")));
        }

        var indexCount = assets.Count(asset => string.Equals(
            asset.Name,
            ReleaseIndexName,
            StringComparison.Ordinal));
        if (indexCount == 0)
        {
            return null;
        }

        if (indexCount != 1)
        {
            throw new InvalidDataException(
                $"Release {version} has duplicate release index assets.");
        }

        return new ReleaseCandidate(
            version,
            releaseUri,
            assets,
            GitHubReleaseNotes.ExtractChanges(releaseNotes));
    }

    private static CrossPlatformReleasePackage ParseReleaseIndex(
        byte[] bytes,
        ReleaseVersion expectedVersion,
        string runtimeIdentifier,
        IReadOnlyList<ReleaseAsset> assets)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || ReadRequiredInt32(root, "schemaVersion") != 1
                || !string.Equals(
                    ReadRequiredString(root, "product"),
                    ProductName,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The cross-platform release index has an incompatible schema or product.");
            }

            var versionText = ReadRequiredString(root, "version");
            if (!ReleaseVersion.TryParse(versionText, out var indexVersion)
                || indexVersion != expectedVersion)
            {
                throw new InvalidDataException(
                    "The release index version does not match the GitHub tag.");
            }

            if (!root.TryGetProperty("packages", out var packagesElement)
                || packagesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException(
                    "The release index has no package array.");
            }

            var packages = packagesElement.EnumerateArray().ToArray();
            if (packages.Length != 2)
            {
                throw new InvalidDataException(
                    "The release index must contain exactly two platform packages.");
            }

            var windows = ParseIndexedPackage(
                packages,
                expectedVersion,
                WinX64RuntimeIdentifier,
                "zip",
                assets);
            var linux = ParseIndexedPackage(
                packages,
                expectedVersion,
                "linux-x64",
                "tar.gz",
                assets);
            return runtimeIdentifier == WinX64RuntimeIdentifier ? windows : linux;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The cross-platform release index is not valid JSON.",
                exception);
        }
    }

    private static CrossPlatformReleasePackage ParseIndexedPackage(
        IReadOnlyList<JsonElement> packages,
        ReleaseVersion version,
        string runtimeIdentifier,
        string archiveType,
        IReadOnlyList<ReleaseAsset> assets)
    {
        var matching = packages.Where(package => string.Equals(
            ReadRequiredString(package, "runtimeIdentifier"),
            runtimeIdentifier,
            StringComparison.Ordinal)).ToArray();
        var suffix = archiveType == "zip" ? ".zip" : ".tar.gz";
        var expectedName = $"{PackageNamePrefix}-{version}-{runtimeIdentifier}{suffix}";
        if (matching.Length != 1
            || !string.Equals(
                ReadRequiredString(matching[0], "archive"),
                expectedName,
                StringComparison.Ordinal)
            || !string.Equals(
                ReadRequiredString(matching[0], "archiveType"),
                archiveType,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The release index has an invalid {runtimeIdentifier} package contract.");
        }

        var selected = matching[0];
        var archiveName = ReadRequiredString(selected, "archive");
        var size = ReadPositiveInt64(selected, "size");
        if (size > MaximumPackageBytes)
        {
            throw new InvalidDataException(
                $"The {runtimeIdentifier} package exceeds the supported size.");
        }

        var sha256 = ReadRequiredString(selected, "sha256").ToLowerInvariant();
        if (sha256.Length != 64 || sha256.Any(character =>
                !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException(
                $"The {runtimeIdentifier} package has an invalid SHA-256 value.");
        }

        var matchingAssets = assets.Where(asset => string.Equals(
            asset.Name,
            archiveName,
            StringComparison.Ordinal)).ToArray();
        if (matchingAssets.Length != 1 || matchingAssets[0].Size != size)
        {
            throw new InvalidDataException(
                $"The {runtimeIdentifier} package does not match its GitHub asset metadata.");
        }

        return new CrossPlatformReleasePackage(
            runtimeIdentifier,
            archiveName,
            archiveType,
            size,
            sha256,
            matchingAssets[0].DownloadUri);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException(
                $"The update response exceeded {maximumBytes:N0} bytes: {uri}");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new InvalidDataException(
                    $"The update response exceeded {maximumBytes:N0} bytes: {uri}");
            }

            await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static HttpRequestMessage CreateGitHubRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        };
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

        var separator = string.IsNullOrEmpty(baseUri.Query) ? "?" : "&";
        return new Uri($"{baseUri.AbsoluteUri}{separator}page={page}");
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                $"The GitHub release has an invalid '{propertyName}' value.");
        }

        return property.GetBoolean();
    }

    private static int ReadRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt32(out var value))
        {
            throw new InvalidDataException(
                $"The update metadata has an invalid '{propertyName}' value.");
        }

        return value;
    }

    private static long ReadPositiveInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || !property.TryGetInt64(out var value)
            || value <= 0)
        {
            throw new InvalidDataException(
                $"The update metadata has an invalid '{propertyName}' value.");
        }

        return value;
    }

    private static string ReadRequiredString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException(
                $"The update metadata has an invalid '{propertyName}' value.");
        }

        return property.GetString()!;
    }

    private static string ReadOptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static Uri ReadRequiredHttpsUri(
        JsonElement element,
        string propertyName)
    {
        var value = ReadRequiredString(element, propertyName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                $"The update metadata has an invalid '{propertyName}' URI.");
        }

        return uri;
    }

    private static void ValidateRuntimeIdentifier(string runtimeIdentifier)
    {
        if (runtimeIdentifier is not (WinX64RuntimeIdentifier or "linux-x64"))
        {
            throw new PlatformNotSupportedException(
                $"The runtime '{runtimeIdentifier}' has no SrvSurvey update package.");
        }
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-XP/1.0");
        return client;
    }

    private sealed record ReleasePage(
        ReleaseCandidate? Latest,
        int ReleaseCount);

    private sealed record ReleaseCandidate(
        ReleaseVersion Version,
        Uri ReleaseUri,
        IReadOnlyList<ReleaseAsset> Assets,
        string ReleaseNotes);

    private sealed record ReleaseAsset(
        string Name,
        long Size,
        Uri DownloadUri);
}
