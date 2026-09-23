using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

/// <summary>
/// Chooses surface materials for a profit search and the station that pays the most inside the radius.
/// </summary>
public static class SurfaceMiningSearchPlan
{
    public static IReadOnlyList<string> MaterialsFor(
        IEnumerable<string> selected,
        IReadOnlyDictionary<string, MiningCommodityPriceSummary>? dailyPrices = null
    )
    {
        string[] named = MiningMaterialSelection.Named(selected);
        if (named.Length > 0)
        {
            return named;
        }

        if (MiningMaterialSelection.IsAny(selected))
        {
            return SurfaceMiningCommodityCatalog
                .HuntReferences.OrderByDescending(reference =>
                    SurfaceMiningCommodityPrices.Find(reference.Material, dailyPrices)?.AverageSellPrice
                        is > 0
                            and var price
                        ? price
                        : reference.AverageGalacticPrice
                )
                .ThenBy(reference => reference.Material, StringComparer.Ordinal)
                .Select(reference => reference.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return [MostProfitableMaterial()];
    }

    public static string MostProfitableMaterial() =>
        SurfaceMiningCommodityCatalog
            .HuntReferences.OrderByDescending(reference => reference.AverageGalacticPrice)
            .ThenBy(reference => reference.Material, StringComparer.Ordinal)
            .First()
            .Material;

    public static MiningMarketResult? BestSell(IEnumerable<MiningMarketResult> markets) =>
        markets
            .Where(market => market.Price > 0 && market.Demand > 0)
            .OrderByDescending(market => market.Price)
            .ThenBy(market => market.Distance ?? double.MaxValue)
            .FirstOrDefault();
}

public static class SurfaceMiningCommodityPrices
{
    public static MiningCommodityPriceSummary? Find(
        string material,
        IReadOnlyDictionary<string, MiningCommodityPriceSummary>? report
    )
    {
        if (report is null)
        {
            return null;
        }

        if (report.TryGetValue(material, out MiningCommodityPriceSummary? exact))
        {
            return exact;
        }

        return report.Values.FirstOrDefault(item => MiningCommodityName.Same(item.Commodity, material));
    }
}
