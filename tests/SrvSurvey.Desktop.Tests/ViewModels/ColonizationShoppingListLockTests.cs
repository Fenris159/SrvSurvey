using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.ViewModels.ColonizationShoppingViewModelTests;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationWishListViewModelTests
{
    [Fact]
    public async Task LockedListSurvivesNewMarketsAndRemainsEditableUntilExplicitlyCleared()
    {
        var search = new SearchStub([Quote(1, "steel", 60)]);
        ColonizationShoppingState? saved = null;
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            search,
            saveState: state => saved = state
        );
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.IsWishListLocked = true;
        search.Run = (_, _) =>
            Task.FromResult(new ColonizationMarketSearchResult([Quote(2, "steel", 100), Quote(2, "water", 50)], []));

        await model.SearchAsync();

        Assert.Equal(60, Assert.Single(saved!.WishList).Quantity);
        Assert.DoesNotContain(saved.Quotes, quote => quote.MarketId == 1);
        Assert.Equal(1, Assert.Single(saved.WishListQuotes).MarketId);
        var list = new ColonizationWishListViewModel(items => model.RemoveWishListItems(items));
        list.Apply(saved, model.Needs.Select(need => need.Need).ToArray());
        Assert.Equal("Port 1", Assert.Single(list.Stations).Label);
        model.Results[0].ToggleWishListCommand.Execute(null);
        Assert.Equal(150, model.CaptureState().WishList.Sum(item => item.Quantity));
        list.Stations[0].RemoveCommand.Execute(null);
        Assert.Equal(90, model.CaptureState().WishList.Sum(item => item.Quantity));
        model.ClearWishListCommand.Execute(null);
        Assert.Empty(saved.WishList);
        Assert.Empty(saved.WishListQuotes);
        Assert.True(saved.IsWishListLocked);
        await model.SearchAsync();
        Assert.Empty(model.CaptureState().WishList);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedListSurvivesCanceledAndSupersededSearchesAndRestoresWithoutNetwork(bool clearSearch)
    {
        var search = new SearchStub([Quote(1, "steel", 60)]);
        ColonizationShoppingState? saved = null;
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            search,
            saveState: state => saved = state
        );
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.IsWishListLocked = true;
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        search.Run = (_, _) => pending.Task;
        Task refresh = model.SearchAsync();
        if (clearSearch)
        {
            model.ClearSearch();
            pending.SetResult(new([Quote(2, "water", 50)], []));
        }
        else
        {
            pending.SetException(new OperationCanceledException());
        }
        await refresh;
        Assert.Equal(60, Assert.Single(saved!.WishList).Quantity);
        if (clearSearch)
        {
            Assert.Empty(saved.Quotes);
            Assert.Empty(model.Results);
        }
        var offline = new SearchStub([]);
        using var restored = new ColonizationShoppingViewModel(
            Projects(),
            () => "Achenar",
            offline,
            restoredState: saved
        );
        Assert.True(restored.IsWishListLocked);
        Assert.Equal(60, Assert.Single(restored.CaptureState().WishList).Quantity);
        Assert.Equal(0, offline.Calls);
        restored.IsWishListLocked = false;
        Assert.Single(restored.CaptureState().WishList);
        await restored.SearchAsync();
        Assert.Empty(restored.CaptureState().WishList);
    }

    [Fact]
    public async Task LockKeepsSelectionsThroughAClosedPendingRequestAndZeroRemainingProjectNeeds()
    {
        var search = new SearchStub([Quote(1, "steel", 60)]);
        ColonizationShoppingState? saved = null;
        using var model = new ColonizationShoppingViewModel(
            Projects(),
            () => "Sol",
            search,
            saveState: state => saved = state
        );
        await model.SearchAsync();
        model.Results[0].ToggleWishListCommand.Execute(null);
        model.IsWishListLocked = true;
        var pending = new TaskCompletionSource<ColonizationMarketSearchResult>();
        search.Run = (_, _) => pending.Task;
        Task refresh = model.SearchAsync();
        model.Dispose();
        pending.SetResult(new([], []));
        await refresh;
        Assert.Single(saved!.WishList);
        using var restored = new ColonizationShoppingViewModel(
            [],
            () => "Sol",
            new SearchStub([]),
            restoredState: saved,
            refreshProjects: _ => Task.FromResult<IReadOnlyList<ColonizationProject>>([])
        );
        await restored.SearchAsync();
        Assert.Empty(restored.Needs);
        Assert.Equal(60, Assert.Single(restored.CaptureState().WishList).Quantity);
        var list = new ColonizationWishListViewModel();
        list.Apply(restored.CaptureState(), []);
        Assert.Equal("Steel: 60", Assert.Single(list.Stations).Children[0].Label);
        list.Apply(restored.CaptureState() with { IsWishListLocked = false }, []);
        Assert.False(list.HasItems);
        restored.ClearWishList();
        Assert.Empty(restored.CaptureState().WishList);
        restored.Dispose();
        restored.ClearWishList();
        restored.IsWishListLocked = false;
        Assert.True(restored.IsWishListLocked);
    }
}
