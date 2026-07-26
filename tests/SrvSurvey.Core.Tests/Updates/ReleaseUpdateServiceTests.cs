using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class ReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckAsyncReportsNewerFourPartGitHubVersion()
    {
        var service = new ReleaseUpdateService(
            new StubIndexClient(CreateIndex(new Version(2, 0, 95, 23))),
            new Uri("https://example.test/releases"));

        var result = await service.CheckAsync(new Version(2, 0, 95, 0));

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(new Version(2, 0, 95, 23), result.LatestVersion);
        Assert.Equal("https://example.test/releases", result.ReleaseUri.AbsoluteUri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(22)]
    public async Task CheckAsyncDoesNotOfferEqualOrOlderBuilds(int revision)
    {
        var service = new ReleaseUpdateService(
            new StubIndexClient(CreateIndex(new Version(2, 0, 95, revision))));

        var result = await service.CheckAsync(new Version(2, 0, 95, 22));

        Assert.False(result.IsUpdateAvailable);
    }

    private static PublishedDataIndex CreateIndex(Version version)
    {
        return new PublishedDataIndex(
            version,
            new Version(2, 0, 95, 0),
            7,
            4,
            10,
            48,
            68,
            15,
            1,
            1);
    }

    private sealed class StubIndexClient(PublishedDataIndex index)
        : IPublishedDataIndexClient
    {
        public Task<PublishedDataIndex> GetAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(index);
        }
    }
}
