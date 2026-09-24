using System.Net;
using System.Net.Http.Headers;
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

    [Fact]
    public void RetryAfterIsNotCappedAtTheLocalFallbackDelay()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));

        Assert.Equal(TimeSpan.FromSeconds(45), SpanshApi.RetryDelay(response, 1));
    }

    [Fact]
    public void MissingRetryAfterUsesARespectfulIncreasingFallback()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.InRange(SpanshApi.RetryDelay(response, 1), TimeSpan.FromSeconds(1.6), TimeSpan.FromSeconds(2.4));
        Assert.InRange(SpanshApi.RetryDelay(response, 2), TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(4.8));
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
