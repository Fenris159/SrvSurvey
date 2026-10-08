using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Search;

public sealed class PowerplayAcquireSearchTests
{
    private const string Pledge = "Aisling Duval";

    [Fact]
    public async Task SupportersCombineFortifiedAndStrongholdSystemsNearestFirst()
    {
        var provider = new StubMiningSearchProvider
        {
            Supporters = (_, _, state) =>
                state == "Fortified"
                    ? [Supporter("Far Fort", 50, "Fortified", At(50)), Supporter("Unplaced", 1, "Fortified")]
                    : [Supporter("Near Hold", 5, "Stronghold", At(5)), Supporter("Far Fort", 50, "Stronghold", At(50))],
        };
        var search = new PowerplayAcquireSearch(provider, Filters());

        MiningSystemResult[] positioned = await search.SupportersAsync(requirePosition: true, CancellationToken.None);
        MiningSystemResult[] all = await search.SupportersAsync(requirePosition: false, CancellationToken.None);

        Assert.Equal(["Near Hold", "Far Fort"], positioned.Select(system => system.System));
        Assert.Equal("Fortified", positioned[1].PowerState);
        Assert.Equal(["Unplaced", "Near Hold", "Far Fort"], all.Select(system => system.System));
        Assert.Equal(["supporters Fortified", "supporters Stronghold"], provider.Calls.Take(2));
    }

    [Fact]
    public async Task BubbleTargetsReadEveryPageAndUseTheIndexedCandidateSearchOnlyWithoutStateFilters()
    {
        var provider = new StubMiningSearchProvider
        {
            CandidatePages = query => Page(query.Page < 1, Target("Open " + query.Page), Owned("Owned")),
            SystemPages = query => Page(false, Target("Filtered " + query.Page)),
        };
        MiningSystemResult supporter = Supporter("Hold", 0, "Stronghold");
        var open = new PowerplayAcquireSearch(
            provider,
            Filters() with
            {
                Security = " High ",
                State = "Any",
                MinimumPopulation = 5,
            }
        );
        var filtered = new PowerplayAcquireSearch(provider, Filters() with { PowerState = "Unoccupied" });
        var states = new PowerplayAcquireSearch(provider, Filters() with { States = ["Boom"] });

        IReadOnlyList<MiningSystemResult> openTargets = await open.BubbleTargetsAsync(
            supporter,
            CancellationToken.None
        );
        IReadOnlyList<MiningSystemResult> filteredTargets = await filtered.BubbleTargetsAsync(
            supporter,
            CancellationToken.None
        );
        await states.BubbleTargetsAsync(supporter, CancellationToken.None);

        Assert.Equal(["Open 0", "Open 1"], openTargets.Select(system => system.System));
        Assert.Equal(["Filtered 0"], filteredTargets.Select(system => system.System));
        MiningSystemQuery query = provider.CandidatePageQueries[0];
        Assert.Equal(
            ("Hold", 30d, "High", "", 5L, PowerplayPlan.Acquire),
            (query.ReferenceSystem, query.Radius, query.Security, query.State, query.MinimumPopulation, query.Objective)
        );
        Assert.Equal(2, provider.SystemPageQueries.Count);
        Assert.Equal(20, open.BubbleQuery(Supporter("Fort", 0, "Fortified"), 3).Radius);
        Assert.Equal(
            "Boom",
            new PowerplayAcquireSearch(provider, Filters() with { State = " Boom " }).BubbleQuery(supporter, 0).State
        );
    }

    [Fact]
    public async Task NearbySourcesKeepThePledgedPowersSupportersInsideTheirReach()
    {
        var provider = new StubMiningSearchProvider
        {
            SystemPages = query =>
                query.Page == 0
                    ? Page(
                        true,
                        Supporter("Close Fort", 15, "Fortified"),
                        Supporter("Far Fort", 25, "Fortified"),
                        Supporter("Far Hold", 25, "Stronghold")
                    )
                    : Page(
                        false,
                        Supporter("Rival", 5, "Fortified") with
                        {
                            Power = "Yuri Grom",
                        },
                        Supporter("Exploited", 5, "Exploited")
                    ),
        };
        var search = new PowerplayAcquireSearch(provider, Filters());

        HashSet<string> sources = await search.NearbySourcesAsync("Target", CancellationToken.None);

        Assert.Equal(["Close Fort", "Far Hold"], sources.Order(StringComparer.Ordinal));
        Assert.All(provider.SystemPageQueries, query => Assert.Equal((30d, Pledge), (query.Radius, query.Power)));
    }

