using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Navigation;

public interface ISystemSummaryClient
{
    Task<SystemSummaryLoadResult> GetAsync(
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default
    );
}

public sealed record SystemTrafficSummary(int Day, int Week, int Total);

public sealed record SystemFactionSummary(string Name, double Influence, string? State);

public sealed record SystemPoiSummary(
    int Bodies,
    int Genus,
    int Starports,
    int Outposts,
    int Settlements,
    int FleetCarriers,
    int Wars
);

public sealed record SystemSpecialSummary(string Location, IReadOnlyList<string> Details);

public sealed record StationLandingPadSummary(int Small, int Medium, int Large)
{
    public string? Largest
    {
        get
        {
            if (Large > 0)
            {
                return "Large";
            }

            if (Medium > 0)
            {
                return "Medium";
            }

            return Small > 0 ? "Small" : null;
        }
    }
}

public sealed record SystemStationSummary(
    long Id,
    string Name,
    string Type,
    string? PrimaryEconomy,
    IReadOnlyDictionary<string, double> Economies,
    string? ControllingFaction,
    string? Government,
    IReadOnlyList<string> Services,
    StationLandingPadSummary? LandingPads,
    IReadOnlyList<string> ProhibitedCommodities,
    DateTimeOffset? UpdatedAt
);

public sealed record SystemSummary(
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? Position,
    string? StarClass,
    bool? IsKnown,
    int ScannedBodyCount,
    int TotalBodyCount,
    string? DiscoveredBy,
    DateTimeOffset? DiscoveredAt,
    DateTimeOffset? LastUpdatedAt,
    SystemTrafficSummary? Traffic,
    SystemPoiSummary PointsOfInterest,
    IReadOnlyList<SystemSpecialSummary> Specials
)
{
    public IReadOnlyList<SystemStationSummary> Stations { get; init; } = [];

    public IReadOnlyList<SystemFactionSummary> Factions { get; init; } = [];
}

public sealed record SystemSummaryLoadResult(SystemSummary Summary, IReadOnlyList<string> Warnings);

public sealed class SystemSummaryClient : ISystemSummaryClient
{
    private const int MaximumResponseBytes = 32 * 1024 * 1024;

