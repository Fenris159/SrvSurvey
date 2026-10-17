namespace SrvSurvey.Core.Colonization;

/// <summary>A fixed purchase allocation; zero requests an amount capped by stock and remaining need.</summary>
public sealed record ColonizationWishListItem(string MarketKey, string Commodity, long Quantity = 0)
{
    public (string MarketKey, string Commodity) Key() => (MarketKey, Commodity);
}

/// <summary>Allocates market stock in click order, preserving chosen quantities and preventing overbuying.</summary>
public sealed class ColonizationShoppingPlan
{
    private readonly IReadOnlyList<ColonizationShoppingNeed> needs;
    private readonly Dictionary<string, long> remaining;
    private readonly Dictionary<(string MarketKey, string Commodity), ColonizationMarketQuote> markets;
    private readonly Dictionary<(string MarketKey, string Commodity), ColonizationWishListItem> chosen = [];
    private readonly List<ColonizationWishListItem> items = [];

    public ColonizationShoppingPlan(
        IReadOnlyList<ColonizationShoppingNeed> needs,
        IReadOnlyList<ColonizationMarketQuote> quotes,
        IEnumerable<ColonizationWishListItem>? selections = null
    )
    {
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(quotes);
        this.needs = needs;
        remaining = needs.ToDictionary(
            need => need.Commodity,
            need => Math.Max(0, need.Required),
            StringComparer.Ordinal
        );
        markets = ColonizationShopping
            .Merge(quotes)
            .ToDictionary(quote => (ColonizationShopping.MarketKey(quote), quote.Commodity));
        foreach (ColonizationWishListItem item in (selections ?? []).Where(item => item is not null))
        {
            Add(item);
        }
    }

    public IReadOnlyList<ColonizationWishListItem> Snapshot() => items.ToArray();

    public IReadOnlyList<ColonizationShoppingNeed> GetRemainingNeeds() =>
        needs
            .Where(need => Remaining(need.Commodity) > 0)
            .Select(need => need with { Required = Remaining(need.Commodity) })
            .ToArray();

    public IReadOnlyList<ColonizationMarketQuote> GetAvailableQuotes() =>
        markets
            .Values.Select(quote =>
                quote with
                {
                    Stock =
                        quote.Stock
                        - (
                            chosen.GetValueOrDefault((ColonizationShopping.MarketKey(quote), quote.Commodity))?.Quantity
                            ?? 0
                        ),
                }
            )
            .Where(quote => quote.Stock > 0)
            .ToArray();

    public long Remaining(string commodity) => remaining.GetValueOrDefault(commodity);

    public void Select(IReadOnlyList<ColonizationWishListItem> selections)
    {
        foreach (ColonizationWishListItem item in selections)
        {
            Add(item, allowTopUp: true);
        }
    }

    public void Remove(IReadOnlyList<ColonizationWishListItem> removals)
    {
        var keys = removals.Select(item => item.Key()).ToHashSet();
        foreach (ColonizationWishListItem item in items.Where(item => keys.Contains(item.Key())).ToArray())
        {
            remaining[item.Commodity] += item.Quantity;
            chosen.Remove(item.Key());
            items.Remove(item);
        }
    }

    private void Add(ColonizationWishListItem item, bool allowTopUp = false)
    {
        ColonizationWishListItem? existing = chosen.GetValueOrDefault(item.Key());
        if (
            item.Quantity < 0
            || (existing is not null && !allowTopUp)
            || !markets.TryGetValue(item.Key(), out ColonizationMarketQuote? quote)
        )
        {
            return;
        }
        long quantity = Math.Min(
            Remaining(item.Commodity),
            Math.Min(quote.Stock - (existing?.Quantity ?? 0), item.Quantity == 0 ? quote.Stock : item.Quantity)
        );
        if (quantity <= 0)
        {
            return;
        }
        ColonizationWishListItem allocation = item with { Quantity = quantity + (existing?.Quantity ?? 0) };
        chosen[item.Key()] = allocation;
        if (existing is not null)
        {
            items[items.IndexOf(existing)] = allocation;
        }
        else
        {
            items.Add(allocation);
        }
        remaining[item.Commodity] -= quantity;
    }
}
