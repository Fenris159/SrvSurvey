using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.ViewModels.ColonizationShoppingViewModelTests;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationWishListViewModelTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    public async Task ChosenCommodityRemainsVisibleAndCanBeToggledOffAtTheSameMarket(int stock)
    {
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([
                Quote(1, "steel", 10) with
                {
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                },
                Quote(1, "steel", stock),
                Quote(1, "water", 50),
                Quote(2, "steel", 500),
            ])
        );
        await model.SearchAsync();
        Assert.Equal(
            stock,
            model
                .Results.Single(row => row.Station == "Port 1")
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
                .Stock
        );
        model
            .Results.Single(row => row.Station == "Port 1")
            .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
            .ToggleWishListCommand.Execute(null);

        ShoppingSupplyViewModel chosen = model
            .Results.Single(row => row.Station == "Port 1")
            .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel");
        Assert.True(chosen.IsWishListed);
        Assert.Equal($"Steel: {stock}", chosen.TableLabel);
        Assert.Contains($"Still need {100 - stock}", chosen.Detail);
        Assert.Equal(stock, Assert.Single(model.CaptureState().WishList).Quantity);
        chosen.ToggleWishListCommand.Execute(null);
        Assert.Empty(model.CaptureState().WishList);
        Assert.False(
            model
                .Results.Single(row => row.Station == "Port 1")
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
                .IsWishListed
        );
        Assert.Equal("Steel: 100, Water: 50", model.NeedsText);
    }

    [Fact]
    public async Task PurchasesDepleteNeedsAcrossPortsAndRemovingOneRestoresOnlyItsTonnes()
    {
        var search = new SearchStub([Quote(1, "steel", 100), Quote(2, "steel", 500), Quote(3, "steel", 150)]);
        using var model = new ColonizationShoppingViewModel(
            [
                new ColonizationProject
                {
                    BuildId = "1",
                    Commodities = new() { ["steel"] = 300 },
                },
            ],
            () => "Sol",
            search
        );
        await model.SearchAsync();
        Assert.Equal("Steel: 300", model.Results[1].AvailableSupplies[0].TableLabel);
        model.Results[0].AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        Assert.Equal("Steel: 200", model.NeedsText);
        Assert.True(model.Results.Single(row => row.Station == "Port 1").IsWishListed);
        Assert.Equal(
            "Steel: 200",
            model.Results.Single(row => row.Station == "Port 2").AvailableSupplies[0].TableLabel
        );
        model.Results.Single(row => row.Station == "Port 3").AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        Assert.Equal("Steel: 50", model.NeedsText);
        model.Results.Single(row => row.Station == "Port 2").AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        Assert.True(model.IsFullyPlanned);
        Assert.Empty(model.VisibleNeeds);
        Assert.All(model.Results, row => Assert.True(row.IsWishListed));
        Assert.Empty(model.NeedsText);
        Assert.Equal(300, model.CaptureState().WishList.Sum(item => item.Quantity));
        var wishList = new ColonizationWishListViewModel(items => model.RemoveWishListItems(items));
        wishList.Apply(model.CaptureState(), model.Needs.Select(need => need.Need).ToArray());
        Assert.Equal("Steel: 50", wishList.Stations.Single(station => station.Label == "Port 2").Children[0].Label);
        wishList.Stations.Single(station => station.Label == "Port 3").Children[0].RemoveCommand.Execute(null);
        Assert.False(model.IsFullyPlanned);
        Assert.Equal("Steel: 150", model.NeedsText);
        Assert.Equal(50, model.CaptureState().WishList.Single(item => item.MarketKey == "2").Quantity);
        Assert.Equal(1, search.Calls);
    }

    [Fact]
    public async Task StationAllocatesAllVisibleGoodsInOneSaveAndCommodityClicksFinishEachNeed()
    {
        ColonizationShoppingState? saved = null;
        int saves = 0;
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([
                Quote(1, "steel", 80),
                Quote(1, "water", 40),
                Quote(2, "steel", 100),
                Quote(2, "water", 50),
            ]),
            saveState: state =>
            {
                saved = state;
                saves++;
            }
        );
        await model.SearchAsync();
        int before = saves;
        model.Results[0].ToggleWishListCommand.Execute(null);
        Assert.Equal(before + 1, saves);
        Assert.Equal(2, saved!.WishList.Count);
        Assert.Equal("Steel: 20, Water: 10", model.NeedsText);
        Assert.Equal(2, model.Results.Count);
        Assert.True(model.Needs.All(need => need.IsSelected));
        model
            .Results.Single(row => row.Station == "Port 2")
            .AvailableSupplies.Single(supply => supply.Need.Commodity == "steel")
            .ToggleWishListCommand.Execute(null);
        Assert.Equal("Water: 10", model.NeedsText);
        Assert.Equal(
            "Water: 10",
            model
                .Results.Single(row => row.Station == "Port 2")
                .AvailableSupplies.Single(supply => supply.Need.Commodity == "water")
                .TableLabel
        );
        model
            .Results.Single(row => row.Station == "Port 2")
            .AvailableSupplies.Single(supply => supply.Need.Commodity == "water")
            .ToggleWishListCommand.Execute(null);
        Assert.True(model.IsFullyPlanned);
        Assert.Equal(150, saved.WishList.Sum(item => item.Quantity));
    }

    [Fact]
    public async Task PartialPurchaseQuantitiesSurviveFiltersAndRestoreWithoutNetwork()
    {
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([
                Quote(1, "steel", 80),
                Quote(1, "water", 100),
                Quote(2, "steel", 100) with
                {
                    Station = "Port 1",
                    System = "Achenar",
                },
            ])
        );
        await model.SearchAsync();
        model.Results[0].Supplies[0].ToggleWishListCommand.Execute(null);
        model.Results.Single(row => row.System == "Achenar").AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        model.Needs[1].IsSelected = false;
        model.MaximumArrivalText = "1";
        Assert.Empty(model.Results);
        var wishList = new ColonizationWishListViewModel();
        wishList.Apply(model.CaptureState(), model.Needs.Select(need => need.Need).ToArray());
        Assert.Equal(2, wishList.Stations.Count);
        Assert.Equal("Steel: 80", wishList.Stations[0].Children[0].Label);
        Assert.Equal("Steel: 20", wishList.Stations[1].Children[0].Label);
        Assert.True(wishList.Stations[0].HasSystem);
        Assert.False(wishList.Stations[0].Children[0].HasSystem);
        var offline = new SearchStub([]);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            offline,
            restoredState: model.CaptureState()
        );
        restored.MaximumArrivalText = "0";
        restored.Needs[1].IsSelected = true;
        Assert.Equal("Water: 50", restored.NeedsText);
        Assert.Equal(2, restored.Results.Count);
        Assert.Equal(
            "Water: 50",
            Assert
                .Single(
                    restored.Results.SelectMany(row => row.AvailableSupplies),
                    supply => supply.Need.Commodity == "water"
                )
                .TableLabel
        );
        Assert.Equal(100, restored.CaptureState().WishList.Sum(item => item.Quantity));
        Assert.Equal(0, offline.Calls);
    }

    [Fact]
    public async Task CommodityAndStationRemovalsReleaseAllTheirAllocations()
    {
        using var model = new ColonizationShoppingViewModel(
            [
                new ColonizationProject
                {
                    BuildId = "1",
                    Commodities = new() { ["steel"] = 300, ["water"] = 50 },
                },
            ],
            () => "Sol",
            new SearchStub([Quote(1, "steel", 100), Quote(1, "water", 50), Quote(2, "steel", 100)])
        );
        await model.SearchAsync();
        model.Results.Single(row => row.Station == "Port 1").ToggleWishListCommand.Execute(null);
        model.Results.Single(row => row.Station == "Port 2").ToggleWishListCommand.Execute(null);
        var wishList = new ColonizationWishListViewModel(items => model.RemoveWishListItems(items));
        ColonizationShoppingNeed[] needs = model.Needs.Select(need => need.Need).ToArray();
        wishList.Apply(model.CaptureState(), needs);
        wishList.Stations[0].Children[0].RemoveCommand.Execute(null);
        Assert.Equal(2, model.CaptureState().WishList.Count);
        Assert.Equal("Steel: 200", model.NeedsText);
        model.Results[0].AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        wishList.Apply(model.CaptureState(), needs);
        wishList.Stations[0].RemoveCommand.Execute(null);
        Assert.Equal("2", Assert.Single(model.CaptureState().WishList).MarketKey);
        Assert.Equal("Steel: 200, Water: 50", model.NeedsText);
    }

    [Fact]
    public async Task NewSearchClearsAllocationsEvenWhenRefreshFailsButInvalidCriteriaKeepThem()
    {
        var search = new SearchStub([Quote(1, "steel", 100)]);
        using var model = new ColonizationShoppingViewModel(Projects(), () => "Sol", search);
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.RadiusText = "bad";
        await model.SearchAsync();
        Assert.Single(model.CaptureState().WishList);
        model.RadiusText = "100";
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        search.Run = (_, _) => pending.Task;
        Task refresh = model.SearchAsync();
        Assert.Empty(model.CaptureState().WishList);
        Assert.Equal("Steel: 100, Water: 50", model.NeedsText);
        pending.SetException(new HttpRequestException("offline"));
        await refresh;
        Assert.Single(model.Results);
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.ClearSearch();
        Assert.Empty(model.CaptureState().WishList);
        Assert.Empty(model.Results);
        Assert.False(model.HasSearchResults);
        Assert.True(model.ShowSearchCriteria);
        Assert.Equal("Search Markets", model.SearchButtonText);
    }

    [Fact]
    public async Task ClearingDuringSearchDiscardsLateResultsAndIgnoresUnavailableSavedSelections()
    {
        var search = new SearchStub([Quote(1, "steel", 100)]);
        using var model = new ColonizationShoppingViewModel(Projects(), () => "Sol", search);
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        ColonizationShoppingState state = model.CaptureState();
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            new SearchStub([]),
            restoredState: state with
            {
                WishList = [new("missing", "steel"), new("1", "copper"), null!, .. state.WishList],
            }
        );
        Assert.Single(restored.CaptureState().WishList);
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        search.Run = (_, _) => pending.Task;
        Task work = model.SearchAsync();
        model.ClearSearch();
        pending.SetResult(new([Quote(2, "water", 100)], []));
        await work;
        Assert.Empty(model.Results);
        Assert.Empty(model.CaptureState().WishList);
        var wishList = new ColonizationWishListViewModel();
        wishList.Apply(null, []);
        Assert.False(wishList.HasItems);
    }
}
