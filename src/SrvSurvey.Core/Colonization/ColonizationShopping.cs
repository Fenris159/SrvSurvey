namespace SrvSurvey.Core.Colonization;

public sealed record ColonizationShoppingNeed(string Commodity, string DisplayName, long Required);

public sealed record ColonizationMarketQuery(
    string ReferenceSystem,
    int Radius,
    IReadOnlyList<ColonizationShoppingNeed> Needs,
    int MaximumAgeDays = 7
)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ReferenceSystem);
        ArgumentOutOfRangeException.ThrowIfLessThan(Radius, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(Radius, 500);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumAgeDays, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumAgeDays, 30);
        ArgumentNullException.ThrowIfNull(Needs);
    }
}

/// <summary>A single observation; providers' stock counts must never be added together.</summary>
public sealed record ColonizationMarketQuote(
    string System,
    string Station,
    long MarketId,
    string StationType,
    int PadSize,
    double Distance,
    double Arrival,
    string Commodity,
    long Stock,
    long Price,
    DateTimeOffset UpdatedAt,
    string Source
);

public sealed record ColonizationMarketSearchResult(
    IReadOnlyList<ColonizationMarketQuote> Quotes,
    IReadOnlyList<string> Warnings
);

public interface IColonizationMarketSearch
{
    Task<ColonizationMarketSearchResult> SearchAsync(
        ColonizationMarketQuery query,
        CancellationToken cancellationToken = default
    );
}

public static class ColonizationShopping
{
    public static string DisplayName(string commodity) => ColonizationMarketCommodities.DisplayName(commodity);

    /// <summary>Sums remaining requirements once per selected, unfinished build, including planned builds.</summary>
    public static IReadOnlyList<ColonizationShoppingNeed> Aggregate(IEnumerable<ColonizationProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        return projects
            .Where(project => !project.IsComplete)
            .DistinctBy(project => project.BuildId, StringComparer.OrdinalIgnoreCase)
            .SelectMany(project => project.Commodities)
            .Where(pair => pair.Value > 0)
            .GroupBy(pair => ColonizationMarketCommodities.Key(pair.Key), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .Select(group => new ColonizationShoppingNeed(
                group.Key,
                ColonizationMarketCommodities.DisplayName(group.Key),
                group.Sum(pair => (long)pair.Value)
            ))
            .OrderBy(need => need.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Subtracts manifest stock once per linked carrier, normalizing legacy commodity aliases.</summary>
    public static IReadOnlyList<ColonizationShoppingNeed> SubtractCarrierCargo(
        IEnumerable<ColonizationShoppingNeed> needs,
        IEnumerable<ColonizationFleetCarrier> carriers,
        long selectedMarketId = 0
    )
    {
        var stock = carriers
            .Where(carrier => carrier.MarketId > 0 && (selectedMarketId == 0 || carrier.MarketId == selectedMarketId))
            .DistinctBy(carrier => carrier.MarketId)
            .SelectMany(carrier =>
                carrier
                    .Cargo.GroupBy(pair => ColonizationMarketCommodities.Key(pair.Key), StringComparer.Ordinal)
                    .Select(group => (Commodity: group.Key, Stock: Math.Max(0L, group.Max(pair => (long)pair.Value))))
            )
            .GroupBy(item => item.Commodity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Stock), StringComparer.Ordinal);
        return needs
            .Select(need =>
                need with
                {
                    Required = Math.Max(0, need.Required - stock.GetValueOrDefault(need.Commodity)),
                }
            )
            .Where(need => need.Required > 0)
            .ToArray();
    }

    /// <summary>Estimates each project's remaining needs covered by stock, capping surplus for each commodity.</summary>
    public static double CarrierCoveragePercent(
        ColonizationProject project,
        IEnumerable<ColonizationFleetCarrier> carriers,
        long selectedMarketId = 0
    )
    {
        IReadOnlyList<ColonizationShoppingNeed> needs = Aggregate([project]);
        long total = needs.Sum(need => need.Required);
        if (total == 0)
        {
            return project.IsComplete || project.Commodities.Count > 0 ? 100 : 0;
        }
        long remaining = SubtractCarrierCargo(needs, carriers, selectedMarketId).Sum(need => need.Required);
        return (total - remaining) * 100d / total;
    }

    /// <summary>Chooses the latest quote for each market and commodity, preferring Ardent on timestamp ties.</summary>
    public static IReadOnlyList<ColonizationMarketQuote> Merge(IEnumerable<ColonizationMarketQuote> quotes) =>
        quotes
            .Where(quote => quote.Stock > 0)
            .GroupBy(quote => (MarketKey(quote), ColonizationMarketCommodities.Key(quote.Commodity)))
            .Select(group =>
                group
                    .OrderByDescending(quote => quote.UpdatedAt)
                    .ThenBy(quote => quote.Source == "Ardent" ? 0 : 1)
                    .First()
            )
            .ToArray();

    public static string MarketKey(ColonizationMarketQuote quote) =>
        quote.MarketId > 0
            ? quote.MarketId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : quote.System.ToUpperInvariant() + "/" + quote.Station.ToUpperInvariant();
}
