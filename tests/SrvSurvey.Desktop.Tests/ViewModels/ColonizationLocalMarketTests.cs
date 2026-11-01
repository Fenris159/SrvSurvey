using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationLocalMarketTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TestNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task DefaultProviderUsesTheWorkspaceClockForQuoteAge()
    {
        using var http = new HttpClient(new FixedMarketHandler());
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), HttpClient = http }
        )
        {
            UseSpansh = false,
            MaximumAgeText = "3",
        };
        await model.SearchAsync();
        ShoppingMarketRowViewModel quote = Assert.Single(model.Results);
        Assert.Equal("Nearby", quote.System);
        Assert.Equal("Clock Port", quote.Station);
        Assert.False(quote.IsOutdated);
        Assert.Empty(model.Warnings);
    }

    private sealed class FixedMarketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""[{"systemName":"Nearby","stationName":"Clock Port","marketId":1,"stationType":"Coriolis Starport","maxLandingPadSize":3,"distance":12,"distanceToArrival":100,"commodityName":"Steel","stock":100,"buyPrice":500,"updatedAt":"{{TestNow.AddDays(-1):O}}"}]"""
                    ),
                }
            );
    }

    /// <summary>Protects the independently worked stock boundary after a shopping-list allocation has reserved sixty tonnes.</summary>
    [Theory]
    [InlineData(50, false)]
    [InlineData(60, false)]
    [InlineData(100, true)]
    public async Task LocalStockCannotCoverBothAnExistingAllocationAndTheRemainingNeed(long localStock, bool covered)
    {
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult([Quote(1, "steel", query.LocalOnly ? localStock : 60)], [])
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        await model.SearchAsync();
        model.Results.Single().AvailableSupplies.Single().ToggleWishListCommand.Execute(null);
        Assert.Equal(40, Need(model, "steel").Remaining);
        await model.ToggleLocalCommoditiesAsync();
        Assert.Equal(covered, Need(model, "steel").IsLocallyExcluded);
        Assert.Equal(60, Assert.Single(model.CaptureState().WishList).Quantity);
    }

    /// <summary>Keeps completed local-stock exclusions durable when the separate nearby-provider request fails.</summary>
    [Fact]
    public async Task AcceptedLocalRefreshUpdatesAndSavesExclusionsEvenWhenTheNearbySearchFails()
    {
        long localStock = 100;
        bool fail = false;
        ColonizationShoppingState? saved = null;
        var search = new LocalSearch(
            (query, _) =>
            {
                if (!query.LocalOnly && fail)
                {
                    throw new HttpRequestException("Offline");
                }
                return Task.FromResult(
                    new ColonizationMarketSearchResult([Quote(1, "steel", query.LocalOnly ? localStock : 500)], [])
                );
            }
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                SaveState = state => saved = state,
            }
        );
        await model.SearchAsync();
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(Need(model, "steel").IsLocallyExcluded);
        localStock = 40;
        fail = true;
        await model.SearchAsync();
        Assert.Contains("Refresh failed", model.Status);
        Assert.True(Need(model, "steel").IsSelected);
        Assert.Single(model.Results);
        Assert.Equal(40, Assert.Single(saved!.LocalMarketQuotes).Stock);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RestoredState = saved,
            }
        );
        Assert.True(Need(restored, "steel").IsSelected);
    }

    /// <summary>Retains each provider warning once even when local checks are repeated and restored.</summary>
    [Fact]
    public async Task RepeatedLocalChecksDeduplicateIndividualWarningsAcrossSessions()
    {
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        [Quote(1, "steel", 40)],
                        query.LocalOnly ? ["Local provider incomplete."] : ["Nearby provider incomplete."]
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        await model.SearchAsync();
        for (int index = 0; index < 3; index++)
        {
            await model.ToggleLocalCommoditiesAsync();
            await model.ToggleLocalCommoditiesAsync();
        }
        Assert.Equal("Nearby provider incomplete. Local provider incomplete.", model.Warnings);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RestoredState = model.CaptureState(),
            }
        );
        await restored.ToggleLocalCommoditiesAsync();
        Assert.Equal(model.Warnings, restored.Warnings);
    }

    /// <summary>Separates stock eligibility for local exclusions from the visible surface-market filter.</summary>
    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(125, true)]
    public async Task LocalCoverageCountsSurfaceStockWhileSurfaceMarketsStayFilteredFromResults(
        long stock,
        bool covered
    )
    {
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        query.LocalOnly
                            ? [Quote(1, "steel", stock) with { StationType = "OnFootSettlement" }]
                            :
                            [
                                Quote(2, "steel", 500) with
                                {
                                    StationType = "Planetary Port",
                                },
                                Quote(3, "steel", 500),
                                Quote(3, "water", 100),
                            ],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        )
        {
            ExcludeSurface = true,
        };
        await model.SearchAsync();
        Assert.Equal("Port 3", Assert.Single(model.Results).Station);
        await model.ToggleLocalCommoditiesAsync();
        Assert.Equal(covered, Need(model, "steel").IsLocallyExcluded);
        Assert.Equal(!covered, Need(model, "steel").IsSelected);
        Assert.Equal("Port 3", Assert.Single(model.Results).Station);
        bool[] surfaceFilters = [false, true, false, true];
        foreach (bool excludeSurface in surfaceFilters)
        {
            model.ExcludeSurface = excludeSurface;
            Assert.Equal(covered, Need(model, "steel").IsLocallyExcluded);
            Assert.Equal(
                !covered,
                model.Results.SelectMany(row => row.AvailableSupplies).Any(supply => supply.Need.Commodity == "steel")
            );
            Assert.Equal(!excludeSurface && !covered, model.Results.Any(row => row.Station == "Port 2"));
        }
        Assert.Equal(3, search.Queries.Count);
    }

    /// <summary>Preserves allocated commodities when display filters leave no additional stock to plan.</summary>
    [Fact]
    public async Task SurfaceFilterPreservesPlannedCommodityAndExplainsZeroLeftToPlan()
    {
        ColonizationProject[] projects = Projects();
        projects[0].Commodities["powergenerators"] = 163;
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        query.LocalOnly
                            ? [Quote(10, "powergenerators", 245) with { StationType = "OnFootSettlement" }]
                            :
                            [
                                Quote(1, "powergenerators", 1000) with
                                {
                                    System = "Nearby",
                                    Distance = 10,
                                },
                                Quote(2, "steel", 100),
                            ],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            projects,
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        await model.SearchAsync();
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(Need(model, "powergenerators").IsLocallyExcluded);
        Assert.DoesNotContain(
            model.Results.SelectMany(row => row.AvailableSupplies),
            supply => supply.Need.Commodity == "powergenerators"
        );
        model.ExcludeSurface = true;
        Assert.True(Need(model, "powergenerators").IsLocallyExcluded);
        await model.ToggleLocalCommoditiesAsync();
        ShoppingSupplyViewModel power = model.Results.Single(row => row.Station == "Port 1").AvailableSupplies.Single();
        power.ToggleWishListCommand.Execute(null);
        await model.ToggleLocalCommoditiesAsync();
        bool[] surfaceFilters = [false, true, false];
        foreach (bool excludeSurface in surfaceFilters)
        {
            model.ExcludeSurface = excludeSurface;
            Assert.Equal(0, Need(model, "powergenerators").Remaining);
            Assert.DoesNotContain(model.VisibleNeeds, need => need.Need.Commodity == "powergenerators");
            power = model.Results.Single(row => row.Station == "Port 1").AvailableSupplies.Single();
            Assert.True(power.IsWishListed);
            Assert.Equal("Power Generators: 163", power.TableLabel);
            Assert.Contains("Still to plan: 0", power.Detail);
            Assert.Contains("On ShoppingList here: 163", power.Detail);
            Assert.Equal(163, Assert.Single(model.CaptureState().WishList).Quantity);
        }
        Assert.Equal(5, search.Queries.Count);
    }

    /// <summary>Carries provider selections consistently through display, local-stock checks, and restoration.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ProviderChoicesFilterResultsApplyToLocalChecksAndSurviveSessions(bool useSpansh, bool useArdent)
    {
        var search = new LocalSearch(
            (_, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        [Quote(1, "steel", 100) with { Source = "Spansh" }, Quote(2, "water", 50)],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Assert.True(model.UseSpansh);
        Assert.True(model.UseArdent);
        await model.SearchAsync();
        Assert.Equal(2, model.Results.Count);
        model.UseSpansh = useSpansh;
        model.UseArdent = useArdent;
        Assert.Equal(useSpansh ? "Port 1" : "Port 2", Assert.Single(model.Results).Station);
        Assert.Contains("criteria changed", model.Status);
        await model.SearchAsync();
        await model.ToggleLocalCommoditiesAsync();
        Assert.All(
            search.Queries.Skip(1),
            query =>
            {
                Assert.Equal(useSpansh, query.UseSpansh);
                Assert.Equal(useArdent, query.UseArdent);
            }
        );
        Assert.Equal(useSpansh, Need(model, "steel").IsLocallyExcluded);
        Assert.Equal(useArdent, Need(model, "water").IsLocallyExcluded);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RestoredState = model.CaptureState(),
            }
        );
        Assert.Equal(useSpansh, restored.UseSpansh);
        Assert.Equal(useArdent, restored.UseArdent);
        restored.UseSpansh = false;
        restored.UseArdent = false;
        Assert.False(restored.CanSearch);
        Assert.True(restored.CanExcludeLocalCommodities);
        await restored.ToggleLocalCommoditiesAsync();
        Assert.False(restored.ExcludeLocalCommodities);
        Assert.False(restored.CanExcludeLocalCommodities);
        int count = search.Queries.Count;
        await restored.SearchAsync();
        await restored.ToggleLocalCommoditiesAsync();
        Assert.Equal(count, search.Queries.Count);
        ColonizationShoppingState legacy = System.Text.Json.JsonSerializer.Deserialize<ColonizationShoppingState>(
            "{}"
        )!;
        Assert.True(legacy.UseSpansh);
        Assert.True(legacy.UseArdent);
    }

    /// <summary>Protects the exact age cutoff and the adjacent ticks using a fixed UTC observation time.</summary>
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task MarketAgeIncludesTheExactCutoffAndFlagsOnlyOlderRows(int ticksAfterCutoff, bool outdated)
    {
        using ColonizationShoppingViewModel model = Model([
            Quote(1, "steel", 100) with
            {
                UpdatedAt = TestNow.AddDays(-3).AddTicks(ticksAfterCutoff),
            },
        ]);
        model.MaximumAgeText = "3";
        await model.SearchAsync();
        Assert.Equal(outdated, Assert.Single(model.Results).IsOutdated);
        model.IncludeOutdatedLocalMarkets = false;
        Assert.Equal(outdated ? 0 : 1, model.Results.Count);
    }

    [Fact]
    public async Task OutdatedLocalEstimatesRemainOptionalButExcludeCoveredNeedsRegardlessOfAge()
    {
        DateTimeOffset oldDate = TestNow.AddDays(-103);
        var search = new LocalSearch(
            (_, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        [
                            Quote(1, "steel", 100) with
                            {
                                UpdatedAt = oldDate,
                            },
                            Quote(2, "steel", 100) with
                            {
                                System = "Nearby",
                                Distance = 10,
                            },
                        ],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        )
        {
            MaximumAgeText = "3",
        };
        await model.SearchAsync();
        Assert.True(search.Queries[0].IncludeOutdatedLocalMarkets);
        Assert.Equal(2, model.Results.Count);
        ShoppingMarketRowViewModel local = model.Results[0];
        Assert.Equal("Sol", local.System);
        Assert.Equal("0.0 ly", local.DistanceText);
        Assert.True(local.IsOutdated);
        Assert.Contains(
            oldDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            local.OutdatedMarketWarning
        );
        Assert.False(model.Results[1].IsOutdated);
        Assert.Empty(model.Results[1].OutdatedMarketWarning);
        await model.ToggleLocalCommoditiesAsync();
        Assert.False(Need(model, "steel").IsSelected);
        Assert.All(
            search.Queries.Skip(1),
            query =>
            {
                Assert.True(query.LocalOnly);
                Assert.False(query.IncludeOutdatedLocalMarkets);
            }
        );
        model.IncludeOutdatedLocalMarkets = false;
        Assert.Empty(model.Results);
        Assert.False(Need(model, "steel").IsSelected);
        Assert.True(model.CaptureState().IsValid);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RestoredState = model.CaptureState(),
            }
        );
        Assert.False(restored.IncludeOutdatedLocalMarkets);
        Assert.Empty(restored.Results);
        Assert.False(Need(restored, "steel").IsSelected);
        await restored.ToggleLocalCommoditiesAsync();
        Assert.Equal("Nearby", Assert.Single(restored.Results).System);
        restored.IncludeOutdatedLocalMarkets = true;
        Assert.Equal(2, restored.Results.Count);
    }

    /// <summary>Combines station stock without double-counting overlapping providers and preserves manual exclusions.</summary>
    [Fact]
    public async Task CombinesDistinctLocalMarketsWithoutDoubleCountingProvidersAndRestoresManualSelections()
    {
        ColonizationProject[] projects = Projects();
        projects[0].Commodities["copper"] = 10;
        projects =
        [
            .. projects,
            projects[0] with
            {
                SystemName = " sol ",
            },
            new()
            {
                BuildId = "done",
                SystemName = "Ignored",
                IsComplete = true,
            },
        ];
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        query.ReferenceSystem == "Sol"
                            ?
                            [
                                Quote(1, "steel", 40),
                                Quote(1, "steel", 80) with
                                {
                                    UpdatedAt = TestNow.AddMinutes(-10),
                                    Source = "Spansh",
                                },
                                Quote(2, "water", 49),
                                Quote(3, "copper", 100),
                            ]
                            :
                            [
                                Quote(4, "steel", 60) with
                                {
                                    System = "Achenar",
                                },
                                Quote(5, "water", 500) with
                                {
                                    System = "Neighbor",
                                },
                            ],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            projects,
            () => "Not a project system",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Need(model, "copper").IsSelected = false;
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(model.ExcludeLocalCommodities);
        Assert.False(Need(model, "steel").IsSelected);
        Assert.True(Need(model, "water").IsSelected);
        Assert.False(Need(model, "copper").IsSelected);
        Assert.Equal(2, search.Queries.Count);
        Assert.Equal("Achenar,Sol", string.Join(",", search.Queries.Select(q => q.ReferenceSystem).Order()));
        Assert.All(
            search.Queries,
            query =>
            {
                Assert.True(query.LocalOnly);
                Assert.Equal(1, query.Radius);
                Assert.Equal(7, query.MaximumAgeDays);
            }
        );
        Assert.Equal("copper", Assert.Single(model.CaptureState().ExcludedCommodities));
        using var restored = new ColonizationShoppingViewModel(
            projects,
            () => "Elsewhere",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RestoredState = model.CaptureState(),
            }
        );
        Assert.True(restored.ExcludeLocalCommodities);
        Assert.False(Need(restored, "steel").IsSelected);
        Assert.Contains("local market stock", Need(restored, "steel").SelectionHint);
        await restored.ToggleLocalCommoditiesAsync();
        Assert.True(Need(restored, "steel").IsSelected);
        Assert.False(Need(restored, "copper").IsSelected);
        Assert.Equal(2, search.Queries.Count);
        await model.ToggleLocalCommoditiesAsync();
        Assert.False(model.ExcludeLocalCommodities);
        Assert.True(Need(model, "steel").IsSelected);
        Assert.False(Need(model, "copper").IsSelected);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("carrier")]
    [InlineData("pad")]
    [InlineData("arrival")]
    [InlineData("future")]
    [InlineData("no price")]
    [InlineData("other system")]
    public async Task DoesNotExcludeNeedsWithoutEnoughEligibleLocalStock(string problem)
    {
        ColonizationMarketQuote quote = Quote(1, "steel", 100);
        quote = problem switch
        {
            "short" => quote with { Stock = 99 },
            "carrier" => quote with { StationType = "Fleet Carrier" },
            "pad" => quote with { PadSize = 2 },
            "arrival" => quote with { Arrival = 200 },
            "future" => quote with { UpdatedAt = TestNow.AddHours(1) },
            "no price" => quote with { Price = 0 },
            _ => quote with { System = "Neighbor" },
        };
        using ColonizationShoppingViewModel model = Model([quote]);
        model.ExcludeSurface = true;
        model.MaximumArrivalText = "150";
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(model.ExcludeLocalCommodities);
        Assert.True(Need(model, "steel").IsSelected);
    }

    [Fact]
    public async Task RecalculatesCoverageForCarrierCargoProjectNeedsLocationAndCriteriaChanges()
    {
        using ColonizationShoppingViewModel model = Model([Quote(1, "steel", 40)]);
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(Need(model, "steel").IsSelected);
        model.UpdateCarriers([
            new()
            {
                MarketId = 42,
                Cargo = new() { ["steel"] = 60 },
            },
        ]);
        Assert.False(Need(model, "steel").IsSelected);
        model.UseCarrierDifference = false;
        Assert.True(Need(model, "steel").IsSelected);
        ColonizationProject[] projects = Projects();
        projects[0].Commodities["steel"] = 0;
        model.UpdateProjects(projects);
        Assert.False(Need(model, "steel").IsSelected);
        model.MaximumAgeText = "bad";
        Assert.False(Need(model, "steel").IsSelected);
        model.MaximumAgeText = "7";
        Assert.False(Need(model, "steel").IsSelected);
        model.MaximumArrivalText = "not a distance";
        Assert.True(Need(model, "steel").IsSelected);
        model.MaximumArrivalText = "0";
        Assert.False(Need(model, "steel").IsSelected);
        model.UpdateProjects(projects.Select(project => project with { SystemName = "Elsewhere" }));
        Assert.True(Need(model, "steel").IsSelected);
    }

    /// <summary>Checks local coverage against unreserved stock without changing existing shopping-list assignments.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCheckPreservesShoppingListAndSubtractsItsAllocations(bool locked)
    {
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        query.LocalOnly ? [Quote(2, "steel", 60)] : [Quote(1, "steel", 40), Quote(1, "water", 50)],
                        []
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        await model.SearchAsync();
        model
            .Results[0]
            .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
            .ToggleWishListCommand.Execute(null);
        model.IsWishListLocked = locked;
        ColonizationShoppingState before = model.CaptureState();
        await model.ToggleLocalCommoditiesAsync();
        Assert.False(Need(model, "steel").IsSelected);
        Assert.Equal(before.WishList, model.CaptureState().WishList);
        Assert.Equal(before.Quotes, model.CaptureState().Quotes);
        Assert.Equal(before.CompletedAt, model.CaptureState().CompletedAt);
        Assert.Equal(locked, model.IsWishListLocked);
        Assert.Equal(60, search.Queries[^1].Needs.Single(need => need.Commodity == "steel").Required);
        model.ApplyPurchasedWishList(before with { WishList = [before.WishList[0] with { Quantity = 20 }] });
        Assert.True(Need(model, "steel").IsSelected);
        Assert.Equal(20, Assert.Single(model.CaptureState().WishList).Quantity);
    }

    /// <summary>Refreshes local coverage while retaining provider diagnostics needed to interpret incomplete results.</summary>
    [Fact]
    public async Task RefreshRechecksLocalStockAndKeepsProviderWarnings()
    {
        long stock = 100;
        var search = new LocalSearch(
            (query, _) =>
                Task.FromResult(
                    new ColonizationMarketSearchResult(
                        [Quote(1, "steel", query.LocalOnly ? stock : 500)],
                        query.LocalOnly ? ["Local provider incomplete."] : ["Nearby provider incomplete."]
                    )
                )
        );
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                RefreshProjects = _ => Task.FromResult<IReadOnlyList<ColonizationProject>>(Projects()),
            }
        );
        await model.ToggleLocalCommoditiesAsync();
        Assert.False(Need(model, "steel").IsSelected);
        stock = 40;
        await model.SearchAsync();
        Assert.True(Need(model, "steel").IsSelected);
        Assert.True(model.ExcludeLocalCommodities);
        Assert.Contains("Local provider incomplete.", model.Warnings);
        Assert.Contains("Nearby provider incomplete.", model.Warnings);
        Assert.Single(model.Results);
        Assert.Equal(5, search.Queries.Count);
    }

    [Fact]
    public async Task UsesTheSharedBusyStateAndDiscardsAnInvalidatedCheck()
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        var search = new LocalSearch((_, _) => pending.Task);
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Task check = model.ToggleLocalCommoditiesAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.CanSearch);
        Assert.False(model.CanExcludeLocalCommodities);
        await model.ToggleLocalCommoditiesAsync();
        await model.SearchAsync();
        Assert.Equal(2, search.Queries.Count);
        model.ClearSearch();
        pending.SetResult(new([Quote(1, "steel", 100)], []));
        await check;
        Assert.False(model.IsBusy);
        Assert.False(model.ExcludeLocalCommodities);
        Assert.All(model.Needs, need => Assert.True(need.IsSelected));
    }

    /// <summary>Uses controlled provider completions to protect the concurrency bound and stop further work after closing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimitsConcurrentSystemChecksAndDoesNotStartAnotherBatchAfterClosing(bool close)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0;
        var concurrency = new System.Collections.Concurrent.ConcurrentBag<int>();
        var search = new LocalSearch(
            async (query, _) =>
            {
                concurrency.Add(Interlocked.Increment(ref active));
                await release.Task;
                Interlocked.Decrement(ref active);
                return new ColonizationMarketSearchResult(
                    [Quote(query.ReferenceSystem[^1], "steel", 1) with { System = query.ReferenceSystem }],
                    []
                );
            }
        );
        ColonizationProject[] projects = Enumerable
            .Range(0, 7)
            .Select(index => new ColonizationProject
            {
                BuildId = "Project " + index,
                SystemName = "System " + index,
                Commodities = new() { ["steel"] = 1 },
            })
            .ToArray();
        using var model = new ColonizationShoppingViewModel(
            projects,
            () => "Elsewhere",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Task check = model.ToggleLocalCommoditiesAsync();
        try
        {
            Assert.Equal(3, search.Queries.Count);
            if (close)
            {
                model.Dispose();
            }
        }
        finally
        {
            release.TrySetResult();
        }
        await check;
        Assert.Equal(3, concurrency.Max());
        Assert.Equal(close ? 3 : 7, search.Queries.Count);
        Assert.Equal(!close, model.ExcludeLocalCommodities);
        Assert.Equal(close, Need(model, "steel").IsSelected);
    }

    /// <summary>Keeps the owned provider alive until its canceled in-flight operations have actually finished.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCancelsChecksAndDefersOwnedClientDisposalUntilTheyFinish(bool cooperative)
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        CancellationToken captured = default;
        var search = new LocalSearch(
            (_, token) =>
            {
                captured = token;
                return cooperative ? pending.Task.WaitAsync(token) : pending.Task;
            }
        );
        var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = search,
                OwnsProvidedSearch = true,
            }
        );
        Task check = model.ToggleLocalCommoditiesAsync();
        model.Dispose();
        Assert.True(captured.IsCancellationRequested);
        pending.SetResult(new([Quote(1, "steel", 100)], []));
        await check;
        Assert.False(model.ExcludeLocalCommodities);
        Assert.Equal(1, search.Disposals);
        await model.ToggleLocalCommoditiesAsync();
        model.Dispose();
        Assert.Equal(1, search.Disposals);
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("0")]
    [InlineData("31")]
    [InlineData("1")]
    [InlineData("30")]
    public async Task LocalExclusionIgnoresMarketAgeAndOutdatedResultsPreference(string age)
    {
        using ColonizationShoppingViewModel model = Model([
            Quote(1, "steel", 100) with
            {
                UpdatedAt = TestNow.AddDays(-400),
            },
        ]);
        model.MaximumAgeText = age;
        model.IncludeOutdatedLocalMarkets = false;
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(model.ExcludeLocalCommodities);
        Assert.False(Need(model, "steel").IsSelected);
        Assert.True(Need(model, "steel").IsLocallyExcluded);
        Assert.True(Need(model, "water").IsSelected);
        await model.ToggleLocalCommoditiesAsync();
        Assert.True(Need(model, "steel").IsSelected);
    }

    [Fact]
    public async Task FailedChecksPreserveSelectionsAndUnavailableContextsCannotStartChecks()
    {
        var search = new LocalSearch((_, _) => throw new HttpRequestException("Offline"));
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        await model.ToggleLocalCommoditiesAsync();
        Assert.Contains("check failed", model.Status);
        Assert.False(model.ExcludeLocalCommodities);
        Assert.False(model.IsBusy);
        Assert.All(model.Needs, need => Assert.True(need.IsSelected));
        using var noSystem = new ColonizationShoppingViewModel(
            ColonizationShoppingViewModelTests.Projects(),
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Assert.False(noSystem.CanExcludeLocalCommodities);
        await noSystem.ToggleLocalCommoditiesAsync();
        using var empty = new ColonizationShoppingViewModel(
            [],
            () => "Sol",
            new ColonizationShoppingOptions { TimeProvider = new FixedClock(), Search = search }
        );
        Assert.False(empty.CanExcludeLocalCommodities);
        await empty.ToggleLocalCommoditiesAsync();
        Assert.False((new ColonizationShoppingState { LocalMarketQuotes = null! }).IsValid);
    }

    internal static ColonizationProject[] Projects() =>
        ColonizationShoppingViewModelTests
            .Projects()
            .Select((project, index) => project with { SystemName = index == 0 ? "Sol" : "Achenar" })
            .ToArray();

    internal static ColonizationMarketQuote Quote(long id, string commodity, long stock) =>
        ColonizationShoppingViewModelTests.Quote(id, commodity, stock) with
        {
            Distance = 0,
            UpdatedAt = TestNow,
        };

    private static ColonizationShoppingViewModel Model(IReadOnlyList<ColonizationMarketQuote> quotes) =>
        new(
            Projects(),
            () => "Sol",
            new ColonizationShoppingOptions
            {
                TimeProvider = new FixedClock(),
                Search = new LocalSearch((_, _) => Task.FromResult(new ColonizationMarketSearchResult(quotes, []))),
            }
        );

    private static ShoppingNeedViewModel Need(ColonizationShoppingViewModel model, string commodity) =>
        model.Needs.Single(need => need.Need.Commodity == commodity);

    internal sealed class LocalSearch(
        Func<ColonizationMarketQuery, CancellationToken, Task<ColonizationMarketSearchResult>> search
    ) : IColonizationMarketSearch, IDisposable
    {
        public List<ColonizationMarketQuery> Queries { get; } = [];
        public int Disposals { get; private set; }

        public Task<ColonizationMarketSearchResult> SearchAsync(
            ColonizationMarketQuery query,
            CancellationToken cancellationToken = default
        )
        {
            Queries.Add(query);
            return search(query, cancellationToken);
        }

        public void Dispose() => Disposals++;
    }
}
