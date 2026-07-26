using System.Net;
using System.Text;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Core.Tests.Updates;

public sealed class PublishedDataIndexClientTests
{
    private const string ValidPayload = """
        {
          "ghVer": "2.0.95.23",
          "msVer": "2.0.95.0",
          "bioCriteria": 7,
          "bioEngine": 4,
          "codexRef": 10,
          "settlementTemplate": 48,
          "guardian": 68,
          "settlements": 15,
          "nicknames": 1,
          "ggg": 1
        }
        """;

    [Fact]
    public async Task GetAsyncParsesTheLegacyPublishedIndex()
    {
        var handler = new StubHandler(HttpStatusCode.OK, ValidPayload);
        var client = new PublishedDataIndexClient(
            new HttpClient(handler),
            new Uri("https://example.test/data.json"));

        var result = await client.GetAsync();

        Assert.Equal(new Version(2, 0, 95, 23), result.GitHubVersion);
        Assert.Equal(new Version(2, 0, 95, 0), result.MicrosoftStoreVersion);
        Assert.Equal(7, result.BiologyCriteriaVersion);
        Assert.Equal(4, result.BiologyEngineVersion);
        Assert.Equal(10, result.CodexReferenceVersion);
        Assert.Equal(48, result.SettlementTemplateVersion);
        Assert.Equal(68, result.GuardianVersion);
        Assert.Equal(15, result.SettlementsVersion);
        Assert.Equal(1, result.NicknamesVersion);
        Assert.Equal(1, result.GreenGasGiantsVersion);
        Assert.Equal("https://example.test/data.json", handler.RequestUri?.AbsoluteUri);
        Assert.True(handler.NoCache);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"ghVer\":\"not-a-version\"}")]
    [InlineData("""
        {
          "ghVer": "2.0.95.23",
          "msVer": "2.0.95.0",
          "bioCriteria": -1,
          "bioEngine": 4,
          "codexRef": 10,
          "settlementTemplate": 48,
          "guardian": 68,
          "settlements": 15,
          "nicknames": 1,
          "ggg": 1
        }
        """)]
    public async Task GetAsyncRejectsIncompleteOrInvalidIndexes(string payload)
    {
        var client = new PublishedDataIndexClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, payload)));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetAsync());
    }

    [Fact]
    public async Task GetAsyncRejectsUnsuccessfulResponses()
    {
        var client = new PublishedDataIndexClient(
            new HttpClient(new StubHandler(
                HttpStatusCode.TooManyRequests,
                "rate limited")));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync());

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
    }

    [Fact]
    public async Task GetAsyncRejectsOversizedPublishedIndexes()
    {
        var client = new PublishedDataIndexClient(
            new HttpClient(new StubHandler(
                HttpStatusCode.OK,
                new string(' ', (64 * 1024) + 1))));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => client.GetAsync());

        Assert.Contains("published-data index", exception.Message);
        Assert.Contains("safety limit", exception.Message);
    }

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string payload) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public bool NoCache { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            NoCache = request.Headers.CacheControl?.NoCache == true;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
