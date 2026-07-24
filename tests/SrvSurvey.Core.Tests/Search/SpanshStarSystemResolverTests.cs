using System.Net;
using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SpanshStarSystemResolverTests
{
    [Fact]
    public async Task SearchReadsLegacySpanshFieldValuesContract()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            {
              "min_max": [
                { "id64": 10477373803, "name": "Sol", "x": 0, "y": 0, "z": 0 },
                { "id64": 1458376315610, "name": "Solati", "x": 66.53125, "y": 29.1875, "z": 34.6875 }
              ],
              "values": ["Sol", "Solati"]
            }
            """);
        var resolver = new SpanshStarSystemResolver(
            new HttpClient(handler),
            new Uri("https://example.test/api/"));

        var systems = await resolver.SearchAsync(" Sol ");

        Assert.Equal(2, systems.Count);
        Assert.Equal(
            new StarSystemReference(
                "Sol",
                10477373803,
                new GalacticCoordinate(0, 0, 0)),
            systems[0]);
        Assert.Equal(
            "https://example.test/api/systems/field_values/system_names?q=Sol",
            handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task SearchRejectsHttpFailures()
    {
        var resolver = new SpanshStarSystemResolver(
            new HttpClient(new StubHandler(HttpStatusCode.ServiceUnavailable, "{}")),
            new Uri("https://example.test/api/"));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => resolver.SearchAsync("Sol"));
    }

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string content) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
