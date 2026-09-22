using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

/// <summary>
/// Chooses surface materials for a profit search and the station that pays the most inside the radius.
/// </summary>
public static class SurfaceMiningSearchPlan
{
    public static IReadOnlyList<string> MaterialsFor(IEnumerable<string> selected)
    {
        string[] named = MiningMaterialSelection.Named(selected);
        if (named.Length > 0)
        {
            return named;
        }

        if (MiningMaterialSelection.IsAny(selected))
        {
            return PlanetaryMiningPlan.Materials;
        }

        return [MostProfitableMaterial()];
    }

    public static string MostProfitableMaterial() =>
        SurfaceMiningCommodityCatalog
            .HuntReferences.OrderByDescending(reference => reference.AverageGalacticPrice)
            .ThenBy(reference => reference.Material, StringComparer.Ordinal)
            .First()
            .Material;

    public static string SpanshReserve(string reserve) => reserve is "All" or "Unknown" or "" ? "" : reserve.Trim();

    public static MiningMarketResult? BestSell(IEnumerable<MiningMarketResult> markets) =>
        markets
            .Where(market => market.Price > 0 && market.Demand > 0)
            .OrderByDescending(market => market.Price)
            .ThenBy(market => market.Distance ?? double.MaxValue)
            .FirstOrDefault();
}
