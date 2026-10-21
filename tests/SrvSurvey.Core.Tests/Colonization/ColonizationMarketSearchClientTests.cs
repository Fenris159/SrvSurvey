using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationMarketSearchClientTests
{
    private static readonly string[] CombinedCommodities = ["Steel", "Water"];

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task RequestsOnlySelectedMarketProviders(bool useSpansh, bool useArdent, bool localOnly)
    {
        var hosts = new ConcurrentBag<string>();
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    string host = request.RequestUri!.Host;
                    hosts.Add(host);
                    if (host == "spansh.co.uk")
                    {
                        Assert.True(useSpansh);
                        return Task.FromResult(SpanshResponse([Spansh(1, "Steel", 100)], 1));
                    }
                    Assert.True(useArdent);
                    return Task.FromResult(Json(new JsonArray(Ardent(2, "steel", 100))));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                UseSpansh = useSpansh,
                UseArdent = useArdent,
                LocalOnly = localOnly,
            }
        );
        Assert.Equal(useSpansh, hosts.Contains("spansh.co.uk"));
        Assert.Equal(useArdent, hosts.Contains("api.ardent-insight.com"));
        Assert.Equal((useSpansh ? 1 : 0) + (useArdent ? 1 : 0), result.Quotes.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RejectsNoDataSourcesBeforeMakingRequests()
    {
        using var http = new HttpClient(
            new Handler((_, _) => throw new InvalidOperationException("No request expected."))
        );
        using var client = new ColonizationMarketSearchClient(http);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SearchAsync(Query() with { UseSpansh = false, UseArdent = false })
        );
    }

    [Fact]
    public async Task OversizedSpanshPageRetriesInSmallerBatchesWithoutDiscardingEarlierResults()
    {
        int posts = 0;
        int recalls = 0;
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.RequestUri!.Host != "spansh.co.uk")
                    {
                        return Json(new JsonArray());
                    }
                    if (request.Method == HttpMethod.Post)
                    {
                        posts++;
                        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                        int size = body.RootElement.GetProperty("size").GetInt32();
                        int page = body.RootElement.GetProperty("page").GetInt32();
                        if (posts == 1)
                        {
                            Assert.Equal(100, size);
                            return Json(
                                new JsonObject
                                {
                                    ["count"] = 101,
                                    ["search_reference"] = "7b9067a9-6ff1-4fa9-bb35-5767867365a0",
                                    ["results"] = new JsonArray(
                                        Enumerable
                                            .Range(1, 100)
                                            .Select(id => (JsonNode)Spansh(id, "Steel", 100))
                                            .ToArray()
                                    ),
                                }
                            );
                        }
                        Assert.Equal(50, size);
                        Assert.Equal(2, page);
                        return SpanshResponse([Spansh(101, "Steel", 100)], 101);
                    }
                    recalls++;
                    HttpResponseMessage oversized = SpanshResponse([], 101);
                    oversized.Content.Headers.ContentLength = 8 * 1024 * 1024 + 1;
                    return oversized;
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Equal(101, result.Quotes.Count);
        Assert.Empty(result.Warnings);
        Assert.Equal(2, posts);
        Assert.Equal(1, recalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task OutdatedLocalMarketsAreExplicitEstimatesAndLocalExclusionIgnoresAge(bool include, bool localOnly)
    {
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    if (request.Method != HttpMethod.Post)
                    {
                        return Task.FromResult(Json(new JsonArray()));
                    }
                    JsonObject local = Spansh(1, "Steel", 200);
                    local["distance"] = 0;
                    local["market_updated_at"] = DateTimeOffset.UtcNow.AddDays(-103).ToString("O");
                    JsonObject nearby = Spansh(2, "Steel", 200);
                    nearby["system_name"] = "Nearby";
                    nearby["market_updated_at"] = DateTimeOffset.UtcNow.AddDays(-103).ToString("O");
                    return Task.FromResult(SpanshResponse([local, nearby], 2));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                MaximumAgeDays = 3,
                IncludeOutdatedLocalMarkets = include,
                LocalOnly = localOnly,
            }
        );
        if (include || localOnly)
        {
            Assert.Equal(1, Assert.Single(result.Quotes).MarketId);
            Assert.Contains(
                result.Warnings,
                warning => warning.Contains("outdated", StringComparison.OrdinalIgnoreCase)
            );
        }
        else
        {
            Assert.Empty(result.Quotes);
        }
    }

    [Fact]
    public async Task ArdentOutdatedLocalResultsHaveNoExtraAgeCutoffWhileNearbyResultsRemainAgeLimited()
    {
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    string path = request.RequestUri!.AbsolutePath;
                    if (path.Contains("/system/name/Sol/commodities", StringComparison.Ordinal))
                    {
                        JsonObject oldLocal = Ardent(1, "steel", 100);
                        oldLocal["updatedAt"] = DateTimeOffset.UtcNow.AddDays(-400).ToString("O");
                        return Task.FromResult(
                            Json(path.EndsWith("/exports", StringComparison.Ordinal) ? [] : new JsonArray(oldLocal))
                        );
                    }
                    Assert.Contains("maxDaysAgo=3", request.RequestUri.Query);
                    JsonObject oldNearby = Ardent(2, "steel", 500);
                    oldNearby["systemName"] = "Nearby";
                    oldNearby["updatedAt"] = DateTimeOffset.UtcNow.AddDays(-400).ToString("O");
                    return Task.FromResult(Json(new JsonArray(oldNearby)));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                UseSpansh = false,
                IncludeOutdatedLocalMarkets = true,
                MaximumAgeDays = 3,
            }
        );
        ColonizationMarketQuote quote = Assert.Single(result.Quotes);
        Assert.Equal(1, quote.MarketId);
        Assert.Equal(0, quote.Distance);
        Assert.Contains("outdated", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task LocalOnlyQueriesBothProvidersWithoutNearbyCommodityRequestsOrNeighboringSystemStock()
    {
        int ardentRequests = 0;
        int spanshRequests = 0;
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        spanshRequests++;
                        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                        Assert.Equal(
                            1,
                            body.RootElement.GetProperty("filters")
                                .GetProperty("distance")
                                .GetProperty("max")
                                .GetInt32()
                        );
                        JsonObject local = Spansh(1, "Steel", 100);
                        local["distance"] = 0;
                        local["market_updated_at"] = DateTimeOffset.UtcNow.AddDays(-401).ToString("O");
                        JsonObject neighbor = Spansh(2, "Steel", 1000);
                        neighbor["system_name"] = "Neighbor";
                        neighbor["distance"] = 0.1;
                        return SpanshResponse([local, neighbor], 2);
                    }
                    ardentRequests++;
                    Assert.EndsWith("/system/name/Sol/commodities", request.RequestUri!.AbsolutePath);
                    Assert.Empty(request.RequestUri.Query);
                    JsonObject nearby = Ardent(3, "steel", 1000);
                    nearby["systemName"] = "Neighbor";
                    JsonObject localArdent = Ardent(1, "steel", 100);
                    localArdent["systemName"] = "sOL";
                    localArdent["updatedAt"] = DateTimeOffset.UtcNow.AddDays(-400).ToString("O");
                    return Json(new JsonArray(localArdent, nearby));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                ReferenceSystem = " Sol ",
                MaximumAgeDays = 3,
                LocalOnly = true,
            }
        );
        Assert.Equal(1, spanshRequests);
        Assert.Equal(1, ardentRequests);
        ColonizationMarketQuote quote = Assert.Single(result.Quotes);
        Assert.Equal(1, quote.MarketId);
        Assert.Equal(100, quote.Stock);
        Assert.Equal(0, quote.Distance);
        Assert.Contains("outdated", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("null", false)]
    [InlineData("\"unknown\"", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData(null, true)]
    public async Task UnknownSpanshCountsKeepPagingUntilAShortPage(string? count, bool initialCountKnown)
    {
        int pages = 0;
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    if (request.RequestUri!.Host != "spansh.co.uk")
                    {
                        return Task.FromResult(Json(new JsonArray()));
                    }
                    int page = pages++;
                    var envelope = new JsonObject
                    {
                        ["search_reference"] = "7b9067a9-6ff1-4fa9-bb35-5767867365a0",
                        ["results"] = new JsonArray(
                            Enumerable
                                .Range(page * 100 + 1, page < 2 ? 100 : 1)
                                .Select(id => (JsonNode)Spansh(id, "Steel", 100))
                                .ToArray()
                        ),
                    };
                    if (page == 0 && initialCountKnown)
                    {
                        envelope["count"] = 201;
                    }
                    else if (count is not null)
                    {
                        envelope["count"] = JsonNode.Parse(count);
                    }
                    return Task.FromResult(Json(envelope));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Equal(3, pages);
        Assert.Equal(201, result.Quotes.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task UsesSavedSpanshReferenceForSubsequentPages()
    {
        int posts = 0;
        int recalls = 0;
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        posts++;
                        return Task.FromResult(
                            Json(
                                new JsonObject
                                {
                                    ["count"] = 101,
                                    ["search_reference"] = "7b9067a9-6ff1-4fa9-bb35-5767867365a0",
                                    ["results"] = new JsonArray(
                                        Enumerable
                                            .Range(1, 100)
                                            .Select(id => (JsonNode)Spansh(id, "Steel", 100))
                                            .ToArray()
                                    ),
                                }
                            )
                        );
                    }
                    if (request.RequestUri!.Host == "spansh.co.uk")
                    {
                        recalls++;
                        Assert.Equal(
                            "/api/stations/search/recall/7b9067a9-6ff1-4fa9-bb35-5767867365a0/1",
                            request.RequestUri.AbsolutePath
                        );
                        return Task.FromResult(SpanshResponse([Spansh(101, "Steel", 100)], 101));
                    }
                    return Task.FromResult(Json(new JsonArray()));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Equal(1, posts);
        Assert.Equal(1, recalls);
        Assert.Equal(101, result.Quotes.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task NearbyArdentRequestsOverlapWithAtMostThreeInFlight()
    {
        int active = 0;
        var concurrency = new ConcurrentBag<int>();
        int requests = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        return SpanshResponse([], 0);
                    }
                    if (request.RequestUri!.AbsolutePath.Contains("/commodities/", StringComparison.Ordinal))
                    {
                        return Json(new JsonArray());
                    }
                    int concurrent = Interlocked.Increment(ref active);
                    Interlocked.Increment(ref requests);
                    concurrency.Add(concurrent);
                    if (concurrent == 3)
                    {
                        started.TrySetResult();
                    }
                    await release.Task.WaitAsync(token);
                    Interlocked.Decrement(ref active);
                    return Json(new JsonArray(Ardent(1, "steel", 100)));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        Task<ColonizationMarketSearchResult> pending = client.SearchAsync(
            Query() with
            {
                Needs =
                [
                    new("steel", "Steel", 100),
                    new("water", "Water", 100),
                    new("copper", "Copper", 100),
                    new("aluminium", "Aluminium", 100),
                ],
            }
        );
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(3, Volatile.Read(ref active));
        }
        finally
        {
            release.TrySetResult();
        }
        ColonizationMarketSearchResult result = await pending;
        Assert.Equal(4, requests);
        Assert.Equal(3, concurrency.Max());
        Assert.Single(result.Quotes);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task NoNeedsDoesNotIssueUnfilteredProviderRequests()
    {
        using var http = new HttpClient(
            new Handler((_, _) => throw new InvalidOperationException("No request expected."))
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query() with { Needs = [] });
        Assert.Empty(result.Quotes);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RetrievesAllPagesBeyondTheOldHundredStationCutoff()
    {
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.Method != HttpMethod.Post)
                    {
                        return Json(new JsonArray());
                    }
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    int size = body.RootElement.GetProperty("size").GetInt32();
                    int page = body.RootElement.GetProperty("page").GetInt32();
                    int start = page * size;
                    return SpanshResponse(
                        Enumerable
                            .Range(start, Math.Min(size, 121 - start))
                            .Select(id => Spansh(id + 1, "Steel", 100))
                            .ToArray(),
                        121
                    );
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Equal(121, result.Quotes.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task BatchesCommodityNamesIntoOneAnyCommodityStationSearch()
    {
        int requests = 0;
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.Method != HttpMethod.Post)
                    {
                        return Json(new JsonArray());
                    }
                    requests++;
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    Assert.Equal(
                        CombinedCommodities,
                        body.RootElement.GetProperty("filters")
                            .GetProperty("marketplace")[0]
                            .GetProperty("commodity")
                            .GetProperty("value")
                            .EnumerateArray()
                            .Select(value => value.GetString())
                    );
                    return SpanshResponse([Spansh(1, "Steel", 100)], 1);
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        await client.SearchAsync(Query() with { Needs = [new("steel", "Steel", 100), new("water", "Water", 50)] });
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SearchesBothSourcesWithCurrentFiltersAndIncludesOriginMarkets()
    {
        var paths = new ConcurrentBag<string>();
        using var http = new HttpClient(
            new Handler(
                async (request, cancellationToken) =>
                {
                    paths.Add(request.RequestUri!.AbsoluteUri);
                    if (request.Method == HttpMethod.Post)
                    {
                        using var body = JsonDocument.Parse(
                            await request.Content!.ReadAsStringAsync(cancellationToken)
                        );
                        JsonElement root = body.RootElement;
                        Assert.Equal("Sol", root.GetProperty("reference_system").GetString());
                        Assert.Equal(100, root.GetProperty("size").GetInt32());
                        JsonElement filters = root.GetProperty("filters");
                        Assert.False(filters.TryGetProperty("selling_commodities", out _));
                        Assert.Equal(
                            "Steel",
                            filters
                                .GetProperty("marketplace")[0]
                                .GetProperty("commodity")
                                .GetProperty("value")[0]
                                .GetString()
                        );
                        Assert.Equal(40, filters.GetProperty("distance").GetProperty("max").GetInt32());
                        return SpanshResponse([Spansh(1, "Steel", 100)], 1);
                    }
                    Assert.Contains("maxDaysAgo=7", request.RequestUri.Query);
                    if (request.RequestUri.AbsolutePath.Contains("/commodities/", StringComparison.Ordinal))
                    {
                        return Json(new JsonArray(Ardent(2, "steel", 80)));
                    }
                    Assert.Contains("minVolume=1", request.RequestUri.Query);
                    Assert.Contains("maxDistance=40", request.RequestUri.Query);
                    Assert.Contains("sort=distance", request.RequestUri.Query);
                    JsonObject newest = Ardent(1, "steel", 50);
                    newest["updatedAt"] = DateTimeOffset.UtcNow.AddSeconds(1);
                    return Json(new JsonArray(newest));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Empty(result.Warnings);
        Assert.Equal(2, result.Quotes.Count);
        Assert.Equal(50, result.Quotes.Single(quote => quote.MarketId == 1).Stock);
        Assert.Equal("Ardent", result.Quotes.Single(quote => quote.MarketId == 1).Source);
        Assert.Equal(0, result.Quotes.Single(quote => quote.MarketId == 2).Distance);
        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public async Task PagesSpanshAndTranslatesCaseSensitiveNamesAndAliases()
    {
        int pages = 0;
        using var http = new HttpClient(
            new Handler(
                async (request, cancellationToken) =>
                {
                    if (request.Method != HttpMethod.Post)
                    {
                        return Json(new JsonArray());
                    }

                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                    JsonElement root = body.RootElement;
                    Assert.Equal(
                        "Microbial Furnaces",
                        root.GetProperty("filters")
                            .GetProperty("marketplace")[0]
                            .GetProperty("commodity")
                            .GetProperty("value")[0]
                            .GetString()
                    );
                    int page = root.GetProperty("page").GetInt32();
                    pages++;
                    return SpanshResponse(
                        Enumerable
                            .Range(page * 100, page == 0 ? 100 : 1)
                            .Select(id => Spansh(id + 1, "Microbial Furnaces", 200))
                            .ToArray(),
                        101
                    );
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                Needs = [new("heliostaticfurnaces", "Microbial furnaces", 100)],
            }
        );
        Assert.Equal(101, result.Quotes.Count);
        Assert.Equal(2, pages);
        Assert.All(result.Quotes, quote => Assert.Equal("heliostaticfurnaces", quote.Commodity));
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("io", false)]
    [InlineData("json", false)]
    [InlineData("oversized", false)]
    [InlineData("timeout", false)]
    [InlineData("http", true)]
    public async Task LocalArdentFailuresDoNotBlockNearbySearchesButRemainFatalForLocalOnlyChecks(
        string failure,
        bool localOnly
    )
    {
        int nearbyRequests = 0;
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    if (!request.RequestUri!.AbsolutePath.Contains("/nearby/", StringComparison.Ordinal))
                    {
                        Exception error = failure switch
                        {
                            "http" => new HttpRequestException("Offline"),
                            "io" => new IOException("Disconnected"),
                            "json" => new JsonException("Invalid JSON"),
                            "oversized" => new InvalidDataException("Response exceeded its limit"),
                            _ => new OperationCanceledException("Request timeout"),
                        };
                        return Task.FromException<HttpResponseMessage>(error);
                    }
                    nearbyRequests++;
                    JsonObject nearby = Ardent(2, "steel", 100);
                    nearby["systemName"] = "Nearby";
                    return Task.FromResult(Json(new JsonArray(nearby)));
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketQuery query = Query() with
        {
            UseSpansh = false,
            IncludeOutdatedLocalMarkets = true,
            LocalOnly = localOnly,
        };
        if (localOnly)
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(query));
            Assert.Equal(0, nearbyRequests);
        }
        else
        {
            ColonizationMarketSearchResult result = await client.SearchAsync(query);
            Assert.Equal("Nearby", Assert.Single(result.Quotes).System);
            Assert.Equal(1, nearbyRequests);
            Assert.Contains("local market data was unavailable", Assert.Single(result.Warnings));
        }
    }

    [Theory]
    [InlineData("Spansh")]
    [InlineData("Ardent")]
    public async Task RetainsOtherProviderResultsAndReportsFailure(string failingSource)
    {
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    bool spansh = request.Method == HttpMethod.Post;
                    if (spansh == (failingSource == "Spansh"))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
                    }

                    return Task.FromResult(
                        spansh
                            ? SpanshResponse([Spansh(1, "Steel", 100)], 1)
                            : Json(new JsonArray(Ardent(2, "steel", 80)))
                    );
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.NotEmpty(result.Quotes);
        Assert.Contains(failingSource, Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("invalid JSON")]
    public async Task RejectsCompleteProviderFailureInsteadOfReportingAValidEmptySearch(string response)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Text(response))));
        using var client = new ColonizationMarketSearchClient(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(Query()));
    }

    [Fact]
    public async Task AcceptsValidEmptyResultsWhileReportingTheOtherProviderFailure()
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Text("[]"))));
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Empty(result.Quotes);
        Assert.Contains("Spansh", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task AnEmptyPartialResponseCannotMasqueradeAsACompletedEmptySearch()
    {
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                    Task.FromResult(
                        request.Method == HttpMethod.Get
                        && request.RequestUri!.AbsolutePath.Contains("/commodities/", StringComparison.Ordinal)
                            ? Json(new JsonArray())
                            : new HttpResponseMessage(HttpStatusCode.BadRequest)
                    )
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SearchAsync(Query()));
    }

    [Theory]
    [InlineData("Spansh")]
    [InlineData("Ardent")]
    public async Task KeepsSuccessfulObservationsWhenALaterProviderRequestFails(string partialSource)
    {
        int calls = 0;
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                {
                    bool spansh = request.Method == HttpMethod.Post;
                    if (spansh != (partialSource == "Spansh") || ++calls > 1)
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                    }
                    return Task.FromResult(
                        spansh
                            ? SpanshResponse(
                                Enumerable.Range(1, 100).Select(id => Spansh(id, "Steel", 100)).ToArray(),
                                150
                            )
                            : Json(new JsonArray(Ardent(1, "steel", 100)))
                    );
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.NotEmpty(result.Quotes);
        Assert.All(result.Quotes, quote => Assert.Equal(partialSource, quote.Source));
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public async Task FiltersNonSellingStaleFutureOutOfRangeAndUnrelatedObservations()
    {
        JsonObject valid = Ardent(1, "steel", 100);
        var rows = new List<JsonObject> { valid };
        foreach (
            (string field, JsonNode? value) in new (string, JsonNode?)[]
            {
                ("stock", 0),
                ("buyPrice", 0),
                ("updatedAt", "invalid"),
                ("updatedAt", DateTimeOffset.UtcNow.AddDays(-10).ToString("O")),
                ("updatedAt", DateTimeOffset.UtcNow.AddHours(1).ToString("O")),
                ("distance", 41),
                ("distance", -1),
                ("systemName", ""),
                ("stationName", ""),
                ("commodityName", "water"),
                ("stock", "not a number"),
            }
        )
        {
            var invalid = (JsonObject)valid.DeepClone();
            invalid[field] = value;
            rows.Add(invalid);
        }
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                    Task.FromResult(
                        request.Method switch
                        {
                            var method when method == HttpMethod.Post => SpanshResponse([], 0),
                            _ when request.RequestUri!.AbsolutePath.Contains(
                                    "/commodities/",
                                    StringComparison.Ordinal
                                ) => Json(new JsonArray()),
                            _ => Json(new JsonArray(rows.Select(row => (JsonNode)row).ToArray())),
                        }
                    )
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(Query());
        Assert.Single(result.Quotes);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    [InlineData(1)]
    [InlineData(0)]
    public async Task ReadsPadSizesAndOnlyRelevantGoodsFromFullSpanshMarket(int pad)
    {
        JsonObject station = Spansh(1, "Steel", 100);
        station["large_pads"] = pad == 3 ? 1 : 0;
        station["medium_pads"] = pad == 2 ? 1 : 0;
        station["small_pads"] = pad == 1 ? 1 : 0;
        station["market"]!
            .AsArray()
            .Add(
                new JsonObject
                {
                    ["commodity"] = "Tea",
                    ["supply"] = 100,
                    ["buy_price"] = 50,
                }
            );
        using var http = new HttpClient(
            new Handler(
                (request, _) =>
                    Task.FromResult(
                        request.Method == HttpMethod.Post ? SpanshResponse([station], 1) : Json(new JsonArray())
                    )
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketQuote quote = Assert.Single((await client.SearchAsync(Query())).Quotes);
        Assert.Equal(pad, quote.PadSize);
        Assert.Equal("steel", quote.Commodity);
    }

    [Fact]
    public async Task WarnsWhenResultCapsAreReached()
    {
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    if (request.Method == HttpMethod.Post)
                    {
                        using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                        int page = body.RootElement.GetProperty("page").GetInt32();
                        return SpanshResponse(
                            Enumerable.Range(page * 100 + 1, 100).Select(id => Spansh(id, "Steel", 100)).ToArray(),
                            12000
                        );
                    }
                    return request.Method switch
                    {
                        _ when request.RequestUri!.AbsolutePath.Contains("/commodities/", StringComparison.Ordinal) =>
                            Json(new JsonArray()),
                        _ => Json(
                            new JsonArray(
                                Enumerable.Range(1, 1000).Select(id => (JsonNode)Ardent(id, "steel", 100)).ToArray()
                            )
                        ),
                    };
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        ColonizationMarketSearchResult result = await client.SearchAsync(
            Query() with
            {
                Needs = [new("steel", "Steel", 100), new("water", "Water", 50)],
            }
        );
        Assert.Equal(10000, result.Quotes.Count);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(
            result.Warnings,
            warning => warning.Contains("Spansh reached its result limit", StringComparison.Ordinal)
        );
        Assert.Contains(result.Warnings, warning => warning.Contains("Ardent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartsBothProvidersConcurrentlyAndPropagatesCallerCancellation()
    {
        var calls = new ConcurrentBag<string>();
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(
            new Handler(
                async (request, token) =>
                {
                    calls.Add(request.RequestUri!.Host);
                    await Task.Delay(Timeout.Infinite, token);
                    return Json(new JsonArray());
                }
            )
        );
        using var client = new ColonizationMarketSearchClient(http);
        Task<ColonizationMarketSearchResult> pending = client.SearchAsync(Query(), cancellation.Token);
        Assert.Equal(2, calls.Count);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.SearchAsync(null!));
    }

    private static ColonizationMarketQuery Query() => new("Sol", 40, [new("steel", "Steel", 100)]);

    private static JsonObject Ardent(long id, string commodity, long stock) =>
        new()
        {
            ["systemName"] = "Sol",
            ["stationName"] = "Port " + id,
            ["marketId"] = id,
            ["stationType"] = "Coriolis Starport",
            ["maxLandingPadSize"] = 3,
            ["distance"] = 12,
            ["distanceToArrival"] = 100,
            ["commodityName"] = commodity,
            ["stock"] = stock,
            ["buyPrice"] = 500,
            ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };

    private static JsonObject Spansh(long id, string commodity, long stock) =>
        new()
        {
            ["system_name"] = "Sol",
            ["name"] = "Port " + id,
            ["market_id"] = id,
            ["type"] = "Coriolis Starport",
            ["large_pads"] = 1,
            ["distance"] = 12,
            ["distance_to_arrival"] = 100,
            ["market_updated_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["market"] = new JsonArray(
                new JsonObject
                {
                    ["commodity"] = commodity,
                    ["supply"] = stock,
                    ["buy_price"] = 500,
                }
            ),
        };

    private static HttpResponseMessage SpanshResponse(JsonObject[] stations, int count) =>
        Json(
            new JsonObject
            {
                ["count"] = count,
                ["results"] = new JsonArray(stations.Select(station => (JsonNode)station).ToArray()),
            }
        );

    private static HttpResponseMessage Json(JsonNode node) => Text(node.ToJsonString());

    private static HttpResponseMessage Text(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => send(request, cancellationToken);
    }
}
