using System.Net;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class RavenColonialProjectCarrierTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendsBodylessCarrierLinkToEncodedProjectAndNumericMarket(bool link)
    {
        int sent = 0;
        using var http = new HttpClient(
            new Handler(request =>
            {
                sent++;
                Assert.Equal(link ? HttpMethod.Put : HttpMethod.Delete, request.Method);
                Assert.Equal("/root/api/project/build%20one/fc/3700123456", request.RequestUri!.AbsolutePath);
                Assert.Null(request.Content);
                return new(HttpStatusCode.NoContent);
            })
        );
        var client = new RavenColonialClient(http, new Uri("https://example.test/root/"));

        await ChangeAsync(client, link, " build one ", 3_700_123_456);
        Assert.Equal(1, sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsInvalidIdentifiersBeforeSendingAndSurfacesRavenRejections(bool link)
    {
        int sent = 0;
        using var http = new HttpClient(
            new Handler(_ =>
            {
                sent++;
                return new(HttpStatusCode.Forbidden) { Content = new StringContent("Carrier link rejected") };
            })
        );
        var client = new RavenColonialClient(http, new Uri("https://example.test/"));
        await Assert.ThrowsAsync<ArgumentException>(() => ChangeAsync(client, link, " ", 42));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChangeAsync(client, link, "build", 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ChangeAsync(client, link, "build", -1));
        Assert.Equal(0, sent);

        RavenColonialServiceException failure = await Assert.ThrowsAsync<RavenColonialServiceException>(() =>
            ChangeAsync(client, link, "build", 42)
        );
        Assert.Equal(HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Contains("Carrier link rejected", failure.Message);
        Assert.Equal(1, sent);
    }

    private static Task ChangeAsync(RavenColonialClient client, bool link, string buildId, long marketId) =>
        link ? client.LinkProjectCarrierAsync(buildId, marketId) : client.UnlinkProjectCarrierAsync(buildId, marketId);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(send(request));
    }
}
