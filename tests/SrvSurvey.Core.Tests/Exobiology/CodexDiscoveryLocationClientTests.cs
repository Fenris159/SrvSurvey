using System.Net;
using System.Text;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Tests.Exobiology;

public sealed class CodexDiscoveryLocationClientTests
{
    [Fact]
    public async Task ResolvesBodyRegionAndSpanshLink()
    {
        using var httpClient = new HttpClient(new StubHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "system":{
                        "name":"Test System",
                        "coords":{"x":0,"y":0,"z":0},
                        "bodies":[
                          {"bodyId":3,"id64":"123456789","name":"Test System 3"}
                        ]
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            }));
        var client = new CodexDiscoveryLocationClient(
            httpClient,
            new Uri("https://example.test/api/"));

        var result = await client.GetAsync(42, 3);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test System", result.Location!.SystemName);
        Assert.Equal("Test System 3", result.Location.BodyName);
        Assert.Equal("Inner Orion Spur", result.Location.Region!.Name);
        Assert.Equal(
            "https://spansh.co.uk/body/123456789",
            result.Location.SpanshUri.AbsoluteUri);
    }

    [Fact]
    public async Task FallsBackToSystemWhenBodyIsUnknown()
    {
        using var httpClient = new HttpClient(new StubHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"system":{"name":"Test System","bodies":[]}}""",
                    Encoding.UTF8,
                    "application/json"),
            }));
        var client = new CodexDiscoveryLocationClient(
            httpClient,
            new Uri("https://example.test/api/"));

        var result = await client.GetAsync(42, 7);

        Assert.True(result.IsSuccess);
        Assert.Equal("Test System #7", result.Location!.BodyName);
        Assert.Equal(
            "https://spansh.co.uk/system/42",
            result.Location.SpanshUri.AbsoluteUri);
    }

    private sealed class StubHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
