using System.Globalization;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

public sealed record PowerplayMeritStation(
    string Name,
    string Type,
    string Pad,
    long Price,
    long Demand,
    string Detail,
    string Commodity = "",
    double? ArrivalLs = null,
    DateTimeOffset? Updated = null
);

public sealed record PowerplayMeritRing(
    string Body,
    string Detail,
    bool Planetary = false,
    IReadOnlyList<string>? SignalLines = null,
    string Reserve = "",
    string RingType = ""
);

public sealed record PowerplayMeritSystem(
    string Name,
    double? DistanceLy,
    string Power,
    string PowerState,
    string FactionState,
    long BestPrice,
    IReadOnlyList<PowerplayMeritRing> Rings,
    IReadOnlyList<PowerplayMeritStation> Stations
)
{
    public IReadOnlyList<string> NearbyPowers { get; init; } = [];
    public IReadOnlyList<PowerplayProgress> Conflict { get; init; } = [];
    public double? ControlProgress { get; init; }
    public int ReserveRank { get; init; } = 6;
}

/// <summary>Joins a Powerplay system list with rings and station prices, best sell price first.</summary>
public static class PowerplayMeritRank
{
    public static bool MorePricesCanRank(int kept, int limit, long weakestKept, long nextCeiling) =>
        kept < Math.Max(1, limit) || nextCeiling >= weakestKept;

    public static long WeakestRankedPrice(IReadOnlyList<long> prices, int limit)
    {
        if (prices.Count < Math.Max(1, limit))
        {
            return 0;
        }

        return prices.OrderByDescending(price => price).ElementAt(Math.Max(1, limit) - 1);
    }

