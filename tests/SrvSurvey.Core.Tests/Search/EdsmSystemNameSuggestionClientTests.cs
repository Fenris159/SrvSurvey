using System.Net;
using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class EdsmSystemNameSuggestionClientTests
{
    [Fact]
    public async Task SearchUsesDocumentedAnonymousPrefixEndpoint()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            [
              { "name": "Leamae UK-D d13-0", "id": 96189209, "id64": 7994265971 },
              { "name": "leamae uk-d d13-0", "id": 96189209, "id64": 7994265971 },
              { "name": "Leamae UK-D d13-1", "id": 96189210, "id64": 9093777597939 },
              { "name": "Invalid", "id": 1, "id64": 0 }
            ]
            """);
        var client = new EdsmSystemNameSuggestionClient(
            new HttpClient(handler),
            new Uri("https://example.test/api-v1/systems"));

        var results = await client.SearchAsync(" Leamae UK-D d13- ");

        Assert.Equal(2, results.Count);
        Assert.Equal(
            new SystemNameSuggestion("Leamae UK-D d13-0", 7994265971, "EDSM"),
            results[0]);
        Assert.Equal(
            new SystemNameSuggestion("Leamae UK-D d13-1", 9093777597939, "EDSM"),
            results[1]);
        Assert.Equal(
            "https://example.test/api-v1/systems?systemName=Leamae%20UK-D%20d13-&showId=1",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Null(handler.LastAuthorization);
    }

    [Fact]
    public async Task SearchRequiresThreeCharactersAndRejectsHttpFailures()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "[]");
        var client = new EdsmSystemNameSuggestionClient(
            new HttpClient(handler),
            new Uri("https://example.test/api-v1/systems"));

        Assert.Empty(await client.SearchAsync("So"));
        Assert.Equal(0, handler.RequestCount);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchAsync("Sol"));
    }

    [Fact]
    public async Task SearchUsesSystemId64ForNumericInput()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """[{ "name": "Sol", "id": 27, "id64": 10477373803 }]""");
        var client = new EdsmSystemNameSuggestionClient(
            new HttpClient(handler),
            new Uri("https://example.test/api-v1/systems"));

        var result = Assert.Single(await client.SearchAsync("10477373803"));

        Assert.Equal(
            new SystemNameSuggestion("Sol", 10477373803, "EDSM"),
            result);
        Assert.Equal(
            "https://example.test/api-v1/systems?systemId64=10477373803&showId=1",
            handler.LastRequestUri?.AbsoluteUri);
    }

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        string content) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public string? LastAuthorization { get; private set; }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
