using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>The last shopping workspace, retained locally without fetching markets on restore.</summary>
public sealed record ColonizationShoppingState
{
    public string ReferenceSystem { get; init; } = string.Empty;
    public string RadiusText { get; init; } = "100";
    public string MaximumAgeText { get; init; } = "7";
    public string MaximumArrivalText { get; init; } = "0";
    public string MinimumPad { get; init; } = "Large";
    public string Sort { get; init; } = "Distance";
    public bool ReverseSort { get; init; }
    public bool ExcludeCarriers { get; init; } = true;
    public bool ExcludeSurface { get; init; }
    public bool RequireFullSupply { get; init; }
    public bool UseCarrierDifference { get; init; } = true;
    public bool HasCarrierDifferencePreference { get; init; }
    public long SelectedCarrierMarketId { get; init; }
    public bool ShowShortSupply { get; init; } = true;
    public bool ShowSearchCriteria { get; init; } = true;
    public IReadOnlyList<string> ExcludedCommodities { get; init; } = [];
    public IReadOnlyList<string> ExpandedMarkets { get; init; } = [];
    public IReadOnlyList<string> SearchedCommodities { get; init; } = [];
    public IReadOnlyList<ColonizationWishListItem> WishList { get; init; } = [];
    public bool IsWishListLocked { get; init; }
    public IReadOnlyList<ColonizationMarketQuote> WishListQuotes { get; init; } = [];
    public IReadOnlyList<string> AppliedPurchaseIds { get; init; } = [];
    public IReadOnlyList<ColonizationMarketQuote> Quotes { get; init; } = [];
    public string Warnings { get; init; } = string.Empty;
    public DateTimeOffset? CompletedAt { get; init; }
    public string CompletedSystem { get; init; } = string.Empty;
    public int CompletedRadius { get; init; }

    // Older snapshots predate the enabled-by-default carrier difference preference.
    internal bool EffectiveUseCarrierDifference => !HasCarrierDifferencePreference || UseCarrierDifference;

    internal IReadOnlyList<ColonizationMarketQuote> PlanningQuotes =>
        ColonizationShopping.Merge(Quotes.Concat(WishListQuotes));

    internal bool IsValid =>
        SelectedCarrierMarketId >= 0
        && ReferenceSystem is not null
        && RadiusText is not null
        && MaximumAgeText is not null
        && MaximumArrivalText is not null
        && MinimumPad is not null
        && Sort is not null
        && Warnings is not null
        && CompletedSystem is not null
        && ExcludedCommodities is not null
        && ExpandedMarkets is not null
        && SearchedCommodities is not null
        && WishList is not null
        && AppliedPurchaseIds is not null
        && AppliedPurchaseIds.All(id => !string.IsNullOrWhiteSpace(id))
        && Quotes is not null
        && WishListQuotes is not null
        && Quotes
            .Concat(WishListQuotes)
            .All(quote =>
                quote is not null
                && !string.IsNullOrWhiteSpace(quote.System)
                && !string.IsNullOrWhiteSpace(quote.Station)
                && !string.IsNullOrWhiteSpace(quote.Commodity)
                && quote.StationType is not null
                && quote.Source is not null
            );
}

internal sealed record ColonizationShoppingSession(IReadOnlyList<string> ProjectIds, ColonizationShoppingState? Search);
