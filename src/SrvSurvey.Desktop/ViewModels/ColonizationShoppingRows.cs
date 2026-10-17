using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ShoppingNeedViewModel : WorkspaceObservable
{
    private bool isSelected = true;
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
        get => isSelected;
        set => Set(ref isSelected, value);
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
        bool showShortSupply = true
    )
    {
        this.expansionChanged = expansionChanged;
        this.wishListChanged = wishListChanged;
        isExpanded = initiallyExpanded;
        key = ColonizationShopping.MarketKey(quotes[0]);
        ToggleExpandedCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
        ColonizationMarketQuote station = quotes.OrderByDescending(quote => quote.UpdatedAt).First();
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
            .Select(need => new ShoppingSupplyViewModel(
                need,
                quotes.FirstOrDefault(quote => quote.Commodity == need.Commodity),
                false,
                supply => SetWishListed([supply], !supply.IsWishListed)
            ))
            .ToArray();
        AvailableSupplies = Supplies
            .Where(supply => supply.Stock > 0 && (showShortSupply || supply.IsEnough))
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
    public ColonizationShoppingNeed Need { get; }
    public ColonizationMarketQuote? Quote { get; }
    public bool IsWishListed => isWishListed;
    public System.Windows.Input.ICommand ToggleWishListCommand { get; }

    public ShoppingSupplyViewModel(
        ColonizationShoppingNeed need,
        ColonizationMarketQuote? quote,
        bool initiallyWishListed = false,
        Action<ShoppingSupplyViewModel>? toggle = null
    )
    {
        Need = need;
        Quote = quote;
        isWishListed = initiallyWishListed;
        ToggleWishListCommand = new WorkspaceCommand(() => toggle?.Invoke(this));
    }

    internal void SetWishListed(bool value) => Set(ref isWishListed, value, nameof(IsWishListed));

    public long Stock => Quote?.Stock ?? 0;
    public bool IsEnough => Stock >= Need.Required;
    public string Label => $"{Need.DisplayName}: {Stock:N0} / {Need.Required:N0}";
    public string TableLabel => $"{Need.DisplayName}: {Math.Min(Stock, Need.Required):N0}";
    public string Detail =>
        Quote is null
            ? "No supply reported"
            : $"Still need {Need.Required:N0} · Location Stock {Stock:N0} · {Quote.Price:N0} CR/t · {Quote.Source}";
}
