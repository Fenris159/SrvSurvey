using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Search;

public sealed record SurfaceSellSystemDetails(
    string PowerState,
    string FactionState,
    IReadOnlyList<string> Powers,
    double? DistanceLy = null
)
{
    public IReadOnlyList<PowerplayProgress> Conflict { get; init; } = [];
    public string ControllingPower { get; init; } = "";
    public double? ControlProgress { get; init; }
}

/// <summary>Which sell systems a surface search may use, where their miners may be, and where more markets come from.</summary>
public sealed record SurfaceSellMarketRules
{
    public static SurfaceSellMarketRules Open { get; } = new();

    public Func<string, IReadOnlySet<string>>? MiningSystemsForSell { get; init; }

    public Func<string, SurfaceSellSystemDetails?>? SellSystemDetailsFor { get; init; }

    public Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<IReadOnlySet<string>>
    >? EligibleSellSystemsAsync { get; init; }

    public Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<IReadOnlyList<MiningMarketResult>>
    >? AdditionalMarketQuotesAsync { get; init; }
}

public sealed record SurfaceSellSearchRequest(string Reference, IReadOnlyList<string> SelectedMaterials)
{
    public bool ForceIncludeReference { get; init; }
    public double Radius { get; init; } = 100;
    public double MineSellRadius { get; init; } = 50;
    public int ResultLimit { get; init; } = 1;
    public string PadSize { get; init; } = "Any";
    public long MinimumDemand { get; init; }
    public long MaximumDemand { get; init; } = 90_000;
    public TimeSpan? MaximumAge { get; init; } = TimeSpan.FromDays(2);
    public IReadOnlyList<string> BodyControllingPowers { get; init; } = [];
    public double? BodySearchRadius { get; init; }
    public bool GroupStationsBySystem { get; init; }
    public bool MarketGalaxyWide { get; init; }
    public bool ExcludeCarrierMarkets { get; init; }
    public bool UseAdditionalMarketsOnly { get; init; }
    public SurfaceSellMarketRules Rules { get; init; } = SurfaceSellMarketRules.Open;
}

public sealed record SurfaceBodyMatch(MiningPlanetaryBody Body, IReadOnlyList<string> Codes);

/// <summary>A station quoting surface materials, with the codes that nearby bodies can supply.</summary>
public sealed record SurfaceSellStation(
    MiningMarketResult Market,
    IReadOnlyList<MiningMarketResult> Quotes,
    IReadOnlySet<string> AvailableCodes,
    bool BodySearchComplete,
    IReadOnlyDictionary<string, long> AverageSellPrices
)
{
    public bool CanToggle { get; init; }

    public bool IsUnavailable(string code) => BodySearchComplete && !AvailableCodes.Contains(code);
}

/// <summary>One ranked sell system and the surface bodies that can supply its stations.</summary>
public sealed record SurfaceSellMatch(
    MiningMarketResult Sell,
    IReadOnlyList<SurfaceSellStation> Stations,
    IReadOnlyList<SurfaceBodyMatch> Bodies,
    IReadOnlySet<string> StationCodes,
    long BestViablePrice,
    IReadOnlyList<long> StationScores,
    SurfaceSellSystemDetails? Details
)
{
    public string System => Sell.System;
    public double? DistanceLy => Details?.DistanceLy ?? Sell.Distance;
    public double ReferenceDistanceLy => DistanceLy ?? double.MaxValue;
    public PowerplayStationRanking StationRanking { get; } = PowerplayStationRanking.FromScores(StationScores);
}

public enum SurfaceSellSearchStage
{
    Ranking,
    CheckingEligibility,
    CheckingSellSystem,
    Ranked,
}

public sealed record SurfaceSellSearchProgress(SurfaceSellSearchStage Stage)
{
    public bool ProximityFirst { get; init; }
    public int Index { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<SurfaceSellMatch> Matches { get; init; } = [];
}

public enum SurfaceSellSearchResultKind
{
    NoReference,
    NoMaterial,
    NoSellStations,
    NoMatchingBodies,
    Found,
}

public sealed record SurfaceSellSearchResult(SurfaceSellSearchResultKind Kind)
{
    public IReadOnlyList<SurfaceSellMatch> Matches { get; init; } = [];
    public IReadOnlyList<string> Materials { get; init; } = [];
    public bool CatalogOrder { get; init; }
    public bool UsedFallback { get; init; }
    public bool ProximityFirst { get; init; }
}

/// <summary>
/// Finds the stations that pay best for surface materials and the landable bodies that can supply them
/// within the mine–sell distance.
/// </summary>
public sealed class SurfaceSellSearch(IMiningSearchProvider provider)
{
    private sealed record SurfaceMaterialRule(string Code, PlanetaryBodyCriteria Criteria);

    private sealed record SurfaceBodySearch(IReadOnlyList<SurfaceBodyMatch> Matches, bool Complete);

