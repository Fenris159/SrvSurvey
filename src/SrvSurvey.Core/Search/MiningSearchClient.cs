using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed record MiningPlanetaryQuery(
    string ReferenceSystem,
    IReadOnlyList<string> BodySubtypes,
    IReadOnlyList<string> LandmarkSubtypes,
    string Reserve = "",
    double Radius = 100,
    IReadOnlyList<string>? ControllingPowers = null,
    string PowerState = ""
);

public sealed record MiningPlanetaryBody(
    string System,
    string Body,
    string Subtype,
    string Reserve,
    double Gravity,
    double ArrivalLs,
    double? DistanceLy = null,
    string Power = "",
    string PowerState = ""
);

public sealed record MiningRingQuery(
    string ReferenceSystem,
    string Mineral,
    string RingType,
    double Radius,
    int MinimumHotspots = 1,
    int Page = 0,
    bool SystemOnly = false
);

public sealed record MiningMarketQuery(
    string ReferenceSystem,
    string Commodity,
    bool Buying,
    double Radius = 500,
    bool GalaxyWide = false,
    bool ExcludeCarriers = false,
    bool LargePads = false,
    int MaximumAgeDays = 2,
    string StationType = "",
    int Page = 0,
    bool SystemOnly = false,
    long MinimumDemand = 0,
    long MaximumDemand = 0,
    TimeSpan? MaximumAge = null,
    string PadSize = "Any"
);

public sealed record MiningMarketResult(
    string System,
    string Station,
    string Type,
    double? Distance,
    double? ArrivalLs,
    long Price,
    long Demand,
    long Supply,
    DateTimeOffset? Updated,
    long MarketId,
    bool? LargePad = null
)
{
    public string PadDescription =>
        LargePad switch
        {
            true => "Large pad",
            false => "Small / medium pads",
            null => "Pad size unknown",
        };
}

public sealed record MiningSystemQuery(
    string ReferenceSystem,
    double Radius = 100,
    string Security = "",
    string Allegiance = "",
    string Government = "",
    string State = "",
    string Economy = "",
    string Power = "",
    string PowerState = "",
    long MinimumPopulation = 0,
    int Page = 0,
    string Objective = ""
);

public sealed record MiningSystemResult(
    string System,
    double? Distance,
    string Security,
    string Allegiance,
    string Government,
    string Economy,
    string State,
    string Power,
    string PowerState,
    long Population,
    GalacticCoordinate? Position = null
);

/// <summary>Mining searches extend the shared Spansh pathway and use the application's network/privacy client.</summary>
public sealed class MiningSearchClient(HttpClient? httpClient = null)
{
    private const string SystemNameField = "system_name";
    private const string DistanceField = "distance";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly HttpClient client = httpClient ?? SharedClient;

    public async Task<IReadOnlyList<MiningRing>> FindRingsAsync(
        MiningRingQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = DistanceFilter(query.Radius);
        if (query.SystemOnly)
        {
            filters[SystemNameField] = new { value = new[] { query.ReferenceSystem } };
        }

        if (query.Mineral.Length > 0)
        {
            filters["ring_signals"] = new[]
            {
                new
                {
                    comparison = "<=>",
                    count = new[] { query.MinimumHotspots, 9999 },
                    name = new[] { query.Mineral },
                },
            };
        }

        if (query.RingType.Length > 0 && query.RingType != "All")
        {
            filters["rings"] = new[] { new { type = new[] { query.RingType } } };
        }

        using JsonDocument response = await SearchAsync(
            "bodies",
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken
        );
        var output = new List<MiningRing>();
        foreach (JsonElement body in Results(response))
        {
            foreach (JsonElement ring in MiningJson.Array(body, "rings"))
            {
                string type = MiningJson.Text(ring, "type");
                if (!MatchesRingType(query.RingType, type))
                {
                    continue;
                }

                var signals = MiningJson
                    .Array(ring, "signals")
                    .Where(s => MiningJson.Text(s, "name").Length > 0)
                    .GroupBy(s => MiningJson.Text(s, "name"))
                    .ToDictionary(g => g.Key, g => (int)g.Max(s => MiningJson.Number(s, "count")));
                if (
                    query.Mineral.Length > 0
                    && !signals.Any(p =>
                        p.Key.Equals(query.Mineral, StringComparison.OrdinalIgnoreCase)
                        && p.Value >= query.MinimumHotspots
                    )
                )
                {
                    continue;
                }

                output.Add(
                    new MiningRing
                    {
                        System = MiningJson.Text(body, SystemNameField),
                        Body = MiningJson.Text(ring, "name"),
                        RingType = type,
                        Reserve = MiningJson.Text(body, "reserve_level"),
                        ArrivalLs = Number(body, "distance_to_arrival"),
                        Position = Position(body),
                        Hotspots = signals,
                        Source = "Spansh",
                        Scanned = DateTimeOffset.UtcNow,
                        DistanceLy = Number(body, DistanceField),
                    }
                );
            }
        }
        return output;
    }

