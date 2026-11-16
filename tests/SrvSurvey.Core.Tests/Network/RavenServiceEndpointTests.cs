using System.Net;
using System.Text;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Quests;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Network;

public sealed class RavenServiceEndpointTests
{
    [Theory]
    [InlineData("colonization", "GET", "https://ravencolonial.net/api/v2/system/Sol/sites")]
    [InlineData("quests", "GET", "https://ravencolonial.net/api/quest/published")]
    [InlineData("green-gas-giant", "PUT", "https://ravencolonial.net/api/ggg/create")]
    public async Task DefaultClientsSendRequestsToTheMigratedHost(string service, string method, string expectedUri)
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);

        Task request = service switch
        {
            "colonization" => new RavenColonialClient(http).GetSystemSitesAsync("Sol"),
            "quests" => new RavenQuestClient(http).GetPublishedQuestsAsync(),
            "green-gas-giant" => new GreenGasGiantClient(http).PublishAsync(
                new GreenGasGiantCandidate("Test Cmdr", "likely", new GalacticCoordinate(1, 2, 3), "{}")
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(service), service, "Unknown Raven service."),
        };
        await request;

        Assert.Equal(method, handler.Method?.Method);
        Assert.Equal(expectedUri, handler.RequestUri?.AbsoluteUri);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