    private sealed record SurfaceRankedSearch(IReadOnlyList<SurfaceSellMatch> Rows);

    private sealed record SurfaceStationCandidate(MiningMarketResult[] Quotes, MiningMarketResult Anchor, int Priority);

    private sealed class SurfaceRankingState(bool catalogOrder)
    {
        public bool CatalogOrder { get; } = catalogOrder;
        public Dictionary<string, SurfaceBodySearch> BodyCache { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> SellSystemEligibility { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ProcessedCandidates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedStations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedSystems { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SurfaceSellMatch> Ranked { get; set; } = [];
    }

    private sealed record Search(
        SurfaceSellSearchRequest Request,
        IProgress<SurfaceSellSearchProgress> Progress,
        CancellationToken Token
    )
    {
        public string Reference => Request.Reference;
        public SurfaceSellMarketRules Rules => Request.Rules;
    }

    public const int MaximumBodyPageRequests = 40;
    private static readonly int[] NearbyMarketRadiusStages = [50, 100, 200];

    public async Task<SurfaceSellSearchResult> FindAsync(
        SurfaceSellSearchRequest request,
        IProgress<SurfaceSellSearchProgress> progress,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(request.Reference))
        {
            return new SurfaceSellSearchResult(SurfaceSellSearchResultKind.NoReference);
        }

        var search = new Search(request, progress, cancellationToken);
        bool catalogOrder = MiningMaterialSelection.IsAny(request.SelectedMaterials);
        IReadOnlyDictionary<string, MiningCommodityPriceSummary>? dailyPrices = catalogOrder
            ? await provider.CommodityPriceReportAsync(cancellationToken)
            : null;
        IReadOnlyList<string> materials = SurfaceMiningSearchPlan.MaterialsFor(request.SelectedMaterials, dailyPrices);
        PlanetaryBodyCriteria? criteria = PlanetaryMiningPlan.For(materials);
        if (criteria is null)
        {
            return new SurfaceSellSearchResult(SurfaceSellSearchResultKind.NoMaterial);
        }

        SurfaceMaterialRule[] rules = materials
            .Select(material => new SurfaceMaterialRule(
                MiningCommodityCode.Abbreviate(material),
                PlanetaryMiningPlan.For([material])!
            ))
            .Where(rule => rule.Criteria is not null)
            .ToArray();

        bool proximityFirst = !catalogOrder && !request.GroupStationsBySystem && !request.UseAdditionalMarketsOnly;
        progress.Report(
            new SurfaceSellSearchProgress(SurfaceSellSearchStage.Ranking) { ProximityFirst = proximityFirst }
        );
        if (proximityFirst)
        {
            return await FindNearbySurfaceSalesAsync(search, materials, criteria, rules);
        }

        return await FindRankedSurfaceSalesAsync(search, materials, criteria, rules, catalogOrder);
    }

    private async Task<SurfaceSellSearchResult> FindRankedSurfaceSalesAsync(
        Search search,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        bool catalogOrder
    )
    {
        (List<MiningMarketResult> quotes, bool usedFallback) = await FindInitialSurfaceQuotesAsync(search, materials);
        usedFallback |= await AddReferenceQuotesIfNeededAsync(search, quotes, materials);

        SurfaceRankedSearch ranked;
        var ranking = new SurfaceRankingState(catalogOrder);
        do
        {
            SurfaceStationCandidate[] candidates = PrioritizeReference(
                search.Request,
                CandidatesFor(quotes, materials, catalogOrder)
            );
            ranked =
                candidates.Length == 0
                    ? new SurfaceRankedSearch([])
                    : await RankStationsAsync(search, candidates, materials, criteria, rules, ranking);
            if (
                ranked.Rows.Count >= search.Request.ResultLimit
                || search.Rules.AdditionalMarketQuotesAsync is not { } additional
            )
            {
                break;
            }

            IReadOnlyList<MiningMarketResult> more = await additional(materials, search.Token);
            if (more.Count == 0)
            {
                break;
            }

            quotes.AddRange(more);
        } while (true);

        return Result(quotes, ranked, materials, catalogOrder, usedFallback, proximityFirst: false);
    }

    private async Task<SurfaceSellSearchResult> FindNearbySurfaceSalesAsync(
        Search search,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules
    )
    {
        var quotes = new List<MiningMarketResult>();
        var ranking = new SurfaceRankingState(catalogOrder: false);
        var ranked = new SurfaceRankedSearch([]);
        bool usedFallback = false;
        foreach (double marketRadius in NearbyMarketRadii(search.Request.Radius))
        {
            (List<MiningMarketResult> found, bool fallback) = await FindInitialSurfaceQuotesAsync(
                search,
                materials,
                marketRadius
            );
            usedFallback |= fallback;
            quotes = found
                .Concat(quotes)
                .DistinctBy(
                    quote => quote.System + "\u001f" + quote.Station + "\u001f" + quote.Commodity,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();
            usedFallback |= await AddReferenceQuotesIfNeededAsync(search, quotes, materials);
            SurfaceStationCandidate[] candidates = PrioritizeReference(
                search.Request,
                CandidatesFor(quotes, materials, catalogOrder: false, proximityFirst: true)
            );
            if (candidates.Length == 0)
            {
                continue;
            }

            ranked = await RankStationsAsync(
                search,
                candidates,
                materials,
                criteria,
                rules,
                ranking,
                proximityFirst: true
            );
            if (ranked.Rows.Count >= search.Request.ResultLimit)
            {
                break;
            }
        }

        return Result(quotes, ranked, materials, catalogOrder: false, usedFallback, proximityFirst: true);
    }

    private static SurfaceSellSearchResult Result(
        List<MiningMarketResult> quotes,
        SurfaceRankedSearch ranked,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        bool usedFallback,
        bool proximityFirst
    )
    {
        SurfaceSellSearchResultKind kind;
        if (quotes.Count == 0)
        {
            kind = SurfaceSellSearchResultKind.NoSellStations;
        }
        else
        {
            kind =
                ranked.Rows.Count == 0
                    ? SurfaceSellSearchResultKind.NoMatchingBodies
                    : SurfaceSellSearchResultKind.Found;
        }

        return new SurfaceSellSearchResult(kind)
        {
            Matches = ranked.Rows,
            Materials = materials,
            CatalogOrder = catalogOrder,
            UsedFallback = usedFallback,
            ProximityFirst = proximityFirst,
        };
    }

    private static IEnumerable<double> NearbyMarketRadii(double radius)
    {
        foreach (int stage in NearbyMarketRadiusStages.Where(stage => stage < radius))
        {
            yield return stage;
        }

        yield return radius;
    }

    private async Task<bool> AddReferenceQuotesIfNeededAsync(
        Search search,
        List<MiningMarketResult> quotes,
        IReadOnlyList<string> materials
    )
    {
        if (
            !search.Request.ForceIncludeReference
            || search.Request.UseAdditionalMarketsOnly
            || quotes.Any(quote => SameSystem(quote.System, search.Reference))
        )
        {
            return false;
        }

        (MiningMarketResult[] referenceQuotes, bool usedFallback) = await FindReferenceQuotesAsync(search, materials);
        quotes.AddRange(referenceQuotes);
        return usedFallback;
    }

    private static SurfaceStationCandidate[] PrioritizeReference(
        SurfaceSellSearchRequest request,
        SurfaceStationCandidate[] candidates
    ) =>
        request.ForceIncludeReference
            ? candidates
                .OrderByDescending(candidate => SameSystem(candidate.Anchor.System, request.Reference))
                .ToArray()
            : candidates;

    private async Task<(List<MiningMarketResult> Quotes, bool UsedFallback)> FindInitialSurfaceQuotesAsync(
        Search search,
        IReadOnlyList<string> materials,
        double? marketRadius = null
    )
    {
        SurfaceSellSearchRequest request = search.Request;
        List<MiningMarketResult> quotes = [];
        bool usedFallback = false;
        foreach (string material in request.UseAdditionalMarketsOnly ? Enumerable.Empty<string>() : materials)
        {
            search.Token.ThrowIfCancellationRequested();
            (IReadOnlyList<MiningMarketResult> found, string source) = await provider.FindMarketsPreferringArdentAsync(
                new MiningMarketQuery(
                    request.Reference.Trim(),
                    material,
                    false,
                    marketRadius ?? request.Radius,
                    GalaxyWide: request.MarketGalaxyWide,
                    MaximumAgeDays: request.MaximumAge is null
                        ? 3650
                        : Math.Clamp((int)Math.Ceiling(request.MaximumAge.Value.TotalDays), 1, 3650),
                    MinimumDemand: request.MinimumDemand,
                    MaximumDemand: request.MaximumDemand,
                    MaximumAge: request.MaximumAge,
                    PadSize: request.PadSize,
                    ExcludeCarriers: request.ExcludeCarrierMarkets
                ),
                search.Token
            );
            quotes.AddRange(
                found.Where(quote => materials.Any(material => MiningCommodityName.Same(material, quote.Commodity)))
            );
            usedFallback |= source != "Ardent";
        }

        return (quotes, usedFallback);
    }

    private async Task<(MiningMarketResult[] Quotes, bool UsedFallback)> FindReferenceQuotesAsync(
        Search search,
        IReadOnlyList<string> materials
    )
    {
        SurfaceSellSearchRequest request = search.Request;
        var query = new MiningMarketQuery(
            request.Reference,
            "Any",
            false,
            request.Radius,
            ExcludeCarriers: request.ExcludeCarrierMarkets,
            SystemOnly: true,
            MinimumDemand: request.MinimumDemand,
            MaximumDemand: request.MaximumDemand,
            MaximumAge: request.MaximumAge ?? TimeSpan.FromDays(3650),
            PadSize: request.PadSize
        );
        (MiningMarketResult[] quotes, bool usedFallback) = await MiningSystemImports.FindAsync(
            provider,
            request.Reference,
            request.Reference,
            query,
            new MiningImportFilter(
                materials,
                request.MinimumDemand,
                request.MaximumDemand,
                request.MaximumAge,
                request.PadSize
            )
            {
                RequireQuotedSale = true,
            },
            search.Token
        );
        return (quotes.Select(quote => quote with { Distance = 0 }).ToArray(), usedFallback);
    }

    private static SurfaceStationCandidate[] CandidatesFor(
        IReadOnlyList<MiningMarketResult> quotes,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        bool proximityFirst = false
    )
    {
        var candidates = new List<SurfaceStationCandidate>();
        foreach (
            MiningMarketResult[] group in quotes
                .Where(quote => quote.Price > 0 && quote.Demand > 0)
                .GroupBy(quote => quote.System + "\u001f" + quote.Station, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(quote => quote.Price).ToArray())
        )
        {
            if (!catalogOrder)
            {
                candidates.Add(new SurfaceStationCandidate(group, group[0], 0));
                continue;
            }

            for (int priority = 0; priority < materials.Count; priority++)
            {
                MiningMarketResult? anchor = group.FirstOrDefault(quote =>
                    MiningCommodityName.Same(quote.Commodity, materials[priority])
                );
                if (anchor is not null)
                {
                    candidates.Add(new SurfaceStationCandidate(group, anchor, priority));
                }
            }
        }

        return proximityFirst
            ? candidates
                .GroupBy(candidate => candidate.Anchor.System, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(candidate => candidate.Anchor.Price).First())
                .OrderByDescending(candidate => candidate.Anchor.Price)
                .ThenBy(candidate => candidate.Anchor.Distance ?? double.MaxValue)
                .ToArray()
            : candidates
                .OrderBy(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Anchor.Price)
                .ThenBy(candidate => candidate.Anchor.Distance ?? double.MaxValue)
                .ToArray();
    }

    private async Task<SurfaceRankedSearch> RankStationsAsync(
        Search search,
        SurfaceStationCandidate[] candidates,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        SurfaceRankingState ranking,
        bool proximityFirst = false
    )
    {
        SurfaceSellSearchRequest request = search.Request;
        List<SurfaceSellMatch> ranked = ranking.Ranked;
        for (int index = 0; index < candidates.Length; index++)
        {
            search.Token.ThrowIfCancellationRequested();
            if (proximityFirst && ranking.SelectedSystems.Contains(candidates[index].Anchor.System))
            {
                continue;
            }

            search.Progress.Report(
                new SurfaceSellSearchProgress(SurfaceSellSearchStage.CheckingSellSystem)
                {
                    Index = index,
                    Count = candidates.Length,
                }
            );
            SurfaceSellMatch? row = await TryDescribeRankedCandidateAsync(
                search,
                candidates,
                index,
                materials,
                criteria,
                rules,
                ranking
            );
            if (row is null)
            {
                continue;
            }

            ranked.Add(row);
            ranked = KeepBestRows(
                ranked,
                ranking.CatalogOrder,
                request.GroupStationsBySystem,
                request.ResultLimit,
                request.ForceIncludeReference ? request.Reference : "",
                proximityFirst
            );
            ranking.Ranked = ranked;

            search.Progress.Report(
                new SurfaceSellSearchProgress(SurfaceSellSearchStage.Ranked) { Matches = ranked.ToArray() }
            );
            if (
                ranked.Count >= request.ResultLimit
                && (
                    proximityFirst
                    || CanStopRanking(request, ranking.CatalogOrder, materials.Count, ranked, candidates, index)
                )
            )
            {
                break;
            }
        }

        return new SurfaceRankedSearch(ranked);
    }

    private async Task<SurfaceSellMatch?> TryDescribeRankedCandidateAsync(
        Search search,
        SurfaceStationCandidate[] candidates,
        int index,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        SurfaceRankingState ranking
    )
    {
        SurfaceStationCandidate candidate = candidates[index];
        string stationKey = candidate.Anchor.System + "\u001f" + candidate.Anchor.Station;
        string candidateKey = stationKey + "\u001f" + MiningCommodityName.Key(candidate.Anchor.Commodity);
        if (!ranking.ProcessedCandidates.Add(candidateKey))
        {
            return null;
        }

        if (!await IsEligibleSellSystemAsync(search, candidates, index, ranking.SellSystemEligibility))
        {
            return null;
        }

        bool groupStations = search.Request.GroupStationsBySystem;
        if (
            ranking.SelectedStations.Contains(stationKey)
            || (groupStations && ranking.SelectedSystems.Contains(candidate.Anchor.System))
        )
        {
            return null;
        }

        IReadOnlySet<string>? miningSystems = search.Rules.MiningSystemsForSell?.Invoke(candidate.Anchor.System);
        if (miningSystems is { Count: 0 })
        {
            return null;
        }

        SurfaceBodySearch bodySearch = await CachedBodySearchAsync(
            search,
            ranking.BodyCache,
            candidate.Anchor.System,
            criteria,
            rules,
            miningSystems
        );
        if (bodySearch.Matches.Count == 0)
        {
            return null;
        }

        SurfaceSellMatch? row = groupStations
            ? await DescribeSellSystemAsync(search, candidate, candidates, bodySearch, materials, ranking.CatalogOrder)
            : await DescribeCandidateAsync(search, candidate, bodySearch, materials, ranking.CatalogOrder);
        if (row is not null)
        {
            ranking.SelectedStations.Add(stationKey);
            ranking.SelectedSystems.Add(candidate.Anchor.System);
        }

        return row;
    }

    private async Task<SurfaceSellMatch?> DescribeSellSystemAsync(
        Search search,
        SurfaceStationCandidate first,
        IReadOnlyList<SurfaceStationCandidate> candidates,
        SurfaceBodySearch bodySearch,
        IReadOnlyList<string> materials,
        bool catalogOrder
    )
    {
        IReadOnlyList<MiningMarketResult> imports = [];
        if (materials.Count > 1)
        {
            try
            {
                imports = await FindStationImportsAsync(search, first.Anchor.System);
            }
            catch (Exception ex) when (MiningProviderFailure.Is(ex))
            {
                // Nearby Ardent quotes remain usable when station imports are unavailable.
            }
        }

        IReadOnlyDictionary<string, long> averages = await provider.AverageSellPricesAsync(search.Token);
        var viable = new List<(SurfaceSellStation Station, long Score, MiningMarketResult[] Quotes)>();
        foreach (
            SurfaceStationCandidate candidate in candidates
                .Where(item => item.Anchor.System.Equals(first.Anchor.System, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(item => item.Anchor.Station, StringComparer.OrdinalIgnoreCase)
        )
        {
            MiningMarketResult[] stationQuotes = await StationQuotesForAsync(
                search,
                candidate.Quotes,
                materials,
                catalogOrder ? candidate.Anchor.Commodity : null,
                imports
            );
            SurfaceBodyMatch[] matching = RelevantBodies(bodySearch.Matches, stationQuotes);
            if (matching.Length == 0)
            {
                continue;
            }

            HashSet<string> available = AvailableCodes(matching);
            long score = stationQuotes
                .Where(quote => available.Contains(MiningCommodityCode.Abbreviate(quote.Commodity)))
                .Max(quote => quote.Price);
            viable.Add(
                (
                    new SurfaceSellStation(
                        candidate.Quotes[0],
                        stationQuotes,
                        available,
                        bodySearch.Complete,
                        averages
                    ),
                    score,
                    stationQuotes
                )
            );
        }

        if (viable.Count == 0)
        {
            return null;
        }

        (SurfaceSellStation Station, long Score, MiningMarketResult[] Quotes)[] rankedStations = viable
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Station.Market.Station)
            .ToArray();
        MiningMarketResult[] bestQuotes = rankedStations[0].Quotes;
        SurfaceBodyMatch[] bodies = RelevantBodies(bodySearch.Matches, bestQuotes);
        if (catalogOrder && !IsViableCandidate(catalogOrder, first, bodies))
        {
            // A later material in the daily catalog order may make this system viable.
            return null;
        }

        SurfaceSellStation[] stations = rankedStations
            .Select((item, index) => item.Station with { CanToggle = index == 0 && rankedStations.Length > 1 })
            .ToArray();
        return Describe(
            search,
            bodies,
            first.Anchor,
            stations,
            bestQuotes,
            rankedStations.Select(item => item.Score).ToArray()
        );
    }

    private static async Task<bool> IsEligibleSellSystemAsync(
        Search search,
        SurfaceStationCandidate[] candidates,
        int index,
        Dictionary<string, bool> eligibility
    )
    {
        if (search.Rules.EligibleSellSystemsAsync is not { } eligibleSellSystems)
        {
            return true;
        }

        string system = candidates[index].Anchor.System;
        if (!eligibility.TryGetValue(system, out bool eligible))
        {
            await CheckSellSystemsAsync(search, eligibleSellSystems, candidates, index, eligibility);
            eligible = eligibility[system];
        }

        return eligible;
    }

    private static async Task CheckSellSystemsAsync(
        Search search,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlySet<string>>> eligibleSellSystems,
        SurfaceStationCandidate[] candidates,
        int start,
        Dictionary<string, bool> eligibility
    )
    {
        SurfaceStationCandidate[] batch = candidates
            .Skip(start)
            .Where(candidate => !eligibility.ContainsKey(candidate.Anchor.System))
            .DistinctBy(candidate => candidate.Anchor.System, StringComparer.OrdinalIgnoreCase)
            .Take(search.Request.MarketGalaxyWide ? Math.Min(search.Request.ResultLimit, 5) : 50)
            .ToArray();
        search.Progress.Report(new SurfaceSellSearchProgress(SurfaceSellSearchStage.CheckingEligibility));
        IReadOnlySet<string> names = await eligibleSellSystems(
            batch.Select(candidate => candidate.Anchor.System).ToArray(),
            search.Token
        );
        foreach (string system in batch.Select(candidate => candidate.Anchor.System))
        {
            eligibility[system] = names.Contains(system);
        }
    }

    private async Task<SurfaceSellMatch?> DescribeCandidateAsync(
        Search search,
        SurfaceStationCandidate candidate,
        SurfaceBodySearch bodySearch,
        IReadOnlyList<string> materials,
        bool catalogOrder
    )
    {
        MiningMarketResult[] stationQuotes = await StationQuotesForAsync(
            search,
            candidate.Quotes,
            materials,
            catalogOrder ? candidate.Anchor.Commodity : null
        );
        if (stationQuotes.Length == 0)
        {
            return null;
        }

        SurfaceBodyMatch[] relevant = RelevantBodies(bodySearch.Matches, stationQuotes);
        if (relevant.Length == 0 || !IsViableCandidate(catalogOrder, candidate, relevant))
        {
            return null;
        }

        IReadOnlyDictionary<string, long> averages = await provider.AverageSellPricesAsync(search.Token);
        return Describe(
            search,
            relevant,
            candidate.Quotes[0],
            [
                new SurfaceSellStation(
                    candidate.Quotes[0],
                    stationQuotes,
                    AvailableCodes(relevant),
                    bodySearch.Complete,
                    averages
                ),
            ],
            stationQuotes
        );
    }

    private async Task<SurfaceBodySearch> CachedBodySearchAsync(
        Search search,
        Dictionary<string, SurfaceBodySearch> cache,
        string system,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlySet<string>? miningSystems
    )
    {
        if (!cache.TryGetValue(system, out SurfaceBodySearch? result))
        {
            result = await FindBodyMatchesAsync(search, system, criteria, rules, miningSystems);
            cache.Add(system, result);
        }

        return result;
    }

    private static List<SurfaceSellMatch> KeepBestRows(
        List<SurfaceSellMatch> ranked,
        bool catalogOrder,
        bool groupStations,
        int resultLimit,
        string pinnedSystem,
        bool proximityFirst = false
    )
    {
        IEnumerable<SurfaceSellMatch> ordered;
        if (catalogOrder || proximityFirst)
        {
            ordered = ranked;
        }
        else if (groupStations)
        {
            ordered = ranked.OrderBy(
                item => item,
                Comparer<SurfaceSellMatch>.Create(
                    (left, right) =>
                        PowerplayStationRanking.CompareDescending(left.StationRanking, right.StationRanking)
                )
            );
        }
        else
        {
            ordered = ranked.OrderByDescending(item => item.BestViablePrice).ThenBy(item => item.ReferenceDistanceLy);
        }

        SurfaceSellMatch[] sorted = ordered.ToArray();
        SurfaceSellMatch[] best = sorted.Take(resultLimit).ToArray();
        SurfaceSellMatch? pinned = sorted.FirstOrDefault(row =>
            pinnedSystem.Length > 0 && SameSystem(row.System, pinnedSystem)
        );
        if (pinned is null || best.Contains(pinned, ReferenceEqualityComparer.Instance))
        {
            return best.ToList();
        }

        return sorted
            .Where(row =>
                best.Take(resultLimit - 1).Contains(row, ReferenceEqualityComparer.Instance)
                || ReferenceEquals(row, pinned)
            )
            .ToList();
    }

    private static bool IsViableCandidate(
        bool catalogOrder,
        SurfaceStationCandidate candidate,
        IReadOnlyList<SurfaceBodyMatch> relevant
    )
    {
        if (!catalogOrder)
        {
            return true;
        }

        string code = MiningCommodityCode.Abbreviate(candidate.Anchor.Commodity);
        return relevant.Any(body => body.Codes.Contains(code, StringComparer.OrdinalIgnoreCase));
    }

    private static bool CanStopRanking(
        SurfaceSellSearchRequest request,
        bool catalogOrder,
        int materialCount,
        List<SurfaceSellMatch> ranked,
        IReadOnlyList<SurfaceStationCandidate> candidates,
        int index
    )
    {
        if (ranked.Count < request.ResultLimit || catalogOrder)
        {
            return ranked.Count == request.ResultLimit && catalogOrder;
        }

        string pinnedSystem = request.ForceIncludeReference ? request.Reference : "";
        bool hasPinned = pinnedSystem.Length > 0 && ranked.Any(row => SameSystem(row.System, pinnedSystem));
        if (hasPinned && request.ResultLimit == 1)
        {
            return true;
        }

        SurfaceSellMatch weakest = hasPinned
            ? ranked.LastOrDefault(row => !SameSystem(row.System, pinnedSystem)) ?? ranked[^1]
            : ranked[^1];

        if (!request.GroupStationsBySystem)
        {
            return index + 1 < candidates.Count && candidates[index + 1].Anchor.Price < weakest.BestViablePrice;
        }

        return materialCount == 1
            && index + 1 < candidates.Count
            && candidates[index + 1].Anchor.Price < weakest.StationRanking.Median * 0.92;
    }

    private async Task<MiningMarketResult[]> StationQuotesForAsync(
        Search search,
        MiningMarketResult[] candidate,
        IReadOnlyList<string> materials,
        string? anchorCommodity,
        IReadOnlyList<MiningMarketResult>? cachedImports = null
    )
    {
        if (materials.Count == 1)
        {
            return candidate.OrderByDescending(quote => quote.Price).Take(1).ToArray();
        }

        SurfaceSellSearchRequest request = search.Request;
        MiningMarketResult best = candidate[0];
        List<MiningMarketResult> stationQuotes = [.. candidate];
        try
        {
            IReadOnlyList<MiningMarketResult> imports =
                cachedImports ?? await FindStationImportsAsync(search, best.System);
            stationQuotes.AddRange(
                imports.Where(market =>
                    market.Station.Equals(best.Station, StringComparison.OrdinalIgnoreCase)
                    && market.Price > 0
                    && market.Demand > 0
                    && market.Demand >= request.MinimumDemand
                    && (request.MaximumDemand == 0 || market.Demand <= request.MaximumDemand)
                    && materials.Any(material => MiningCommodityName.Same(material, market.Commodity))
                )
            );
        }
        catch (Exception ex) when (MiningProviderFailure.Is(ex))
        {
            // Nearby quotes can still be used when station imports are unavailable.
        }

        MiningMarketResult[] sorted = stationQuotes
            .GroupBy(market => MiningCommodityName.Key(market.Commodity), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(market => market.Price).First())
            .OrderByDescending(market => market.Price)
            .ToArray();
        MiningMarketResult[] top = sorted.Take(7).ToArray();
        if (
            anchorCommodity is not null
            && !top.Any(quote => MiningCommodityName.Same(quote.Commodity, anchorCommodity))
        )
        {
            MiningMarketResult anchor = sorted.First(quote =>
                MiningCommodityName.Same(quote.Commodity, anchorCommodity)
            );
            top[^1] = anchor;
            top = top.OrderByDescending(quote => quote.Price).ToArray();
        }

        return top;
    }

    private Task<IReadOnlyList<MiningMarketResult>> FindStationImportsAsync(Search search, string system) =>
        provider.FindSystemImportsAsync(
            system,
            new MiningMarketQuery(
                system,
                "Any",
                false,
                ExcludeCarriers: search.Request.ExcludeCarrierMarkets,
                SystemOnly: true,
                MaximumAge: search.Request.MaximumAge ?? TimeSpan.FromDays(3650)
            ),
            search.Token
        );

    private async Task<SurfaceBodySearch> FindBodyMatchesAsync(
        Search search,
        string system,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlySet<string>? miningSystems
    )
    {
        var matches = new List<SurfaceBodyMatch>();
        double bodyRadius = search.Request.BodySearchRadius ?? search.Request.MineSellRadius;
        int page = 0;
        double minimumDistance = 0;
        bool hasMore = true;
        bool complete = false;
        for (int requests = 0; hasMore && requests < MaximumBodyPageRequests; requests++)
        {
            MiningPlanetaryBodyPage found = await provider.FindPlanetaryBodyPageAsync(
                new MiningPlanetaryQuery(
                    system,
                    criteria.BodySubtypes,
                    criteria.LandmarkSubtypes,
                    "",
                    bodyRadius,
                    search.Request.BodyControllingPowers,
                    Page: page,
                    VolcanismTypes: criteria.VolcanismTypes,
                    SystemNames: miningSystems?.ToArray(),
                    MinimumDistance: minimumDistance
                ),
                search.Token
            );
            MiningPlanetaryBody[] bodies = found
                .Bodies.Where(body =>
                    (miningSystems is null || miningSystems.Contains(body.System))
                    && (body.DistanceLy is null || body.DistanceLy <= bodyRadius)
                )
                .ToArray();
            MiningPlanetaryBody[] whiteDwarfCandidates = rules
                .Where(rule => rule.Criteria.RequiresWhiteDwarfHost)
                .SelectMany(rule => bodies.Where(body => PlanetaryMiningPlan.Matches(rule.Criteria, body)))
                .DistinctBy(body => body.System + "\u001f" + body.Body, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            IReadOnlySet<string> whiteDwarfHosted =
                whiteDwarfCandidates.Length > 0
                    ? await provider.FindWhiteDwarfHostedBodiesAsync(system, whiteDwarfCandidates, search.Token)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            matches.AddRange(MatchSurfaceBodies(bodies, rules, whiteDwarfHosted));
            // The table displays at most 30 mining systems for one selected material.
            // Once the nearest 30 systems are known, deeper pages cannot change the result.
            if (HasCompleteSingleMaterialResult(rules, matches))
            {
                complete = true;
                break;
            }

            if (found.ResumeDistance is { } nextMinimum && nextMinimum > minimumDistance)
            {
                minimumDistance = nextMinimum;
                page = 0;
                continue;
            }

            hasMore = found.HasMore;
            page++;
        }

        return new SurfaceBodySearch(
            matches
                .DistinctBy(match => match.Body.System + "\u001f" + match.Body.Body, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            complete || !hasMore
        );
    }

    private static bool HasCompleteSingleMaterialResult(
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlyList<SurfaceBodyMatch> matches
    ) =>
        rules.Count == 1
        && matches.Select(match => match.Body.System).Distinct(StringComparer.OrdinalIgnoreCase).Take(30).Count() == 30;

    private static IEnumerable<SurfaceBodyMatch> MatchSurfaceBodies(
        IReadOnlyList<MiningPlanetaryBody> bodies,
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlySet<string> whiteDwarfHosted
    ) =>
        bodies
            .Select(body => new SurfaceBodyMatch(
                body,
                rules
                    .Where(rule =>
                        PlanetaryMiningPlan.Matches(rule.Criteria, body)
                        && (
                            !rule.Criteria.RequiresWhiteDwarfHost
                            || whiteDwarfHosted.Contains(body.System + "\u001f" + body.Body)
                        )
                    )
                    .Select(rule => rule.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            ))
            .Where(match => rules.Count == 1 && !rules[0].Criteria.RequiresWhiteDwarfHost || match.Codes.Count > 0)
            .Select(match => match.Codes.Count > 0 ? match : match with { Codes = [rules[0].Code] });

    private static SurfaceBodyMatch[] RelevantBodies(
        IReadOnlyList<SurfaceBodyMatch> bodies,
        IReadOnlyList<MiningMarketResult> stationQuotes
    )
    {
        string[] quoteCodes = stationQuotes.Select(quote => MiningCommodityCode.Abbreviate(quote.Commodity)).ToArray();
        SurfaceBodyMatch[] eligible = bodies
            .Where(match => match.Codes.Any(code => quoteCodes.Contains(code, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(match => match.Body.DistanceLy ?? double.MaxValue)
            .ThenBy(match => match.Body.ArrivalLs)
            .ToArray();
        SurfaceBodyMatch[] representatives = quoteCodes
            .Select(code =>
                eligible.FirstOrDefault(match => match.Codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            )
            .OfType<SurfaceBodyMatch>()
            .ToArray();
        SurfaceBodyMatch[] systemRepresentatives = eligible
            .DistinctBy(match => match.Body.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return representatives
            .Concat(systemRepresentatives)
            .Concat(eligible)
            .DistinctBy(match => match.Body.System + "\u001f" + match.Body.Body, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .OrderBy(match => match.Body.DistanceLy ?? double.MaxValue)
            .ThenBy(match => match.Body.ArrivalLs)
            .ToArray();
    }

    private static HashSet<string> AvailableCodes(IReadOnlyList<SurfaceBodyMatch> bodies) =>
        bodies.SelectMany(body => body.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static SurfaceSellMatch Describe(
        Search search,
        IReadOnlyList<SurfaceBodyMatch> bodies,
        MiningMarketResult best,
        IReadOnlyList<SurfaceSellStation> stations,
        IReadOnlyList<MiningMarketResult> stationQuotes,
        IReadOnlyList<long>? stationScores = null
    )
    {
        var stationCodes = stationQuotes
            .Select(quote => MiningCommodityCode.Abbreviate(quote.Commodity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> availableCodes = AvailableCodes(bodies);
        long bestViablePrice = stationQuotes
            .Where(quote => availableCodes.Contains(MiningCommodityCode.Abbreviate(quote.Commodity)))
            .Max(quote => quote.Price);
        return new SurfaceSellMatch(
            best,
            stations,
            bodies,
            stationCodes,
            bestViablePrice,
            stationScores ?? [bestViablePrice],
            search.Rules.SellSystemDetailsFor?.Invoke(best.System)
        );
    }

    private static bool SameSystem(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
