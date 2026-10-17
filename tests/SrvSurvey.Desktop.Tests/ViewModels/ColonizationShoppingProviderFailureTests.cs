using System.Net;
using System.Text;
using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationShoppingProviderFailureTests
{
    [Fact]
    public async Task CompleteProviderOutagePreservesThePreviousSearchForReopening()
    {
        var handler = new MarketHandler();
        using var http = new HttpClient(handler);
        using var client = new ColonizationMarketSearchClient(http);
        using var model = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            client
        );
        await model.SearchAsync();
        Assert.Single(model.Results);
        DateTimeOffset? completed = model.CaptureState().CompletedAt;
        handler.IsOffline = true;
        await model.SearchAsync();
        Assert.Single(model.Results);
        Assert.Equal(completed, model.CaptureState().CompletedAt);
        Assert.Contains("Previous results", model.Status);
        using var reopened = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            client,
            restoredState: model.CaptureState()
        );
        Assert.Single(reopened.Results);
        Assert.Equal(completed, reopened.CaptureState().CompletedAt);
    }

    private sealed class MarketHandler : HttpMessageHandler
    {
        public bool IsOffline { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (IsOffline)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            string json =
                request.Method == HttpMethod.Post
                    ? JsonSerializer.Serialize(
                        new
                        {
                            count = 1,
                            results = new[]
                            {
                                new
                                {
                                    name = "Abraham Lincoln",
                                    system_name = "Sol",
                                    market_id = 1,
                                    type = "Orbis Starport",
                                    large_pads = 1,
                                    distance = 0,
                                    distance_to_arrival = 504,
                                    market_updated_at = DateTimeOffset.UtcNow,
                                    market = new[]
                                    {
                                        new
                                        {
                                            commodity = "Steel",
                                            supply = 200,
                                            buy_price = 500,
                                        },
                                    },
                                },
                            },
                        }
                    )
                    : "[]";
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