    public async Task<IReadOnlyList<MiningPlanetaryBody>> FindPlanetaryBodiesAsync(
        MiningPlanetaryQuery query,
        CancellationToken cancellationToken = default
    )
    {
        if (query.BodySubtypes.Count == 0)
        {
            return [];
        }

        Dictionary<string, object> filters = DistanceFilter(query.Radius);
        filters["is_landable"] = new { value = true };
        filters["subtype"] = new { value = query.BodySubtypes.ToArray() };
        if (query.ControllingPowers is { Count: > 0 })
        {
            filters["system_controlling_power"] = new { value = query.ControllingPowers.ToArray() };
        }

        if (query.PowerState.Length > 0)
        {
            filters["system_power_state"] = new { value = new[] { query.PowerState } };
        }
        if (query.LandmarkSubtypes.Count > 0)
        {
            filters["landmarks"] = new[]
            {
                new
                {
                    comparison = "<=>",
                    subtype = query.LandmarkSubtypes.ToArray(),
                    count = new[] { 1, 999 },
                },
            };
        }

        if (query.Reserve.Length > 0)
        {
            filters["reserve_level"] = new { value = new[] { query.Reserve } };
        }

        using JsonDocument response = await SearchAsync("bodies", query.ReferenceSystem, filters, 0, cancellationToken);
        return Results(response)
            .Select(ReadPlanetaryBody)
            .Where(body => body is not null)
            .Cast<MiningPlanetaryBody>()
            .ToArray();
    }

    private static MiningPlanetaryBody? ReadPlanetaryBody(JsonElement body)
    {
        string name = MiningJson.Text(body, "name");
        string system = MiningJson.Text(body, SystemNameField);
        if (name.Length == 0 || system.Length == 0)
        {
            return null;
        }

        return new MiningPlanetaryBody(
            system,
            name,
            MiningJson.Text(body, "subtype"),
            MiningJson.Text(body, "reserve_level"),
            Number(body, "gravity") ?? 0,
            Number(body, "distance_to_arrival") ?? 0,
            Number(body, DistanceField),
            MiningJson.Text(body, "system_controlling_power"),
            MiningJson.Text(body, "system_power_state")
        );
    }

    private static bool MatchesRingType(string requested, string actual) =>
        requested.Length == 0 || requested == "All" || actual.Equals(requested, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<MiningMarketResult>> FindMarketsAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Commodity);
        if (query.SystemOnly && !query.GalaxyWide)
        {
            return await FindSpanshMarketsAsync(query, cancellationToken).ConfigureAwait(false);
        }

