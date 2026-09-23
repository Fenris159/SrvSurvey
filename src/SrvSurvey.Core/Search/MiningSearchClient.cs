using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Search;

public sealed record MiningCommodityPriceSummary(string Commodity, long AverageSellPrice, long MaximumSellPrice);

public sealed record MiningBodyParent(long Id64, string Type, string Subtype);

public sealed record MiningPlanetaryQuery(
    string ReferenceSystem,
    IReadOnlyList<string> BodySubtypes,
    IReadOnlyList<string> LandmarkSubtypes,
    string Reserve = "",
    double Radius = 100,
    IReadOnlyList<string>? ControllingPowers = null,
    string PowerState = "",
    int Page = 0,
    bool GalaxyWide = false,
    IReadOnlyList<string>? VolcanismTypes = null,
    IReadOnlyList<string>? SystemNames = null
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
    string PowerState = "",
    IReadOnlyList<string>? Landmarks = null,
    IReadOnlyList<MiningBodyParent>? Parents = null,
    string VolcanismType = ""
);

public sealed record MiningRingQuery(
    string ReferenceSystem,
    string Mineral,
    string RingType,
    double Radius,
    int MinimumHotspots = 1,
    int Page = 0,
    bool SystemOnly = false,
    IReadOnlyList<string>? Minerals = null,
    IReadOnlyList<string>? SystemNames = null
)
{
    public bool GalaxyWide { get; init; }
}

public sealed record MiningRingPage(IReadOnlyList<MiningRing> Rings, bool HasMore);

public sealed record MiningPlanetaryBodyPage(IReadOnlyList<MiningPlanetaryBody> Bodies, bool HasMore);

public sealed record MiningSystemPage(IReadOnlyList<MiningSystemResult> Systems, bool HasMore);

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

public sealed record MiningSellQuote(
    string System,
    string Station,
    string StationType,
    string Pad,
    double? ArrivalLs,
    string Commodity,
    long Price,
    long Demand,
    DateTimeOffset? Updated = null
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
    public string Commodity { get; init; } = "";

    public string? QuotedPad { get; init; }

    public string PadDescription =>
        QuotedPad
        ?? LargePad switch
        {
            true => "Large",
            false => "Small / medium pads",
            null => "Pad unknown",
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
)
{
    public bool GalaxyWide { get; init; }
}

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
)
{
    public IReadOnlyList<string> NearbyPowers { get; init; } = [];
    public IReadOnlyList<PowerplayProgress> Conflict { get; init; } = [];
}

/// <summary>Mining searches extend the shared Spansh pathway and use the application's network/privacy client.</summary>
public sealed class MiningSearchClient
{
    private const string SystemNameField = "system_name";
    private const string DistanceField = "distance";
    private const string ArrivalField = "distance_to_arrival";
    private const string SubtypeField = "subtype";
    private const string DemandField = "demand";
    private const string CommodityField = "commodity";
    private const string ArdentProvider = "Ardent";
    private const string SpanshProvider = "Spansh";
    private const string MarketResponse = "Mining market response";
    private const string MarketUpdatedSort = "market_updated_at";
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan CommodityReportCheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan CommodityReportRetryDelay = TimeSpan.FromHours(1);
    private static readonly IReadOnlyDictionary<string, MiningCommodityPriceSummary> EmptyCommodityReport =
        new Dictionary<string, MiningCommodityPriceSummary>(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(35) };
    private readonly ArdentApi ardent;
    private readonly SpanshApi spansh;
    private readonly ProviderFailureLog diagnostics = new();
    private readonly SemaphoreSlim commodityReportGate = new(1, 1);
    private readonly TimeProvider timeProvider;
    private readonly MiningCommodityPriceReportStore? commodityReportStore;
    private IReadOnlyDictionary<string, MiningCommodityPriceSummary>? commodityReport;
    private DateTimeOffset nextCommodityReportRefresh;
    private string? lastCommodityName;
    private DateTimeOffset? lastCommodityUpdatedAt;
    private DateTimeOffset? liveCommodityQuoteUpdatedAt;
    private int liveSurfaceQuoteCount;
    private Dictionary<string, long>? averageSellPrices;

    public Action<string>? DiagnosticLog { get; set; }

