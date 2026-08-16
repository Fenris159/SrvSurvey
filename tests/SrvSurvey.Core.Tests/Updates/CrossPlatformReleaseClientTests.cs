using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class CrossPlatformReleaseClientTests
{
    private static readonly Uri ReleasesUri = new(
        "https://api.example.test/releases");
    private static readonly Uri IndexUri = new(
        "https://downloads.example.test/release-index.json");

    [Fact]
    public async Task GetLatestAsyncSelectsChecksumIndexedPackageForRuntime()
    {
        var payload = CreatePayload();
        var handler = new StubHandler(
            new Dictionary<Uri, string>
            {
                [ReleasesUri] = payload.Releases,
                [IndexUri] = payload.Index,
            });
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri,
            ReleasesUri);

        var result = await client.GetLatestAsync(
            "win-x64",
            ReleaseChannel.Development);

        Assert.NotNull(result);
        Assert.Equal(ReleaseVersion.Parse("2.0.95.23"), result.Version);
        Assert.Equal(
            "https://example.test/releases/2.0.95.23",
            result.ReleaseUri.AbsoluteUri);
        Assert.Equal("win-x64", result.Package.RuntimeIdentifier);
        Assert.Equal(
            "SrvSurvey-XP-2.0.95.23-win-x64.zip",
            result.Package.ArchiveName);
        Assert.Equal("zip", result.Package.ArchiveType);
        Assert.Equal(12_345, result.Package.Size);
        Assert.Equal(new string('a', 64), result.Package.Sha256);
        Assert.Equal(
            "https://downloads.example.test/windows.zip",
            result.Package.DownloadUri.AbsoluteUri);
        Assert.Contains("## What's changed", result.ReleaseNotes);
        Assert.DoesNotContain("## Packaging", result.ReleaseNotes);
        Assert.Equal([ReleasesUri, IndexUri], handler.RequestUris);
        Assert.All(handler.UserAgents, value =>
            Assert.Contains("SrvSurvey-XP/1.0", value));
        Assert.True(handler.FirstRequestDisabledCache);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("missing-package")]
    public async Task GetLatestAsyncRejectsInconsistentReleaseIndex(
        string mutation)
    {
        var payload = CreatePayload(mutation);
        var client = new CrossPlatformReleaseClient(
            new HttpClient(new StubHandler(
                new Dictionary<Uri, string>
                {
                    [ReleasesUri] = payload.Releases,
                    [IndexUri] = payload.Index,
                })),
            ReleasesUri);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetLatestAsync(
                "linux-x64",
                ReleaseChannel.Development));
    }

    [Fact]
    public async Task GetLatestAsyncIgnoresLegacyAndPrereleaseAssets()
    {
        var releases = JsonSerializer.Serialize(new object[]
        {
            new
            {
                tag_name = "3.0.0.0",
                draft = false,
                prerelease = true,
                html_url = "https://example.test/releases/3.0.0.0",
                assets = Array.Empty<object>(),
            },
            new
            {
                tag_name = "2.0.95.33",
                draft = false,
                prerelease = false,
                html_url = "https://example.test/releases/2.0.95.33",
                assets = new[]
                {
                    new
                    {
                        name = "SrvSurvey-2.0.95.33.zip",
                        size = 9_999L,
                        browser_download_url =
                            "https://downloads.example.test/legacy.zip",
                    },
                },
            },
        });
        var handler = new StubHandler(new Dictionary<Uri, string>
        {
            [ReleasesUri] = releases,
        });
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri,
            ReleasesUri);

        var result = await client.GetLatestAsync(
            "win-x64",
            ReleaseChannel.Stable);

        Assert.Null(result);
        Assert.Equal([ReleasesUri], handler.RequestUris);
    }

    [Fact]
    public async Task GetLatestAsyncRejectsUnsupportedRuntimeBeforeNetwork()
    {
        var handler = new StubHandler(new Dictionary<Uri, string>());
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => client.GetLatestAsync(
                "osx-arm64",
                ReleaseChannel.Development));

        Assert.Empty(handler.RequestUris);
    }

    [Fact]
    public async Task DevelopmentChannelSelectsXpReleaseCandidate()
    {
        const string version = "2.1.4.0-rc.3";
        var payload = CreatePayload(version: version, prerelease: true);
        var handler = new StubHandler(new Dictionary<Uri, string>
        {
            [ReleasesUri] = payload.Releases,
            [IndexUri] = payload.Index,
        });
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri,
            ReleasesUri);

        var development = await client.GetLatestAsync(
            "linux-x64",
            ReleaseChannel.Development);

        Assert.NotNull(development);
        Assert.Equal(ReleaseVersion.Parse(version), development.Version);
        Assert.Equal(
            $"SrvSurvey-XP-{version}-linux-x64.tar.gz",
            development.Package.ArchiveName);
    }

    [Fact]
    public async Task StableChannelDoesNotSelectXpReleaseCandidate()
    {
        var payload = CreatePayload(
            version: "2.1.4.0-rc.3",
            prerelease: true);
        var handler = new StubHandler(new Dictionary<Uri, string>
        {
            [ReleasesUri] = payload.Releases,
        });
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri,
            ReleasesUri);

        var stable = await client.GetLatestAsync(
            "win-x64",
            ReleaseChannel.Stable);

        Assert.Null(stable);
        Assert.Equal([ReleasesUri], handler.RequestUris);
    }

    [Fact]
    public async Task StableChannelUsesItsConfiguredRepositoryFeed()
    {
        var stableReleasesUri = new Uri(
            "https://api.example.test/upstream/releases");
        var handler = new StubHandler(new Dictionary<Uri, string>
        {
            [stableReleasesUri] = "[]",
        });
        var client = new CrossPlatformReleaseClient(
            new HttpClient(handler),
            ReleasesUri,
            stableReleasesUri);

        var stable = await client.GetLatestAsync(
            "win-x64",
            ReleaseChannel.Stable);

        Assert.Null(stable);
        Assert.Equal([stableReleasesUri], handler.RequestUris);
    }

    private static ReleasePayload CreatePayload(
        string? mutation = null,
        string version = "2.0.95.23",
        bool prerelease = false)
    {
        var indexNode = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1,
            product = "SrvSurvey.XP",
            version,
            packages = new object[]
            {
                new
                {
                    runtimeIdentifier = "win-x64",
                    archive = $"SrvSurvey-XP-{version}-win-x64.zip",
                    archiveType = "zip",
                    size = 12_345L,
                    sha256 = new string('a', 64),
                },
                new
                {
                    runtimeIdentifier = "linux-x64",
                    archive = $"SrvSurvey-XP-{version}-linux-x64.tar.gz",
                    archiveType = "tar.gz",
                    size = 23_456L,
                    sha256 = new string('b', 64),
                },
            },
        })!.AsObject();
        var packages = indexNode["packages"]!.AsArray();
        switch (mutation)
        {
            case "version":
                indexNode["version"] = "2.0.95.22";
                break;
            case "hash":
                packages[1]!["sha256"] = "not-a-sha256";
                break;
            case "size":
                packages[0]!["size"] = 12_346L;
                break;
            case "missing-package":
                packages.RemoveAt(1);
                break;
        }

        var index = indexNode.ToJsonString();
        var releases = JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = $"xp-v{version}",
                draft = false,
                prerelease,
                html_url = $"https://example.test/releases/{version}",
                body = """
                    # SrvSurvey-XP

                    Release summary.

                    ## What's changed

                    - A useful change.

                    ## Packaging

                    - Package detail that is not shown in the app.
                    """,
                assets = new[]
                {
                    new
                    {
                        name = "release-index.json",
                        size = (long)Encoding.UTF8.GetByteCount(index),
                        browser_download_url = IndexUri.AbsoluteUri,
                    },
                    new
                    {
                        name = $"SrvSurvey-XP-{version}-win-x64.zip",
                        size = 12_345L,
                        browser_download_url =
                            "https://downloads.example.test/windows.zip",
                    },
                    new
                    {
                        name = $"SrvSurvey-XP-{version}-linux-x64.tar.gz",
                        size = 23_456L,
                        browser_download_url =
                            "https://downloads.example.test/linux.tar.gz",
                    },
                },
            },
        });
        return new ReleasePayload(releases, index);
    }

    private sealed record ReleasePayload(string Releases, string Index);

    private sealed class StubHandler(
        IReadOnlyDictionary<Uri, string> responses) : HttpMessageHandler
    {
        public List<Uri> RequestUris { get; } = [];

        public List<string> UserAgents { get; } = [];

        public bool FirstRequestDisabledCache { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("Request URI was missing.");
            if (RequestUris.Count == 0)
            {
                FirstRequestDisabledCache = request.Headers.CacheControl?.NoCache == true;
            }

            RequestUris.Add(uri);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            if (!responses.TryGetValue(uri, out var payload))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
