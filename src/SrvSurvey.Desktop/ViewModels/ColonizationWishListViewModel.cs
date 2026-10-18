using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Projects saved market selections into a tree that remains available when the search window is closed.</summary>
public sealed class ColonizationWishListViewModel(Action<IReadOnlyList<ColonizationWishListItem>>? removeItems = null)
    : WorkspaceObservable
{
    private IReadOnlyList<ColonizationShoppingNeed>? previousNeeds;
    private IReadOnlyList<ColonizationMarketQuote> previousQuotes = [];
    private IReadOnlyList<ColonizationWishListItem> previousAllocations = [];

    public IReadOnlyList<ColonizationWishListNode> Stations { get; private set; } = [];
    public bool HasItems => Stations.Count > 0;
    public string Summary =>
        $"{Stations.Count:N0} stations · {Stations.Sum(station => station.Children.Count):N0} commodities";

    internal void Apply(ColonizationShoppingState? state, IReadOnlyList<ColonizationShoppingNeed> needs)
    {
        IReadOnlyList<ColonizationMarketQuote> quotes = state?.Quotes ?? [];
        IReadOnlyList<ColonizationWishListItem> allocations = state?.WishList ?? [];
        if (
            previousNeeds is not null
            && previousNeeds.SequenceEqual(needs)
            && previousQuotes.SequenceEqual(quotes)
            && previousAllocations.SequenceEqual(allocations)
        )
        {
            return;
        }

        var plan = new ColonizationShoppingPlan(needs, state?.Quotes ?? [], state?.WishList);
        var requirements = needs.ToDictionary(need => need.Commodity, StringComparer.Ordinal);
        var markets = ColonizationShopping
            .Merge(state?.Quotes ?? [])
            .ToDictionary(quote => (ColonizationShopping.MarketKey(quote), quote.Commodity));
        Stations = plan.Snapshot()
            .GroupBy(item => item.MarketKey)
            .OrderBy(group => markets[group.First().Key()].Distance)
            .ThenBy(group => markets[group.First().Key()].Station, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ColonizationWishListNode(
                markets[group.First().Key()].Station,
                markets[group.First().Key()].System,
                group
                    .OrderBy(item => requirements[item.Commodity].DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ColonizationWishListNode(
                        $"{requirements[item.Commodity].DisplayName}: {item.Quantity:N0}",
                        string.Empty,
                        [],
                        new WorkspaceCommand(() => removeItems?.Invoke([item]))
                    ))
                    .ToArray(),
                new WorkspaceCommand(() => removeItems?.Invoke(group.ToArray()))
            ))
            .ToArray();
        previousNeeds = needs.ToArray();
        previousQuotes = quotes.ToArray();
        previousAllocations = allocations.ToArray();
        Changed(nameof(Stations));
        Changed(nameof(HasItems));
        Changed(nameof(Summary));
    }
}

public sealed record ColonizationWishListNode(
    string Label,
    string System,
    IReadOnlyList<ColonizationWishListNode> Children,
    System.Windows.Input.ICommand RemoveCommand
)
{
    public bool HasSystem => System.Length > 0;
}