    public static IReadOnlyList<PowerplayMeritSystem> Compose(
        IReadOnlyList<MiningSystemResult> systems,
        IReadOnlyList<MiningRing> rings,
        IReadOnlyList<MiningMarketResult> markets,
        int limit,
        string pinnedSystem = ""
    )
    {
        var rows = new List<PowerplayMeritSystem>();
        foreach (MiningSystemResult system in systems)
        {
            PowerplayMeritRing[] systemRings = rings
                .Where(ring => ring.System.Equals(system.System, StringComparison.OrdinalIgnoreCase))
                .Select(DescribeRing)
                .ToArray();
            PowerplayMeritStation[] systemStations = OrderForHeadline(StationsFor(markets, system.System), systemRings);
            if (systemRings.Length == 0 || systemStations.Length == 0)
            {
                continue;
            }

            string headline = HeadlineCommodity(systemStations, systemRings);
            rows.Add(
                new PowerplayMeritSystem(
                    system.System,
                    system.Distance,
                    system.Power,
                    system.PowerState,
                    system.State,
                    HeadlinePrice(systemStations, headline),
                    systemRings,
                    systemStations
                )
                {
                    NearbyPowers = system.NearbyPowers,
                    Conflict = system.Conflict,
                    ControlProgress = system.ControlProgress,
                    ReserveRank = ReserveRank(systemRings, headline),
                }
            );
        }

        PowerplayMeritSystem[] ordered = rows.OrderByDescending(row => row.BestPrice)
            .ThenBy(row => row.DistanceLy ?? double.MaxValue)
            .ToArray();
        int count = Math.Max(1, limit);
        PowerplayMeritSystem[] best = ordered.Take(count).ToArray();
        PowerplayMeritSystem? pinned = ordered.FirstOrDefault(row =>
            pinnedSystem.Length > 0 && row.Name.Equals(pinnedSystem, StringComparison.OrdinalIgnoreCase)
        );
        if (pinned is null || best.Contains(pinned))
        {
            return best;
        }

        return best.Take(count - 1)
            .Append(pinned)
            .OrderByDescending(row => row.BestPrice)
            .ThenBy(row => row.DistanceLy ?? double.MaxValue)
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
                {
                    NearbyPowers = system?.NearbyPowers ?? [],
                    Conflict = system?.Conflict ?? [],
                    ControlProgress = system?.ControlProgress,
                }
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
            .ThenByDescending(market => market.Demand)
            .DistinctBy(market => market.Station + "\u001f" + MiningCommodityName.Key(market.Commodity))
            .Select(market => new PowerplayMeritStation(
                market.Station,
                market.Type,
                market.PadDescription,
                market.Price,
                market.Demand,
                $"{market.Price:N0} CR/t · {market.Demand:N0} t · {market.PadDescription}",
                market.Commodity,
                market.ArrivalLs,
                market.Updated
            ))
            .ToArray();

    private static PowerplayMeritStation[] OrderForHeadline(
        PowerplayMeritStation[] stations,
        PowerplayMeritRing[] rings
    )
    {
        HashSet<string> hotspots = HotspotNames(rings);
        return stations
            .Where(station => !PlanetaryMiningPlan.IsSurfaceExclusive(station.Commodity))
            .OrderByDescending(station => hotspots.Contains(MiningCommodityName.Key(station.Commodity)))
            .ThenByDescending(station => station.Price)
            .ThenByDescending(station => station.Demand)
            .ToArray();
    }

    private static string HeadlineCommodity(PowerplayMeritStation[] stations, PowerplayMeritRing[] rings)
    {
        HashSet<string> hotspots = HotspotNames(rings);
        return stations
                .Where(station => hotspots.Contains(MiningCommodityName.Key(station.Commodity)))
                .OrderByDescending(station => station.Price)
                .Select(station => station.Commodity)
                .FirstOrDefault()
            ?? "";
    }

    private static long HeadlinePrice(PowerplayMeritStation[] stations, string headline)
    {
        long best = 0;
        bool matched = false;
        foreach (PowerplayMeritStation station in stations)
        {
            if (headline.Length == 0 || MiningCommodityName.Same(station.Commodity, headline))
            {
                best = Math.Max(best, station.Price);
                matched = true;
            }
        }

        return matched ? best : stations[0].Price;
    }

    private static bool NamesHotspot(PowerplayMeritRing ring, string headline)
    {
        IReadOnlyList<string> lines = ring.SignalLines ?? [];
        for (int index = 0; index < lines.Count; index++)
        {
            string signal = lines[index].Split(':', 2)[0].Trim();
            if (MiningCommodityName.Same(signal, headline))
            {
                return true;
            }
        }

        return false;
    }

    private static int ReserveRank(PowerplayMeritRing[] rings, string headline)
    {
        string reserve = rings.Length == 0 ? "" : rings[0].Reserve;
        for (int index = 0; index < rings.Length && headline.Length > 0; index++)
        {
            if (!NamesHotspot(rings[index], headline))
            {
                continue;
            }

            reserve = rings[index].Reserve;
            break;
        }

        return reserve.ToLowerInvariant() switch
        {
            "pristine" => 1,
            "major" => 2,
            "common" => 3,
            "low" => 4,
            "depleted" => 5,
            _ => 6,
        };
    }

    private static HashSet<string> HotspotNames(PowerplayMeritRing[] rings)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PowerplayMeritRing ring in rings)
        {
            IReadOnlyList<string> lines = ring.SignalLines ?? [];
            for (int index = 0; index < lines.Count; index++)
            {
                int split = lines[index].IndexOf(':');
                string name = (split < 0 ? lines[index] : lines[index][..split]).Trim();
                if (name.Length > 0 && !PlanetaryMiningPlan.IsSurfaceExclusive(name))
                {
                    names.Add(MiningCommodityName.Key(name));
                }
            }
        }

        return names;
    }

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

    public static PowerplayMeritRing DescribeRing(MiningRing ring)
    {
        string mapped = MiningMappedSpotCatalog.Describe(ring.System, ring.Body);
        string detail = string.Join(
            " · ",
            new[] { ring.Minerals, ring.RingType, ring.Reserve, mapped }.Where(part => part.Length > 0)
        );
        return new PowerplayMeritRing(
            ring.Body,
            detail,
            false,
            ring.Hotspots.Where(spot => PlanetaryMiningPlan.IsEdpmCommodity(spot.Key))
                .Select(spot => $"{spot.Key}: {spot.Value} {(spot.Value == 1 ? "Hotspot" : "Hotspots")}")
                .ToArray(),
            ring.Reserve,
            ring.RingType
        );
    }
}