    public MiningSearchClient(
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        MiningCommodityPriceReportStore? commodityReportStore = null
    )
    {
        HttpClient http = httpClient ?? SharedClient;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.commodityReportStore = commodityReportStore;
        ardent = new ArdentApi(
            http,
            onFailure: (route, exception) => diagnostics.Record(ArdentProvider, route, exception)
        );
        spansh = new SpanshApi(
            http,
            onFailure: (route, exception) => diagnostics.Record(SpanshProvider, route, exception)
        );
        if (commodityReportStore?.Load() is { } snapshot)
        {
            var cached = snapshot
                .Prices.Where(item => !string.IsNullOrWhiteSpace(item.Commodity))
                .GroupBy(item => MiningCommodityName.Key(item.Commodity), StringComparer.Ordinal)
                .Select(group =>
                    group.First() with
                    {
                        Commodity = MiningCommodityName.Canonical(group.First().Commodity),
                    }
                )
                .ToDictionary(item => item.Commodity, StringComparer.OrdinalIgnoreCase);
            if (cached.Count > 0)
            {
                commodityReport = cached;
                CommodityReportFetchedAt = snapshot.FetchedAt;
                lastCommodityUpdatedAt = snapshot.SourceUpdatedAt;
                lastCommodityName = snapshot.LastCommodityName;
                liveCommodityQuoteUpdatedAt = snapshot.LiveQuoteUpdatedAt;
                liveSurfaceQuoteCount = snapshot.LiveSurfaceQuoteCount;
                averageSellPrices = cached
                    .Values.Where(item => item.AverageSellPrice > 0)
                    .ToDictionary(
                        item => item.Commodity,
                        item => item.AverageSellPrice,
                        StringComparer.OrdinalIgnoreCase
                    );
            }
        }
    }

    public bool PriceMarksUnavailable { get; private set; }

    public DateTimeOffset? CommodityReportFetchedAt { get; private set; }

    public DateTimeOffset? CommodityReportSourceUpdatedAt => lastCommodityUpdatedAt;

    public DateTimeOffset? CommodityLiveQuoteUpdatedAt => liveCommodityQuoteUpdatedAt;

    public int LiveSurfaceQuoteCount => liveSurfaceQuoteCount;

    public static int SurfaceQuoteCount => SurfaceMiningCommodityCatalog.All.Count;

    public IReadOnlyDictionary<string, MiningCommodityPriceSummary>? CachedCommodityPriceReport => commodityReport;

    public void ResetDiagnostics()
    {
        _ = diagnostics.Drain();
        PriceMarksUnavailable = false;
    }

    public void FlushDiagnostics()
    {
        if (DiagnosticLog is not { } log)
        {
            _ = diagnostics.Drain();
            return;
        }

        foreach (string line in diagnostics.Drain())
        {
            try
            {
                log(line);
            }
            catch (Exception)
            {
                // Diagnostics must never interrupt a search.
            }
        }
    }

    public async Task<IReadOnlyList<MiningRing>> FindRingsAsync(
        MiningRingQuery query,
        CancellationToken cancellationToken = default
    )
    {
        MiningRingPage page = await FindRingPageAsync(query, cancellationToken).ConfigureAwait(false);
        return page.Rings;
    }

