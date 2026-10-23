using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ShoppingNeedViewModel : WorkspaceObservable
{
    private bool isSelected = true;
    private bool isLocallyExcluded;
    private long remaining;

    public ShoppingNeedViewModel(ColonizationShoppingNeed need)
    {
        Need = need;
        remaining = need.Required;
    }

    public ColonizationShoppingNeed Need { get; }
    public long Remaining => remaining;
    public string Label => $"{Need.DisplayName}: {Remaining:N0}";
    public bool IsSelected
    {
        get => isSelected && !IsLocallyExcluded;
        set => Set(ref isSelected, value);
    }

    internal bool IsManuallySelected => isSelected;

    /// <summary>Whether the active local-stock filter covers this need.</summary>
    public bool IsLocallyExcluded => isLocallyExcluded;

    /// <summary>Explains why a locally covered commodity is unavailable as a search filter.</summary>
    public string SelectionHint =>
        IsLocallyExcluded
            ? "Covered by local market stock. Turn off Exclude Local Commodities to select it."
            : "Toggle this commodity in market results";

    internal void SetLocallyExcluded(bool value)
    {
        if (Set(ref isLocallyExcluded, value, nameof(IsLocallyExcluded)))
        {
            Changed(nameof(IsSelected));
            Changed(nameof(SelectionHint));
        }
    }

    internal void SetRemaining(long value)
    {
        if (Set(ref remaining, value, nameof(Remaining)))
        {
            Changed(nameof(Label));
        }
    }
}

public sealed class ShoppingMarketRowViewModel : WorkspaceObservable
{
    private bool isExpanded;
    private readonly Action<string, bool>? expansionChanged;
    private readonly string key;
    private readonly DateTimeOffset oldestObservation;
    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (Set(ref isExpanded, value))
            {
                Changed(nameof(ExpansionGlyph));
                expansionChanged?.Invoke(key, value);
            }
        }
    }
    public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
    public System.Windows.Input.ICommand ToggleExpandedCommand { get; }
    public System.Windows.Input.ICommand ToggleWishListCommand { get; }
    public bool IsWishListed => AvailableSupplies.Count > 0 && AvailableSupplies.All(supply => supply.IsWishListed);
    private readonly Action<IReadOnlyList<ColonizationWishListItem>, bool>? wishListChanged;
    public IReadOnlyList<ShoppingSupplyViewModel> AvailableSupplies { get; }

    public ShoppingMarketRowViewModel(
        IReadOnlyList<ColonizationMarketQuote> quotes,
        IReadOnlyList<ColonizationShoppingNeed> needs,
        bool initiallyExpanded = false,
        Action<string, bool>? expansionChanged = null,
        Action<IReadOnlyList<ColonizationWishListItem>, bool>? wishListChanged = null,
        bool showShortSupply = true,
        IReadOnlyList<ColonizationWishListItem>? wishList = null
    )
    {
        this.expansionChanged = expansionChanged;
        this.wishListChanged = wishListChanged;
        isExpanded = initiallyExpanded;
        key = ColonizationShopping.MarketKey(quotes[0]);
        ToggleExpandedCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
        ColonizationMarketQuote station = quotes.OrderByDescending(quote => quote.UpdatedAt).First();
        oldestObservation = quotes.Min(quote => quote.UpdatedAt);
        ApplyMarketAge(7);
        Station = station.Station;
        System = station.System;
        StationType = station.StationType;
        Distance = station.Distance;
        Arrival = station.Arrival;
        Pad = station.PadSize switch
        {
            3 => "L",
            2 => "M",
            1 => "S",
            _ => "?",
        };
        Supplies = needs
            .Select(need =>
            {
                long allocated =
                    wishList
                        ?.FirstOrDefault(item => item.MarketKey == key && item.Commodity == need.Commodity)
                        ?.Quantity
                    ?? 0;
                return new ShoppingSupplyViewModel(
                    need with
                    {
                        Required = need.Required + allocated,
                    },
                    quotes.FirstOrDefault(quote => quote.Commodity == need.Commodity),
                    allocated > 0,
                    supply => SetWishListed([supply], !supply.IsWishListed),
                    allocated
                );
            })
            .ToArray();
        AvailableSupplies = Supplies
            .Where(supply =>
                supply.IsWishListed
                || supply.Need.Required > 0 && supply.Stock > 0 && (showShortSupply || supply.IsEnough)
            )
            .ToArray();
        ToggleWishListCommand = new WorkspaceCommand(() => SetWishListed(AvailableSupplies, !IsWishListed));
    }

    private void SetWishListed(IReadOnlyList<ShoppingSupplyViewModel> supplies, bool selected)
    {
        foreach (ShoppingSupplyViewModel supply in supplies)
        {
            supply.SetWishListed(selected);
        }
        Changed(nameof(IsWishListed));
        wishListChanged?.Invoke(
            supplies.Select(supply => new ColonizationWishListItem(key, supply.Need.Commodity)).ToArray(),
            selected
        );
    }

    public string Station { get; }
    public string System { get; }
    public string StationType { get; }

    /// <summary>Identifies station rows containing stock estimates older than the selected market age.</summary>
    public bool IsOutdated { get; private set; }

    /// <summary>Shows the observation date so old local stock cannot be mistaken for current availability.</summary>
    public string OutdatedMarketWarning { get; private set; } = string.Empty;

    internal void ApplyMarketAge(int maximumAgeDays)
    {
        IsOutdated = oldestObservation < DateTimeOffset.UtcNow.AddDays(-maximumAgeDays);
        OutdatedMarketWarning = IsOutdated
            ? $"Outdated market data from {oldestObservation:yyyy-MM-dd}. Verify stock before travelling."
            : string.Empty;
    }

    public string Pad { get; }
    public double Distance { get; }
    public double Arrival { get; }
    public string DistanceText => $"{Distance:N1} ly";
    public string ArrivalText => $"{Arrival:N0} ls";
    public IReadOnlyList<ShoppingSupplyViewModel> Supplies { get; }
    public int Matches => AvailableSupplies.Count;
    public bool CanFulfillAll => Supplies.Count > 0 && Supplies.All(supply => supply.IsEnough);
}

