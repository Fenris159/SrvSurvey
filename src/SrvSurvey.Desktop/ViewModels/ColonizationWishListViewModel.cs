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
    private int shipCargoCapacity;
    private bool previousIsLocked;

    public IReadOnlyList<ColonizationWishListNode> Stations { get; private set; } = [];
    public bool HasItems => Stations.Count > 0;
    public string Summary =>
        $"{Stations.Count:N0} stations · {Stations.Sum(station => station.Children.Count):N0} commodities";
    public long? TotalTripsInCurrentShip =>
        shipCargoCapacity > 0 ? Stations.Sum(station => station.TripsInCurrentShip ?? 0) : null;
    public string TotalTripsText =>
        TotalTripsInCurrentShip is long trips
            ? $"Total estimated trips: {trips:N0} · Ship capacity: {shipCargoCapacity:N0} t"
            : "Total estimated trips: ship capacity unknown";

    internal void UpdateShipCapacity(int capacity)
    {
        capacity = Math.Max(0, capacity);
        if (capacity == shipCargoCapacity)
        {
            return;
        }
        shipCargoCapacity = capacity;
        Stations = Stations.Select(station => station with { ShipCargoCapacity = capacity }).ToArray();
        Changed(nameof(Stations));
        NotifyTrips();
    }

    internal void Apply(ColonizationShoppingState? state, IReadOnlyList<ColonizationShoppingNeed> needs)
    {
        IReadOnlyList<ColonizationMarketQuote> quotes = state?.PlanningQuotes ?? [];
        IReadOnlyList<ColonizationWishListItem> allocations = state?.WishList ?? [];
        if (
            previousNeeds is not null
            && previousIsLocked == (state?.IsWishListLocked == true)
            && previousNeeds.SequenceEqual(needs)
            && previousQuotes.SequenceEqual(quotes)
            && previousAllocations.SequenceEqual(allocations)
        )
        {
            return;
        }

        var plan = new ColonizationShoppingPlan(needs, quotes, state?.WishList, state?.IsWishListLocked == true);
        var requirements = needs.ToDictionary(need => need.Commodity, StringComparer.Ordinal);
        var markets = ColonizationShopping
            .Merge(quotes)
            .ToDictionary(quote => (ColonizationShopping.MarketKey(quote), quote.Commodity));
        Stations = plan.Snapshot()
            .GroupBy(item => item.MarketKey)
            .OrderBy(group => markets[group.First().Key()].Distance)
            .ThenBy(group => markets[group.First().Key()].Station, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ColonizationWishListNode(
                markets[group.First().Key()].Station,
                markets[group.First().Key()].System,
                group
                    .OrderBy(item => DisplayName(item.Commodity), StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ColonizationWishListNode(
                        $"{DisplayName(item.Commodity)}: {item.Quantity:N0}",
                        string.Empty,
                        [],
                        new WorkspaceCommand(() => removeItems?.Invoke([item]))
                    ))
                    .ToArray(),
                new WorkspaceCommand(() => removeItems?.Invoke(group.ToArray())),
                group.Sum(item => item.Quantity),
                shipCargoCapacity
            ))
            .ToArray();
        previousNeeds = needs.ToArray();
        previousIsLocked = state?.IsWishListLocked == true;
        previousQuotes = quotes.ToArray();
        previousAllocations = allocations.ToArray();
        Changed(nameof(Stations));
        Changed(nameof(HasItems));
        Changed(nameof(Summary));
        NotifyTrips();

        string DisplayName(string commodity) =>
            requirements.GetValueOrDefault(commodity)?.DisplayName ?? ColonizationShopping.DisplayName(commodity);
    }

    private void NotifyTrips()
    {
        Changed(nameof(TotalTripsInCurrentShip));
        Changed(nameof(TotalTripsText));
    }
}

public sealed record ColonizationWishListNode(
    string Label,
    string System,
    IReadOnlyList<ColonizationWishListNode> Children,
    System.Windows.Input.ICommand RemoveCommand,
    long Tonnes = 0,
    int ShipCargoCapacity = 0
)
{
    public bool HasSystem => System.Length > 0;
    public long? TripsInCurrentShip => ColonizationProjectPreview.Trips(Tonnes, ShipCargoCapacity);
    public string TripsText =>
        TripsInCurrentShip is long trips ? $"Estimated trips remaining: {trips:N0}" : "Trips: capacity unknown";
}
