using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

public sealed record PowerplayMeritStation(
    string Name,
    string Type,
    string Pad,
    long Price,
    long Demand,
    string Detail
);

public sealed record PowerplayMeritRing(string Body, string Detail);

public sealed record PowerplayMeritSystem(
    string Name,
    double? DistanceLy,
    string Power,
    string PowerState,
    string FactionState,
    long BestPrice,
    IReadOnlyList<PowerplayMeritRing> Rings,
    IReadOnlyList<PowerplayMeritStation> Stations
);

/// <summary>Joins a Powerplay system list with rings and station prices, best sell price first.</summary>
public static class PowerplayMeritRank
{
    public static IReadOnlyList<PowerplayMeritSystem> Compose(
        IReadOnlyList<MiningSystemResult> systems,
        IReadOnlyList<MiningRing> rings,
        IReadOnlyList<MiningMarketResult> markets,
        int limit
    )
    {
        var rows = new List<PowerplayMeritSystem>();
        foreach (MiningSystemResult system in systems)
        {
            PowerplayMeritRing[] systemRings = rings
                .Where(ring => ring.System.Equals(system.System, StringComparison.OrdinalIgnoreCase))
                .Select(DescribeRing)
                .ToArray();
            PowerplayMeritStation[] systemStations = markets
                .Where(market => market.System.Equals(system.System, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(market => market.Price)
                .Select(market => new PowerplayMeritStation(
                    market.Station,
                    market.Type,
                    market.PadDescription,
                    market.Price,
                    market.Demand,
                    $"{market.Price:N0} CR/t · {market.Demand:N0} t · {market.PadDescription}"
                ))
                .ToArray();
            if (systemRings.Length == 0 || systemStations.Length == 0)
            {
                continue;
            }

            rows.Add(
                new PowerplayMeritSystem(
                    system.System,
                    system.Distance,
                    system.Power,
                    system.PowerState,
                    system.State,
                    systemStations[0].Price,
                    systemRings,
                    systemStations
                )
            );
        }

        return rows.OrderByDescending(row => row.BestPrice)
            .ThenBy(row => row.DistanceLy ?? double.MaxValue)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static PowerplayMeritRing DescribeRing(MiningRing ring)
    {
        string mapped = MiningMappedSpotCatalog.Describe(ring.System, ring.Body);
        string detail = string.Join(
            " · ",
            new[] { ring.Minerals, ring.RingType, ring.Reserve, mapped }.Where(part => part.Length > 0)
        );
        return new PowerplayMeritRing(ring.Body, detail);
    }
}