    public async Task<IReadOnlyList<MiningRing>> FindRingsForSystemsAsync(
        MiningRingQuery query,
        IReadOnlyList<string> systems,
        CancellationToken cancellationToken = default
    )
    {
        var rings = new List<MiningRing>();
        foreach (string[] names in systems.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(20))
        {
            for (int page = 0; page < 10; page++)
            {
                MiningRingPage batch = await FindRingPageAsync(
                        query with
                        {
                            Page = page,
                            SystemOnly = false,
                            SystemNames = names,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                rings.AddRange(batch.Rings);
                if (!batch.HasMore)
                {
                    break;
                }
            }
        }

        return rings;
    }

    public async Task<MiningRingPage> FindRingPageAsync(
        MiningRingQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = InitialFilters(query.Radius, query.GalaxyWide);
        if (query.SystemNames is { Count: > 0 })
        {
            filters[SystemNameField] = new { value = query.SystemNames.ToArray() };
        }
        else if (query.SystemOnly)
        {
            filters[SystemNameField] = new { value = new[] { query.ReferenceSystem } };
        }

        string[] minerals = (query.Minerals ?? [])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (minerals.Length == 0 && query.Mineral.Length > 0)
        {
            minerals = [query.Mineral];
        }

        if (minerals.Length is > 0 and <= 8)
        {
            filters["ring_signals"] = new[]
            {
                new
                {
                    comparison = "<=>",
                    count = new[] { query.MinimumHotspots, 9999 },
                    name = minerals,
                },
            };
        }
        else if (query.MinimumHotspots > 0)
        {
            // A long name list is treated as "any of these hotspots", which Spansh cannot express
            // as one OR filter. Ask for any hotspot and keep the mineral check while reading.
            filters["ring_signals"] = new[] { new { comparison = ">=", count = query.MinimumHotspots } };
        }

        if (query.RingType.Length > 0 && query.RingType != "All")
        {
            filters["rings"] = new[] { new { type = new[] { query.RingType } } };
        }

        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Bodies,
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken: cancellationToken
        );
        var rings = new List<MiningRing>();
        int bodies = 0;
        foreach (JsonElement body in Results(response))
        {
            bodies++;
            foreach (JsonElement ring in MiningJson.Array(body, "rings"))
            {
                MiningRing? read = ReadRing(body, ring, query);
                if (read is not null)
                {
                    rings.Add(read);
                }
            }
        }

        return new MiningRingPage(rings, bodies >= SpanshRoutes.PageSize(SpanshRoutes.Bodies));
    }

    private static MiningRing? ReadRing(JsonElement body, JsonElement ring, MiningRingQuery query)
    {
        string type = MiningJson.Text(ring, "type");
        if (!MatchesRingType(query.RingType, type))
        {
            return null;
        }

        var signals = MiningJson
            .Array(ring, "signals")
            .Where(signal => MiningJson.Text(signal, "name").Length > 0)
            .GroupBy(signal => MiningCommodityName.Canonical(MiningJson.Text(signal, "name")))
            .ToDictionary(group => group.Key, group => (int)group.Max(signal => MiningJson.Number(signal, "count")));
        if (
            query.Mineral.Length > 0
            && !signals.Any(pair =>
                MiningCommodityName.Same(pair.Key, query.Mineral) && pair.Value >= query.MinimumHotspots
            )
        )
        {
            return null;
        }

        if (
            query.Minerals is { Count: > 8 } wanted
            && !signals.Any(pair =>
                wanted.Any(name => MiningCommodityName.Same(name, pair.Key)) && pair.Value >= query.MinimumHotspots
            )
        )
        {
            return null;
        }

        return new MiningRing
        {
            System = MiningJson.Text(body, SystemNameField),
            Body = MiningJson.Text(ring, "name"),
            RingType = type,
            Reserve = MiningJson.Text(body, "reserve_level"),
            ArrivalLs = Number(body, ArrivalField),
            Position = Position(body),
            Hotspots = signals,
            Source = "Spansh",
            Scanned = DateTimeOffset.UtcNow,
            DistanceLy = Number(body, DistanceField),
            Power = MiningJson.Text(body, "system_controlling_power"),
            PowerState = MiningJson.Text(body, "system_power_state"),
        };
    }

    public async Task<IReadOnlyList<MiningPlanetaryBody>> FindPlanetaryBodiesAsync(
        MiningPlanetaryQuery query,
        CancellationToken cancellationToken = default
    ) => (await FindPlanetaryBodyPageAsync(query, cancellationToken).ConfigureAwait(false)).Bodies;

    public async Task<MiningPlanetaryBodyPage> FindPlanetaryBodyPageAsync(
        MiningPlanetaryQuery query,
        CancellationToken cancellationToken = default
    )
    {
        if (query.BodySubtypes.Count == 0)
        {
            return new MiningPlanetaryBodyPage([], false);
        }

        Dictionary<string, object> filters = query.GalaxyWide ? [] : DistanceFilter(query.Radius);
        filters["is_landable"] = new { value = true };
        filters[SubtypeField] = new { value = query.BodySubtypes.ToArray() };
        if (query.ControllingPowers is { Count: > 0 })
        {
            filters["system_controlling_power"] = new
            {
                value = query.ControllingPowers.Select(PowerplayPlan.SpanshPowerName).ToArray(),
            };
        }

        if (query.SystemNames is { Count: > 0 })
        {
            filters[SystemNameField] = new { value = query.SystemNames.ToArray() };
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

        if (query.VolcanismTypes is { Count: > 0 })
        {
            filters["volcanism_type"] = new { value = query.VolcanismTypes.ToArray() };
        }

        if (query.Reserve.Length > 0)
        {
            filters["reserve_level"] = new { value = new[] { query.Reserve } };
        }

        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Bodies,
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken: cancellationToken
        );
        JsonElement[] results = Results(response).ToArray();
        MiningPlanetaryBody[] bodies = results
            .Select(ReadPlanetaryBody)
            .Where(body => body is not null)
            .Cast<MiningPlanetaryBody>()
            .ToArray();
        return new MiningPlanetaryBodyPage(bodies, results.Length >= SpanshRoutes.PageSize(SpanshRoutes.Bodies));
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
            MiningJson.Text(body, SubtypeField),
            MiningJson.Text(body, "reserve_level"),
            Number(body, "gravity") ?? 0,
            Number(body, ArrivalField) ?? 0,
            Number(body, DistanceField),
            MiningJson.Text(body, "system_controlling_power"),
            MiningJson.Text(body, "system_power_state"),
            ReadLandmarkSubtypes(body),
            ReadBodyParents(body),
            MiningJson.Text(body, "volcanism_type")
        );
    }

