using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationShoppingViewModelTests
{
    [Fact]
    public async Task BulkCommoditySelectionFiltersOnceAndPreservesPurchasesAndMarketData()
    {
        var search = new SearchStub([Quote(1, "steel", 40), Quote(2, "steel", 100), Quote(2, "water", 50)]);
        using ColonizationShoppingViewModel model = Create(search);
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        ColonizationWishListItem[] purchases = model.CaptureState().WishList.ToArray();
        int resultUpdates = 0;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ColonizationShoppingViewModel.Results))
            {
                resultUpdates++;
            }
        };
        model.SetAllCommoditiesSelected(false);
        Assert.Empty(model.Results);
        Assert.All(model.Needs, need => Assert.False(need.IsSelected));
        Assert.Equal(1, resultUpdates);
        Assert.Equal(purchases, model.CaptureState().WishList);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            restoredState: model.CaptureState()
        );
        Assert.All(restored.Needs, need => Assert.False(need.IsSelected));
        Assert.Empty(restored.Results);
        model.SetAllCommoditiesSelected(true);
        Assert.All(model.Needs, need => Assert.True(need.IsSelected));
        Assert.Equal(2, resultUpdates);
        Assert.Equal("Steel: 60, Water: 50", model.NeedsText);
        Assert.Equal(2, model.Results.Count);
        Assert.Equal(purchases, model.CaptureState().WishList);
        Assert.Equal(1, search.Calls);
        model.Dispose();
        model.SetAllCommoditiesSelected(false);
        Assert.Equal(2, resultUpdates);
    }

    [Fact]
    public async Task AggregatesAndFiltersWithoutRepeatingNetworkSearches()
    {
        var search = new SearchStub([Quote(1, "steel", 100), Quote(1, "water", 40), Quote(2, "water", 100)]);
        using ColonizationShoppingViewModel model = Create(search);
        Assert.Equal("Sol", model.ReferenceSystem);
        Assert.Contains("Steel: 100, Water: 50", model.NeedsText);
        Assert.Contains("150", model.TotalText);
        Assert.Equal("First, Second", model.ProjectSummary);
        await model.SearchAsync();
        Assert.Equal(2, model.Results.Count);
        ShoppingMarketRowViewModel first = model.Results.Single(row => row.Station == "Port 1");
        Assert.Equal("L", first.Pad);
        Assert.Contains("ly", first.DistanceText);
        Assert.Contains("ls", first.ArrivalText);
        Assert.Contains("CR/t", first.Supplies[0].Detail);
        Assert.False(first.CanFulfillAll);
        Assert.False(first.Supplies.Single(supply => supply.Need.Commodity == "water").IsEnough);
        Assert.Contains("shortage", model.Warnings);
        model.Needs.Single(need => need.Need.Commodity == "water").IsSelected = false;
        Assert.Single(model.Results);
        Assert.True(model.Results[0].CanFulfillAll);
        model.Needs[0].IsSelected = false;
        Assert.Empty(model.Results);
        model.Needs[1].IsSelected = true;
        Assert.Equal(2, model.Results.Count);
        Assert.Equal(1, search.Calls);
    }

    [Fact]
    public async Task AppliesPadSurfaceCarrierArrivalAndSupplyFilters()
    {
        var search = new SearchStub([
            Quote(1, "steel", 100),
            Quote(1, "water", 50),
            Quote(2, "steel", 200) with
            {
                StationType = "Fleet Carrier",
                Arrival = 200,
            },
            Quote(3, "steel", 300) with
            {
                StationType = "Planetary Port",
                PadSize = 2,
                Arrival = 300,
            },
            Quote(4, "steel", 300) with
            {
                StationType = "Settlement",
                PadSize = 1,
            },
            Quote(5, "steel", 300) with
            {
                StationType = "Crater Port",
                PadSize = 0,
            },
        ]);
        using ColonizationShoppingViewModel model = Create(search);
        await model.SearchAsync();
        Assert.Single(model.Results);
        model.ExcludeCarriers = false;
        Assert.Equal(2, model.Results.Count);
        model.MinimumPad = "Medium";
        Assert.Equal(3, model.Results.Count);
        model.MinimumPad = "Small";
        Assert.Equal(4, model.Results.Count);
        model.MinimumPad = "Any";
        Assert.Equal(5, model.Results.Count);
        Assert.Contains(model.Results, row => row.Pad == "?");
        Assert.Contains(model.Results, row => row.Pad == "S");
        Assert.Contains(model.Results, row => row.Pad == "M");
        model.ExcludeSurface = true;
        Assert.Equal(2, model.Results.Count);
        model.MaximumArrivalText = "150";
        Assert.Single(model.Results);
        model.MaximumArrivalText = "invalid";
        Assert.Empty(model.Results);
        model.MaximumArrivalText = "NaN";
        Assert.Empty(model.Results);
        model.MaximumArrivalText = "0";
        model.RequireFullSupply = true;
        Assert.Single(model.Results);
        Assert.True(model.Results[0].CanFulfillAll);
        model.RequireFullSupply = false;
        Assert.Equal(2, model.Results.Count);
        Assert.Equal("No supply reported", model.Results.Single(row => row.Station == "Port 2").Supplies[1].Detail);
        Assert.Equal(1, search.Calls);
    }

    [Fact]
    public async Task SortsByDistanceMatchesNameAndArrival()
    {
        using ColonizationShoppingViewModel model = Create(
            new SearchStub([
                Quote(2, "steel", 100) with
                {
                    Station = "Alpha",
                    Distance = 20,
                    Arrival = 50,
                },
                Quote(1, "steel", 100) with
                {
                    Station = "Zulu",
                    Distance = 10,
                    Arrival = 100,
                },
                Quote(2, "water", 50) with
                {
                    Station = "Alpha",
                    Distance = 20,
                    Arrival = 50,
                },
            ])
        );
        await model.SearchAsync();
        Assert.Equal("Zulu", model.Results[0].Station);
        model.Sort = "Most commodities available";
        Assert.Equal("Alpha", model.Results[0].Station);
        model.Sort = "Station";
        Assert.Equal("Alpha", model.Results[0].Station);
        model.Sort = "Arrival";
        Assert.Equal("Alpha", model.Results[0].Station);
        model.Sort = "Distance";
        Assert.Equal("Zulu", model.Results[0].Station);
    }

    [Fact]
    public async Task UsesRavenExpansionAndSortableCompactTableRows()
    {
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([
                Quote(1, "steel", 100),
                Quote(1, "water", 10),
                Quote(2, "water", 80) with
                {
                    System = "Achenar",
                },
            ])
        );
        await model.SearchAsync();
        Assert.False(model.ShowSearchCriteria);
        Assert.True(model.Results[0].IsExpanded);
        Assert.Equal("▾", model.Results[0].ExpansionGlyph);
        Assert.False(model.Results[1].IsExpanded);
        model.ToggleAllExpanded();
        Assert.All(model.Results, row => Assert.False(row.IsExpanded));
        Assert.Equal("Expand all", model.ExpandAllText);
        model.ToggleAllExpanded();
        Assert.All(model.Results, row => Assert.True(row.IsExpanded));
        Assert.Equal("Collapse all", model.ExpandAllText);
        model.SortBy("Distance");
        Assert.Equal(20, model.Results[0].Distance);
        Assert.EndsWith("▾", model.DistanceSortLabel);
        model.SortBy("Distance");
        Assert.Equal(10, model.Results[0].Distance);
        model.SortBy("System");
        Assert.Equal("Achenar", model.Results[0].System);
        Assert.EndsWith("▴", model.SystemSortLabel);
        model.SortBy("Station");
        Assert.EndsWith("▴", model.StationSortLabel);
        model.SortBy("Most commodities available");
        Assert.EndsWith("▾", model.MatchesSortLabel);
        model.SortBy("Arrival");
        Assert.EndsWith("▴", model.ArrivalSortLabel);
        Assert.Equal(
            "Steel: 100",
            model.Results.Single(row => row.Station == "Port 1").AvailableSupplies[0].TableLabel
        );
        model.ToggleCommodity("water");
        Assert.Single(model.Results);
        Assert.True(model.Results[0].IsExpanded);
        model.ToggleCommodity("unknown");
        Assert.Single(model.Results);
    }

    [Theory]
    [InlineData("", "100", "7")]
    [InlineData("Sol", "0", "7")]
    [InlineData("Sol", "501", "7")]
    [InlineData("Sol", "bad", "7")]
    [InlineData("Sol", "100", "0")]
    [InlineData("Sol", "100", "31")]
    [InlineData("Sol", "100", "bad")]
    public async Task ValidatesEditableCriteriaBeforeCallingProviders(string system, string radius, string age)
    {
        var search = new SearchStub([]);
        using ColonizationShoppingViewModel model = Create(search);
        model.ReferenceSystem = system;
        model.RadiusText = radius;
        model.MaximumAgeText = age;
        await model.SearchAsync();
        Assert.Equal(0, search.Calls);
        Assert.Contains("Enter a system", model.Status);
    }

    [Fact]
    public async Task RetainsPreviousResultsUntilRefreshingTheEditedCriteria()
    {
        string? location = "Sol";
        var search = new SearchStub([Quote(1, "steel", 100)]);
        using var model = new ColonizationShoppingViewModel(Projects(), () => location, search);
        await model.SearchAsync();
        Assert.NotEmpty(model.Results);
        location = "Achenar";
        model.UseCurrentLocation();
        Assert.Equal("Achenar", model.ReferenceSystem);
        Assert.NotEmpty(model.Results);
        Assert.Equal("Sol", model.ResultsReferenceSystem);
        model.RadiusText = "50";
        model.MaximumAgeText = "2";
        await model.SearchAsync();
        Assert.Equal("Achenar", search.LastQuery!.ReferenceSystem);
        Assert.Equal(50, search.LastQuery.Radius);
        Assert.Equal(2, search.LastQuery.MaximumAgeDays);
        location = null;
        model.UseCurrentLocation();
        Assert.Empty(model.ReferenceSystem);
    }

    [Fact]
    public async Task PreventsOverlappingSearchAndDiscardsLateResultsAfterCriteriaChange()
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        var search = new SearchStub([]) { Run = (_, _) => pending.Task };
        using ColonizationShoppingViewModel model = Create(search);
        Task first = model.SearchAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.CanSearch);
        await model.SearchAsync();
        Assert.Equal(1, search.Calls);
        model.ReferenceSystem = "Other";
        pending.SetResult(new([Quote(1, "steel", 100)], []));
        await first;
        Assert.Empty(model.Results);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteFailuresCannotReplaceClearOrEditedCriteriaStatus(bool clear)
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        using ColonizationShoppingViewModel model = Create(new SearchStub([]) { Run = (_, _) => pending.Task });
        Task work = model.SearchAsync();
        if (clear)
        {
            model.ClearSearch();
        }
        else
        {
            model.ReferenceSystem = "Achenar";
        }
        string status = model.Status;
        pending.SetException(new HttpRequestException("offline"));
        await work;
        Assert.Equal(status, model.Status);
        Assert.Empty(model.Results);
        Assert.Empty(model.CaptureState().WishList);
    }

    [Fact]
    public async Task CancelsOnCloseAndDiscardsResponsesFromUncooperativeProvider()
    {
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        CancellationToken captured = default;
        var search = new SearchStub([])
        {
            Run = (_, token) =>
            {
                captured = token;
                return pending.Task;
            },
        };
        ColonizationShoppingViewModel model = Create(search);
        Task work = model.SearchAsync();
        model.Dispose();
        model.Dispose();
        Assert.True(captured.IsCancellationRequested);
        Assert.False(model.CanSearch);
        pending.SetResult(new([Quote(1, "steel", 100)], []));
        await work;
        Assert.Empty(model.Results);
        await model.SearchAsync();
        Assert.Equal(1, search.Calls);
    }

    [Fact]
    public async Task HandlesCanceledSearchAndNetworkFailureWithoutLosingWindow()
    {
        var search = new SearchStub([]) { Run = (_, _) => throw new HttpRequestException("offline") };
        using ColonizationShoppingViewModel model = Create(search);
        await model.SearchAsync();
        Assert.Contains("failed", model.Status);
        Assert.True(model.CanSearch);
        search.Run = async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new([], []);
        };
        Task pending = model.SearchAsync();
        model.Dispose();
        await pending;
        Assert.False(model.IsBusy);
        using var empty = new ColonizationShoppingViewModel([], () => null);
        await empty.SearchAsync();
        Assert.False(empty.CanSearch);
    }

    [Fact]
    public async Task RestoresResultsAndAllLocalControlsWithoutSearchingAndRefreshesOnRequest()
    {
        var search = new SearchStub([Quote(1, "steel", 100), Quote(2, "water", 100)]);
        using ColonizationShoppingViewModel original = Create(search);
        original.RadiusText = "45";
        original.MaximumAgeText = "3";
        await original.SearchAsync();
        original.Needs[1].IsSelected = false;
        original.MinimumPad = "Medium";
        original.MaximumArrivalText = "200";
        original.ExcludeCarriers = false;
        original.ExcludeSurface = true;
        original.RequireFullSupply = true;
        original.SortBy("Station");
        original.SortBy("Station");
        original.Results[0].IsExpanded = false;
        original.ShowSearchCriteria = true;
        var offline = new SearchStub([]) { Run = (_, _) => throw new HttpRequestException("offline") };
        using var restored = new ColonizationShoppingViewModel(
            Projects().Reverse(),
            () => "Achenar",
            offline,
            restoredState: original.CaptureState()
        );
        Assert.Equal(0, offline.Calls);
        Assert.True(restored.HasSearchResults);
        Assert.Equal("Refresh", restored.SearchButtonText);
        Assert.Equal("Sol", restored.ReferenceSystem);
        Assert.Equal("Sol", restored.ResultsReferenceSystem);
        Assert.Equal("45", restored.RadiusText);
        Assert.Equal("3", restored.MaximumAgeText);
        Assert.Equal("Medium", restored.MinimumPad);
        Assert.Equal("200", restored.MaximumArrivalText);
        Assert.False(restored.ExcludeCarriers);
        Assert.True(restored.ExcludeSurface);
        Assert.True(restored.RequireFullSupply);
        Assert.True(restored.ShowSearchCriteria);
        Assert.Contains("▾", restored.StationSortLabel);
        Assert.False(restored.Needs[1].IsSelected);
        Assert.False(Assert.Single(restored.Results).IsExpanded);
        Assert.Contains("Saved results near Sol", restored.Status);
        await restored.SearchAsync();
        Assert.Single(restored.Results);
        Assert.Contains("Previous results", restored.Status);
        offline.Run = (_, _) => Task.FromResult(new ColonizationMarketSearchResult([Quote(3, "steel", 200)], []));
        await restored.SearchAsync();
        Assert.Equal("Port 3", Assert.Single(restored.Results).Station);
        Assert.Equal(2, offline.Calls);
    }

    [Fact]
    public async Task SavesOnSuccessAndCloseRetainingResultsWhenClosedDuringRefresh()
    {
        SrvSurvey.Desktop.Configuration.ColonizationShoppingState? saved = null;
        var search = new SearchStub([Quote(1, "steel", 100)]);
        var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            search,
            saveState: state => saved = state
        );
        await model.SearchAsync();
        Assert.NotNull(saved?.CompletedAt);
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        search.Run = (_, _) => pending.Task;
        Task refreshing = model.SearchAsync();
        model.Needs[1].IsSelected = false;
        model.Dispose();
        Assert.Contains("water", saved.ExcludedCommodities);
        Assert.Single(saved.Quotes);
        Assert.NotEmpty(saved.ExpandedMarkets);
        pending.SetResult(new([], []));
        await refreshing;
        Assert.Single(saved.Quotes);
    }

    [Fact]
    public async Task RestoredQuotesUseCurrentNeedsAndWarnAboutNewCommodities()
    {
        using ColonizationShoppingViewModel original = Create(new SearchStub([Quote(1, "steel", 100)]));
        await original.SearchAsync();
        ColonizationProject[] projects = Projects();
        projects[0].Commodities["steel"] = 200;
        projects[0].Commodities["copper"] = 10;
        using var restored = new ColonizationShoppingViewModel(
            projects,
            () => "Sol",
            new SearchStub([]),
            restoredState: original.CaptureState()
        );
        Assert.Contains("newly required commodities", restored.Warnings);
        Assert.False(
            Assert.Single(restored.Results).Supplies.Single(supply => supply.Need.Commodity == "steel").IsEnough
        );
    }

    [Fact]
    public async Task SaveFailureKeepsWorkspaceUsable()
    {
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([Quote(1, "steel", 100)]),
            saveState: _ => throw new IOException("disk full")
        );
        await model.SearchAsync();
        Assert.Single(model.Results);
        Assert.Contains("could not be saved", model.Warnings);
        Assert.True(model.CanSearch);
    }

    [Fact]
    public async Task HidesShortSuppliesAndRanksTheVisibleCommodityCountsWithoutLosingWishListItems()
    {
        var search = new SearchStub([
            Quote(1, "steel", 80),
            Quote(1, "water", 40),
            Quote(2, "steel", 100),
            Quote(3, "steel", 100),
            Quote(3, "water", 50),
            Quote(4, "water", 1),
        ]);
        using ColonizationShoppingViewModel model = Create(search);
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        Assert.True(model.ShowShortSupply);
        model.ShowShortSupply = false;
        Assert.Collection(
            model.Results,
            row => Assert.Equal("Port 1", row.Station),
            row => Assert.Equal("Port 2", row.Station),
            row => Assert.Equal("Port 3", row.Station)
        );
        Assert.All(
            model.Results.SelectMany(row => row.AvailableSupplies).Where(supply => !supply.IsWishListed),
            supply => Assert.True(supply.IsEnough)
        );
        model.Sort = "Most commodities available";
        Assert.Equal("Port 1", model.Results[0].Station);
        Assert.Equal("Port 3", model.Results[1].Station);
        Assert.Equal(2, model.Results[0].Matches);
        model.SortBy("Most commodities available");
        Assert.Equal("Port 2", model.Results[0].Station);
        model.ShowShortSupply = true;
        Assert.Equal(4, model.Results.Count);
        Assert.Equal(2, model.CaptureState().WishList.Count);
        model.ShowShortSupply = false;
        var offline = new SearchStub([]);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            offline,
            restoredState: model.CaptureState()
        );
        Assert.False(restored.ShowShortSupply);
        Assert.Equal("Most commodities available", restored.Sort);
        Assert.Equal(3, restored.Results.Count);
        Assert.Equal(2, restored.CaptureState().WishList.Count);
        Assert.Equal(0, offline.Calls);
        Assert.Equal(1, search.Calls);
    }

    [Fact]
    public async Task RestoresThePreviousCommoditySortSetting()
    {
        using ColonizationShoppingViewModel model = Create(
            new SearchStub([Quote(1, "steel", 100), Quote(2, "steel", 100), Quote(2, "water", 50)])
        );
        await model.SearchAsync();
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([]),
            restoredState: model.CaptureState() with
            {
                Sort = "Most matches",
            }
        );
        Assert.Equal("Most commodities available", restored.Sort);
        Assert.Equal("Port 2", restored.Results[0].Station);
    }

    private static ColonizationShoppingViewModel Create(SearchStub search) => new(Projects(), () => "Sol", search);

    internal static ColonizationProject[] Projects() =>
        [
            new()
            {
                BuildId = "1",
                BuildName = "First",
                Commodities = new() { ["steel"] = 60, ["water"] = 50 },
            },
            new()
            {
                BuildId = "2",
                BuildName = "Second",
                Commodities = new() { ["steel"] = 40 },
            },
        ];

    internal static ColonizationMarketQuote Quote(long id, string commodity, long stock) =>
        new(
            "Sol",
            "Port " + id,
            id,
            "Coriolis Starport",
            3,
            id * 10,
            100,
            commodity,
            stock,
            500,
            DateTimeOffset.UtcNow,
            "Ardent"
        );

    internal sealed class SearchStub(IReadOnlyList<ColonizationMarketQuote> quotes) : IColonizationMarketSearch
    {
        public int Calls { get; private set; }
        public ColonizationMarketQuery? LastQuery { get; private set; }
        public Func<ColonizationMarketQuery, CancellationToken, Task<ColonizationMarketSearchResult>>? Run { get; set; }

        public Task<ColonizationMarketSearchResult> SearchAsync(
            ColonizationMarketQuery query,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            LastQuery = query;
            return Run?.Invoke(query, cancellationToken)
                ?? Task.FromResult(new ColonizationMarketSearchResult(quotes, ["stock shortage example"]));
        }
    }
}
