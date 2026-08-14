using System.Net;
using System.Text;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class ArdentSystemNameSuggestionClientTests
{
    [Fact]
    public async Task SearchUsesAnonymousNameEndpointAndReturnsUniqueSuggestions()
    {
        var handler = new StubHandler(
            HttpStatusCode.OK,
            """
            [
              { "systemAddress": 10477373803, "systemName": "Sol" },
              { "systemAddress": 10477373803, "systemName": "sol" },
              { "systemAddress": 1458376315610, "systemName": "Solati" },
              { "systemAddress": 0, "systemName": "Invalid" }
            ]
            """);
        var client = new ArdentSystemNameSuggestionClient(
            new HttpClient(handler),
            new Uri("https://example.test/v2/search/system/name/"));

        var results = await client.SearchAsync(" Sol ");

        Assert.Equal(2, results.Count);
        Assert.Equal(new SystemNameSuggestion("Sol", 10477373803, "Ardent"), results[0]);
        Assert.Equal(new SystemNameSuggestion("Solati", 1458376315610, "Ardent"), results[1]);
        Assert.Equal(
            "https://example.test/v2/search/system/name/Sol",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Null(handler.LastAuthorization);
    }

    [Fact]
    public async Task SearchRequiresThreeCharactersAndRejectsHttpFailures()
    {
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, "[]");
        var client = new ArdentSystemNameSuggestionClient(
            new HttpClient(handler),
            new Uri("https://example.test/"));

        Assert.Empty(await client.SearchAsync("So"));
        Assert.Equal(0, handler.RequestCount);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SearchAsync("Sol"));
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