    private static readonly Uri DefaultEdsmBaseUri = new("https://www.edsm.net/");
    private static readonly Uri DefaultSpanshBaseUri = new("https://spansh.co.uk/api/");
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private static readonly HashSet<string> StarportTypes = new(
        [
            "Coriolis Starport",
            "Orbis Starport",
            "Ocellus Starport",
            "Asteroid base",
            "Planetary Port",
            "Planetary Outpost",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private readonly HttpClient client;
    private readonly Uri edsmBaseUri;
    private readonly Uri spanshBaseUri;
    private readonly Func<bool> useSpanshLastUpdated;

    public SystemSummaryClient(
        HttpClient? client = null,
        Uri? edsmBaseUri = null,
        Uri? spanshBaseUri = null,
        Func<bool>? useSpanshLastUpdated = null
    )
    {
        this.client = client ?? SharedClient;
        this.edsmBaseUri = edsmBaseUri ?? DefaultEdsmBaseUri;
        this.spanshBaseUri = spanshBaseUri ?? DefaultSpanshBaseUri;
        this.useSpanshLastUpdated = useSpanshLastUpdated ?? (() => false);
    }

    public async Task<SystemSummaryLoadResult> GetAsync(
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        string normalizedName = systemName.Trim();
        Task<FetchResult<EdsmBodiesFragment>> bodiesTask = FetchAsync(
            "EDSM bodies",
            new Uri(edsmBaseUri, "api-system-v1/bodies?systemName=" + Uri.EscapeDataString(normalizedName)),
            ParseEdsmBodies,
            cancellationToken
        );
        Task<FetchResult<EdsmTrafficFragment>> trafficTask = FetchAsync(
            "EDSM traffic",
            new Uri(edsmBaseUri, "api-system-v1/traffic?systemName=" + Uri.EscapeDataString(normalizedName)),
            ParseEdsmTraffic,
            cancellationToken
        );
        Task<FetchResult<SpanshFragment>> spanshTask =
            systemAddress > 0
                ? FetchAsync(
                    "Spansh system dump",
                    new Uri(
                        spanshBaseUri,
                        UriPath.CombineWithTrailingSeparator(
                            "dump",
                            systemAddress.ToString(CultureInfo.InvariantCulture)
                        )
                    ),
                    ParseSpanshDump,
                    cancellationToken
                )
                : Task.FromResult(FetchResult<SpanshFragment>.Empty);

        await Task.WhenAll(bodiesTask, trafficTask, spanshTask).ConfigureAwait(false);
        FetchResult<EdsmBodiesFragment> bodies = await bodiesTask.ConfigureAwait(false);
        FetchResult<EdsmTrafficFragment> traffic = await trafficTask.ConfigureAwait(false);
        FetchResult<SpanshFragment> spansh = await spanshTask.ConfigureAwait(false);
        string[] warnings = new[] { bodies.Warning, traffic.Warning, spansh.Warning }
            .Where(warning => warning is not null)
            .Cast<string>()
            .ToArray();

        long resolvedAddress =
            systemAddress > 0 ? systemAddress : bodies.Value?.SystemAddress ?? traffic.Value?.SystemAddress ?? 0;
        int scannedBodyCount = Math.Max(bodies.Value?.ScannedBodyCount ?? 0, spansh.Value?.ScannedBodyCount ?? 0);
        int totalBodyCount = Math.Max(bodies.Value?.TotalBodyCount ?? 0, spansh.Value?.TotalBodyCount ?? 0);
        int attemptedProviders = systemAddress > 0 ? 3 : 2;
        bool? isKnown;
        if (bodies.Value?.SystemAddress > 0 || traffic.Value?.SystemAddress > 0 || spansh.Value is not null)
        {
            isKnown = true;
        }
        else if (warnings.Length == attemptedProviders)
        {
            isKnown = null;
        }
        else
        {
            isKnown = false;
        }
        SystemPoiSummary points =
            spansh.Value?.PointsOfInterest ?? new SystemPoiSummary(totalBodyCount, 0, 0, 0, 0, 0, 0);
        points = points with { Bodies = totalBodyCount };
        var summary = new SystemSummary(
            normalizedName,
            resolvedAddress,
            spansh.Value?.Position,
            bodies.Value?.StarClass ?? spansh.Value?.StarClass,
            isKnown,
            scannedBodyCount,
            totalBodyCount,
            bodies.Value?.DiscoveredBy ?? traffic.Value?.DiscoveredBy,
            bodies.Value?.DiscoveredAt ?? traffic.Value?.DiscoveredAt,
            useSpanshLastUpdated() ? spansh.Value?.LastUpdatedAt : bodies.Value?.LastUpdatedAt,
            traffic.Value?.Traffic,
            points,
            spansh.Value?.Specials ?? []
        )
        {
            Stations = spansh.Value?.Stations ?? [],
            Factions = spansh.Value?.Factions ?? [],
        };
        return new SystemSummaryLoadResult(summary, warnings);
    }

    private async Task<FetchResult<T>> FetchAsync<T>(
        string provider,
        Uri requestUri,
        Func<JsonElement, T> parser,
        CancellationToken cancellationToken
    )
        where T : class
    {
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using JsonDocument document = await BoundedHttpContent
                .ReadJsonDocumentAsync(
                    response.Content,
                    MaximumResponseBytes,
                    $"The {provider} response",
                    cancellationToken
                )
                .ConfigureAwait(false);
            return new FetchResult<T>(parser(document.RootElement), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            return new FetchResult<T>(null, $"{provider} data is unavailable: {exception.Message}");
        }
    }

    private static EdsmBodiesFragment ParseEdsmBodies(JsonElement root)
    {
        RequireObject(root, "EDSM bodies response");
        JsonElement[] bodies = GetArray(root, "bodies");
        string? starClass = null;
        string? discoveredBy = null;
        DateTimeOffset? discoveredAt = null;
        DateTimeOffset? lastUpdatedAt = null;
        foreach (JsonElement body in bodies)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (
                starClass is null
                && GetBoolean(body, "isMainStar") == true
                && GetString(body, "spectralClass") is { Length: > 0 } spectral
            )
            {
                starClass = spectral[..1];
            }

            if (discoveredBy is null && TryGetObject(body, "discovery", out JsonElement discovery))
            {
                discoveredBy = GetString(discovery, "commander");
                discoveredAt = GetDateTimeOffset(discovery, "date");
            }

            DateTimeOffset? updated = GetDateTimeOffset(body, "updateTime");
            if (updated is not null && (lastUpdatedAt is null || updated > lastUpdatedAt))
            {
                lastUpdatedAt = updated;
            }
        }

        return new EdsmBodiesFragment(
            GetInt64(root, "id64") ?? 0,
            bodies.Length,
            GetInt32(root, "bodyCount") ?? 0,
            starClass,
            discoveredBy,
            discoveredAt,
            lastUpdatedAt
        );
    }

    private static EdsmTrafficFragment ParseEdsmTraffic(JsonElement root)
    {
        RequireObject(root, "EDSM traffic response");
        SystemTrafficSummary? traffic = null;
        if (TryGetObject(root, "traffic", out JsonElement trafficElement))
        {
            traffic = new SystemTrafficSummary(
                GetInt32(trafficElement, "day") ?? 0,
                GetInt32(trafficElement, "week") ?? 0,
                GetInt32(trafficElement, "total") ?? 0
            );
        }

        string? discoveredBy = null;
        DateTimeOffset? discoveredAt = null;
        if (TryGetObject(root, "discovery", out JsonElement discovery))
        {
            discoveredBy = GetString(discovery, "commander");
            discoveredAt = GetDateTimeOffset(discovery, "date");
        }

        return new EdsmTrafficFragment(GetInt64(root, "id64") ?? 0, discoveredBy, discoveredAt, traffic);
    }

    private static SpanshFragment ParseSpanshDump(JsonElement root)
    {
        RequireObject(root, "Spansh system dump response");
        if (!TryGetObject(root, "system", out JsonElement system))
        {
            throw new InvalidDataException("The Spansh system dump has no system object.");
        }

        GalacticCoordinate? position = ReadSpanshPosition(system);
        JsonElement[] bodies = GetArray(system, "bodies");
        (int scannedBodies, int genus, string? starClass) = SummarizeSpanshBodies(bodies);
        JsonElement[] stationElements = EnumerateStations(system, bodies).ToArray();
        (Dictionary<string, List<string>>? specials, int starports, int outposts, int settlements, int fleetCarriers) =
            SummarizeSpanshStations(stationElements);
        SystemFactionSummary[] factions = ParseSpanshFactions(system, out int warPresences);
        int totalBodies = GetInt32(system, "bodyCount") ?? 0;
        return new SpanshFragment(
            position,
            starClass,
            scannedBodies,
            totalBodies,
            GetDateTimeOffset(system, "updated_at"),
            new SystemPoiSummary(totalBodies, genus, starports, outposts, settlements, fleetCarriers, warPresences / 2),
            specials.Select(pair => new SystemSpecialSummary(pair.Key, pair.Value.ToArray())).ToArray(),
            stationElements
                .Select(ParseStation)
                .Where(station => !string.IsNullOrWhiteSpace(station.Name))
                .OrderBy(station => station.Name)
                .ToArray(),
            factions
        );
    }

    private static GalacticCoordinate? ReadSpanshPosition(JsonElement system)
    {
        if (
            TryGetObject(system, "coords", out JsonElement coords)
            && GetDouble(coords, "x") is { } x
            && GetDouble(coords, "y") is { } y
            && GetDouble(coords, "z") is { } z
        )
        {
            return new GalacticCoordinate(x, y, z);
        }

        return null;
    }

    private static (int ScannedBodies, int Genus, string? StarClass) SummarizeSpanshBodies(
        IReadOnlyList<JsonElement> bodies
    )
    {
        int scannedBodies = 0;
        int genus = 0;
        string? starClass = null;
        foreach (JsonElement body in bodies)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AccumulateSpanshBody(body, ref scannedBodies, ref genus, ref starClass);
        }

        return (scannedBodies, genus, starClass);
    }

