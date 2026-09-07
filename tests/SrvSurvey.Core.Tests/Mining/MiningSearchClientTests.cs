using System.Net;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningSearchClientTests
{
    [Fact]
    public async Task HotspotResultsKeepCoordinatesUnknownAndFilterAtRingLevel()
    {
        using var http = new HttpClient(new Handler("""{"results":[{"system_name":"Sol","name":"Earth","distance":4,"rings":[{"name":"Earth A Ring","type":"Metallic","signals":[{"name":"Platinum","count":2}]},{"name":"Earth B Ring","type":"Icy","signals":[{"name":"Tritium","count":1}]}]}]}"""));
        var client = new MiningSearchClient(http);
        var results = await client.FindRingsAsync(new MiningRingQuery("Sol", "Platinum", "Metallic", 50, 2));
        Assert.Single(results);
        Assert.Equal("Earth A Ring", results[0].Body);
        Assert.Null(results[0].Position);
        Assert.Equal(2, results[0].Hotspots["Platinum"]);
    }
    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}
