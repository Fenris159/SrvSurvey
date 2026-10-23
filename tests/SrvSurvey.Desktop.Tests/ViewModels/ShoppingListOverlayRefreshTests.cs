using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.ViewModels.ColonizationShoppingViewModelTests;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationWishListViewModelTests
{
    /// <summary>Equivalent shopping inputs keep rows alive, but new market identities update their removal commands.</summary>
    [Fact]
    public void OverlayRefreshRetainsShoppingRowsWithoutKeepingStaleCommands()
    {
        var removed = new List<ColonizationWishListItem>();
        var model = new ColonizationWishListViewModel(items => removed.AddRange(items));
        ColonizationShoppingNeed[] needs = [new("steel", "Steel", 100)];
        var state = new ColonizationShoppingState
        {
            Quotes = [Quote(1, "steel", 100)],
            WishList = [new("1", "steel", 40)],
        };
        model.Apply(state, needs);
        IReadOnlyList<ColonizationWishListNode> stations = model.Stations;
        Assert.Single(stations);
        model.Apply(
            state with
            {
                Quotes = state.Quotes.ToArray(),
                WishList = state.WishList.ToArray(),
            },
            needs.ToArray()
        );
        Assert.Same(stations, model.Stations);

        model.Apply(
            state with
            {
                Quotes = [state.Quotes[0] with { MarketId = 2 }],
                WishList = [new("2", "steel", 40)],
            },
            needs
        );
        Assert.NotSame(stations, model.Stations);
        Assert.Single(Assert.Single(model.Stations).Children).RemoveCommand.Execute(null);
        Assert.Equal("2", Assert.Single(removed).MarketKey);

        model.Apply(null, []);
        Assert.Empty(model.Stations);
    }
}
