using System.Net;
using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningSearchClientTests
{
    private static readonly string[] BuyingStationOrder = ["Good", "Better"];
    private static readonly string[] SellingStationOrder = ["Better", "Good"];

    [Fact]
    public async Task HotspotResultsKeepCoordinatesUnknownAndFilterAtRingLevel()
    {
        using var http = new HttpClient(
            new Handler(
                """{"results":[{"system_name":"Sol","name":"Earth","distance":4,"rings":[{"name":"Earth A Ring","type":"Metallic","signals":[{"name":"Platinum","count":2}]},{"name":"Earth B Ring","type":"Icy","signals":[{"name":"Tritium","count":1}]}]}]}"""
            )
        );
        var client = new MiningSearchClient(http);
        IReadOnlyList<MiningRing> results = await client.FindRingsAsync(
            new MiningRingQuery("Sol", "Platinum", "Metallic", 50, 2)
        );
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
        Assert.Equal(
            75,
            body.RootElement.GetProperty("filters").GetProperty("distance").GetProperty("max").GetDouble()
        );
        Assert.Equal(
            "Raw",
            body.RootElement.GetProperty("filters").GetProperty("material_trader").GetProperty("value").GetString()
        );
    }

    [Fact]
    public async Task ChosenSystemScopesRingRequest()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindRingsAsync(new("Wille", "", "All", 50, SystemOnly: true));
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "Wille",
            body.RootElement.GetProperty("filters").GetProperty("system_name").GetProperty("value")[0].GetString()
        );
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MarketsKeepOnlyFreshUsableStationsAndSortForTradeDirection(bool spansh, bool buying)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
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
            ("No trade volume", "Coriolis", true, now, 300L, 0L, 0L),
            ("Below selected volume", "Coriolis", true, now, 300L, 80L, 80L),
            ("Above selected volume", "Coriolis", true, now, 300L, 120L, 120L),
        };
        object[] rows = candidates
            .Select(c =>
            {
                int maxLandingPadSize = c.Pad ? 3 : 2;
                return spansh
                    ? (object)
                        new
                        {
                            system_name = "Sol",
                            name = c.Name,
                            type = c.Type,
                            has_large_pad = c.Pad,
                            market_updated_at = c.Age,
                            market_id = 42,
                            market = new[]
                            {
                                new
                                {
                                    commodity = "Platinum",
                                    buy_price = c.Price,
                                    sell_price = c.Price,
                                    supply = c.Supply,
                                    demand = c.Demand,
                                },
                            },
                        }
                    : new
                    {
                        systemName = "Sol",
                        stationName = c.Name,
                        stationType = c.Type,
                        maxLandingPadSize,
                        updatedAt = c.Age,
                        marketId = 42,
                        buyPrice = c.Price,
                        sellPrice = c.Price,
                        stock = c.Supply,
                        demand = c.Demand,
                    };
            })
            .ToArray();
        string payload = spansh
            ? System.Text.Json.JsonSerializer.Serialize(new { results = rows })
            : System.Text.Json.JsonSerializer.Serialize(rows);
        using var http = new HttpClient(new Handler(payload));
        var client = new MiningSearchClient(http);
        var query = new MiningMarketQuery(
            "Sol",
            "Platinum",
            buying,
            ExcludeCarriers: true,
            LargePads: true,
            StationType: "Coriolis",
            MinimumDemand: 90,
            MaximumDemand: 110
        );
        IReadOnlyList<MiningMarketResult> results = spansh
            ? await client.FindSpanshMarketsAsync(query)
            : await client.FindMarketsAsync(query);
        Assert.Equal(buying ? BuyingStationOrder : SellingStationOrder, results.Select(r => r.Station));
        Assert.All(
            results,
            r =>
            {
                Assert.True(r.LargePad);
                Assert.Equal(42, r.MarketId);
                Assert.Null(r.Distance);
                Assert.Null(r.ArrivalLs);
            }
        );
    }

    [Fact]
    public async Task MissingAndMalformedMarketDatesCannotLookLikeFreshPrices()
    {
        using var http = new HttpClient(
            new Handler(
                """[{"systemName":"Sol","stationName":"Missing","sellPrice":200,"demand":10},{"stationName":"Bad","sellPrice":200,"demand":10,"updatedAt":"not a date"}]"""
            )
        );
        Assert.Empty(await new MiningSearchClient(http).FindMarketsAsync(new("Sol", "Platinum", false)));
    }

    [Fact]
    public async Task SpanshFallbackUsesStockAndNumericPadCount()
    {
        string payload =
            $$"""{"results":[{"system_name":"Sol","name":"Market","large_pads":2,"market_updated_at":"{{DateTimeOffset.UtcNow:O}}","market":[{"commodity":"Platinum","buy_price":50,"stock":25},{"commodity":"Gold","buy_price":100,"stock":20}]}]}""";
        using var http = new HttpClient(new Handler(payload));
        MiningMarketResult result = Assert.Single(
            await new MiningSearchClient(http).FindSpanshMarketsAsync(new("Sol", "Platinum", true, LargePads: true))
        );
        Assert.Equal(25, result.Supply);
        Assert.Equal(50, result.Price);
        Assert.True(result.LargePad);
    }

    [Fact]
    public async Task SystemFiltersAndPageAreForwardedAndMetadataSurvives()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindSystemsAsync(
            new("Wille", 75, "High", "Empire", "Democracy", "Boom", "Industrial", "Aisling Duval", "Fortified", 1000, 3)
        );
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        JsonElement filters = body.RootElement.GetProperty("filters");
        Assert.Equal(3, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal("High", filters.GetProperty("security").GetProperty("value").GetString());
        Assert.Equal("Aisling Duval", filters.GetProperty("controlling_power").GetProperty("value")[0].GetString());
        Assert.Equal("Fortified", filters.GetProperty("power_state").GetProperty("value")[0].GetString());
        Assert.Equal(1000, filters.GetProperty("population").GetProperty("min").GetInt64());
    }

    [Fact]
    public void ConflictProgressInfersAcquisitionStateUntilControlIsSettled()
    {
        PowerplayProgress[] alrai =
        [
            new("Nakato Kaine", 0.392558),
            new("Yuri Grom", 0.044717),
            new("Aisling Duval", 0.019533),
            new("Edmund Mahon", 0.017),
            new("Jerome Archer", 1.439767),
        ];
        Assert.Equal("Contested", PowerplayStanding.Infer("", "Unoccupied", alrai));
        Assert.Equal("Expansion", PowerplayStanding.Infer("", "Unoccupied", [new("Nakato Kaine", 0.392558)]));
        Assert.Equal("Unoccupied", PowerplayStanding.Infer("", "Unoccupied", [new("Yuri Grom", 0.044717)]));
        Assert.Equal("", PowerplayStanding.Infer("", "", []));
        Assert.Equal("Fortified", PowerplayStanding.Infer("Jerome Archer", "Fortified", alrai));
    }

    [Fact]
    public async Task ContestedSearchReadsTheUnoccupiedIndexAndKeepsThresholdCrossings()
    {
        const string payload = """
            {"results":[
              {"name":"Alrai Sector FG-X b1-6","distance":12,"power_state":"Unoccupied","power_conflict_progress":[
                {"power":"Nakato Kaine","progress":0.392558},
                {"power":"Jerome Archer","progress":1.439767}
              ]},
              {"name":"Quiet","distance":4,"power_state":"Unoccupied","power_conflict_progress":[
                {"power":"Aisling Duval","progress":0.39}
              ]},
              {"name":"Empty","distance":8,"power_state":"Unoccupied"},
              {"name":"Owned","distance":1,"controlling_power":"Jerome Archer","power_state":"Exploited"}
            ]}
            """;
        using var handler = new RequestHandler(payload);
        using var http = new HttpClient(handler);
        IReadOnlyList<MiningSystemResult> contested = await new MiningSearchClient(http).FindSystemsAsync(
            new("Sol", 80, PowerState: "Contested")
        );
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "Unoccupied",
            body.RootElement.GetProperty("filters").GetProperty("power_state").GetProperty("value")[0].GetString()
        );
        MiningSystemResult alrai = Assert.Single(contested);
        Assert.Equal("Alrai Sector FG-X b1-6", alrai.System);
        Assert.Equal("", alrai.Power);
        Assert.Equal("Contested", alrai.PowerState);

        IReadOnlyList<MiningSystemResult> open = await new MiningSearchClient(http).FindSystemsAsync(
            new("Sol", 80, Objective: PowerplayPlan.Acquire)
        );
        Assert.Equal(4, open.Count);
        Assert.Contains(open, system => system.System == "Quiet" && system.PowerState == "Expansion");
        Assert.Contains(open, system => system.System == "Empty" && system.PowerState == "Unoccupied");
        Assert.Contains(open, system => system.System == "Owned" && system.PowerState == "Exploited");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    [InlineData(double.NaN)]
    public async Task InvalidRadiusIsRejectedBeforeSending(double radius)
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new MiningSearchClient(http).FindTradersAsync("Sol", "Raw", radius: radius)
        );
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task PlanetaryBodiesAskSpanshForLandableSystemsAndMagma()
    {
        using var handler = new RequestHandler(
            """{"results":[{"system_name":"HR 5098","name":"HR 5098 2","subtype":"High metal content world","reserve_level":"Pristine","gravity":2.532,"distance_to_arrival":294.25}]}"""
        );
        using var http = new HttpClient(handler);
        IReadOnlyList<MiningPlanetaryBody> bodies = await new MiningSearchClient(http).FindPlanetaryBodiesAsync(
            new MiningPlanetaryQuery(
                "Timbalderis",
                ["High metal content world", "Rocky body"],
                ["Iron Magma Lava Spout", "Silicate Magma Lava Spout"],
                "Pristine",
                150,
                PlanetaryMiningPlan.OtherPowers("Aisling Duval"),
                "Exploited"
            )
        );

        MiningPlanetaryBody body = Assert.Single(bodies);
        Assert.Equal("HR 5098", body.System);
        Assert.Equal("HR 5098 2", body.Body);
        Assert.Equal(2.532, body.Gravity);
        Assert.Equal(294.25, body.ArrivalLs);
        using var request = JsonDocument.Parse(handler.Body!);
        JsonElement filters = request.RootElement.GetProperty("filters");
        Assert.True(filters.GetProperty("is_landable").GetProperty("value").GetBoolean());
        Assert.False(filters.TryGetProperty("system_name", out _));
        Assert.Equal("Timbalderis", request.RootElement.GetProperty("reference_system").GetString());
        Assert.Equal(150, filters.GetProperty("distance").GetProperty("max").GetDouble());
        JsonElement powers = filters.GetProperty("system_controlling_power").GetProperty("value");
        Assert.Equal(11, powers.GetArrayLength());
        Assert.DoesNotContain(powers.EnumerateArray(), power => power.GetString() == "Aisling Duval");
        Assert.Contains(powers.EnumerateArray(), power => power.GetString() == "A. Lavigny-Duval");
        Assert.Equal("Exploited", filters.GetProperty("system_power_state").GetProperty("value")[0].GetString());
        Assert.Equal("Pristine", filters.GetProperty("reserve_level").GetProperty("value")[0].GetString());
        Assert.Equal(
            "Silicate Magma Lava Spout",
            filters.GetProperty("landmarks")[0].GetProperty("subtype")[1].GetString()
        );
    }

    [Fact]
    public async Task ExactSystemMarketScopeIsAppliedBeforeStationPagination()
    {
        using var handler = new RequestHandler();
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindMarketsAsync(new("Wille", "Platinum", false, Page: 2, SystemOnly: true));
        using var request = System.Text.Json.JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "Wille",
            request.RootElement.GetProperty("filters").GetProperty("system_name").GetProperty("value")[0].GetString()
        );
        Assert.Equal(2, request.RootElement.GetProperty("page").GetInt32());
    }

    private sealed class RequestHandler(string payload = "{\"results\":[]}") : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(payload) };
        }
    }

    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}
