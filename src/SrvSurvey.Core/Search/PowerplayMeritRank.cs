using System.Globalization;
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

public sealed record PowerplayMeritRing(string Body, string Detail, bool Planetary = false);

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
            PowerplayMeritStation[] systemStations = StationsFor(markets, system.System);
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

    public static IReadOnlyList<PowerplayMeritSystem> ComposePlanets(
        IReadOnlyList<MiningSystemResult> systems,
        IReadOnlyList<MiningPlanetaryBody> bodies,
        IReadOnlyList<MiningMarketResult> markets,
        int limit
    )
    {
        var rows = new List<PowerplayMeritSystem>();
        foreach (
            IGrouping<string, MiningPlanetaryBody> group in bodies.GroupBy(
                body => body.System,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            MiningSystemResult? system = systems.FirstOrDefault(candidate =>
                candidate.System.Equals(group.Key, StringComparison.OrdinalIgnoreCase)
            );
            MiningPlanetaryBody first = group.First();
            string power = system?.Power is { Length: > 0 } knownPower ? knownPower : first.Power;
            string powerState = system?.PowerState is { Length: > 0 } knownState ? knownState : first.PowerState;
            PowerplayMeritRing[] planets = group.Select(DescribePlanet).ToArray();
            PowerplayMeritStation[] systemStations = StationsFor(markets, group.Key);
            rows.Add(
                new PowerplayMeritSystem(
                    group.Key,
                    system?.Distance ?? first.DistanceLy,
                    power,
                    powerState,
                    system?.State ?? "",
                    systemStations.Length == 0 ? 0 : systemStations[0].Price,
                    planets,
                    systemStations
                )
            );
        }

        return rows.OrderBy(row => row.DistanceLy ?? double.MaxValue)
            .ThenByDescending(row => row.BestPrice)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static PowerplayMeritStation[] StationsFor(IReadOnlyList<MiningMarketResult> markets, string system) =>
        markets
            .Where(market => market.System.Equals(system, StringComparison.OrdinalIgnoreCase))
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

    private static PowerplayMeritRing DescribePlanet(MiningPlanetaryBody body)
    {
        string gravity = body.Gravity.ToString("0.##", CultureInfo.CurrentCulture) + " g";
        string arrival = body.ArrivalLs.ToString("0.#", CultureInfo.CurrentCulture) + " ls";
        string detail = string.Join(
            " · ",
            new[] { body.System, gravity, arrival, body.Subtype, body.Reserve }.Where(part => part.Length > 0)
        );
        return new PowerplayMeritRing(body.Body, detail, true);
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