    private static void AccumulateSpanshBody(
        JsonElement body,
        ref int scannedBodies,
        ref int genus,
        ref string? starClass
    )
    {
        if (!string.Equals(GetString(body, "type"), "Barycentre", StringComparison.OrdinalIgnoreCase))
        {
            scannedBodies++;
        }

        if (
            starClass is null
            && GetBoolean(body, "mainStar") == true
            && GetString(body, "spectralClass") is { Length: > 0 } spectral
        )
        {
            starClass = spectral[..1];
        }

        if (
            TryGetObject(body, "signals", out JsonElement signals)
            && TryGetObject(signals, "signals", out JsonElement signalCounts)
        )
        {
            genus += GetInt32(signalCounts, "$SAA_SignalType_Biological;") ?? 0;
        }
    }

    private static (
        Dictionary<string, List<string>> Specials,
        int Starports,
        int Outposts,
        int Settlements,
        int FleetCarriers
    ) SummarizeSpanshStations(IReadOnlyList<JsonElement> stationElements)
    {
        var specials = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        int starports = 0;
        int outposts = 0;
        int settlements = 0;
        int fleetCarriers = 0;
        foreach (JsonElement station in stationElements)
        {
            CountStationType(station, ref starports, ref outposts, ref settlements, ref fleetCarriers);
            AddStationSpecials(station, specials);
        }

        return (specials, starports, outposts, settlements, fleetCarriers);
    }

