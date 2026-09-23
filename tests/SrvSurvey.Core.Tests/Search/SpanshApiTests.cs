using System.Net;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class SpanshApiTests
{
    [Fact]
    public async Task SearchRetriesOneBadGatewayResponse()
    {
        using var handler = new OneBadGatewayHandler();
        using var http = new HttpClient(handler);
        var api = new SpanshApi(http);

        using System.Text.Json.JsonDocument result = await api.SearchAsync(SpanshRoutes.Bodies, "Timbalderis", [], 0);

        Assert.Equal(2, handler.RequestCount);
        Assert.Single(result.RootElement.GetProperty("results").EnumerateArray());
    }

    private sealed class OneBadGatewayHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            HttpResponseMessage response =
                RequestCount == 1
                    ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"results":[{"name":"Candidate"}]}"""),
                    };
            return Task.FromResult(response);
        }
    }
}
