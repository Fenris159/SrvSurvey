using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed record MiningRingQuery(string ReferenceSystem, string Mineral, string RingType, double Radius, int MinimumHotspots = 1, int Page = 0, bool SystemOnly = false);
public sealed record MiningMarketQuery(string ReferenceSystem, string Commodity, bool Buying, double Radius = 500, bool GalaxyWide = false, bool ExcludeCarriers = false, bool LargePads = false, int MaximumAgeDays = 2, string StationType = "", int Page = 0, bool SystemOnly = false);
public sealed record MiningMarketResult(string System, string Station, string Type, double? Distance, double? ArrivalLs, long Price, long Demand, long Supply, DateTimeOffset? Updated, long MarketId, bool? LargePad = null)
{
    public string PadDescription => LargePad switch { true => "Large pad", false => "Small / medium pads", null => "Pad size unknown" };
}
public sealed record MiningSystemQuery(string ReferenceSystem, double Radius = 100, string Security = "", string Allegiance = "", string Government = "", string State = "", string Economy = "", string Power = "", string PowerState = "", long MinimumPopulation = 0, int Page = 0);
public sealed record MiningSystemResult(string System, double? Distance, string Security, string Allegiance, string Government, string Economy, string State, string Power, string PowerState, long Population);

/// <summary>Mining searches extend the shared Spansh pathway and use the application's network/privacy client.</summary>
public sealed class MiningSearchClient(HttpClient? httpClient = null)
{
    private const string DistanceField = "distance";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly HttpClient client = httpClient ?? SharedClient;
    public async Task<IReadOnlyList<MiningRing>> FindRingsAsync(MiningRingQuery query, CancellationToken cancellationToken = default)
    {
        var filters = DistanceFilter(query.Radius);
        if (query.SystemOnly) filters["system_name"] = new { value = new[] { query.ReferenceSystem } };
        if (query.Mineral.Length > 0) filters["ring_signals"] = new[] { new { comparison = "<=>", count = new[] { query.MinimumHotspots, 9999 }, name = new[] { query.Mineral } } };
        if (query.RingType.Length > 0 && query.RingType != "All") filters["rings"] = new[] { new { type = new[] { query.RingType } } };
        using var response = await SearchAsync("bodies", query.ReferenceSystem, filters, query.Page, cancellationToken);
        var output = new List<MiningRing>();
        foreach (var body in Results(response))
        {
            foreach (var ring in MiningJson.Array(body, "rings"))
            {
                var type = MiningJson.Text(ring, "type");
                if (query.RingType.Length > 0 && query.RingType != "All" && !type.Equals(query.RingType, StringComparison.OrdinalIgnoreCase)) continue;
                var signals = MiningJson.Array(ring, "signals").Where(s => MiningJson.Text(s, "name").Length > 0)
                    .GroupBy(s => MiningJson.Text(s, "name")).ToDictionary(g => g.Key, g => (int)g.Max(s => MiningJson.Number(s, "count")));
                if (query.Mineral.Length > 0 && !signals.Any(p => p.Key.Equals(query.Mineral, StringComparison.OrdinalIgnoreCase) && p.Value >= query.MinimumHotspots)) continue;
                output.Add(new MiningRing
                {
                    System = MiningJson.Text(body, "system_name"),
                    Body = MiningJson.Text(ring, "name"),
                    RingType = type,
                    Reserve = MiningJson.Text(body, "reserve_level"),
                    ArrivalLs = Number(body, "distance_to_arrival"),
                    Position = Position(body),
                    Hotspots = signals,
                    Source = "Spansh",
                    Scanned = DateTimeOffset.UtcNow,
                    DistanceLy = Number(body, DistanceField),
                });
            }
        }
        return output;
    }