    [Fact]
    public async Task ForcedReferenceMustBeAnAcquisitionTargetMatchingTheSystemFilters()
    {
        MiningSystemResult reference = Target("Sol") with { Security = "High", State = "Boom", Population = 10 };
        var provider = new StubMiningSearchProvider { SystemsByName = (_, _) => [Target("Other"), reference] };

        Assert.Same(
            reference,
            await new PowerplayAcquireSearch(
                provider,
                Filters() with
                {
                    Security = "high",
                    States = ["Any", "Boom"],
                    MinimumPopulation = 10,
                }
            ).ForcedReferenceTargetAsync(CancellationToken.None)
        );
        Assert.Null(
            await new PowerplayAcquireSearch(
                provider,
                Filters() with
                {
                    Allegiance = "Empire",
                }
            ).ForcedReferenceTargetAsync(CancellationToken.None)
        );
        Assert.Null(
            await new PowerplayAcquireSearch(provider, Filters() with { States = ["War"] }).ForcedReferenceTargetAsync(
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ImportsQueryCarrierFreeStationsAndFallBackToSpansh()
    {
        MiningMarketQuery? sent = null;
        var provider = new StubMiningSearchProvider
        {
            Imports = (_, query) =>
            {
                sent = query;
                return [];
            },
            SpanshCommodities = (_, system) => [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 1)],
        };
        var search = new PowerplayAcquireSearch(
            provider,
            Filters() with
            {
                MinimumDemand = 10,
                MarketFreshness = TimeSpan.FromHours(4),
                PadSize = "L",
            }
        );

        MiningMarketResult[] quotes = await search.ImportsAsync("Sell", ["Monazite"], CancellationToken.None);

        Assert.Equal("Port", Assert.Single(quotes).Station);
        Assert.NotNull(sent);
        Assert.True(sent.ExcludeCarriers);
        Assert.True(sent.SystemOnly);
        Assert.Equal((10L, TimeSpan.FromHours(4), "L"), (sent.MinimumDemand, sent.MaximumAge, sent.PadSize));
        Assert.Contains("spansh Sol Sell", provider.Calls);
    }

    [Fact]
    public async Task ReinforceRulesKeepEachSellSystemsOwnBodiesAndCheckPowerplayEligibility()
    {
        var provider = new StubMiningSearchProvider
        {
            SystemsByName = (_, names) =>
                names
                    .Select(name =>
                        name switch
                        {
                            "Own" => Supporter("Own", 12, "Fortified") with { NearbyPowers = [Pledge, "Yuri Grom"] },
                            "Rival" => Supporter("Rival", 8, "Fortified") with { Power = "Yuri Grom" },
                            _ => Supporter(name, 4, "Exploited"),
                        }
                    )
                    .ToArray(),
        };
        var markets = new PowerplayPlanetarySellMarkets(
            provider,
            Filters() with
            {
                Objective = PowerplayPlan.Reinforce,
                PowerState = "Fortified",
            }
        );

        SurfaceSellMarketRules rules = await markets.RulesAsync(NoOrigin, CancellationToken.None);
        IReadOnlySet<string> eligible = await rules.EligibleSellSystemsAsync!(
            ["Own", "Rival", "Exploited"],
            CancellationToken.None
        );
        IReadOnlySet<string> again = await rules.EligibleSellSystemsAsync(["Own"], CancellationToken.None);

        Assert.Null(rules.AdditionalMarketQuotesAsync);
        Assert.Equal(["Own"], eligible);
        Assert.Equal(["Own"], again);
        Assert.Single(provider.Calls, call => call.StartsWith("names", StringComparison.Ordinal));
        Assert.Equal(["Own"], rules.MiningSystemsForSell!("Own"));
        SurfaceSellSystemDetails details = rules.SellSystemDetailsFor!("Own")!;
        Assert.Equal(("Fortified", 12d, Pledge), (details.PowerState, details.DistanceLy, details.ControllingPower));
        Assert.Equal([Pledge, "Yuri Grom"], details.Powers);
        Assert.Null(rules.SellSystemDetailsFor("Rival"));
    }

    [Fact]
    public async Task StateChoicesLimitWhichSellSystemsAreEligible()
    {
        var provider = new StubMiningSearchProvider
        {
            SystemsByName = (_, names) =>
                names.Select(name => Supporter(name, 1, "Fortified") with { State = name }).ToArray(),
        };
        var markets = new PowerplayPlanetarySellMarkets(
            provider,
            Filters() with
            {
                Objective = PowerplayPlan.Reinforce,
                States = ["Any", "Boom"],
            }
        );

        SurfaceSellMarketRules rules = await markets.RulesAsync(NoOrigin, CancellationToken.None);

        Assert.Equal(["Boom"], await rules.EligibleSellSystemsAsync!(["Boom", "War"], CancellationToken.None));
    }

    [Fact]
    public async Task AcquireRulesContinueThroughSupporterBubblesUntilAMarketIsFound()
    {
        var provider = new StubMiningSearchProvider
        {
            Supporters = (_, _, state) =>
                state == "Fortified"
                    ? [Supporter("Fort A", 100, "Fortified", At(100)), Supporter("Fort B", 200, "Fortified", At(200))]
                    : [],
            CandidatePages = query =>
                query.ReferenceSystem switch
                {
                    "Fort A" when query.Page == 0 => Page(true, Target("Empty", At(110)), Target("Far", At(150))),
                    "Fort A" => Page(false, Target("Empty", At(110))),
                    _ => Page(false, Target("Good", At(205)), Target("Unplaced") with { Distance = 3 }),
                },
            Imports = (system, _) =>
                system == "Good" ? [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 400_000)] : [],
            SystemsByName = (_, names) => names.Select(name => Target(name) with { Distance = 333 }).ToArray(),
        };
        var markets = new PowerplayPlanetarySellMarkets(provider, Filters() with { ResultLimit = 1 });

        SurfaceSellMarketRules rules = await markets.RulesAsync(
            _ => Task.FromResult<GalacticCoordinate?>(At(0)),
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> first = await rules.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> second = await rules.AdditionalMarketQuotesAsync(
            ["Monazite"],
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> exhausted = await rules.AdditionalMarketQuotesAsync(
            ["Monazite"],
            CancellationToken.None
        );

        Assert.Equal(("Good", 205d), (Assert.Single(first).System, Assert.Single(first).Distance));
        Assert.Equal(["Fort B"], rules.MiningSystemsForSell!("Good").Order(StringComparer.Ordinal));
        Assert.Equal(205, rules.SellSystemDetailsFor!("Good")!.DistanceLy);
        Assert.Empty(second);
        Assert.Equal(333, rules.SellSystemDetailsFor("Unplaced")!.DistanceLy);
        Assert.Empty(exhausted);
        Assert.Empty(rules.MiningSystemsForSell("Unknown"));
        Assert.Equal(
            ["imports Empty", "imports Good", "imports Unplaced"],
            provider.Calls.Where(call => call.StartsWith("imports", StringComparison.Ordinal))
        );
    }

    [Fact]
    public async Task AcquireRulesResolveTheOriginByNameAndSkipTargetsOutsideTheChosenStates()
    {
        var provider = new StubMiningSearchProvider
        {
            Supporters = (_, _, state) => state == "Fortified" ? [Supporter("Fort", 5, "Fortified", At(5))] : [],
            CandidatePages = _ => Page(false, Target("War Target", At(6)) with { State = "War" }),
            SystemPages = _ =>
                Page(
                    false,
                    Target("Boom Target", At(7)) with
                    {
                        State = "Boom",
                    },
                    Target("War Target", At(6)) with
                    {
                        State = "War",
                    }
                ),
            Imports = (system, _) => [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 1)],
            SystemsByName = (_, names) => names.Contains("Sol") ? [Target("Sol", At(1))] : [],
        };
        var markets = new PowerplayPlanetarySellMarkets(provider, Filters() with { States = ["Boom"] });

        SurfaceSellMarketRules rules = await markets.RulesAsync(NoOrigin, CancellationToken.None);
        IReadOnlyList<MiningMarketResult> quotes = await rules.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );

        Assert.Equal("Boom Target", Assert.Single(quotes).System);
        Assert.Equal(6, Assert.Single(quotes).Distance);
        Assert.Contains("names Sol", provider.Calls);
    }

    [Fact]
    public async Task CachedBubbleTargetsAreRefreshedBeforeTheirMarketsAreUsed()
    {
        bool failRefresh = false;
        var provider = new StubMiningSearchProvider
        {
            Supporters = (_, _, state) => state == "Fortified" ? [Supporter("Fort", 5, "Fortified", At(5))] : [],
            CandidatePages = _ =>
                Page(false, Target("Taken", At(6)), Target("Still Open", At(7))) with
                {
                    FromCache = true,
                },
            SystemsByName = (_, names) =>
                failRefresh
                    ? throw new HttpRequestException("offline")
                    : names
                        .Select(name =>
                            name == "Taken"
                                ? Supporter(name, 1, "Exploited")
                                : Target(name) with
                                {
                                    State = "Boom",
                                    Power = "",
                                    NearbyPowers = ["Yuri Grom"],
                                }
                        )
                        .ToArray(),
            Imports = (system, _) => [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 1)],
        };

        SurfaceSellMarketRules rules = await new PowerplayPlanetarySellMarkets(provider, Filters()).RulesAsync(
            _ => Task.FromResult<GalacticCoordinate?>(At(0)),
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> quotes = await rules.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );
        failRefresh = true;
        SurfaceSellMarketRules offline = await new PowerplayPlanetarySellMarkets(provider, Filters()).RulesAsync(
            _ => Task.FromResult<GalacticCoordinate?>(At(0)),
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> unrefreshed = await offline.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );

        Assert.Equal("Still Open", Assert.Single(quotes).System);
        Assert.Null(rules.SellSystemDetailsFor!("Taken"));
        SurfaceSellSystemDetails refreshed = rules.SellSystemDetailsFor("Still Open")!;
        Assert.Equal(("Boom", 7d), (refreshed.FactionState, refreshed.DistanceLy));
        Assert.Equal(["Yuri Grom"], refreshed.Powers);
        Assert.Equal(["Taken", "Still Open"], unrefreshed.Select(quote => quote.System));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExhaustedCachedBatchContinuesToLaterTargets(bool allTaken)
    {
        var provider = new StubMiningSearchProvider
        {
            Supporters = (_, _, state) => state == "Fortified" ? [Supporter("Fort", 0, "Fortified", At(0))] : [],
            CandidatePages = query =>
                Page(query.Page == 0, Target(query.Page == 0 ? "Taken" : "Later", At(1))) with
                {
                    FromCache = true,
                },
            SystemsByName = (_, names) =>
                names.Select(name => allTaken || name == "Taken" ? Owned(name) : Target(name, At(1))).ToArray(),
            Imports = (system, _) => [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 1)],
        };
        SurfaceSellMarketRules rules = await new PowerplayPlanetarySellMarkets(
            provider,
            Filters() with
            {
                ResultLimit = 1,
            }
        ).RulesAsync(NoOrigin, CancellationToken.None);
        IReadOnlyList<MiningMarketResult> quotes = await rules.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );
        IEnumerable<string> expected = allTaken ? [] : ["Later"];
        Assert.Equal(expected, quotes.Select(quote => quote.System));
        Assert.Equal([0, 1], provider.CandidatePageQueries.Select(query => query.Page));
        Assert.Empty(await rules.AdditionalMarketQuotesAsync(["Monazite"], CancellationToken.None));
    }

    [Fact]
    public async Task ForcedAcquireReferenceIsOfferedFirstWhenItHasSupportersAndMarkets()
    {
        bool referenceHasMarkets = true;
        var provider = new StubMiningSearchProvider
        {
            SystemsByName = (_, names) => names.Contains("Sol") ? [Target("Sol")] : [],
            SystemPages = _ => Page(false, Supporter("Fort", 10, "Fortified")),
            Imports = (system, _) =>
                referenceHasMarkets || system != "Sol"
                    ? [StubMiningSearchProvider.Quote(system, "Port", "Monazite", 1, distance: 99)]
                    : [],
        };
        PowerplaySearchFilters filters = Filters() with { ForceIncludeReference = true };

        SurfaceSellMarketRules rules = await new PowerplayPlanetarySellMarkets(provider, filters).RulesAsync(
            NoOrigin,
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> forced = await rules.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> later = await rules.AdditionalMarketQuotesAsync(
            ["Monazite"],
            CancellationToken.None
        );
        referenceHasMarkets = false;
        SurfaceSellMarketRules unpriced = await new PowerplayPlanetarySellMarkets(provider, filters).RulesAsync(
            NoOrigin,
            CancellationToken.None
        );
        IReadOnlyList<MiningMarketResult> none = await unpriced.AdditionalMarketQuotesAsync!(
            ["Monazite"],
            CancellationToken.None
        );

        Assert.Equal(("Sol", 0d), (Assert.Single(forced).System, Assert.Single(forced).Distance));
        Assert.Equal(["Fort"], rules.MiningSystemsForSell!("Sol"));
        Assert.Equal(0, rules.SellSystemDetailsFor!("Sol")!.DistanceLy);
        Assert.Empty(later);
        Assert.Empty(none);
    }

    [Fact]
    public async Task ForcedAcquireReferenceWithoutSupportersFallsBackToTheBubbleCursor()
    {
        var provider = new StubMiningSearchProvider
        {
            SystemsByName = (_, names) => names.Contains("Sol") ? [Target("Sol")] : [],
        };
        var markets = new PowerplayPlanetarySellMarkets(provider, Filters() with { ForceIncludeReference = true });

        SurfaceSellMarketRules rules = await markets.RulesAsync(NoOrigin, CancellationToken.None);

        Assert.Empty(await rules.AdditionalMarketQuotesAsync!(["Monazite"], CancellationToken.None));
        Assert.DoesNotContain(provider.Calls, call => call.StartsWith("imports", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcquireEligibilityFindsSupportersForUnresolvedTargets()
    {
        var provider = new StubMiningSearchProvider
        {
            SystemsByName = (_, names) => names.Select(name => Target(name)).ToArray(),
            SystemPages = query =>
                query.ReferenceSystem == "Supported" ? Page(false, Supporter("Fort", 10, "Fortified")) : Page(false),
        };
        var markets = new PowerplayPlanetarySellMarkets(provider, Filters());

        SurfaceSellMarketRules rules = await markets.RulesAsync(NoOrigin, CancellationToken.None);
        IReadOnlySet<string> eligible = await rules.EligibleSellSystemsAsync!(
            ["Supported", "Alone"],
            CancellationToken.None
        );

        Assert.Equal(["Supported"], eligible);
        Assert.Equal(["Fort"], rules.MiningSystemsForSell!("Supported"));
    }

    private static readonly Func<CancellationToken, Task<GalacticCoordinate?>> NoOrigin = _ =>
        Task.FromResult<GalacticCoordinate?>(null);

    private static PowerplaySearchFilters Filters() => new("Sol", PowerplayPlan.Acquire, Pledge, "Any");

    private static GalacticCoordinate At(double x) => new(x, 0, 0);

    private static MiningSystemPage Page(bool hasMore, params MiningSystemResult[] systems) => new(systems, hasMore);

    private static MiningSystemResult Supporter(
        string name,
        double distance,
        string state,
        GalacticCoordinate? position = null
    ) => StubMiningSearchProvider.System(name, distance, Pledge, state, position);

    private static MiningSystemResult Target(string name, GalacticCoordinate? position = null) =>
        StubMiningSearchProvider.System(name, null, "", "Unoccupied", position);

    private static MiningSystemResult Owned(string name) => Supporter(name, 1, "Exploited");
}
