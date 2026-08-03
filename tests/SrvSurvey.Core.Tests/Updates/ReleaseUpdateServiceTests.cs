using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReportsNewerPackageForTheCurrentRuntime()
    {
        var release = CreateRelease(new Version(2, 0, 95, 23));
        var service = new ReleaseUpdateService(
            new StubReleaseClient(release),
            "win-x64",
            new Uri("https://example.test/releases"));

        var result = await service.CheckAsync(
            new Version(2, 0, 95, 0),
            ReleaseChannel.Development);

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(ReleaseVersion.Parse("2.0.95.23"), result.LatestVersion);
        Assert.Equal(release.Package, result.Package);
        Assert.Equal(
            "https://example.test/releases/2.0.95.23",
            result.ReleaseUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    public async Task CheckAsyncDoesNotOfferEqualOrOlderBuilds(int revision)
    {
        var service = new ReleaseUpdateService(
            new StubReleaseClient(CreateRelease(new Version(2, 0, 95, revision))),
            "win-x64");

        var result = await service.CheckAsync(
            new Version(2, 0, 95, 22),
            ReleaseChannel.Development);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task CheckAsyncTreatsNoCompatibleReleaseAsCurrent()
    {
        var service = new ReleaseUpdateService(
            new StubReleaseClient(null),
            "linux-x64",
            new Uri("https://example.test/releases"));
        var current = new Version(2, 0, 95, 0);

        var result = await service.CheckAsync(current, ReleaseChannel.Development);

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestVersion);
        Assert.Equal(
            "https://example.test/releases",
            result.ReleaseUri.AbsoluteUri);
    }

    private static CrossPlatformRelease CreateRelease(Version version)
    {
        return new CrossPlatformRelease(
            version,
            new Uri($"https://example.test/releases/{version}"),
            new CrossPlatformReleasePackage(
                "win-x64",
                $"SrvSurvey-XP-{version}-win-x64.zip",
                "zip",
                1_024,
                new string('a', 64),
                new Uri("https://example.test/package.zip")));
    }

    private sealed class StubReleaseClient(CrossPlatformRelease? release)
        : ICrossPlatformReleaseClient
    {
        public Task<CrossPlatformRelease?> GetLatestAsync(
            string runtimeIdentifier,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(release);
        }
    }
}
