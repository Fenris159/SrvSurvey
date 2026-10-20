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
        IEnumerable<ColonizationWishListItem>? selections = null,
        bool preserveSelections = false
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
            Add(item, preserveQuantity: preserveSelections);
        }
    }

    public IReadOnlyList<ColonizationWishListItem> Snapshot() => items.ToArray();

    /// <summary>Retains the station metadata required to display selections after another market search.</summary>
    public IReadOnlyList<ColonizationMarketQuote> SnapshotQuotes() =>
        items.Select(item => markets[item.Key()]).ToArray();

    /// <summary>Credits purchases from any market against the smallest matching allocations first.</summary>
    public static IReadOnlyList<ColonizationWishListItem> ConsumePurchase(
        IReadOnlyList<ColonizationWishListItem> allocations,
        string commodity,
        long quantity
    )
    {
        ArgumentNullException.ThrowIfNull(allocations);
        if (quantity <= 0)
        {
            return allocations;
        }
        string key = ColonizationMarketCommodities.Key(commodity);
        var updated = allocations.ToList();
        foreach (
            ColonizationWishListItem item in allocations
                .Where(item => item.Quantity > 0 && ColonizationMarketCommodities.Key(item.Commodity) == key)
                .OrderBy(item => item.Quantity)
        )
        {
            long consumed = Math.Min(quantity, item.Quantity);
            int index = updated.IndexOf(item);
            if (consumed == item.Quantity)
            {
                updated.RemoveAt(index);
            }
            else
            {
                updated[index] = item with { Quantity = item.Quantity - consumed };
            }
            quantity -= consumed;
            if (quantity == 0)
            {
                break;
            }
        }
        return updated;
    }

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
            chosen.Remove(item.Key());
            items.Remove(item);
            long required = Math.Max(0, needs.FirstOrDefault(need => need.Commodity == item.Commodity)?.Required ?? 0);
            remaining[item.Commodity] = items
                .Where(allocation => allocation.Commodity == item.Commodity)
                .Aggregate(required, (needed, allocation) => Math.Max(0, needed - allocation.Quantity));
        }
    }

    private void Add(ColonizationWishListItem item, bool allowTopUp = false, bool preserveQuantity = false)
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
        long requested = item.Quantity == 0 ? quote.Stock : item.Quantity;
        long quantity =
            preserveQuantity && item.Quantity > 0
                ? item.Quantity
                : Math.Min(Remaining(item.Commodity), Math.Min(quote.Stock - (existing?.Quantity ?? 0), requested));
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
        remaining[item.Commodity] = Math.Max(0, Remaining(item.Commodity) - quantity);
    }
}
