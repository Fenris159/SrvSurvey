using System.Text.Json;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Mining;

public sealed record MiningPowerObservation(string Power, string State, DateTimeOffset Time);
public sealed record MiningCommunitySystem(string Name, GalacticCoordinate? Position, MiningPowerObservation Power);
public sealed record MiningCommunityMarket(string System, string Station, long Id, string Commodity, long Buy, long Sell, long Demand, long Stock, DateTimeOffset Time);
public sealed record MiningCommunitySnapshot(IReadOnlyList<MiningCommunitySystem> Systems, IReadOnlyList<MiningCommunityMarket> Markets);

/// <summary>Bounded, shared observations from EDDN. Unknown positions never become zero-distance results.</summary>
public sealed class MiningCommunityCache
{
    private readonly object gate = new();
    private readonly Dictionary<string, MiningCommunitySystem> systems = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(long, string), MiningCommunityMarket> markets = new();
    public MiningPowerObservation? Power(string system) { lock (gate) return systems.GetValueOrDefault(system)?.Power; }
    public GalacticCoordinate? Position(string system) { lock (gate) return systems.GetValueOrDefault(system)?.Position; }
    public int Count { get { lock (gate) return markets.Count; } }

    public void Apply(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return;
        var schema = MiningJson.Text(root, "$schemaRef");
        if (!DateTimeOffset.TryParse(MiningJson.Text(message, "timestamp"), out var time) || time > now.AddMinutes(5) || time < now.AddDays(-1)) return;
        lock (gate)
        {
            if (schema == "https://eddn.edcd.io/schemas/journal/1")
            {
                var name = MiningJson.Text(message, "StarSystem");
                if (name.Length == 0 || MiningJson.Text(message, "event") is not ("FSDJump" or "Location" or "CarrierJump")) return;
                var coordinates = MiningJson.Array(message, "StarPos").ToArray();
                GalacticCoordinate? position = coordinates.Length == 3 && coordinates.All(c => c.ValueKind == JsonValueKind.Number && c.TryGetDouble(out var n) && double.IsFinite(n))
                    ? new(coordinates[0].GetDouble(), coordinates[1].GetDouble(), coordinates[2].GetDouble()) : null;
                if (!systems.TryGetValue(name, out var old) || old.Power.Time < time)
                    systems[name] = new(name, position ?? old?.Position, new(MiningJson.Text(message, "ControllingPower"), MiningJson.Text(message, "PowerplayState"), time));
            }
            else if (schema == "https://eddn.edcd.io/schemas/commodity/3")
            {
                var id = (long)MiningJson.Number(message, "marketId");
                var system = MiningJson.Text(message, "systemName");
                var station = MiningJson.Text(message, "stationName");
                if (id <= 0 || system.Length == 0 || station.Length == 0) return;
                foreach (var item in MiningJson.Array(message, "commodities"))
                {
                    var commodity = MiningCommodityName.Normalize(MiningJson.Text(item, "name"));
                    if (commodity.Length == 0) continue;
                    var key = (id, commodity);
                    if (markets.TryGetValue(key, out var previous) && previous.Time >= time) continue;
                    markets[key] = new(system, station, id, commodity, (long)MiningJson.Number(item, "buyPrice"), (long)MiningJson.Number(item, "sellPrice"), (long)MiningJson.Number(item, "demand"), (long)MiningJson.Number(item, "stock"), time);
                }
            }
            Prune(now);
        }
    }
    public IReadOnlyList<MiningMarketResult> Markets(MiningMarketQuery query, GalacticCoordinate? origin, DateTimeOffset now)
    {
        lock (gate)
        {
            // Commodity messages do not certify station type or pad size. Keep these results out of such filtered searches.
            if (query.LargePads || query.ExcludeCarriers || query.StationType.Length > 0) return [];
            return markets.Values.Where(m => m.Commodity == MiningCommodityName.Normalize(query.Commodity) && m.Time >= now.AddDays(-Math.Min(1, query.MaximumAgeDays)))
                .Select(m => new MiningMarketResult(m.System, m.Station, "Unknown · EDDN", origin is { } p && systems.GetValueOrDefault(m.System)?.Position is { } target ? p.DistanceTo(target) : null,
                    null, query.Buying ? m.Buy : m.Sell, m.Demand, m.Stock, m.Time, m.Id))
                .Where(m => m.Price > 0 && (query.Buying ? m.Supply : m.Demand) > 0 && (query.GalaxyWide || m.Distance is { } distance && distance <= query.Radius)).ToArray();
        }
    }
    public string Export() { lock (gate) return JsonSerializer.Serialize(new MiningCommunitySnapshot(systems.Values.ToArray(), markets.Values.ToArray())); }
    public void Restore(string json, DateTimeOffset now)
    {
        var snapshot = JsonSerializer.Deserialize<MiningCommunitySnapshot>(json) ?? throw new JsonException("Empty community cache.");
        if (snapshot.Systems is null || snapshot.Markets is null || snapshot.Systems.Any(s => s is null || s.Name is null || s.Power is null) || snapshot.Markets.Any(m => m is null || m.Commodity is null)) throw new JsonException("Invalid community cache.");
        lock (gate)
        {
            foreach (var system in snapshot.Systems.Take(50000)) systems[system.Name] = system;
            foreach (var market in snapshot.Markets.Where(m => m.Time >= now.AddDays(-1)).Take(50000)) markets[(market.Id, market.Commodity)] = market;
            Prune(now);
        }
    }
    private void Prune(DateTimeOffset now)
    {
        if (markets.Count > 50000) foreach (var key in markets.OrderBy(p => p.Value.Time).Take(markets.Count - 45000).Select(p => p.Key).ToArray()) markets.Remove(key);
        if (systems.Count > 50000) foreach (var key in systems.OrderBy(p => p.Value.Power.Time).Take(systems.Count - 45000).Select(p => p.Key).ToArray()) systems.Remove(key);
        // Expiry is also checked on queries; periodic pruning avoids scanning the entire cache for every broadcast.
        if (now.Second == 0) foreach (var key in markets.Where(p => p.Value.Time < now.AddDays(-1)).Select(p => p.Key).ToArray()) markets.Remove(key);
    }
}
