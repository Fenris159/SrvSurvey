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
    [Fact]
    public async Task TraderRequestUsesRadiusAndSmallPages()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindTradersAsync("Wille", "Raw", radius: 75, page: 2);
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal(20, body.RootElement.GetProperty("size").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(75, body.RootElement.GetProperty("filters").GetProperty("distance").GetProperty("max").GetDouble());
        Assert.Equal("Raw", body.RootElement.GetProperty("filters").GetProperty("material_trader").GetProperty("value").GetString());
    }
    [Fact]
    public async Task ChosenSystemScopesRingRequest()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindRingsAsync(new("Wille", "", "All", 50, SystemOnly: true));
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal("Wille", body.RootElement.GetProperty("filters").GetProperty("system_name").GetProperty("value")[0].GetString());
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MarketsKeepOnlyFreshUsableStationsAndSortForTradeDirection(bool spansh, bool buying)
    {
        var now = DateTimeOffset.UtcNow;
        (string Name, string Type, bool Pad, DateTimeOffset Age, long Price, long Demand, long Supply)[] candidates =
        {
            ("Good", "Coriolis", true, now, 100L, 100L, 100L),
            ("Better", "Coriolis", true, now, 200L, 100L, 100L),
            ("Carrier", "Fleet Carrier", true, now, 300L, 100L, 100L),
            ("Small", "Coriolis", false, now, 300L, 100L, 100L),
            ("Old", "Coriolis", true, now.AddDays(-10), 300L, 100L, 100L),
            ("Future", "Coriolis", true, now.AddDays(10), 300L, 100L, 100L),
            ("Wrong type", "Outpost", true, now, 300L, 100L, 100L),
            ("Zero price", "Coriolis", true, now, 0L, 100L, 100L),
            ("No trade volume", "Coriolis", true, now, 300L, 0L, 0L)
        };
        var rows = candidates.Select(c => spansh ? (object)new
        {
            system_name = "Sol",
            name = c.Name,
            type = c.Type,
            has_large_pad = c.Pad,
            market_updated_at = c.Age,
            market_id = 42,
            market = new[] { new { commodity = "Platinum", buy_price = c.Price, sell_price = c.Price, supply = c.Supply, demand = c.Demand } }
        } : new
        {
            systemName = "Sol",
            stationName = c.Name,
            stationType = c.Type,
            maxLandingPadSize = c.Pad ? 3 : 2,
            updatedAt = c.Age,
            marketId = 42,
            buyPrice = c.Price,
            sellPrice = c.Price,
            stock = c.Supply,
            demand = c.Demand
        }).ToArray();
        var payload = spansh ? System.Text.Json.JsonSerializer.Serialize(new { results = rows }) : System.Text.Json.JsonSerializer.Serialize(rows);
        using var http = new HttpClient(new Handler(payload));
        var client = new MiningSearchClient(http);
        var query = new MiningMarketQuery("Sol", "Platinum", buying, ExcludeCarriers: true, LargePads: true, StationType: "Coriolis");
        var results = spansh ? await client.FindSpanshMarketsAsync(query) : await client.FindMarketsAsync(query);
        Assert.Equal(buying ? new[] { "Good", "Better" } : new[] { "Better", "Good" }, results.Select(r => r.Station));
        Assert.All(results, r => { Assert.True(r.LargePad); Assert.Equal(42, r.MarketId); Assert.Null(r.Distance); Assert.Null(r.ArrivalLs); });
    }
    [Fact]
    public async Task MissingAndMalformedMarketDatesCannotLookLikeFreshPrices()
    {
        using var http = new HttpClient(new Handler("""[{"systemName":"Sol","stationName":"Missing","sellPrice":200,"demand":10},{"stationName":"Bad","sellPrice":200,"demand":10,"updatedAt":"not a date"}]"""));
        Assert.Empty(await new MiningSearchClient(http).FindMarketsAsync(new("Sol", "Platinum", false)));
    }
    [Fact]
    public async Task SpanshFallbackUsesStockAndNumericPadCount()
    {
        var payload = $$"""{"results":[{"system_name":"Sol","name":"Market","large_pads":2,"market_updated_at":"{{DateTimeOffset.UtcNow:O}}","market":[{"commodity":"Platinum","buy_price":50,"stock":25},{"commodity":"Gold","buy_price":100,"stock":20}]}]}""";
        using var http = new HttpClient(new Handler(payload));
        var result = Assert.Single(await new MiningSearchClient(http).FindSpanshMarketsAsync(new("Sol", "Platinum", true, LargePads: true)));
        Assert.Equal(25, result.Supply); Assert.Equal(50, result.Price); Assert.True(result.LargePad);
    }
    [Fact]
    public async Task SystemFiltersAndPageAreForwardedAndMetadataSurvives()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindSystemsAsync(new("Wille", 75, "High", "Empire", "Democracy", "Boom", "Industrial", "Aisling Duval", "Fortified", 1000, 3));
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var filters = body.RootElement.GetProperty("filters");
        Assert.Equal(3, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal("High", filters.GetProperty("security").GetProperty("value").GetString());
        Assert.Equal("Aisling Duval", filters.GetProperty("controlling_power").GetProperty("value")[0].GetString());
        Assert.Equal("Fortified", filters.GetProperty("power_state").GetProperty("value")[0].GetString());
        Assert.Equal(1000, filters.GetProperty("population").GetProperty("min").GetInt64());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    [InlineData(double.NaN)]
    public async Task InvalidRadiusIsRejectedBeforeSending(double radius)
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new MiningSearchClient(http).FindTradersAsync("Sol", "Raw", radius: radius));
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task ExactSystemMarketScopeIsAppliedBeforeStationPagination()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindMarketsAsync(new("Wille", "Platinum", false, Page: 2, SystemOnly: true));
        using var request = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal("Wille", request.RootElement.GetProperty("filters").GetProperty("system_name").GetProperty("value")[0].GetString());
        Assert.Equal(2, request.RootElement.GetProperty("page").GetInt32());
    }

    private sealed class RequestHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") };
        }
    }
    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}
