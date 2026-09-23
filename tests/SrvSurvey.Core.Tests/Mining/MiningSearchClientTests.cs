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
    public async Task NearestMaterialTradersComeFromArdentAndDropStationsOutsideTheRadius()
    {
        using var http = new HttpClient(
            new Handler(
                """[{"systemName":"Sirius","stationName":"Patterson Enterprise","stationType":"Coriolis","distanceToArrival":955,"maxLandingPadSize":3,"marketId":12,"distance":9,"updatedAt":"2026-09-21T00:00:00Z"},{"systemName":"Far","stationName":"Distant","stationType":"Outpost","distance":400,"marketId":13}]"""
            )
        );
        (IReadOnlyList<MiningMarketResult> traders, string source) = await new MiningSearchClient(
            http
        ).FindTradersPreferringArdentAsync("Sol", "Encoded", radius: 75);
        MiningMarketResult trader = Assert.Single(traders);
        Assert.Equal("Ardent", source);
        Assert.Equal("Patterson Enterprise", trader.Station);
        Assert.Equal("Large", trader.QuotedPad);
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
    public void MonaziteUsesTheMeritMinerAbbreviation()
    {
        Assert.Equal("MON", MiningCommodityCode.Abbreviate("Monazite"));
        Assert.Equal("ALE", MiningCommodityCode.Abbreviate("Alexandrite"));
        Assert.Equal("LHY", MiningCommodityCode.Abbreviate("Lithium Hydroxide"));
        Assert.Equal("MNL", MiningCommodityCode.Abbreviate("Methanol Monohydrate Crystals"));
        Assert.Equal("JAD", MiningCommodityCode.Abbreviate("Jadeite"));
        Assert.Equal("IDT", MiningCommodityCode.Abbreviate("Indite"));
        Assert.Equal("BIS", MiningCommodityCode.Abbreviate("Bismuth"));
    }

    [Fact]
    public async Task TargetStationQuotesKeepPriceAndDemand()
    {
        using var handler = new RequestHandler(
            """{"results":[{"system_name":"Shui Wei Sector EQ-Y b0","name":"Birkhoff Platform","type":"Orbis Starport","distance_to_arrival":5,"medium_pads":1,"market":[{"commodity":"Monazite","sell_price":420153,"demand":31},{"commodity":"Alexandrite","sell_price":228919,"demand":31}]}]}"""
        );
        using var http = new HttpClient(handler);
        IReadOnlyList<MiningSellQuote> quotes = await new MiningSearchClient(http).FindSellQuotesAsync(
            "Sol",
            200,
            ["Shui Wei Sector EQ-Y b0"],
            ["Monazite", "Alexandrite"]
        );

        Assert.Equal(2, quotes.Count);
        Assert.Equal("Birkhoff Platform", quotes[0].Station);
        Assert.Equal("Medium", quotes[0].Pad);
        Assert.Equal(420153, quotes[0].Price);
        Assert.Equal(31, quotes[0].Demand);
        Assert.False(
            JsonDocument.Parse(handler.Body!).RootElement.GetProperty("filters").TryGetProperty("power_state", out _)
        );
    }

    [Fact]
    public async Task PlanetaryBodiesAskSpanshForLandableSystemsAndMagma()
    {
        using var handler = new RequestHandler(
            """{"results":[{"system_name":"HR 5098","name":"HR 5098 2","subtype":"High metal content world","reserve_level":"Pristine","gravity":2.532,"distance_to_arrival":294.25,"landmarks":[{"subtype":"Iron Magma Lava Spout"},{"subtype":"Iron Magma Lava Spout"}]}]}"""
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
                "Exploited",
                Page: 2
            )
        );

        MiningPlanetaryBody body = Assert.Single(bodies);
        Assert.Equal("HR 5098", body.System);
        Assert.Equal("HR 5098 2", body.Body);
        Assert.Equal(2.532, body.Gravity);
        Assert.Equal(294.25, body.ArrivalLs);
        Assert.Equal(["Iron Magma Lava Spout"], body.Landmarks);
        using var request = JsonDocument.Parse(handler.Body!);
        JsonElement filters = request.RootElement.GetProperty("filters");
        Assert.True(filters.GetProperty("is_landable").GetProperty("value").GetBoolean());
        Assert.False(filters.TryGetProperty("system_name", out _));
        Assert.Equal("Timbalderis", request.RootElement.GetProperty("reference_system").GetString());
        Assert.Equal(2, request.RootElement.GetProperty("page").GetInt32());
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
    public async Task ArdentFailureFallsBackToSpanshStationPrices()
    {
        using var http = new HttpClient(new ArdentThenSpanshHandler());
        (IReadOnlyList<MiningMarketResult> markets, string source) = await new MiningSearchClient(
            http
        ).FindMarketsPreferringArdentAsync(new("Sol", "Platinum", false, MaximumAge: TimeSpan.FromDays(30)));
        MiningMarketResult market = Assert.Single(markets);
        Assert.Equal("Spansh fallback", source);
        Assert.Equal("Birkhoff Platform", market.Station);
        Assert.Equal(420153, market.Price);
        Assert.Equal(31, market.Demand);
    }

    [Fact]
    public async Task ArdentQuotesAvoidSpanshAndEmptyArdentResultsUseTheFallback()
    {
        using var handler = new ArdentThenSpanshHandler { ArdentResponse = "quote" };
        using var http = new HttpClient(handler);
        var client = new MiningSearchClient(http);
        var query = new MiningMarketQuery("Sol", "Platinum", false, MaximumAge: TimeSpan.FromDays(30));

        (IReadOnlyList<MiningMarketResult> quotes, string source) = await client.FindMarketsPreferringArdentAsync(
            query
        );
        Assert.Equal("Ardent", source);
        Assert.Equal(900_000, Assert.Single(quotes).Price);
        Assert.Equal(0, handler.SpanshRequests);

        handler.ArdentResponse = "empty";
        (quotes, source) = await client.FindMarketsPreferringArdentAsync(query);
        Assert.Equal("Spansh fallback", source);
        Assert.Equal(420_153, Assert.Single(quotes).Price);
        Assert.Equal(1, handler.SpanshRequests);
    }

    private sealed class ArdentThenSpanshHandler : HttpMessageHandler
    {
        public string ArdentResponse { get; set; } = "failure";
        public int SpanshRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri?.Host.Contains("ardent", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (ArdentResponse == "empty")
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }
                    );
                }

                if (ArdentResponse == "quote")
                {
                    return Task.FromResult(
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(
                                $$"""[{"systemName":"Sol","stationName":"Ardent Port","stationType":"Coriolis","maxLandingPadSize":3,"sellPrice":900000,"demand":1000,"stock":0,"updatedAt":"{{DateTimeOffset.UtcNow:O}}","commodityName":"Platinum"}]"""
                            ),
                        }
                    );
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            SpanshRequests++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"results":[{"system_name":"Sol","name":"Birkhoff Platform","type":"Orbis Starport","medium_pads":1,"market_updated_at":"{{DateTimeOffset.UtcNow:O}}","market":[{"commodity":"Platinum","sell_price":420153,"demand":31}]}]}"""
                    ),
                }
            );
        }
    }

    [Fact]
    public async Task ExactSystemMarketScopeIsAppliedBeforeStationPagination()
    {
        using var handler = new RequestHandler("[]");
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindMarketsAsync(new("Wille", "Platinum", false, Page: 2, SystemOnly: true));
        Assert.Contains("/v2/system/name/Wille/commodity/name/", handler.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GalaxyMarketsOmitCarriersUntilTheSearchExcludesThem()
    {
        using var included = new RequestHandler("[]");
        using var http = new HttpClient(included);
        await new MiningSearchClient(http).FindMarketsAsync(new("Sol", "Platinum", false, GalaxyWide: true));
        Assert.DoesNotContain("fleetCarriers", included.Uri!.Query, StringComparison.Ordinal);

        using var excluded = new RequestHandler("[]");
        using var excluding = new HttpClient(excluded);
        await new MiningSearchClient(excluding).FindMarketsAsync(
            new("Sol", "Platinum", false, GalaxyWide: true, ExcludeCarriers: true)
        );
        Assert.Contains("fleetCarriers=false", excluded.Uri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AveragePricesCacheAndAFailedCatalogueLeavesMarksUnavailable()
    {
        var logs = new List<string>();
        using var http = new HttpClient(new RequestHandler("""[{"commodityName":"Platinum","avgSellPrice":1000}]"""));
        var client = new MiningSearchClient(http) { DiagnosticLog = logs.Add };
        IReadOnlyDictionary<string, long> prices = await client.AverageSellPricesAsync();
        Assert.Equal(1000, prices["Platinum"]);
        Assert.False(client.PriceMarksUnavailable);
        Assert.Same(prices, await client.AverageSellPricesAsync());

        using var failed = new HttpClient(new StatusHandler(System.Net.HttpStatusCode.TooManyRequests));
        var failing = new MiningSearchClient(failed)
        {
            DiagnosticLog = _ => throw new InvalidOperationException("log sink failed"),
        };
        Assert.Empty(await failing.AverageSellPricesAsync());
        Assert.True(failing.PriceMarksUnavailable);
        failing.FlushDiagnostics();
        failing.ResetDiagnostics();
        Assert.False(failing.PriceMarksUnavailable);
    }

    [Fact]
    public async Task ArdentImportNamesUseTheSharedCommodityIdentity()
    {
        string updated = DateTimeOffset.UtcNow.ToString("O");
        using var handler = new RequestHandler(
            $$"""[{"systemName":"Sol","stationName":"Hub","stationType":"Coriolis","sellPrice":800000,"demand":100,"updatedAt":"{{updated}}","commodityName":"periclasedunite"},{"systemName":"Sol","stationName":"Hub","stationType":"Coriolis","sellPrice":700000,"demand":100,"updatedAt":"{{updated}}","commodityName":"lowtemperaturediamond"},{"systemName":"Sol","stationName":"Hub","stationType":"Coriolis","sellPrice":600000,"demand":100,"updatedAt":"{{updated}}","commodityName":"diamond"}]"""
        );
        using var http = new HttpClient(handler);

        IReadOnlyList<MiningMarketResult> imports = await new MiningSearchClient(http).FindSystemImportsAsync(
            "Sol",
            TimeSpan.FromDays(2)
        );

        Assert.Equal("Periclase Dunite", imports[0].Commodity);
        Assert.Equal("Low Temperature Diamonds", imports[1].Commodity);
        Assert.Equal("Diamond", imports[2].Commodity);
    }

    [Fact]
    public async Task DailyPriceReportUsesTheCommodityNameQueriedFromArdent()
    {
        using var handler = new RequestHandler(
            """[{"commodityName":"periclase dunite","avgSellPrice":634136,"maxSellPrice":1038104},{"commodityName":"periclasedunite","avgSellPrice":207564,"maxSellPrice":1038104},{"commodityName":"diamond","avgSellPrice":134784,"maxSellPrice":720648},{"commodityName":"lowtemperaturediamond","avgSellPrice":130184,"maxSellPrice":384562}]"""
        );
        using var http = new HttpClient(handler);

        IReadOnlyDictionary<string, MiningCommodityPriceSummary> report = await new MiningSearchClient(
            http
        ).CommodityPriceReportAsync();

        Assert.Equal(207_564, report["Periclase Dunite"].AverageSellPrice);
        Assert.Equal(134_784, report["Diamond"].AverageSellPrice);
        Assert.Equal(130_184, report["Low Temperature Diamonds"].AverageSellPrice);
    }

    [Fact]
    public async Task CurrentMarketQuotesReplaceStaleAggregatePricesForSurfaceMaterials()
    {
        using var handler = new CurrentSurfacePriceHandler();
        using var http = new HttpClient(handler);
        var client = new MiningSearchClient(http);

        IReadOnlyDictionary<string, MiningCommodityPriceSummary> prices = await client.CommodityPriceReportAsync();

        Assert.Equal(195_083, prices["Monazite"].AverageSellPrice);
        Assert.Equal(865_908, prices["Monazite"].MaximumSellPrice);
        Assert.Equal(129_763, prices["Periclase Dunite"].AverageSellPrice);
        Assert.Equal(944_252, prices["Periclase Dunite"].MaximumSellPrice);
        Assert.Equal(2, client.LiveSurfaceQuoteCount);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero), client.CommodityLiveQuoteUpdatedAt);
    }

    [Fact]
    public async Task CommodityReportRefreshesWhenArdentFinishesANewReport()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
        var handler = new CommodityReportHandler();
        using var http = new HttpClient(handler);
        var client = new MiningSearchClient(http, clock);

        IReadOnlyDictionary<string, MiningCommodityPriceSummary> first = await client.CommodityPriceReportAsync();
        Assert.Equal(136_783, first["Diamond"].AverageSellPrice);
        Assert.Equal(720_648, first["Diamond"].MaximumSellPrice);
        Assert.Equal(1, handler.ReportRequests);
        Assert.Equal(1, handler.MarkerRequests);
        Assert.Equal(clock.GetUtcNow(), client.CommodityReportFetchedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 9, 19, 53, TimeSpan.Zero), client.CommodityReportSourceUpdatedAt);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Same(first, await client.CommodityPriceReportAsync());
        Assert.Equal(1, handler.ReportRequests);
        Assert.Equal(2, handler.MarkerRequests);

        handler.MarkerTimestamp = "2026-09-23T14:00:00Z";
        handler.AverageSellPrice = 200_000;
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Same(first, await client.CommodityPriceReportAsync());
        Assert.Equal(1, handler.ReportRequests);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(200_000, (await client.CommodityPriceReportAsync())["Diamond"].AverageSellPrice);
        Assert.Equal(2, handler.ReportRequests);
    }

    [Fact]
    public async Task CommodityReportSurvivesRestartWithItsSourceTimestamp()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-Ardent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MiningCommodityPriceReportStore(directory);
            using var handler = new CommodityReportHandler();
            using var http = new HttpClient(handler);
            var first = new MiningSearchClient(http, commodityReportStore: store);

            await first.CommodityPriceReportAsync();

            Assert.True(File.Exists(store.Path));
            var reloaded = new MiningSearchClient(http, commodityReportStore: store);
            Assert.Equal(136_783, reloaded.CachedCommodityPriceReport!["Diamond"].AverageSellPrice);
            Assert.Equal(first.CommodityReportFetchedAt, reloaded.CommodityReportFetchedAt);
            Assert.Equal(first.CommodityReportSourceUpdatedAt, reloaded.CommodityReportSourceUpdatedAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task WhiteDwarfHostLookupFollowsPlanetParentAndKeepsLargeBodyIdsExact()
    {
        using var http = new HttpClient(
            new Handler(
                """{"results":[{"id64":9007199254740993,"parents":[{"id64":123,"type":"Star","subtype":"White Dwarf (DC) Star"}]}]}"""
            )
        );
        var client = new MiningSearchClient(http);
        MiningPlanetaryBody moon = new(
            "Sirius",
            "Sirius B 1 a",
            "Rocky body",
            "",
            0.1,
            100,
            Parents: [new MiningBodyParent(9007199254740993, "Planet", "Rocky body")]
        );
        MiningPlanetaryBody other = moon with
        {
            Body = "Sirius A 1",
            Parents = [new MiningBodyParent(456, "Star", "A (Blue-White) Star")],
        };

        IReadOnlySet<string> hosted = await client.FindWhiteDwarfHostedBodiesAsync("Sirius", [moon, other]);

        Assert.Contains("Sirius\u001fSirius B 1 a", hosted);
        Assert.DoesNotContain("Sirius\u001fSirius A 1", hosted);
    }

    [Fact]
    public async Task PericlaseQueryUsesVolcanismTypeInsteadOfLavaSpoutLandmarks()
    {
        using var handler = new RequestHandler(
            """{"results":[{"system_name":"Sirius","name":"Sirius B 1","subtype":"Rocky body","volcanism_type":"Minor Metallic Magma"}]}"""
        );
        using var http = new HttpClient(handler);
        PlanetaryBodyCriteria criteria = PlanetaryMiningPlan.For(["Periclase Dunite"])!;

        MiningPlanetaryBody body = Assert.Single(
            await new MiningSearchClient(http).FindPlanetaryBodiesAsync(
                new MiningPlanetaryQuery(
                    "Sirius",
                    criteria.BodySubtypes,
                    criteria.LandmarkSubtypes,
                    VolcanismTypes: criteria.VolcanismTypes
                )
            )
        );

        Assert.Equal("Minor Metallic Magma", body.VolcanismType);
        using var request = JsonDocument.Parse(handler.Body!);
        JsonElement filters = request.RootElement.GetProperty("filters");
        Assert.False(filters.TryGetProperty("landmarks", out _));
        Assert.Equal(3, filters.GetProperty("volcanism_type").GetProperty("value").GetArrayLength());
    }

    [Fact]
    public async Task AFullBodyPageAsksForAnotherAndNamedSystemsStayInTheFilter()
    {
        using var handler = new PageHandler();
        using var http = new HttpClient(handler);
        var client = new MiningSearchClient(http);
        MiningRingPage first = await client.FindRingPageAsync(new("Timbalderis", "", "All", 200));
        Assert.True(first.HasMore);
        Assert.Equal(100, handler.Bodies);

        IReadOnlyList<MiningRing> rings = await client.FindRingsForSystemsAsync(
            new("Timbalderis", "", "All", 200),
            ["LHS 3802"]
        );
        Assert.Equal(2, handler.Pages);
        Assert.Contains("LHS 3802", handler.BodiesRequest, StringComparison.Ordinal);
        Assert.Contains("Platinum", rings[0].Hotspots.Keys);
    }

    [Fact]
    public void FartherHighPricesStayInRangeUntilThirtyAreKept()
    {
        Assert.True(PowerplayMeritRank.MorePricesCanRank(3, 30, 0, 100));
        Assert.Equal(0, PowerplayMeritRank.WeakestRankedPrice([900, 800], 30));
        Assert.Equal(700, PowerplayMeritRank.WeakestRankedPrice([900, 800, 700, 100], 3));
        Assert.False(PowerplayMeritRank.MorePricesCanRank(3, 3, 700, 650));
        Assert.True(PowerplayMeritRank.MorePricesCanRank(3, 3, 700, 700));
    }

    [Fact]
    public async Task NamedSystemLookupKeepsEveryPowerAndTheFactionState()
    {
        using var handler = new RequestHandler(
            """
            {"results":[{"name":"LHS 3802","distance":79.6,"controlling_power":"Yuri Grom","power_state":"Fortified","controlling_minor_faction_state":"Boom","power":["Yuri Grom","Aisling Duval","Denton Patreus"]}]}
            """
        );
        using var http = new HttpClient(handler);
        MiningSystemResult system = Assert.Single(
            await new MiningSearchClient(http).FindSystemsByNameAsync("Timbalderis", ["LHS 3802"])
        );

        Assert.Equal("Boom", system.State);
        Assert.Equal("Fortified", system.PowerState);
        Assert.Equal(["Yuri Grom", "Aisling Duval", "Denton Patreus"], system.NearbyPowers);
        Assert.Contains("LHS 3802", handler.Body, StringComparison.Ordinal);
        Assert.Contains("systems/search", handler.Uri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RingSearchCanNameSeveralMinerals()
    {
        using var handler = new RequestHandler("""{"results":[]}""");
        using var http = new HttpClient(handler);
        await new MiningSearchClient(http).FindRingsAsync(new("Sol", "", "All", 20, Minerals: ["Platinum", "Painite"]));
        Assert.Contains("Painite", handler.Body, StringComparison.Ordinal);
        Assert.Contains("Platinum", handler.Body, StringComparison.Ordinal);
    }

    private sealed class PageHandler : HttpMessageHandler
    {
        public int Pages { get; private set; }
        public int Bodies { get; private set; }
        public string BodiesRequest { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Pages++;
            BodiesRequest = body;
            using var document = JsonDocument.Parse(body);
            int page = document.RootElement.GetProperty("page").GetInt32();
            if (page == 0 && !body.Contains("LHS 3802", StringComparison.Ordinal))
            {
                Bodies = 100;
                string results = string.Join(
                    ',',
                    Enumerable
                        .Range(0, 100)
                        .Select(index =>
                            "{\"name\":\"Body " + index + "\",\"system_name\":\"Near\",\"distance\":1,\"rings\":[]}"
                        )
                );
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"results\":[" + results + "]}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"results":[{"name":"LHS 3802 A","system_name":"LHS 3802","distance":79.6,"rings":[{"name":"A Ring","type":"Rocky","signals":[{"name":"Platinum","count":1}]}]}]}
                    """
                ),
            };
        }
    }

    private sealed class StatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("") });
    }

    private sealed class RequestHandler(string payload = "{\"results\":[]}") : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestCount++;
            Uri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(payload) };
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset now = now;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class CommodityReportHandler : HttpMessageHandler
    {
        public int ReportRequests { get; private set; }
        public int MarkerRequests { get; private set; }
        public long AverageSellPrice { get; set; } = 136_783;
        public string MarkerTimestamp { get; set; } = "2026-09-07T09:19:53Z";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string payload;
            if (request.RequestUri?.AbsolutePath.EndsWith("/commodities", StringComparison.Ordinal) == true)
            {
                ReportRequests++;
                payload =
                    $$"""[{"commodityName":"Diamond","avgSellPrice":{{AverageSellPrice}},"maxSellPrice":720648}]""";
            }
            else if (request.RequestUri?.AbsolutePath.EndsWith("/imports", StringComparison.Ordinal) == true)
            {
                payload = "[]";
            }
            else
            {
                MarkerRequests++;
                payload = $$"""{"timestamp":"{{MarkerTimestamp}}"}""";
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
        }
    }

    private sealed class CurrentSurfacePriceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri?.AbsolutePath ?? "";
            string payload = path switch
            {
                "/v2/commodities" =>
                    """[{"commodityName":"monazite","avgSellPrice":267777,"maxSellPrice":892835},{"commodityName":"periclasedunite","avgSellPrice":207564,"maxSellPrice":1038104}]""",
                "/v2/commodity/name/monazite/imports" =>
                    """[{"meanPrice":195083,"sellPrice":865908,"updatedAt":"2026-09-23T10:00:00Z"}]""",
                "/v2/commodity/name/periclasedunite/imports" =>
                    """[{"meanPrice":129763,"sellPrice":944252,"updatedAt":"2026-09-23T09:00:00Z"}]""",
                _ when path.EndsWith("/imports", StringComparison.Ordinal) => "[]",
                _ => """{"timestamp":"2026-09-07T09:19:53Z"}""",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
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
