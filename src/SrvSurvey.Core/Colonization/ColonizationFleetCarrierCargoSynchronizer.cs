using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public static class ColonizationFleetCarrierCargoSynchronizer
{
    public static IReadOnlyDictionary<string, int> CreateMarketReplacement(
        MarketSnapshot market,
        ColonizationFleetCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(carrier);
        if (market.MarketId != carrier.MarketId)
        {
            throw new ArgumentException(
                "The market snapshot does not belong to the Fleet Carrier.",
                nameof(market));
        }

        var currentCargo = carrier.Cargo
            .GroupBy(
                pair => ColonizationConstructionState.NormalizeCommodityName(
                    pair.Key),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(pair => Math.Max(0, pair.Value)),
                StringComparer.OrdinalIgnoreCase);
        var replacement = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in market.Items)
        {
            var commodity = item.Commodity;
            if (commodity.Length == 0)
            {
                continue;
            }

            var stock = Math.Max(0, item.Stock);
            var tracked = currentCargo.GetValueOrDefault(commodity);
            if (item.Producer && tracked != stock)
            {
                replacement[commodity] = stock;
            }
            else if (!item.Producer
                && !item.Consumer
                && tracked > 0)
            {
                replacement[commodity] = stock;
            }
        }

        return replacement;
    }
}