    public async Task<IReadOnlyDictionary<long, IReadOnlyList<MiningBodyParent>>> FindBodyParentsAsync(
        string reference,
        IReadOnlyList<long> bodyIds,
        CancellationToken cancellationToken = default
    )
    {
        var parents = new Dictionary<long, IReadOnlyList<MiningBodyParent>>();
        foreach (long[] chunk in bodyIds.Where(id => id > 0).Distinct().Chunk(100))
        {
            Dictionary<string, object> filters = new() { ["id64"] = new { value = chunk } };
            using JsonDocument response = await spansh
                .SearchAsync(SpanshRoutes.Bodies, reference, filters, 0, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (JsonElement body in Results(response))
            {
                long id = BodyId(body);
                if (id > 0)
                {
                    parents[id] = ReadBodyParents(body);
                }
            }
        }

        return parents;
    }

    private static MiningBodyParent[] ReadBodyParents(JsonElement body) =>
        MiningJson
            .Array(body, "parents")
            .Select(parent => new MiningBodyParent(
                BodyId(parent),
                MiningJson.Text(parent, "type"),
                MiningJson.Text(parent, SubtypeField)
            ))
            .ToArray();

    private static long BodyId(JsonElement body) =>
        body.TryGetProperty("id64", out JsonElement value) && value.TryGetInt64(out long id) ? id : 0;

    public async Task<IReadOnlySet<string>> FindWhiteDwarfHostedBodiesAsync(
        string reference,
        IReadOnlyList<MiningPlanetaryBody> bodies,
        CancellationToken cancellationToken = default
    )
    {
        HashSet<string> hosted = new(StringComparer.OrdinalIgnoreCase);
        (string Key, MiningBodyParent Parent)[] frontier = bodies
            .SelectMany(body =>
                (body.Parents ?? []).Select(parent => (Key: body.System + "\u001f" + body.Body, Parent: parent))
            )
            .ToArray();
        var knownParents = new Dictionary<long, IReadOnlyList<MiningBodyParent>>();
        for (int depth = 0; depth < 6 && frontier.Length > 0; depth++)
        {
            long[] unresolved = frontier
                .Select(entry => entry.Parent)
                .Where(parent => parent.Id64 > 0 && parent.Type.Equals("Planet", StringComparison.OrdinalIgnoreCase))
                .Select(parent => parent.Id64)
                .Distinct()
                .Where(id => !knownParents.ContainsKey(id))
                .ToArray();
            if (unresolved.Length > 0)
            {
                IReadOnlyDictionary<long, IReadOnlyList<MiningBodyParent>> fetched = await FindBodyParentsAsync(
                        reference,
                        unresolved,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                foreach (long id in unresolved)
                {
                    knownParents[id] = fetched.GetValueOrDefault(id) ?? [];
                }
            }

            var next = new List<(string Key, MiningBodyParent Parent)>();
            foreach ((string key, MiningBodyParent parent) in frontier)
            {
                if (PlanetaryMiningPlan.IsWhiteDwarf(parent))
                {
                    hosted.Add(key);
                }
                else if (parent.Type.Equals("Planet", StringComparison.OrdinalIgnoreCase) && parent.Id64 > 0)
                {
                    next.AddRange(knownParents[parent.Id64].Select(ancestor => (key, ancestor)));
                }
            }

            frontier = next.ToArray();
        }

        return hosted;
    }

    private static string[] ReadLandmarkSubtypes(JsonElement body)
    {
        if (!body.TryGetProperty("landmarks", out JsonElement landmarks) || landmarks.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return landmarks
            .EnumerateArray()
            .Where(landmark => landmark.ValueKind == JsonValueKind.Object)
            .Select(landmark => MiningJson.Text(landmark, SubtypeField))
            .Where(subtype => subtype.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesRingType(string requested, string actual) =>
        requested.Length == 0 || requested == "All" || actual.Equals(requested, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<MiningMarketResult>> FindMarketsAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Commodity);
        string commodity = MiningCommodityName.Normalize(query.Commodity);
        string direction = query.Buying ? "exports" : "imports";
        long minimumVolume = Math.Max(1, query.MinimumDemand);
        int maximumDays = Math.Clamp((int)Math.Ceiling(MarketAge(query).TotalDays), 1, 3650);
        string radius = query.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string route;
        if (query.SystemOnly && !query.GalaxyWide)
        {
            route = ArdentRoutes.SystemCommodity(query.ReferenceSystem, commodity, maximumDays);
        }
        else if (query.GalaxyWide)
        {
            route = ArdentRoutes.GalaxyCommodity(
                commodity,
                direction,
                minimumVolume,
                maximumDays,
                query.ExcludeCarriers
            );
        }
        else
        {
            route = ArdentRoutes.NearbyCommodity(
                query.ReferenceSystem,
                commodity,
                direction,
                minimumVolume,
                maximumDays,
                radius,
                query.ExcludeCarriers
            );
        }
        using JsonDocument document = await ardent
            .GetAsync(route, MaximumResponseBytes, MarketResponse, cancellationToken)
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

    public async Task<IReadOnlyList<MiningMarketResult>> FindSystemImportsAsync(
        string system,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default
    ) =>
        await FindSystemImportsAsync(
                system,
                new MiningMarketQuery(system, "Any", false, MaximumAge: maximumAge),
                cancellationToken
            )
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<MiningMarketResult>> FindSystemImportsAsync(
        string system,
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        int maximumDays = Math.Clamp((int)Math.Ceiling(MarketAge(query).TotalDays), 1, 3650);
        using JsonDocument document = await ardent
            .GetAsync(
                ArdentRoutes.SystemImports(system, maximumDays),
                MaximumResponseBytes,
                MarketResponse,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return document
            .RootElement.EnumerateArray()
            .Select(item => ReadArdentMarket(item, query))
            .OfType<MiningMarketResult>()
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, MiningCommodityPriceSummary>> CommodityPriceReportAsync(
        CancellationToken cancellationToken = default
    )
    {
        await commodityReportGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (now < nextCommodityReportRefresh)
            {
                return commodityReport ?? EmptyCommodityReport;
            }

            if (await ShouldUseCachedCommodityReportAsync(now, cancellationToken).ConfigureAwait(false))
            {
                return commodityReport ?? EmptyCommodityReport;
            }

            using JsonDocument document = await ardent
                .GetAsync(ArdentRoutes.Commodities, MaximumResponseBytes, MarketResponse, cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Commodity prices were not a list.");
            }

            JsonElement[] items = document.RootElement.EnumerateArray().ToArray();
            var prices = items
                .Select(item => new MiningCommodityPriceSummary(
                    MiningJson.Text(item, "commodityName"),
                    (long)MiningJson.Number(item, "avgSellPrice"),
                    (long)MiningJson.Number(item, "maxSellPrice")
                ))
                .Where(item => item.Commodity.Length > 0 && (item.AverageSellPrice > 0 || item.MaximumSellPrice > 0))
                .GroupBy(item => MiningCommodityName.Key(item.Commodity), StringComparer.Ordinal)
                .Select(group =>
                    group
                        .OrderByDescending(item =>
                            item.Commodity.Equals(
                                MiningCommodityName.Normalize(item.Commodity),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .First()
                )
                .Select(item => item with { Commodity = MiningCommodityName.Canonical(item.Commodity) })
                .ToDictionary(item => item.Commodity, StringComparer.OrdinalIgnoreCase);
            if (prices.Count == 0)
            {
                throw new InvalidDataException("Commodity prices were empty.");
            }

            (int liveCount, DateTimeOffset? quoteUpdatedAt, bool anyFailures) = await OverlayCurrentSurfacePricesAsync(
                    prices,
                    cancellationToken
                )
                .ConfigureAwait(false);

            commodityReport = prices;
            CommodityReportFetchedAt = now;
            liveSurfaceQuoteCount = liveCount;
            liveCommodityQuoteUpdatedAt = quoteUpdatedAt;
            lastCommodityName = MiningJson.Text(items[^1], "commodityName");
            lastCommodityUpdatedAt = await LastCommodityUpdatedAtAsync(cancellationToken).ConfigureAwait(false);
            averageSellPrices = prices
                .Values.Where(item => item.AverageSellPrice > 0)
                .ToDictionary(item => item.Commodity, item => item.AverageSellPrice, StringComparer.OrdinalIgnoreCase);
            nextCommodityReportRefresh = NextCommodityRefreshBoundary(now, now + CommodityReportCheckInterval);
            PriceMarksUnavailable = anyFailures;
            if (commodityReportStore is not null)
            {
                try
                {
                    commodityReportStore.Save(
                        new MiningCommodityPriceReportSnapshot(
                            now,
                            lastCommodityUpdatedAt,
                            lastCommodityName,
                            prices.Values.ToArray(),
                            Version: 2,
                            LiveSurfaceQuoteCount: liveCount,
                            LiveQuoteUpdatedAt: quoteUpdatedAt
                        )
                    );
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Record("Local cache", commodityReportStore.Path, ex);
                }
            }

            return prices;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            PriceMarksUnavailable = true;
            nextCommodityReportRefresh = timeProvider.GetUtcNow() + CommodityReportRetryDelay;
            if (ex is InvalidDataException)
            {
                diagnostics.Record(ArdentProvider, ArdentRoutes.Commodities, ex);
            }

            return commodityReport ?? EmptyCommodityReport;
        }
        finally
        {
            commodityReportGate.Release();
        }
    }

    private async Task<(int Count, DateTimeOffset? UpdatedAt, bool AnyFailures)> OverlayCurrentSurfacePricesAsync(
        Dictionary<string, MiningCommodityPriceSummary> prices,
        CancellationToken cancellationToken
    )
    {
        int count = 0;
        bool anyFailures = false;
        DateTimeOffset? latest = null;
        foreach (string material in SurfaceMiningCommodityCatalog.All.Select(commodity => commodity.Name))
        {
            string route = ArdentRoutes.CurrentCommodityImporters(MiningCommodityName.Key(material));
            try
            {
                using JsonDocument document = await ardent
                    .GetAsync(route, MaximumResponseBytes, MarketResponse, cancellationToken)
                    .ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("Commodity importers were not a list.");
                }

                JsonElement[] quotes = document
                    .RootElement.EnumerateArray()
                    .Where(item => MiningJson.Number(item, "sellPrice") > 0 && MiningJson.Number(item, "meanPrice") > 0)
                    .ToArray();
                if (quotes.Length == 0)
                {
                    continue;
                }

                long average = (long)MiningJson.Number(quotes[0], "meanPrice");
                long maximum = (long)quotes.Max(item => MiningJson.Number(item, "sellPrice"));
                prices[material] = new MiningCommodityPriceSummary(material, average, maximum);
                count++;
                latest = LatestQuoteUpdate(quotes, latest);
            }
            catch (Exception ex)
                when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
            {
                anyFailures = true;
                diagnostics.Record(ArdentProvider, route, ex);
            }
        }

        return (count, latest, anyFailures);
    }

    private static DateTimeOffset? LatestQuoteUpdate(JsonElement[] quotes, DateTimeOffset? latest)
    {
        foreach (JsonElement quote in quotes)
        {
            if (
                DateTimeOffset.TryParse(
                    MiningJson.Text(quote, "updatedAt"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset updatedAt
                ) && (latest is null || updatedAt > latest)
            )
            {
                latest = updatedAt;
            }
        }

        return latest;
    }

    private async Task<bool> ShouldUseCachedCommodityReportAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (commodityReport is null || CommodityReportFetchedAt is not { } fetchedAt)
        {
            return false;
        }

        DateTimeOffset? latest = await LastCommodityUpdatedAtAsync(cancellationToken).ConfigureAwait(false);
        if (latest is { } updated && now - updated < TimeSpan.FromMinutes(5))
        {
            nextCommodityReportRefresh = updated + TimeSpan.FromMinutes(5);
            return true;
        }

        DateTimeOffset dailyRefresh = NextCommodityRefreshBoundary(fetchedAt, DateTimeOffset.MaxValue);

        if (now >= dailyRefresh || latest != lastCommodityUpdatedAt && latest is not null)
        {
            return false;
        }

        DateTimeOffset hourlyRefresh = now + CommodityReportCheckInterval;
        nextCommodityReportRefresh = hourlyRefresh < dailyRefresh ? hourlyRefresh : dailyRefresh;
        return true;
    }

    private static DateTimeOffset NextCommodityRefreshBoundary(DateTimeOffset fetchedAt, DateTimeOffset hourlyRefresh)
    {
        DateTimeOffset dailyRefresh = new(fetchedAt.UtcDateTime.Date.AddHours(6), TimeSpan.Zero);
        if (fetchedAt >= dailyRefresh)
        {
            dailyRefresh = dailyRefresh.AddDays(1);
        }

        return hourlyRefresh < dailyRefresh ? hourlyRefresh : dailyRefresh;
    }

    private async Task<DateTimeOffset?> LastCommodityUpdatedAtAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lastCommodityName))
        {
            return null;
        }

        try
        {
            using JsonDocument summary = await ardent
                .GetAsync(
                    ArdentRoutes.CommoditySummary(lastCommodityName),
                    64 * 1024,
                    MarketResponse,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (summary.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string timestamp = MiningJson.Text(summary.RootElement, "timestamp");
            return DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset updated
            )
                ? updated
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, long>> AverageSellPricesAsync(
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyDictionary<string, MiningCommodityPriceSummary> report = await CommodityPriceReportAsync(
                cancellationToken
            )
            .ConfigureAwait(false);
        return averageSellPrices
            ?? report
                .Values.Where(item => item.AverageSellPrice > 0)
                .ToDictionary(item => item.Commodity, item => item.AverageSellPrice, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<(IReadOnlyList<MiningMarketResult> Markets, string Source)> FindMarketsPreferringArdentAsync(
        MiningMarketQuery query,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<MiningMarketResult> markets;
        try
        {
            markets = await FindMarketsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            markets = [];
        }

        if (markets.Count > 0)
        {
            return (markets, ArdentProvider);
        }

        return (await FindSpanshMarketsAsync(query, cancellationToken).ConfigureAwait(false), "Spansh fallback");
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
        long demand = (long)MiningJson.Number(item, DemandField);
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
        )
        {
            Commodity = MiningCommodityName.Canonical(
                MiningJson.Text(item, "commodityName") is { Length: > 0 } name ? name : query.Commodity
            ),
        };
    }

    public async Task<IReadOnlyList<MiningMarketResult>> FindSpanshSystemCommoditiesAsync(
        string reference,
        string system,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = new() { [SystemNameField] = new { value = new[] { system } } };
        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Stations,
            reference,
            filters,
            0,
            cancellationToken: cancellationToken
        );
        return Results(response)
            .SelectMany(station =>
                MiningJson
                    .Array(station, "market")
                    .Select(item =>
                    {
                        string commodity = MiningJson.Text(item, CommodityField);
                        long price = (long)MiningJson.Number(item, "sell_price");
                        long demand = (long)MiningJson.Number(item, DemandField);
                        if (commodity.Length == 0 || price <= 0 || demand <= 0)
                        {
                            return null;
                        }

                        int? padSize = MaxPad(station);
                        return new MiningMarketResult(
                            MiningJson.Text(station, SystemNameField),
                            MiningJson.Text(station, "name"),
                            MiningJson.Text(station, "type"),
                            Number(station, DistanceField),
                            Number(station, ArrivalField),
                            price,
                            demand,
                            0,
                            RecentObservation(MiningJson.Text(station, MarketUpdatedSort), TimeSpan.FromDays(3650)),
                            (long)MiningJson.Number(station, "market_id"),
                            padSize >= 3
                        )
                        {
                            Commodity = MiningCommodityName.Canonical(commodity),
                            QuotedPad = padSize switch
                            {
                                >= 3 => "Large",
                                2 => "Medium",
                                1 => "Small",
                                _ => "Pad unknown",
                            },
                        };
                    })
            )
            .OfType<MiningMarketResult>()
            .ToArray();
    }

    public async Task<IReadOnlyList<MiningSellQuote>> FindSellQuotesAsync(
        string reference,
        double radius,
        IReadOnlyList<string> systems,
        IReadOnlyList<string> commodities,
        TimeSpan? maximumAge = null,
        CancellationToken cancellationToken = default
    )
    {
        if (commodities.Count == 0)
        {
            return [];
        }

        Dictionary<string, object> filters = DistanceFilter(radius);
        if (systems.Count > 0)
        {
            filters[SystemNameField] = new { value = systems.ToArray() };
        }
        filters["buying_commodities"] = new { value = commodities.ToArray() };
        var quotes = new List<MiningSellQuote>();
        int page = 0;
        bool hasMore = true;
        while (hasMore)
        {
            using JsonDocument response = await spansh.SearchAsync(
                SpanshRoutes.Stations,
                reference,
                filters,
                page,
                sort: MarketUpdatedSort,
                cancellationToken: cancellationToken
            );
            JsonElement[] stations = Results(response).ToArray();
            quotes.AddRange(stations.SelectMany(station => ReadSellQuotes(station, commodities, maximumAge)));
            hasMore = stations.Length >= SpanshRoutes.PageSize(SpanshRoutes.Stations);
            page++;
        }

        return quotes;
    }

    private static IEnumerable<MiningSellQuote> ReadSellQuotes(
        JsonElement station,
        IReadOnlyList<string> commodities,
        TimeSpan? maximumAge
    )
    {
        DateTimeOffset? updated = maximumAge is { } age
            ? RecentObservation(MiningJson.Text(station, MarketUpdatedSort), age)
            : null;
        if (maximumAge is not null && updated is null)
        {
            yield break;
        }

        string pad = MaxPad(station) switch
        {
            >= 3 => "Large",
            2 => "Medium",
            1 => "Small",
            _ => "Pad unknown",
        };
        double? arrival = Number(station, ArrivalField);
        foreach (JsonElement item in MiningJson.Array(station, "market"))
        {
            string commodity = MiningJson.Text(item, CommodityField);
            if (!commodities.Any(wanted => MiningCommodityName.Same(wanted, commodity)))
            {
                continue;
            }

            long price = (long)MiningJson.Number(item, "sell_price");
            long demand = (long)MiningJson.Number(item, DemandField);
            if (price <= 0)
            {
                continue;
            }

            yield return new(
                MiningJson.Text(station, SystemNameField),
                MiningJson.Text(station, "name"),
                MiningJson.Text(station, "type"),
                pad,
                arrival,
                MiningCommodityName.Canonical(commodity),
                price,
                demand,
                updated
            );
        }
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
        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Stations,
            query.ReferenceSystem,
            filters,
            query.Page,
            sort: MarketUpdatedSort,
            cancellationToken: cancellationToken
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

        DateTimeOffset? updated = RecentObservation(MiningJson.Text(station, MarketUpdatedSort), MarketAge(query));
        if (updated is null)
        {
            yield break;
        }

        foreach (
            JsonElement item in MiningJson
                .Array(station, "market")
                .Where(m => MiningCommodityName.Same(MiningJson.Text(m, CommodityField), query.Commodity))
        )
        {
            long price = (long)MiningJson.Number(item, query.Buying ? "buy_price" : "sell_price");
            long supply = (long)(Number(item, "supply") ?? Number(item, "stock") ?? 0);
            long demand = (long)MiningJson.Number(item, DemandField);
            if (!HasTradeVolume(price, demand, supply, query))
            {
                continue;
            }

            yield return new(
                MiningJson.Text(station, SystemNameField),
                MiningJson.Text(station, "name"),
                type,
                Number(station, DistanceField),
                Number(station, ArrivalField),
                price,
                demand,
                supply,
                updated,
                (long)MiningJson.Number(station, "market_id"),
                largePad
            )
            {
                Commodity = MiningCommodityName.Canonical(MiningJson.Text(item, CommodityField)),
            };
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
    ) => (await FindSystemPageAsync(query, cancellationToken).ConfigureAwait(false)).Systems;

    public async Task<MiningSystemPage> FindSystemPageAsync(
        MiningSystemQuery query,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, object> filters = query.GalaxyWide ? [] : DistanceFilter(query.Radius);
        PowerplaySpanshQuery spanshQuery = PowerplayPlan.SpanshFilter(query.Objective, query.PowerState);
        foreach (
            (string? name, string? value, bool array) in new[]
            {
                ("security", query.Security, false),
                ("allegiance", query.Allegiance, false),
                ("government", query.Government, false),
                ("primary_economy", query.Economy, false),
                ("controlling_minor_faction_state", query.State, true),
                ("controlling_power", PowerplayPlan.SpanshPowerName(query.Power), true),
                ("power_state", spanshQuery.IndexedState, true),
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

        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Systems,
            query.ReferenceSystem,
            filters,
            query.Page,
            cancellationToken: cancellationToken
        );
        JsonElement[] results = Results(response).ToArray();
        IEnumerable<MiningSystemResult> systems = results.Select(ReadSystem);
        if (spanshQuery.RequiredState.Length > 0)
        {
            systems = systems.Where(system =>
                system.PowerState.Equals(spanshQuery.RequiredState, StringComparison.OrdinalIgnoreCase)
            );
        }

        return new MiningSystemPage(systems.ToArray(), results.Length >= SpanshRoutes.PageSize(SpanshRoutes.Systems));
    }

    public async Task<IReadOnlyList<MiningSystemResult>> FindSystemsByNameAsync(
        string reference,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken = default
    )
    {
        var found = new List<MiningSystemResult>();
        foreach (
            string[] chunk in names.Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50)
        )
        {
            Dictionary<string, object> filters = new() { ["name"] = new { value = chunk } };
            using JsonDocument response = await spansh
                .SearchAsync(SpanshRoutes.Systems, reference, filters, 0, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            found.AddRange(Results(response).Select(ReadSystem));
        }

        return found;
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
        )
        {
            NearbyPowers = MiningJson
                .Array(system, "power")
                .Select(power =>
                    power.ValueKind == JsonValueKind.String ? power.GetString() ?? "" : MiningJson.Text(power, "name")
                )
                .Where(power => power.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Conflict = progress,
        };
    }

    public async Task<(IReadOnlyList<MiningMarketResult> Traders, string Source)> FindTradersPreferringArdentAsync(
        string reference,
        string trader,
        double radius = 100,
        int page = 0,
        int minimumPadSize = 1,
        CancellationToken cancellationToken = default
    )
    {
        _ = DistanceFilter(radius);
        try
        {
            return (await FindArdentTradersAsync(reference, radius, minimumPadSize, cancellationToken), ArdentProvider);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or InvalidDataException)
        {
            return (await FindTradersAsync(reference, trader, radius, page, cancellationToken), "Spansh fallback");
        }
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindArdentTradersAsync(
        string reference,
        double radius,
        int minimumPadSize,
        CancellationToken cancellationToken
    )
    {
        int pad = Math.Clamp(minimumPadSize, 1, 3);
        using JsonDocument document = await ardent
            .GetAsync(
                ArdentRoutes.NearestMaterialTrader(reference, pad),
                MaximumResponseBytes,
                MarketResponse,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            var failure = new JsonException("Unexpected material trader response.");
            diagnostics.Record(ArdentProvider, "material-trader", failure);
            throw failure;
        }

        return document
            .RootElement.EnumerateArray()
            .Select(ReadArdentTrader)
            .OfType<MiningMarketResult>()
            .Where(trader => trader.Distance is null || trader.Distance <= radius)
            .ToArray();
    }

    private static MiningMarketResult? ReadArdentTrader(JsonElement item)
    {
        string name = MiningJson.Text(item, "stationName");
        string system = MiningJson.Text(item, "systemName");
        if (name.Length == 0 || system.Length == 0)
        {
            return null;
        }

        int? pad = Number(item, "maxLandingPadSize") is { } size ? (int)size : null;
        return new MiningMarketResult(
            system,
            name,
            MiningJson.Text(item, "stationType"),
            Number(item, DistanceField),
            Number(item, "distanceToArrival"),
            0,
            0,
            0,
            DateTimeOffset.TryParse(
                MiningJson.Text(item, "updatedAt"),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset updated
            )
                ? updated
                : null,
            (long)MiningJson.Number(item, "marketId"),
            pad >= 3
        )
        {
            QuotedPad = pad switch
            {
                >= 3 => "Large",
                2 => "Medium",
                1 => "Small",
                _ => null,
            },
        };
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
        using JsonDocument response = await spansh.SearchAsync(
            SpanshRoutes.Stations,
            reference,
            filters,
            page,
            cancellationToken: cancellationToken
        );
        return Results(response)
            .Select(s => new MiningMarketResult(
                MiningJson.Text(s, SystemNameField),
                MiningJson.Text(s, "name"),
                MiningJson.Text(s, "type"),
                Number(s, DistanceField),
                Number(s, ArrivalField),
                0,
                0,
                0,
                null,
                (long)MiningJson.Number(s, "market_id"),
                HasLargePad(s)
            ))
            .ToArray();
    }

    private static Dictionary<string, object> DistanceFilter(double radius)
    {
        if (!double.IsFinite(radius) || radius is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Use a search radius from 1 to 500 ly.");
        }

        return new() { [DistanceField] = new { min = 0, max = radius } };
    }

    private static Dictionary<string, object> InitialFilters(double radius, bool galaxyWide) =>
        galaxyWide ? [] : DistanceFilter(radius);

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
