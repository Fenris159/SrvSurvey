using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Mining;

public sealed record MiningPowerObservation(string Power, string State, DateTimeOffset Time);

public sealed record MiningCommunitySystem(string Name, GalacticCoordinate? Position, MiningPowerObservation Power);

public sealed record MiningCommunityMarket(
    string System,
    string Station,
    long Id,
    string Commodity,
    long Buy,
    long Sell,
    long Demand,
    long Stock,
    DateTimeOffset Time
);

public sealed record MiningCommunitySnapshot(
    IReadOnlyList<MiningCommunitySystem> Systems,
    IReadOnlyList<MiningCommunityMarket> Markets
);

/// <summary>Bounded, shared observations from EDDN. Unknown positions never become zero-distance results.</summary>
public sealed class MiningCommunityCache
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, MiningCommunitySystem> systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(long, string), MiningCommunityMarket> markets = [];

    public MiningPowerObservation? Power(string system)
    {
        lock (gate)
        {
            return systems.GetValueOrDefault(system)?.Power;
        }
    }

    public GalacticCoordinate? Position(string system)
    {
        lock (gate)
        {
            return systems.GetValueOrDefault(system)?.Position;
        }
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return markets.Count;
            }
        }
    }

    public void Apply(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("message", out JsonElement message)
            || message.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }

        string schema = MiningJson.Text(root, "$schemaRef");
        if (
            !DateTimeOffset.TryParse(
                MiningJson.Text(message, "timestamp"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset time
            )
            || time > now.AddMinutes(5)
            || time < now.AddDays(-1)
        )
        {
            return;
        }

        lock (gate)
        {
            if (schema == "https://eddn.edcd.io/schemas/journal/1")
            {
                ApplySystem(message, time);
            }
            else if (schema == "https://eddn.edcd.io/schemas/commodity/3")
            {
                ApplyMarket(message, time);
            }

            Prune(now);
        }
    }

    private void ApplySystem(JsonElement message, DateTimeOffset time)
    {
        string name = MiningJson.Text(message, "StarSystem");
        if (name.Length == 0 || MiningJson.Text(message, "event") is not ("FSDJump" or "Location" or "CarrierJump"))
        {
            return;
        }

        JsonElement[] coordinates = MiningJson.Array(message, "StarPos").ToArray();
        GalacticCoordinate? position =
            coordinates.Length == 3
            && coordinates.All(c =>
                c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out double n) && double.IsFinite(n)
            )
                ? new(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble())
                : null;
        if (!systems.TryGetValue(name, out MiningCommunitySystem? old) || old.Power.Time < time)
        {
            systems[name] = new(
                name,
                position ?? old?.Position,
                new(MiningJson.Text(message, "ControllingPower"), MiningJson.Text(message, "PowerplayState"), time)
            );
        }
    }

    private void ApplyMarket(JsonElement message, DateTimeOffset time)
    {
        long id = (long)MiningJson.Number(message, "marketId");
        string system = MiningJson.Text(message, "systemName");
        string station = MiningJson.Text(message, "stationName");
        if (id <= 0 || system.Length == 0 || station.Length == 0)
        {
            return;
        }

        foreach (JsonElement item in MiningJson.Array(message, "commodities"))
        {
            string commodity = MiningCommodityName.Normalize(MiningJson.Text(item, "name"));
            if (commodity.Length == 0)
            {
                continue;
            }

            (long id, string commodity) key = (id, commodity);
            if (markets.TryGetValue(key, out MiningCommunityMarket? previous) && previous.Time >= time)
            {
                continue;
            }

            markets[key] = new(
                system,
                station,
                id,
                commodity,
                (long)MiningJson.Number(item, "buyPrice"),
                (long)MiningJson.Number(item, "sellPrice"),
                (long)MiningJson.Number(item, "demand"),
                (long)MiningJson.Number(item, "stock"),
                time
            );
        }
    }

    public IReadOnlyList<MiningMarketResult> Markets(
        MiningMarketQuery query,
        GalacticCoordinate? origin,
        DateTimeOffset now
    )
    {
        // Commodity messages do not certify station type or pad size.
        if (!CanUseCommunityMarkets(query))
        {
            return [];
        }

        lock (gate)
        {
            return markets
                .Values.Where(m =>
                    m.Commodity == MiningCommodityName.Normalize(query.Commodity)
                    && m.Time >= now.AddDays(-Math.Min(1, query.MaximumAgeDays))
                )
                .Select(m => new MiningMarketResult(
                    m.System,
                    m.Station,
                    "Unknown · EDDN",
                    origin is { } p && systems.GetValueOrDefault(m.System)?.Position is { } target
                        ? p.DistanceTo(target)
                        : null,
                    null,
                    query.Buying ? m.Buy : m.Sell,
                    m.Demand,
                    m.Stock,
                    m.Time,
                    m.Id
                )
                {
                    Commodity = MiningCommodityName.Canonical(m.Commodity),
                })
                .Where(m => MatchesMarketQuery(m, query))
                .ToArray();
        }
    }

    private static bool CanUseCommunityMarkets(MiningMarketQuery query) =>
        !query.LargePads
        && !query.ExcludeCarriers
        && query.StationType.Length == 0
        && query.StationTypes.Count == 0
        && (query.PadSize.Length == 0 || query.PadSize.Equals("Any", StringComparison.OrdinalIgnoreCase));

    private static bool MatchesMarketQuery(MiningMarketResult market, MiningMarketQuery query)
    {
        long volume = query.Buying ? market.Supply : market.Demand;
        return market.Price > 0
            && volume > 0
            && volume >= query.MinimumDemand
            && (query.MaximumDemand == 0 || volume <= query.MaximumDemand)
            && (query.GalaxyWide || market.Distance is { } distance && distance <= query.Radius);
    }

    public IReadOnlyList<MiningSystemResult> FindSystems(MiningSystemQuery query, DateTimeOffset now)
    {
        // Journal broadcasts do not certify these other indexed fields.
        if (
            query.Security.Length > 0
            || query.Allegiance.Length > 0
            || query.Government.Length > 0
            || query.State.Length > 0
            || query.Economy.Length > 0
            || query.MinimumPopulation > 0
        )
        {
            return [];
        }

        lock (gate)
        {
            GalacticCoordinate? origin = systems.GetValueOrDefault(query.ReferenceSystem)?.Position;
            return systems
                .Values.Where(s => s.Power.Time >= now.AddDays(-1) && s.Power.Time <= now.AddMinutes(5))
                .Where(s =>
                    (query.Power.Length == 0 || s.Power.Power.Equals(query.Power, StringComparison.OrdinalIgnoreCase))
                    && (
                        query.PowerState.Length == 0
                        || s.Power.State.Equals(query.PowerState, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(s => new MiningSystemResult(
                    s.Name,
                    SystemDistance(s, query.ReferenceSystem, origin),
                    "",
                    "",
                    "",
                    "",
                    "",
                    s.Power.Power,
                    s.Power.State,
                    0
                ))
                .Where(s => s.Distance is { } distance && distance <= query.Radius)
                .ToArray();
        }
    }

    private static double? SystemDistance(MiningCommunitySystem system, string reference, GalacticCoordinate? origin)
    {
        if (system.Name.Equals(reference, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return origin is { } from && system.Position is { } to ? from.DistanceTo(to) : null;
    }

    public string Export()
    {
        lock (gate)
        {
            return JsonSerializer.Serialize(
                new MiningCommunitySnapshot(systems.Values.ToArray(), markets.Values.ToArray())
            );
        }
    }

    public void Restore(string json, DateTimeOffset now)
    {
        MiningCommunitySnapshot snapshot =
            JsonSerializer.Deserialize<MiningCommunitySnapshot>(json)
            ?? throw new JsonException("Empty community cache.");
        if (
            snapshot.Systems is null
            || snapshot.Markets is null
            || snapshot.Systems.Any(s => s is null || s.Name is null || s.Power is null)
            || snapshot.Markets.Any(m => m is null || m.Commodity is null)
        )
        {
            throw new JsonException("Invalid community cache.");
        }

        lock (gate)
        {
            foreach (MiningCommunitySystem? system in snapshot.Systems.Take(50000))
            {
                systems[system.Name] = system;
            }

            foreach (
                MiningCommunityMarket? market in snapshot.Markets.Where(m => m.Time >= now.AddDays(-1)).Take(50000)
            )
            {
                markets[(market.Id, market.Commodity)] = market;
            }

            Prune(now);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (markets.Count > 50000)
        {
            foreach (
                (long, string) key in markets
                    .OrderBy(p => p.Value.Time)
                    .Take(markets.Count - 45000)
                    .Select(p => p.Key)
                    .ToArray()
            )
            {
                markets.Remove(key);
            }
        }

        if (systems.Count > 50000)
        {
            foreach (
                string? key in systems
                    .OrderBy(p => p.Value.Power.Time)
                    .Take(systems.Count - 45000)
                    .Select(p => p.Key)
                    .ToArray()
            )
            {
                systems.Remove(key);
            }
        }
        // Expiry is also checked on queries; periodic pruning avoids scanning the entire cache for every broadcast.
        if (now.Second == 0)
        {
            foreach (
                (long, string) key in markets.Where(p => p.Value.Time < now.AddDays(-1)).Select(p => p.Key).ToArray()
            )
            {
                markets.Remove(key);
            }
        }
    }
}