public sealed class ShoppingSupplyViewModel : WorkspaceObservable
{
    private bool isWishListed;
    private readonly long allocatedQuantity;
    public ColonizationShoppingNeed Need { get; }
    public ColonizationMarketQuote? Quote { get; }
    public bool IsWishListed => isWishListed;
    public System.Windows.Input.ICommand ToggleWishListCommand { get; }

    public ShoppingSupplyViewModel(
        ColonizationShoppingNeed need,
        ColonizationMarketQuote? quote,
        bool initiallyWishListed = false,
        Action<ShoppingSupplyViewModel>? toggle = null,
        long allocatedQuantity = 0
    )
    {
        Need = need;
        Quote = quote;
        isWishListed = initiallyWishListed;
        this.allocatedQuantity = allocatedQuantity;
        ToggleWishListCommand = new WorkspaceCommand(() => toggle?.Invoke(this));
    }

    internal void SetWishListed(bool value) => Set(ref isWishListed, value, nameof(IsWishListed));

    public long Stock => Quote?.Stock ?? 0;
    public bool IsEnough => Stock >= Need.Required;
    public string Label => $"{Need.DisplayName}: {Stock:N0} / {Need.Required:N0}";
    public string TableLabel =>
        $"{Need.DisplayName}: {(IsWishListed ? allocatedQuantity : Math.Min(Stock, Need.Required)):N0}";
    public string PlanningDetail => $"Still to plan: {Math.Max(0, Need.Required - allocatedQuantity):N0}";
    public string AllocationDetail => $"On ShoppingList here: {allocatedQuantity:N0}";
    public string StockDetail =>
        Quote is null ? "No supply reported" : $"Location Stock {Stock:N0} · {Quote.Price:N0} CR/t · {Quote.Source}";
    public string Detail =>
        Quote is null ? StockDetail : string.Join('\n', PlanningDetail, AllocationDetail, StockDetail);
}