        string commodity = MiningCommodityName.Normalize(query.Commodity);
        string direction = query.Buying ? "exports" : "imports";
        string path = query.GalaxyWide
            ? $"commodity/name/{Uri.EscapeDataString(commodity)}/{direction}"
            : $"system/name/{Uri.EscapeDataString(query.ReferenceSystem)}/commodity/name/{Uri.EscapeDataString(commodity)}/nearby/{direction}";
        long minimumVolume = Math.Max(1, query.MinimumDemand);
        int maximumDays = Math.Clamp((int)Math.Ceiling(MarketAge(query).TotalDays), 1, 3650);
        string uri =
            $"https://api.ardent-insight.com/v2/{path}?minVolume={minimumVolume}&maxDaysAgo={maximumDays}&maxDistance={query.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture)}&fleetCarriers={!query.ExcludeCarriers}";
        using HttpResponseMessage response = await client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = await BoundedHttpContent
            .ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Mining market response", cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Unexpected market response.");
        }

        return SortMarkets(
            document
                .RootElement.EnumerateArray()
                .Select(item => ReadArdentMarket(item, query))
                .OfType<MiningMarketResult>(),
            query.Buying
        );
    }

    private static MiningMarketResult? ReadArdentMarket(JsonElement item, MiningMarketQuery query)
    {
        string type = MiningJson.Text(item, "stationType");
        int? maxPad = Number(item, "maxLandingPadSize") is { } pad ? (int)pad : null;
        bool? largePad = maxPad is { } size ? size >= 3 : null;
        if (!MatchesStation(type, largePad, maxPad, query))
        {
            return null;
        }

        long price = (long)MiningJson.Number(item, query.Buying ? "buyPrice" : "sellPrice");
        long demand = (long)MiningJson.Number(item, "demand");
        long supply = (long)MiningJson.Number(item, "stock");
        DateTimeOffset? updated = RecentObservation(MiningJson.Text(item, "updatedAt"), MarketAge(query));
        if (!HasTradeVolume(price, demand, supply, query) || updated is null)
        {
            return null;
        }

        return new(
            MiningJson.Text(item, "systemName"),
            MiningJson.Text(item, "stationName"),
            type,
            Number(item, DistanceField),
            Number(item, "distanceToArrival"),
            price,
            demand,
            supply,
            updated,
            (long)MiningJson.Number(item, "marketId"),
            largePad
        );
    }

    public async Task<IReadOnlyList<MiningMarketResult>> FindSpanshMarketsAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = query.GalaxyWide ? [] : DistanceFilter(query.Radius);
        if (query.SystemOnly && !query.GalaxyWide)
        {
            filters[SystemNameField] = new { value = new[] { query.ReferenceSystem } };
        }

        filters[query.Buying ? "selling_commodities" : "buying_commodities"] = new
        {
            value = new[] { query.Commodity },
        };
        using JsonDocument response = await SearchAsync(
            "stations",
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken,
            "market_updated_at"
        );
        return SortMarkets(Results(response).SelectMany(station => ReadSpanshMarkets(station, query)), query.Buying);
    }

    private static IEnumerable<MiningMarketResult> ReadSpanshMarkets(JsonElement station, MiningMarketQuery query)
    {
        string type = MiningJson.Text(station, "type");
        int? maxPad = MaxPad(station);
        bool? largePad = maxPad is { } size ? size >= 3 : HasLargePad(station);
        if (!MatchesStation(type, largePad, maxPad, query))
        {
            yield break;
        }

        DateTimeOffset? updated = RecentObservation(MiningJson.Text(station, "market_updated_at"), MarketAge(query));
        if (updated is null)
        {
            yield break;
        }

        foreach (
            JsonElement item in MiningJson
                .Array(station, "market")
                .Where(m => MiningJson.Text(m, "commodity").Equals(query.Commodity, StringComparison.OrdinalIgnoreCase))
        )
        {
            long price = (long)MiningJson.Number(item, query.Buying ? "buy_price" : "sell_price");
            long supply = (long)(Number(item, "supply") ?? Number(item, "stock") ?? 0);
            long demand = (long)MiningJson.Number(item, "demand");
            if (!HasTradeVolume(price, demand, supply, query))
            {
                continue;
            }

            yield return new(
                MiningJson.Text(station, SystemNameField),
                MiningJson.Text(station, "name"),
                type,
                Number(station, DistanceField),
                Number(station, "distance_to_arrival"),
                price,
                demand,
                supply,
                updated,
                (long)MiningJson.Number(station, "market_id"),
                largePad
            );
        }
    }

    private static bool MatchesStation(string type, bool? largePad, int? maxPad, MiningMarketQuery query) =>
        (!query.ExcludeCarriers || !type.Contains("Carrier", StringComparison.OrdinalIgnoreCase))
        && (!query.LargePads || largePad == true)
        && MatchesPad(maxPad, largePad, query.PadSize)
        && (query.StationType.Length == 0 || type.Contains(query.StationType, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesPad(int? maxPad, bool? largePad, string pad)
    {
        if (pad.Length == 0 || pad.Equals("Any", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        int? size = maxPad ?? (largePad == true ? 3 : null);
        return pad switch
        {
            "L" => size >= 3,
            "M" => size == 2,
            "S" => size == 1,
            _ => true,
        };
    }

    private static TimeSpan MarketAge(MiningMarketQuery query) =>
        query.MaximumAge ?? TimeSpan.FromDays(query.MaximumAgeDays);

    private static int? MaxPad(JsonElement station)
    {
        if (Number(station, "large_pads") is > 0)
        {
            return 3;
        }

        if (Number(station, "medium_pads") is > 0)
        {
            return 2;
        }

        if (Number(station, "small_pads") is > 0)
        {
            return 1;
        }

        return HasLargePad(station) == true ? 3 : null;
    }

    private static bool HasTradeVolume(long price, long demand, long supply, MiningMarketQuery query)
    {
        long volume = query.Buying ? supply : demand;
        return price > 0
            && volume > 0
            && volume >= query.MinimumDemand
            && (query.MaximumDemand == 0 || volume <= query.MaximumDemand);
    }

    private static MiningMarketResult[] SortMarkets(IEnumerable<MiningMarketResult> results, bool buying) =>
        buying ? results.OrderBy(r => r.Price).ToArray() : results.OrderByDescending(r => r.Price).ToArray();

    private static DateTimeOffset? RecentObservation(string value, TimeSpan maximumAge) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset time
        )
        && DateTimeOffset.UtcNow - time <= maximumAge
        && time <= DateTimeOffset.UtcNow.AddMinutes(5)
            ? time
            : null;

    private static bool? HasLargePad(JsonElement station)
    {
        if (Number(station, "large_pads") is { } pads)
        {
            return pads > 0;
        }

        if (
            station.TryGetProperty("has_large_pad", out JsonElement pad)
            && pad.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            return pad.GetBoolean();
        }

        return null;
    }

    public async Task<IReadOnlyList<MiningSystemResult>> FindSystemsAsync(
        MiningSystemQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = DistanceFilter(query.Radius);
        PowerplaySpanshQuery spansh = PowerplayPlan.SpanshFilter(query.Objective, query.PowerState);
        foreach (
            (string? name, string? value, bool array) in new[]
            {
                ("security", query.Security, false),
                ("allegiance", query.Allegiance, false),
                ("government", query.Government, false),
                ("primary_economy", query.Economy, false),
                ("controlling_minor_faction_state", query.State, true),
                ("controlling_power", query.Power, true),
                ("power_state", spansh.IndexedState, true),
            }
        )
        {
            if (value.Length > 0)
            {
                filters[name] = new { value = array ? (object)new[] { value } : value };
            }
        }

        if (query.MinimumPopulation > 0)
        {
            filters["population"] = new { min = query.MinimumPopulation };
        }

        using JsonDocument response = await SearchAsync(
            "systems",
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken
        );
        IEnumerable<MiningSystemResult> systems = Results(response).Select(ReadSystem);
        if (spansh.RequiredState.Length > 0)
        {
            systems = systems.Where(system =>
                system.PowerState.Equals(spansh.RequiredState, StringComparison.OrdinalIgnoreCase)
            );
        }

        return systems.ToArray();
    }

    private static MiningSystemResult ReadSystem(JsonElement system)
    {
        string controllingPower = MiningJson.Text(system, "controlling_power");
        string reportedState = MiningJson.Text(system, "power_state");
        IReadOnlyList<PowerplayProgress> progress = MiningJson
            .Array(system, "power_conflict_progress")
            .Select(entry => new PowerplayProgress(
                MiningJson.Text(entry, "power"),
                MiningJson.Number(entry, "progress")
            ))
            .Where(entry => entry.Power.Length > 0 && double.IsFinite(entry.Progress))
            .ToArray();
        return new MiningSystemResult(
            MiningJson.Text(system, "name"),
            Number(system, DistanceField),
            MiningJson.Text(system, "security"),
            MiningJson.Text(system, "allegiance"),
            MiningJson.Text(system, "government"),
            MiningJson.Text(system, "primary_economy"),
            MiningJson.Text(system, "controlling_minor_faction_state"),
            controllingPower,
            PowerplayPlan.Infer(controllingPower, reportedState, progress),
            (long)MiningJson.Number(system, "population"),
            Coordinates(system)
        );
    }

    public async Task<IReadOnlyList<MiningMarketResult>> FindTradersAsync(
        string reference,
        string trader,
        double radius = 100,
        int page = 0,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = DistanceFilter(radius);
        filters["material_trader"] = new { value = trader };
        using JsonDocument response = await SearchAsync("stations", reference, filters, page, cancellationToken);
        return Results(response)
            .Select(s => new MiningMarketResult(
                MiningJson.Text(s, SystemNameField),
                MiningJson.Text(s, "name"),
                MiningJson.Text(s, "type"),
                Number(s, DistanceField),
                Number(s, "distance_to_arrival"),
                0,
                0,
                0,
                null,
                (long)MiningJson.Number(s, "market_id"),
                HasLargePad(s)
            ))
            .ToArray();
    }

    private async Task<JsonDocument> SearchAsync(
        string entity,
        string reference,
        Dictionary<string, object> filters,
        int page,
        CancellationToken cancellationToken,
        string sort = DistanceField
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://spansh.co.uk/api/{entity}/search")
        {
            Content = JsonContent.Create(
                new
                {
                    filters,
                    reference_system = reference.Trim(),
                    size = entity == "stations" ? 20 : 100,
                    page,
                    sort = new[]
                    {
                        new Dictionary<string, object>
                        {
                            [sort] = new { direction = sort == DistanceField ? "asc" : "desc" },
                        },
                    },
                }
            ),
        };
        using HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await BoundedHttpContent
            .ReadJsonDocumentAsync(response.Content, MaximumResponseBytes, "Mining search response", cancellationToken)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, object> DistanceFilter(double radius)
    {
        if (!double.IsFinite(radius) || radius is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Use a search radius from 1 to 500 ly.");
        }

        return new() { [DistanceField] = new { min = 0, max = radius } };
    }

    private static IEnumerable<JsonElement> Results(JsonDocument document) =>
        MiningJson.Array(document.RootElement, "results");

    private static double? Number(JsonElement data, string property) =>
        data.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number)
        && double.IsFinite(number)
            ? number
            : null;

    private static GalacticCoordinate? Coordinates(JsonElement system) =>
        Number(system, "x") is { } x && Number(system, "y") is { } y && Number(system, "z") is { } z
            ? new GalacticCoordinate(x, y, z)
            : null;

    private static GalacticCoordinate? Position(JsonElement data) =>
        Number(data, "system_x") is { } x && Number(data, "system_y") is { } y && Number(data, "system_z") is { } z
            ? new(x, y, z)
            : null;
}