    private static void CountStationType(
        JsonElement station,
        ref int starports,
        ref int outposts,
        ref int settlements,
        ref int fleetCarriers
    )
    {
        string type = GetString(station, "type") ?? string.Empty;
        if (string.Equals(type, "Drake-Class Carrier", StringComparison.OrdinalIgnoreCase))
        {
            fleetCarriers++;
        }

        if (string.Equals(type, "Settlement", StringComparison.OrdinalIgnoreCase))
        {
            settlements++;
        }

        if (string.Equals(type, "Outpost", StringComparison.OrdinalIgnoreCase))
        {
            outposts++;
        }

        if (IsStarportStation(station, type))
        {
            starports++;
        }
    }

    private static bool IsStarportStation(JsonElement station, string type)
    {
        if (StarportTypes.Contains(type))
        {
            return true;
        }

        return string.Equals(type, "Mega ship", StringComparison.OrdinalIgnoreCase)
            && station.TryGetProperty("landingPads", out JsonElement landingPads)
            && landingPads.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static SystemFactionSummary[] ParseSpanshFactions(JsonElement system, out int warPresences)
    {
        JsonElement[] factionElements = GetArray(system, "factions");
        warPresences = factionElements.Count(faction =>
            faction.ValueKind == JsonValueKind.Object && GetString(faction, "state") is "War" or "Civil War"
        );
        return factionElements
            .Where(faction => faction.ValueKind == JsonValueKind.Object)
            .Select(faction => new SystemFactionSummary(
                GetString(faction, "name") ?? string.Empty,
                GetDouble(faction, "influence") ?? 0,
                GetString(faction, "state")
            ))
            .Where(faction => !string.IsNullOrWhiteSpace(faction.Name))
            .OrderByDescending(faction => faction.Influence)
            .ThenBy(faction => faction.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SystemStationSummary ParseStation(JsonElement station)
    {
        Dictionary<string, double> economies = TryGetObject(station, "economies", out JsonElement values)
            ? values
                .EnumerateObject()
                .Where(property =>
                    property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out _)
                )
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.GetDouble(),
                    StringComparer.OrdinalIgnoreCase
                )
            : new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string[] services = GetArray(station, "services")
            .Where(service => service.ValueKind == JsonValueKind.String)
            .Select(service => service.GetString())
            .Where(service => !string.IsNullOrWhiteSpace(service))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(service => service)
            .ToArray();
        StationLandingPadSummary? landingPads = null;
        if (TryGetObject(station, "landingPads", out JsonElement pads))
        {
            landingPads = new StationLandingPadSummary(
                GetInt32(pads, "small") ?? GetInt32(pads, "Small") ?? 0,
                GetInt32(pads, "medium") ?? GetInt32(pads, "Medium") ?? 0,
                GetInt32(pads, "large") ?? GetInt32(pads, "Large") ?? 0
            );
        }

        IReadOnlyList<string> prohibited = [];
        if (TryGetObject(station, "market", out JsonElement market))
        {
            prohibited = GetArray(market, "prohibitedCommodities")
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .OrderBy(item => item)
                .ToArray();
        }

        return new SystemStationSummary(
            GetInt64(station, "id") ?? 0,
            GetString(station, "name") ?? string.Empty,
            GetString(station, "type") ?? "Station",
            GetString(station, "primaryEconomy"),
            economies,
            GetString(station, "controllingFaction"),
            GetString(station, "government"),
            services,
            landingPads,
            prohibited,
            GetDateTimeOffset(station, "updateTime")
        );
    }

    private static IEnumerable<JsonElement> EnumerateStations(JsonElement system, IReadOnlyList<JsonElement> bodies)
    {
        foreach (JsonElement station in GetArray(system, "stations"))
        {
            if (station.ValueKind == JsonValueKind.Object)
            {
                yield return station;
            }
        }

        foreach (JsonElement body in bodies)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonElement station in GetArray(body, "stations"))
            {
                if (station.ValueKind == JsonValueKind.Object)
                {
                    yield return station;
                }
            }
        }
    }

    private static void AddStationSpecials(JsonElement station, Dictionary<string, List<string>> specials)
    {
        string stationName = GetString(station, "name") ?? "Station";
        var services = GetArray(station, "services")
            .Select(service => service.ValueKind == JsonValueKind.String ? service.GetString() : null)
            .Where(service => service is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (services.Contains("Material Trader"))
        {
            AddSpecial(specials, stationName, "Material Trader" + FormatType(GetMaterialTraderType(station)));
        }

        if (services.Contains("Technology Broker"))
        {
            AddSpecial(specials, stationName, "Technology Broker" + FormatType(GetTechnologyBrokerType(station)));
        }

        if (string.Equals(GetString(station, "government"), "Engineer", StringComparison.OrdinalIgnoreCase))
        {
            string? faction = GetString(station, "controllingFaction");
            AddSpecial(specials, stationName, string.IsNullOrWhiteSpace(faction) ? "Engineer" : faction + " Engineer");
        }
    }

    private static string? GetMaterialTraderType(JsonElement station)
    {
        foreach (string economy in GetEconomies(station))
        {
            if (economy is "high tech" or "military")
            {
                return "Encoded";
            }

            if (economy is "extraction" or "refinery")
            {
                return "Raw";
            }

            if (economy == "industrial")
            {
                return "Manufactured";
            }
        }

        return null;
    }

    private static string? GetTechnologyBrokerType(JsonElement station)
    {
        if (
            GetInt64(station, "id") is > 4_200_000_000
            || string.Equals(GetString(station, "type"), "Dodec Starport", StringComparison.OrdinalIgnoreCase)
        )
        {
            return "Human";
        }

        foreach (string economy in GetEconomies(station))
        {
            if (economy == "high tech")
            {
                return "Guardian";
            }

            if (economy is "industrial" or "rescue")
            {
                return "Human";
            }
        }

        return null;
    }

    private static IEnumerable<string> GetEconomies(JsonElement station)
    {
        if (GetString(station, "primaryEconomy") is { Length: > 0 } primary)
        {
            yield return primary.ToLowerInvariant();
        }

        if (!TryGetObject(station, "economies", out JsonElement economies))
        {
            yield break;
        }

        string? secondary = economies
            .EnumerateObject()
            .Select(property => new
            {
                property.Name,
                Share = property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetDouble(out double share)
                    ? share
                    : double.MaxValue,
            })
            .OrderBy(economy => economy.Share)
            .Skip(1)
            .Select(economy => economy.Name)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            yield return secondary.ToLowerInvariant();
        }
    }

    private static string FormatType(string? type)
    {
        return string.IsNullOrWhiteSpace(type) ? string.Empty : " - " + type;
    }

    private static void AddSpecial(Dictionary<string, List<string>> specials, string location, string detail)
    {
        if (!specials.TryGetValue(location, out List<string>? details))
        {
            details = [];
            specials[location] = details;
        }

        if (!details.Contains(detail, StringComparer.OrdinalIgnoreCase))
        {
            details.Add(detail);
        }
    }

    private static void RequireObject(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"The {description} is not an object.");
        }
    }

    private static JsonElement[] GetArray(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        return element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
            ? result
            : null;
    }

    private static long? GetInt64(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long result)
            ? result
            : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double result)
            && double.IsFinite(result)
            ? result
            : null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        return
            element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out DateTimeOffset result)
            ? result
            : null;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SrvSurvey-Avalonia/1.0");
        return client;
    }

    private sealed record FetchResult<T>(T? Value, string? Warning)
        where T : class
    {
        public static FetchResult<T> Empty { get; } = new(null, null);
    }

    private sealed record EdsmBodiesFragment(
        long SystemAddress,
        int ScannedBodyCount,
        int TotalBodyCount,
        string? StarClass,
        string? DiscoveredBy,
        DateTimeOffset? DiscoveredAt,
        DateTimeOffset? LastUpdatedAt
    );

    private sealed record EdsmTrafficFragment(
        long SystemAddress,
        string? DiscoveredBy,
        DateTimeOffset? DiscoveredAt,
        SystemTrafficSummary? Traffic
    );

    private sealed record SpanshFragment(
        GalacticCoordinate? Position,
        string? StarClass,
        int ScannedBodyCount,
        int TotalBodyCount,
        DateTimeOffset? LastUpdatedAt,
        SystemPoiSummary PointsOfInterest,
        IReadOnlyList<SystemSpecialSummary> Specials,
        IReadOnlyList<SystemStationSummary> Stations,
        IReadOnlyList<SystemFactionSummary> Factions
    );
}