    public async Task<IReadOnlyList<MiningMarketResult>> FindMarketsAsync(MiningMarketQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Commodity);
        if (query.SystemOnly && !query.GalaxyWide) return await FindSpanshMarketsAsync(query, cancellationToken).ConfigureAwait(false);
        var commodity = MiningCommodityName.Normalize(query.Commodity);
        var direction = query.Buying ? "exports" : "imports";
        var path = query.GalaxyWide ? $"commodity/name/{Uri.EscapeDataString(commodity)}/{direction}" : $"system/name/{Uri.EscapeDataString(query.ReferenceSystem)}/commodity/name/{Uri.EscapeDataString(commodity)}/nearby/{direction}";
        var uri = $"https://api.ardent-insight.com/v2/{path}?minVolume=1&maxDaysAgo={query.MaximumAgeDays}&maxDistance={query.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture)}&fleetCarriers={!query.ExcludeCarriers}";
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await BoundedHttpContent.ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Mining market response", cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Unexpected market response.");
        return SortMarkets(document.RootElement.EnumerateArray().Select(item => ReadArdentMarket(item, query)).OfType<MiningMarketResult>(), query.Buying);
    }
    private static MiningMarketResult? ReadArdentMarket(JsonElement item, MiningMarketQuery query)
    {
        var type = MiningJson.Text(item, "stationType");
        var largePad = Number(item, "maxLandingPadSize") is { } pad ? pad >= 3 : (bool?)null;
        if (!MatchesStation(type, largePad, query)) return null;
        var price = (long)MiningJson.Number(item, query.Buying ? "buyPrice" : "sellPrice");
        var demand = (long)MiningJson.Number(item, "demand");
        var supply = (long)MiningJson.Number(item, "stock");
        var updated = RecentObservation(MiningJson.Text(item, "updatedAt"), query.MaximumAgeDays);
        if (!HasTradeVolume(price, demand, supply, query.Buying) || updated is null) return null;
        return new(MiningJson.Text(item, "systemName"), MiningJson.Text(item, "stationName"), type,
            Number(item, DistanceField), Number(item, "distanceToArrival"), price, demand, supply, updated, (long)MiningJson.Number(item, "marketId"), largePad);
    }

    public async Task<IReadOnlyList<MiningMarketResult>> FindSpanshMarketsAsync(MiningMarketQuery query, CancellationToken cancellationToken = default)
    {
        var filters = query.GalaxyWide ? new Dictionary<string, object>() : DistanceFilter(query.Radius);
        if (query.SystemOnly && !query.GalaxyWide) filters["system_name"] = new { value = new[] { query.ReferenceSystem } };
        filters[query.Buying ? "selling_commodities" : "buying_commodities"] = new { value = new[] { query.Commodity } };
        using var response = await SearchAsync("stations", query.ReferenceSystem, filters, query.Page, cancellationToken, "market_updated_at");
        return SortMarkets(Results(response).SelectMany(station => ReadSpanshMarkets(station, query)), query.Buying);
    }
    private static IEnumerable<MiningMarketResult> ReadSpanshMarkets(JsonElement station, MiningMarketQuery query)
    {
        var type = MiningJson.Text(station, "type");
        var largePad = HasLargePad(station);
        if (!MatchesStation(type, largePad, query)) yield break;
        var updated = RecentObservation(MiningJson.Text(station, "market_updated_at"), query.MaximumAgeDays);
        if (updated is null) yield break;
        foreach (var item in MiningJson.Array(station, "market").Where(m => MiningJson.Text(m, "commodity").Equals(query.Commodity, StringComparison.OrdinalIgnoreCase)))
        {
            var price = (long)MiningJson.Number(item, query.Buying ? "buy_price" : "sell_price");
            var supply = (long)(Number(item, "supply") ?? Number(item, "stock") ?? 0);
            var demand = (long)MiningJson.Number(item, "demand");
            if (!HasTradeVolume(price, demand, supply, query.Buying)) continue;
            yield return new(MiningJson.Text(station, "system_name"), MiningJson.Text(station, "name"), type,
                Number(station, DistanceField), Number(station, "distance_to_arrival"), price, demand, supply, updated, (long)MiningJson.Number(station, "market_id"), largePad);
        }
    }
    private static bool MatchesStation(string type, bool? largePad, MiningMarketQuery query) =>
        (!query.ExcludeCarriers || !type.Contains("Carrier", StringComparison.OrdinalIgnoreCase))
        && (!query.LargePads || largePad == true)
        && (query.StationType.Length == 0 || type.Contains(query.StationType, StringComparison.OrdinalIgnoreCase));
    private static bool HasTradeVolume(long price, long demand, long supply, bool buying) => price > 0 && (buying ? supply : demand) > 0;
    private static MiningMarketResult[] SortMarkets(IEnumerable<MiningMarketResult> results, bool buying) =>
        buying ? results.OrderBy(r => r.Price).ToArray() : results.OrderByDescending(r => r.Price).ToArray();
    private static DateTimeOffset? RecentObservation(string value, int maximumAgeDays) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
        && DateTimeOffset.UtcNow - time <= TimeSpan.FromDays(maximumAgeDays) && time <= DateTimeOffset.UtcNow.AddMinutes(5) ? time : null;
    private static bool? HasLargePad(JsonElement station)
    {
        if (Number(station, "large_pads") is { } pads) return pads > 0;
        if (station.TryGetProperty("has_large_pad", out var pad) && pad.ValueKind is JsonValueKind.True or JsonValueKind.False) return pad.GetBoolean();
        return null;
    }

    public async Task<IReadOnlyList<MiningSystemResult>> FindSystemsAsync(MiningSystemQuery query, CancellationToken cancellationToken = default)
    {
        var filters = DistanceFilter(query.Radius);
        foreach (var (name, value, array) in new[] { ("security", query.Security, false), ("allegiance", query.Allegiance, false), ("government", query.Government, false), ("primary_economy", query.Economy, false), ("controlling_minor_faction_state", query.State, true), ("controlling_power", query.Power, true), ("power_state", query.PowerState, true) })
            if (value.Length > 0) filters[name] = new { value = array ? (object)new[] { value } : value };
        if (query.MinimumPopulation > 0) filters["population"] = new { min = query.MinimumPopulation };
        using var response = await SearchAsync("systems", query.ReferenceSystem, filters, query.Page, cancellationToken);
        return Results(response).Select(s => new MiningSystemResult(MiningJson.Text(s, "name"), Number(s, DistanceField), MiningJson.Text(s, "security"), MiningJson.Text(s, "allegiance"), MiningJson.Text(s, "government"), MiningJson.Text(s, "primary_economy"), MiningJson.Text(s, "controlling_minor_faction_state"), MiningJson.Text(s, "controlling_power"), MiningJson.Text(s, "power_state"), (long)MiningJson.Number(s, "population"))).ToArray();
    }
    public async Task<IReadOnlyList<MiningMarketResult>> FindTradersAsync(string reference, string trader, CancellationToken cancellationToken = default, double radius = 100, int page = 0)
    {
        var filters = DistanceFilter(radius);
        filters["material_trader"] = new { value = trader };
        using var response = await SearchAsync("stations", reference, filters, page, cancellationToken);
        return Results(response).Select(s => new MiningMarketResult(MiningJson.Text(s, "system_name"), MiningJson.Text(s, "name"), MiningJson.Text(s, "type"), Number(s, DistanceField), Number(s, "distance_to_arrival"), 0, 0, 0, null, (long)MiningJson.Number(s, "market_id"), HasLargePad(s))).ToArray();
    }
    private async Task<JsonDocument> SearchAsync(string entity, string reference, Dictionary<string, object> filters, int page, CancellationToken cancellationToken, string sort = DistanceField)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://spansh.co.uk/api/{entity}/search")
        {
            Content = JsonContent.Create(new { filters, reference_system = reference.Trim(), size = entity == "stations" ? 20 : 100, page, sort = new[] { new Dictionary<string, object> { [sort] = new { direction = sort == DistanceField ? "asc" : "desc" } } } }),
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await BoundedHttpContent.ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Mining search response", cancellationToken).ConfigureAwait(false);
    }
    private static Dictionary<string, object> DistanceFilter(double radius)
    {
        if (!double.IsFinite(radius) || radius is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(radius), "Use a search radius from 1 to 500 ly.");
        return new() { [DistanceField] = new { min = 0, max = radius } };
    }
    private static IEnumerable<JsonElement> Results(JsonDocument document) => MiningJson.Array(document.RootElement, "results");
    private static double? Number(JsonElement data, string property) => data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    private static GalacticCoordinate? Position(JsonElement data) => Number(data, "system_x") is { } x && Number(data, "system_y") is { } y && Number(data, "system_z") is { } z ? new(x, y, z) : null;
}
