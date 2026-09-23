using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

internal static class SurfaceMaterialBadgePalette
{
    private static readonly IReadOnlyDictionary<string, string> ColorsByCode = SurfaceMiningCommodityCatalog
        .All.GroupBy(commodity => MiningCommodityCode.Abbreviate(commodity.Name), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().ColorHex, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> RingColorsByCode = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["ALU"] = "#AEBCC4",
        ["BAU"] = "#B96B42",
        ["BEN"] = "#4F83B8",
        ["BRT"] = "#B98966",
        ["BER"] = "#7FB9A3",
        ["BIS"] = "#BDA5C9",
        ["BRO"] = "#C4F1F7",
        ["COB"] = "#4E6F9C",
        ["CLT"] = "#78665B",
        ["CRY"] = "#DDF5F5",
        ["GAL"] = "#A68A5B",
        ["GLM"] = "#8BBAD0",
        ["GOS"] = "#A7C9A9",
        ["HAF"] = "#74858D",
        ["IDT"] = "#A88F73",
        ["IND"] = "#718DA4",
        ["LAN"] = "#B3A070",
        ["LEP"] = "#AB8DBD",
        ["LHY"] = "#B8D5E7",
        ["MCL"] = "#B4D8E8",
        ["MOI"] = "#DBD9D1",
        ["MUS"] = "#7D5894",
        ["PAI"] = "#C93551",
        ["PRA"] = "#7FAE8F",
        ["PYR"] = "#D4B6A6",
        ["RUT"] = "#9C6047",
        ["TAF"] = "#E2A7BE",
        ["THL"] = "#94A4B6",
        ["VOP"] = "#68C7DB",
    };

    public static string ColorHexFor(string code) =>
        ColorsByCode.GetValueOrDefault(code) ?? RingColorsByCode.GetValueOrDefault(code, "#E6D59A");

    public static string ForegroundFor(string colorHex)
    {
        var color = Color.Parse(colorHex);
        return color.R * 0.2126 + color.G * 0.7152 + color.B * 0.0722 >= 140 ? "#111111" : "#FFFFFF";
    }
}

public sealed record SurfaceBodyTag
{
    public SurfaceBodyTag(string code, bool matchesStation)
    {
        Code = code;
        MatchesStation = matchesStation;
        ColorHex = SurfaceMaterialBadgePalette.ColorHexFor(code);
        ContrastForeground = SurfaceMaterialBadgePalette.ForegroundFor(ColorHex);
    }

    public string Code { get; }
    public bool MatchesStation { get; }
    public bool IsExtraMaterial => !MatchesStation;
    public string ColorHex { get; }
    public string ContrastForeground { get; }
}

public sealed record SurfaceBodyLine
{
    public SurfaceBodyLine(IReadOnlyList<string> codes, string details, IReadOnlySet<string>? stationCodes = null)
    {
        Codes = codes;
        Details = details;
        Tags = codes.Select(code => new SurfaceBodyTag(code, stationCodes?.Contains(code) == true)).ToArray();
    }

    public IReadOnlyList<string> Codes { get; }
    public string Details { get; }
    public IReadOnlyList<SurfaceBodyTag> Tags { get; }
}

public sealed class SurfaceMiningSystemRowViewModel : WorkspaceObservable
{
    private bool isExpanded;
    private string connector = "Single";
    private IReadOnlyList<SurfaceBodyLine> visibleBodies;

    public SurfaceMiningSystemRowViewModel(string system, double distanceLy, IReadOnlyList<SurfaceBodyLine> bodies)
    {
        System = system;
        DistanceLy = distanceLy;
        Bodies = bodies;
        visibleBodies = bodies.Count > 0 ? [bodies[0]] : [];
        ToggleCommand = new WorkspaceCommand(() => IsExpanded = !IsExpanded);
    }

    public string System { get; }
    public double DistanceLy { get; }
    public string Distance =>
        DistanceLy < double.MaxValue ? DistanceLy.ToString("0.0", CultureInfo.CurrentCulture) + " ly" : "";
    public IReadOnlyList<SurfaceBodyLine> Bodies { get; }
    public IReadOnlyList<SurfaceBodyLine> VisibleBodies => visibleBodies;
    public bool HasAdditionalBodies => Bodies.Count > 1;
    public ICommand ToggleCommand { get; }
    public string Chevron => IsExpanded ? "▾" : "▸";

    public string Connector
    {
        get => connector;
        set => Set(ref connector, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (HasAdditionalBodies && Set(ref isExpanded, value))
            {
                visibleBodies = value ? Bodies : [Bodies[0]];
                Changed(nameof(VisibleBodies));
                Changed(nameof(Chevron));
            }
        }
    }
}

public sealed class SurfaceSellRowViewModel : WorkspaceObservable
{
    private IReadOnlyList<SurfaceMiningSystemRowViewModel> systems;
    private IReadOnlyList<SurfaceMiningSystemRowViewModel> visibleSystems = [];
    private bool showAllSystems;
    private bool showAllStations;
    private bool showAllBodies;
    private IReadOnlyList<AcquireStationViewModel> visibleStations;
    private SurfaceBodyLine[] bodyLines;
    private IReadOnlyList<SurfaceBodyLine> visibleBodyLines;

    public SurfaceSellRowViewModel(
        string target,
        string distance,
        IReadOnlyList<AcquireStationViewModel> stations,
        IReadOnlyList<SurfaceMiningSystemRowViewModel> systems,
        double referenceDistanceLy,
        long bestViablePrice,
        SurfaceSellRowOptions? options = null
    )
    {
        Target = target;
        Distance = distance;
        Stations = stations;
        visibleStations = stations.Take(1).ToArray();
        StationRanking = PowerplayStationRanking.FromScores(options?.StationScores ?? [bestViablePrice]);
        SurfaceSellSystemDetails? details = options?.Details;
        PowerState = details?.PowerState ?? "";
        FactionState = details?.FactionState ?? "";
        Powers = details?.Powers.Count > 0 ? string.Join("\n", details.Powers) : "";
        ReferenceDistanceLy = referenceDistanceLy;
        BestViablePrice = bestViablePrice;
        this.systems = systems;
        bodyLines = systems.SelectMany(system => system.Bodies).ToArray();
        visibleBodyLines = bodyLines.Take(1).ToArray();
        ToggleAllCommand = new WorkspaceCommand(() =>
        {
            showAllSystems = !showAllSystems;
            RefreshVisibleSystems();
            Changed(nameof(ShowAllLabel));
        });
        ToggleStationsCommand = new WorkspaceCommand(() =>
        {
            showAllStations = !showAllStations;
            visibleStations = showAllStations ? Stations : Stations.Take(1).ToArray();
            Changed(nameof(VisibleStations));
            Changed(nameof(StationToggleLabel));
        });
        ToggleBodiesCommand = new WorkspaceCommand(() =>
        {
            showAllBodies = !showAllBodies;
            visibleBodyLines = showAllBodies ? bodyLines : bodyLines.Take(1).ToArray();
            Changed(nameof(VisibleBodyLines));
            Changed(nameof(BodyToggleLabel));
        });
        RefreshVisibleSystems();
    }

    public string Target { get; }
    public string Distance { get; }
    public IReadOnlyList<AcquireStationViewModel> Stations { get; }
    public IReadOnlyList<AcquireStationViewModel> VisibleStations => visibleStations;
    public bool CanToggleStations => Stations.Count > 1;
    public string StationToggleLabel => showAllStations ? "Show fewer stations" : "Show all stations";
    public ICommand ToggleStationsCommand { get; }
    public IReadOnlyList<SurfaceBodyLine> BodyLines => bodyLines;
    public IReadOnlyList<SurfaceBodyLine> VisibleBodyLines => visibleBodyLines;
    public bool HasAdditionalBodies => bodyLines.Length > 1;
    public string BodyToggleLabel => showAllBodies ? "Show fewer bodies" : "Show all bodies";
    public ICommand ToggleBodiesCommand { get; }
    internal PowerplayStationRanking StationRanking { get; }
    public string PowerState { get; }
    public string FactionState { get; }
    public string Powers { get; }
    public double ReferenceDistanceLy { get; }
    public long BestViablePrice { get; }
    public IReadOnlyList<SurfaceMiningSystemRowViewModel> Systems => systems;
    public IReadOnlyList<SurfaceMiningSystemRowViewModel> VisibleSystems => visibleSystems;
    public bool HasAdditionalSystems => systems.Count > 5;
    public ICommand ToggleAllCommand { get; }
    public string ShowAllLabel => showAllSystems ? "Show fewer Systems" : "Show all Systems";

    public void SortSystems(bool nearestFirst)
    {
        systems = nearestFirst
            ? systems.OrderBy(system => system.DistanceLy).ToArray()
            : systems.OrderByDescending(system => system.DistanceLy).ToArray();
        bodyLines = systems.SelectMany(system => system.Bodies).ToArray();
        visibleBodyLines = showAllBodies ? bodyLines : bodyLines.Take(1).ToArray();
        Changed(nameof(Systems));
        Changed(nameof(BodyLines));
        Changed(nameof(VisibleBodyLines));
        RefreshVisibleSystems();
    }

    private void RefreshVisibleSystems()
    {
        SurfaceMiningSystemRowViewModel[] visible = (showAllSystems ? systems : systems.Take(5)).ToArray();
        for (int index = 0; index < visible.Length; index++)
        {
            visible[index].Connector = AcquireConnector.ForIndex(index, visible.Length);
        }

        visibleSystems = visible;
        Changed(nameof(VisibleSystems));
    }
}

public sealed record SurfaceSellSystemDetails(
    string PowerState,
    string FactionState,
    IReadOnlyList<string> Powers,
    double? DistanceLy = null
);

public sealed record SurfaceSellRowOptions(IReadOnlyList<long>? StationScores, SurfaceSellSystemDetails? Details);

public sealed class SurfaceMiningSearchViewModel : WorkspaceObservable, IDisposable
{
    private enum SellRowSort
    {
        Value,
        DistanceAscending,
        DistanceDescending,
        StationDescending,
        StationAscending,
    }

    private sealed record SurfaceMaterialRule(string Code, PlanetaryBodyCriteria Criteria);

    private sealed record SurfaceBodyMatch(MiningPlanetaryBody Body, IReadOnlyList<string> Codes);

    private sealed record SurfaceBodySearch(IReadOnlyList<SurfaceBodyMatch> Matches, bool Complete);

    private sealed record SurfaceRankedSearch(IReadOnlyList<SurfaceSellRowViewModel> Rows);

    private sealed record SurfaceStationCandidate(MiningMarketResult[] Quotes, MiningMarketResult Anchor, int Priority);

    private sealed class SurfaceRankingState(bool catalogOrder)
    {
        public bool CatalogOrder { get; } = catalogOrder;
        public Dictionary<string, SurfaceBodySearch> BodyCache { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, bool> SellSystemEligibility { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ProcessedCandidates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedStations { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedSystems { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SurfaceSellRowViewModel> Ranked { get; set; } = [];
    }

    private const string RequestFailed = "Request failed. Try again.";
    private readonly MiningSearchClient client;
    private readonly int maximumResults;
    private CancellationTokenSource? pending;
    private string reference = "";
    private string currentSystem = "";
    private bool referenceTracksCommander;
    private double radius = 100;
    private int resultLimit = 1;
    private string padSize = "Any";
    private long minimumDemand;
    private long maximumDemand = 90_000;
    private string status = "Choose a reference system and a surface material.";
    private bool busy;
    private bool nearestFirst = true;
    private SellRowSort sellRowSort;
    private IReadOnlyList<SurfaceSellRowViewModel> rows = [];

    public SurfaceMiningSearchViewModel(MiningSearchClient client, int maximumResults = 5)
    {
        this.client = client;
        this.maximumResults = maximumResults;
        SearchCommand = new WorkspaceCommand(() => _ = SearchAsync(CancellationToken.None));
        CancelCommand = new WorkspaceCommand(Cancel);
        DistanceSortCommand = new WorkspaceCommand(ToggleDistanceSort);
        SellDistanceSortCommand = new WorkspaceCommand(ToggleSellDistanceSort);
        BestStationSortCommand = new WorkspaceCommand(ToggleBestStationSort);
    }

    public MiningChipBoxViewModel Materials { get; } =
        new(
            "Mineral / metal",
            new[] { MiningMaterialSelection.Any }.Concat(PlanetaryMiningPlan.Materials).ToArray(),
            "",
            [MiningMaterialSelection.Any]
        );

    public static IReadOnlyList<string> PadSizes { get; } = MiningSearchViewModel.PadSizes;

    public ICommand SearchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DistanceSortCommand { get; }
    public ICommand SellDistanceSortCommand { get; }
    public ICommand BestStationSortCommand { get; }
    public string DistanceSortLabel => nearestFirst ? "Nearest first" : "Farthest first";
    public string DistanceSortIndicator => nearestFirst ? "↑" : "↓";
    public string SellDistanceSortIndicator =>
        sellRowSort switch
        {
            SellRowSort.DistanceAscending => "↑",
            SellRowSort.DistanceDescending => "↓",
            _ => "",
        };
    public string BestStationSortIndicator =>
        sellRowSort switch
        {
            SellRowSort.StationDescending => "↓",
            SellRowSort.StationAscending => "↑",
            _ => "",
        };

    public string Reference
    {
        get => reference;
        set
        {
            string next = value ?? "";
            if (next.Length == 0 && currentSystem.Length > 0)
            {
                next = currentSystem;
            }

            bool restored = string.IsNullOrEmpty(value) && next.Length > 0;
            referenceTracksCommander = next.Equals(currentSystem, StringComparison.OrdinalIgnoreCase);
            if (!Set(ref reference, next) && restored)
            {
                Changed(nameof(Reference));
            }
        }
    }

    public void UpdateCurrentLocation(string? system)
    {
        string next = system ?? "";
        string previous = currentSystem;
        currentSystem = next;
        if (
            next.Length > 0
            && (
                referenceTracksCommander
                || Reference.Length == 0
                || Reference.Equals(previous, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            referenceTracksCommander = true;
            Reference = next;
        }
    }

    public double Radius
    {
        get => radius;
        set => Set(ref radius, value);
    }

    public int ResultLimit
    {
        get => resultLimit;
        set => Set(ref resultLimit, Math.Clamp(value, 1, maximumResults));
    }

    public TimeSpan? MaximumAge { get; set; } = TimeSpan.FromDays(2);

    public IReadOnlyList<string> BodyControllingPowers { get; set; } = [];

    public double? BodySearchRadius { get; set; }

    public Func<string, IReadOnlySet<string>>? MiningSystemsForSell { get; set; }

    public Func<string, SurfaceSellSystemDetails?>? SellSystemDetailsFor { get; set; }

    public bool GroupStationsBySystem { get; set; }

    public bool MarketGalaxyWide { get; set; }

    public bool UseAdditionalMarketsOnly { get; set; }

    public Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<IReadOnlyList<MiningMarketResult>>
    >? AdditionalMarketQuotesAsync { get; set; }

    public bool DefaultSellDistanceSort { get; set; }

    public string NoSellStationsMessage { get; set; } =
        "No sell station matches the selected material, demand, and landing pad within the distance.";

    public string NoMatchingBodiesMessage { get; set; } =
        "No sell station has a matching surface mining body within the distance.";

    public Func<
        IReadOnlyList<string>,
        CancellationToken,
        Task<IReadOnlySet<string>>
    >? EligibleSellSystemsAsync { get; set; }

    public string PadSize
    {
        get => padSize;
        set => Set(ref padSize, string.IsNullOrWhiteSpace(value) ? "Any" : value);
    }

    public long MinimumDemand
    {
        get => minimumDemand;
        set => Set(ref minimumDemand, Math.Max(0, value));
    }

    public long MaximumDemand
    {
        get => maximumDemand;
        set => Set(ref maximumDemand, Math.Max(0, value));
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public bool IsBusy
    {
        get => busy;
        private set => Set(ref busy, value);
    }

    public IReadOnlyList<SurfaceSellRowViewModel> Rows
    {
        get => rows;
        private set
        {
            if (Set(ref rows, value))
            {
                Changed(nameof(HasRows));
            }
        }
    }

    public bool HasRows => Rows.Count > 0;

    public void UseDiagnosticLog(Action<string>? log) => client.DiagnosticLog = log;

    private async Task<string> FindSurfaceSalesAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Reference))
        {
            Rows = [];
            return "Choose a reference system and a surface material.";
        }

        bool catalogOrder = MiningMaterialSelection.IsAny(Materials.Selected);
        IReadOnlyDictionary<string, MiningCommodityPriceSummary>? dailyPrices = catalogOrder
            ? await client.CommodityPriceReportAsync(token)
            : null;
        IReadOnlyList<string> materials = SurfaceMiningSearchPlan.MaterialsFor(Materials.Selected, dailyPrices);
        PlanetaryBodyCriteria? criteria = PlanetaryMiningPlan.For(materials);
        if (criteria is null)
        {
            Rows = [];
            return "Choose a surface material.";
        }

        SurfaceMaterialRule[] rules = materials
            .Select(material => new SurfaceMaterialRule(
                MiningCommodityCode.Abbreviate(material),
                PlanetaryMiningPlan.For([material])!
            ))
            .Where(rule => rule.Criteria is not null)
            .ToArray();

        Rows = [];
        sellRowSort = DefaultSellDistanceSort ? SellRowSort.DistanceAscending : SellRowSort.Value;
        Changed(nameof(SellDistanceSortIndicator));
        Changed(nameof(BestStationSortIndicator));
        (List<MiningMarketResult> quotes, bool usedFallback) = await FindInitialSurfaceQuotesAsync(materials, token);

        SurfaceRankedSearch search;
        var ranking = new SurfaceRankingState(catalogOrder);
        do
        {
            SurfaceStationCandidate[] candidates = CandidatesFor(quotes, materials, catalogOrder);
            search =
                candidates.Length == 0
                    ? new SurfaceRankedSearch([])
                    : await RankStationsAsync(candidates, materials, criteria, rules, catalogOrder, ranking, token);
            if (search.Rows.Count >= ResultLimit || AdditionalMarketQuotesAsync is null)
            {
                break;
            }

            IReadOnlyList<MiningMarketResult> more = await AdditionalMarketQuotesAsync(materials, token);
            if (more.Count == 0)
            {
                break;
            }

            quotes.AddRange(more);
        } while (true);

        return quotes.Count == 0 ? NoSellStationsMessage : ResultMessage(search, materials, catalogOrder, usedFallback);
    }

    private async Task<(List<MiningMarketResult> Quotes, bool UsedFallback)> FindInitialSurfaceQuotesAsync(
        IReadOnlyList<string> materials,
        CancellationToken token
    )
    {
        List<MiningMarketResult> quotes = [];
        bool usedFallback = false;
        foreach (string material in UseAdditionalMarketsOnly ? Enumerable.Empty<string>() : materials)
        {
            token.ThrowIfCancellationRequested();
            (IReadOnlyList<MiningMarketResult> found, string provider) = await client.FindMarketsPreferringArdentAsync(
                new MiningMarketQuery(
                    Reference.Trim(),
                    material,
                    false,
                    Radius,
                    GalaxyWide: MarketGalaxyWide,
                    MaximumAgeDays: MaximumAge is null
                        ? 3650
                        : Math.Clamp((int)Math.Ceiling(MaximumAge.Value.TotalDays), 1, 3650),
                    MinimumDemand: MinimumDemand,
                    MaximumDemand: MaximumDemand,
                    MaximumAge: MaximumAge,
                    PadSize: PadSize
                ),
                token
            );
            quotes.AddRange(
                found.Where(quote => materials.Any(material => MiningCommodityName.Same(material, quote.Commodity)))
            );
            usedFallback |= provider != "Ardent";
        }

        return (quotes, usedFallback);
    }

    private string ResultMessage(
        SurfaceRankedSearch search,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        bool usedFallback
    )
    {
        if (search.Rows.Count == 0)
        {
            return NoMatchingBodiesMessage;
        }

        int bodyCount = search.Rows.Sum(row => row.Systems.Sum(system => system.Bodies.Count));
        return bodyCount
            + " landable bodies for "
            + (
                MiningMaterialSelection.IsAny(Materials.Selected)
                    ? "Any surface material"
                    : string.Join(", ", materials)
            )
            + ". "
            + search.Rows.Count
            + (
                catalogOrder
                    ? " sell systems, daily commodity value order. Best sell from "
                    : " sell systems, best viable price first. Best sell from "
            )
            + (usedFallback ? "Ardent/Spansh fallback" : "Ardent")
            + "."
            + (client.PriceMarksUnavailable ? " " + RequestFailed : "");
    }

    private static SurfaceStationCandidate[] CandidatesFor(
        IReadOnlyList<MiningMarketResult> quotes,
        IReadOnlyList<string> materials,
        bool catalogOrder
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

        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenByDescending(candidate => candidate.Anchor.Price)
            .ThenBy(candidate => candidate.Anchor.Distance ?? double.MaxValue)
            .ToArray();
    }

    private async Task<SurfaceRankedSearch> RankStationsAsync(
        SurfaceStationCandidate[] candidates,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        bool catalogOrder,
        SurfaceRankingState ranking,
        CancellationToken token
    )
    {
        List<SurfaceSellRowViewModel> ranked = ranking.Ranked;
        for (int index = 0; index < candidates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            Status = $"Checking sell systems {index + 1}/{candidates.Length}…";
            SurfaceSellRowViewModel? row = await TryDescribeRankedCandidateAsync(
                candidates,
                index,
                materials,
                criteria,
                rules,
                ranking,
                token
            );
            if (row is null)
            {
                continue;
            }

            ranked.Add(row);
            ranked = KeepBestRows(ranked, catalogOrder, GroupStationsBySystem, ResultLimit);
            ranking.Ranked = ranked;

            Rows = SortRows(ranked);
            if (
                CanStopRanking(
                    catalogOrder,
                    GroupStationsBySystem,
                    materials.Count,
                    ranked,
                    candidates,
                    index,
                    ResultLimit
                )
            )
            {
                break;
            }
        }

        return new SurfaceRankedSearch(ranked);
    }

    private async Task<SurfaceSellRowViewModel?> TryDescribeRankedCandidateAsync(
        SurfaceStationCandidate[] candidates,
        int index,
        IReadOnlyList<string> materials,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        SurfaceRankingState ranking,
        CancellationToken token
    )
    {
        SurfaceStationCandidate candidate = candidates[index];
        string stationKey = candidate.Anchor.System + "\u001f" + candidate.Anchor.Station;
        string candidateKey = stationKey + "\u001f" + MiningCommodityName.Key(candidate.Anchor.Commodity);
        if (!ranking.ProcessedCandidates.Add(candidateKey))
        {
            return null;
        }

        if (!await IsEligibleSellSystemAsync(candidates, index, ranking.SellSystemEligibility, token))
        {
            return null;
        }

        if (
            IsAlreadyRankedCandidate(
                stationKey,
                candidate.Anchor.System,
                ranking.SelectedStations,
                ranking.SelectedSystems
            )
        )
        {
            return null;
        }

        IReadOnlySet<string>? miningSystems = MiningSystemsForSell?.Invoke(candidate.Anchor.System);
        if (miningSystems is { Count: 0 })
        {
            return null;
        }

        SurfaceBodySearch bodySearch = await CachedBodySearchAsync(
            ranking.BodyCache,
            candidate.Anchor.System,
            criteria,
            rules,
            miningSystems,
            token
        );
        if (bodySearch.Matches.Count == 0)
        {
            return null;
        }

        SurfaceSellRowViewModel? row = GroupStationsBySystem
            ? await DescribeSellSystemAsync(candidate, candidates, bodySearch, materials, ranking.CatalogOrder, token)
            : await DescribeCandidateAsync(candidate, bodySearch, materials, ranking.CatalogOrder, token);
        if (row is not null)
        {
            ranking.SelectedStations.Add(stationKey);
            ranking.SelectedSystems.Add(candidate.Anchor.System);
        }

        return row;
    }

    private bool IsAlreadyRankedCandidate(
        string stationKey,
        string system,
        HashSet<string> selectedStations,
        HashSet<string> selectedSystems
    ) => selectedStations.Contains(stationKey) || (GroupStationsBySystem && selectedSystems.Contains(system));

    private async Task<SurfaceSellRowViewModel?> DescribeSellSystemAsync(
        SurfaceStationCandidate first,
        IReadOnlyList<SurfaceStationCandidate> candidates,
        SurfaceBodySearch bodySearch,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        CancellationToken token
    )
    {
        IReadOnlyList<MiningMarketResult> imports = [];
        if (materials.Count > 1)
        {
            try
            {
                imports = await client.FindSystemImportsAsync(
                    first.Anchor.System,
                    MaximumAge ?? TimeSpan.FromDays(3650),
                    token
                );
            }
            catch (Exception ex)
                when (ex
                        is HttpRequestException
                            or System.Text.Json.JsonException
                            or IOException
                            or InvalidDataException
                )
            {
                // Nearby Ardent quotes remain usable when station imports are unavailable.
            }
        }

        IReadOnlyDictionary<string, long> averages = await client.AverageSellPricesAsync(token);
        var viable = new List<(AcquireStationViewModel Station, long Score, MiningMarketResult[] Quotes)>();
        foreach (
            SurfaceStationCandidate candidate in candidates
                .Where(item => item.Anchor.System.Equals(first.Anchor.System, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(item => item.Anchor.Station, StringComparer.OrdinalIgnoreCase)
        )
        {
            MiningMarketResult[] stationQuotes = await StationQuotesForAsync(
                candidate.Quotes,
                materials,
                catalogOrder ? candidate.Anchor.Commodity : null,
                token,
                imports
            );
            SurfaceBodyMatch[] matching = RelevantBodies(bodySearch.Matches, stationQuotes);
            if (matching.Length == 0)
            {
                continue;
            }

            var available = matching.SelectMany(body => body.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long score = stationQuotes
                .Where(quote => available.Contains(MiningCommodityCode.Abbreviate(quote.Commodity)))
                .Max(quote => quote.Price);
            viable.Add(
                (
                    StationFor(candidate.Quotes[0], stationQuotes, matching, bodySearch.Complete, averages)[0],
                    score,
                    stationQuotes
                )
            );
        }

        if (viable.Count == 0)
        {
            return null;
        }

        (AcquireStationViewModel Station, long Score, MiningMarketResult[] Quotes)[] rankedStations = viable
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Station.Name)
            .ToArray();
        MiningMarketResult[] bestQuotes = rankedStations[0].Quotes;
        SurfaceBodyMatch[] bodies = RelevantBodies(bodySearch.Matches, bestQuotes);
        if (catalogOrder && !IsViableCandidate(catalogOrder, first, bodies))
        {
            // A later material in the daily catalog order may make this system viable.
            return null;
        }

        AcquireStationViewModel[] stations = rankedStations
            .Select((item, index) => item.Station with { CanToggle = index == 0 && rankedStations.Length > 1 })
            .ToArray();
        return Describe(
            bodies,
            first.Anchor,
            stations,
            bestQuotes,
            rankedStations.Select(item => item.Score).ToArray()
        );
    }

    private async Task<bool> IsEligibleSellSystemAsync(
        SurfaceStationCandidate[] candidates,
        int index,
        Dictionary<string, bool> eligibility,
        CancellationToken token
    )
    {
        if (EligibleSellSystemsAsync is null)
        {
            return true;
        }

        string system = candidates[index].Anchor.System;
        if (!eligibility.TryGetValue(system, out bool eligible))
        {
            await CheckSellSystemsAsync(candidates, index, eligibility, token);
            eligible = eligibility[system];
        }

        return eligible;
    }

    private async Task CheckSellSystemsAsync(
        SurfaceStationCandidate[] candidates,
        int start,
        Dictionary<string, bool> eligibility,
        CancellationToken token
    )
    {
        SurfaceStationCandidate[] batch = candidates
            .Skip(start)
            .Where(candidate => !eligibility.ContainsKey(candidate.Anchor.System))
            .DistinctBy(candidate => candidate.Anchor.System, StringComparer.OrdinalIgnoreCase)
            .Take(MarketGalaxyWide ? Math.Min(ResultLimit, 5) : 50)
            .ToArray();
        Status = "Checking sell-system eligibility…";
        IReadOnlySet<string> names = await EligibleSellSystemsAsync!(
            batch.Select(candidate => candidate.Anchor.System).ToArray(),
            token
        );
        foreach (string system in batch.Select(candidate => candidate.Anchor.System))
        {
            eligibility[system] = names.Contains(system);
        }
    }

    private async Task<SurfaceSellRowViewModel?> DescribeCandidateAsync(
        SurfaceStationCandidate candidate,
        SurfaceBodySearch bodySearch,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        CancellationToken token
    )
    {
        MiningMarketResult[] stationQuotes = await StationQuotesForAsync(
            candidate.Quotes,
            materials,
            catalogOrder ? candidate.Anchor.Commodity : null,
            token
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

        IReadOnlyDictionary<string, long> averages = await client.AverageSellPricesAsync(token);
        return Describe(
            relevant,
            candidate.Quotes[0],
            StationFor(candidate.Quotes[0], stationQuotes, relevant, bodySearch.Complete, averages),
            stationQuotes
        );
    }

    private async Task<SurfaceBodySearch> CachedBodySearchAsync(
        Dictionary<string, SurfaceBodySearch> cache,
        string system,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlySet<string>? miningSystems,
        CancellationToken token
    )
    {
        if (!cache.TryGetValue(system, out SurfaceBodySearch? result))
        {
            result = await FindBodyMatchesAsync(system, criteria, rules, miningSystems, token);
            cache.Add(system, result);
        }

        return result;
    }

    private static List<SurfaceSellRowViewModel> KeepBestRows(
        List<SurfaceSellRowViewModel> ranked,
        bool catalogOrder,
        bool groupStations,
        int resultLimit
    )
    {
        if (catalogOrder)
        {
            return ranked;
        }

        if (groupStations)
        {
            return ranked
                .OrderBy(
                    item => item,
                    Comparer<SurfaceSellRowViewModel>.Create(
                        (left, right) =>
                            PowerplayStationRanking.CompareDescending(left.StationRanking, right.StationRanking)
                    )
                )
                .Take(resultLimit)
                .ToList();
        }

        return ranked
            .OrderByDescending(item => item.BestViablePrice)
            .ThenBy(item => item.ReferenceDistanceLy)
            .Take(resultLimit)
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
        bool catalogOrder,
        bool groupStations,
        int materialCount,
        List<SurfaceSellRowViewModel> ranked,
        IReadOnlyList<SurfaceStationCandidate> candidates,
        int index,
        int resultLimit
    )
    {
        if (ranked.Count < resultLimit || catalogOrder)
        {
            return ranked.Count == resultLimit && catalogOrder;
        }

        if (!groupStations)
        {
            return index + 1 < candidates.Count && candidates[index + 1].Anchor.Price < ranked[^1].BestViablePrice;
        }

        return materialCount == 1
            && index + 1 < candidates.Count
            && candidates[index + 1].Anchor.Price < ranked[^1].StationRanking.Median * 0.92;
    }

    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? previous = pending;
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pending = current;
        IsBusy = true;
        Status = "Searching…";
        client.ResetDiagnostics();
        CancellationToken token = current.Token;
        try
        {
            if (previous is not null)
            {
                await previous.CancelAsync();
            }

            Status = await FindSurfaceSalesAsync(token);
        }
        catch (OperationCanceledException)
        {
            if (pending == current)
            {
                Status = "Search canceled.";
            }
        }
        catch (Exception ex)
            when (ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException)
        {
            if (pending == current)
            {
                Rows = [];
                Status = RequestFailed;
            }
        }
        catch (ArgumentException ex)
        {
            if (pending == current)
            {
                Status = ex.Message;
            }
        }
        finally
        {
            client.FlushDiagnostics();
            if (pending == current)
            {
                pending = null;
                IsBusy = false;
            }
        }
    }

    public void Cancel() => pending?.Cancel();

    public void Dispose()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }

    private async Task<MiningMarketResult[]> StationQuotesForAsync(
        MiningMarketResult[] candidate,
        IReadOnlyList<string> materials,
        string? anchorCommodity,
        CancellationToken token,
        IReadOnlyList<MiningMarketResult>? cachedImports = null
    )
    {
        if (materials.Count == 1)
        {
            return candidate.OrderByDescending(quote => quote.Price).Take(1).ToArray();
        }

        MiningMarketResult best = candidate[0];
        List<MiningMarketResult> stationQuotes = [.. candidate];
        try
        {
            IReadOnlyList<MiningMarketResult> imports =
                cachedImports
                ?? await client.FindSystemImportsAsync(best.System, MaximumAge ?? TimeSpan.FromDays(3650), token);
            stationQuotes.AddRange(
                imports.Where(market =>
                    market.Station.Equals(best.Station, StringComparison.OrdinalIgnoreCase)
                    && market.Price > 0
                    && market.Demand > 0
                    && market.Demand >= MinimumDemand
                    && (MaximumDemand == 0 || market.Demand <= MaximumDemand)
                    && materials.Any(material => MiningCommodityName.Same(material, market.Commodity))
                )
            );
        }
        catch (Exception ex)
            when (ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException)
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

    private async Task<SurfaceBodySearch> FindBodyMatchesAsync(
        string system,
        PlanetaryBodyCriteria criteria,
        IReadOnlyList<SurfaceMaterialRule> rules,
        IReadOnlySet<string>? miningSystems,
        CancellationToken token
    )
    {
        var matches = new List<SurfaceBodyMatch>();
        int page = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningPlanetaryBodyPage found = await client.FindPlanetaryBodyPageAsync(
                new MiningPlanetaryQuery(
                    system,
                    criteria.BodySubtypes,
                    criteria.LandmarkSubtypes,
                    "",
                    BodySearchRadius ?? Radius,
                    BodyControllingPowers,
                    Page: page,
                    VolcanismTypes: criteria.VolcanismTypes,
                    SystemNames: miningSystems?.ToArray()
                ),
                token
            );
            IReadOnlyList<MiningPlanetaryBody> bodies = miningSystems is null
                ? found.Bodies
                : found.Bodies.Where(body => miningSystems.Contains(body.System)).ToArray();
            MiningPlanetaryBody[] whiteDwarfCandidates = rules
                .Where(rule => rule.Criteria.RequiresWhiteDwarfHost)
                .SelectMany(rule => bodies.Where(body => PlanetaryMiningPlan.Matches(rule.Criteria, body)))
                .DistinctBy(body => body.System + "\u001f" + body.Body, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            IReadOnlySet<string> whiteDwarfHosted =
                whiteDwarfCandidates.Length > 0
                    ? await client.FindWhiteDwarfHostedBodiesAsync(system, whiteDwarfCandidates, token)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            matches.AddRange(
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
                    .Where(match =>
                        rules.Count == 1 && !rules[0].Criteria.RequiresWhiteDwarfHost || match.Codes.Count > 0
                    )
                    .Select(match => match.Codes.Count > 0 ? match : match with { Codes = [rules[0].Code] })
            );
            hasMore = found.HasMore;
            page++;
        }

        return new SurfaceBodySearch(
            matches
                .DistinctBy(match => match.Body.System + "\u001f" + match.Body.Body, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            true
        );
    }

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

    private static AcquireStationViewModel[] StationFor(
        MiningMarketResult best,
        IReadOnlyList<MiningMarketResult> stationQuotes,
        IReadOnlyList<SurfaceBodyMatch> bodies,
        bool complete,
        IReadOnlyDictionary<string, long> averages
    )
    {
        var available = bodies.SelectMany(body => body.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            new AcquireStationViewModel(
                best.Station,
                best.PadDescription,
                best.ArrivalLs is { } arrival
                    ? "Distance: " + arrival.ToString("0", CultureInfo.CurrentCulture) + " ls"
                    : "",
                "",
                stationQuotes
                    .Select(quote => new AcquireQuoteViewModel(
                        MiningCommodityCode.Abbreviate(quote.Commodity),
                        quote.Price.ToString("N0", CultureInfo.CurrentCulture)
                            + " CR "
                            + MiningPriceMarks.For(
                                quote.Price,
                                averages.TryGetValue(quote.Commodity, out long average) ? average : 0
                            ),
                        quote.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand",
                        complete && !available.Contains(MiningCommodityCode.Abbreviate(quote.Commodity)),
                        true
                    ))
                    .ToArray()
            ),
        ];
    }

    private SurfaceSellRowViewModel Describe(
        IReadOnlyList<SurfaceBodyMatch> bodies,
        MiningMarketResult best,
        IReadOnlyList<AcquireStationViewModel> stations,
        IReadOnlyList<MiningMarketResult> stationQuotes,
        IReadOnlyList<long>? stationScores = null
    )
    {
        var stationCodes = stationQuotes
            .Select(quote => MiningCommodityCode.Abbreviate(quote.Commodity))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var availableCodes = bodies.SelectMany(body => body.Codes).ToHashSet(StringComparer.OrdinalIgnoreCase);
        SurfaceMiningSystemRowViewModel[] systems = bodies
            .GroupBy(match => match.Body.System, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SurfaceMiningSystemRowViewModel(
                group.Key,
                group.First().Body.DistanceLy ?? double.MaxValue,
                group.Select(match => new SurfaceBodyLine(match.Codes, BodyDetails(match.Body), stationCodes)).ToArray()
            ))
            .ToArray();
        long bestViablePrice = stationQuotes
            .Where(quote => availableCodes.Contains(MiningCommodityCode.Abbreviate(quote.Commodity)))
            .Max(quote => quote.Price);
        SurfaceSellSystemDetails? details = SellSystemDetailsFor?.Invoke(best.System);
        double? distanceLy = details?.DistanceLy ?? best.Distance;
        var row = new SurfaceSellRowViewModel(
            best.System,
            distanceLy is { } distance ? distance.ToString("0", CultureInfo.CurrentCulture) + " ly" : "",
            stations,
            systems,
            distanceLy ?? double.MaxValue,
            bestViablePrice,
            new SurfaceSellRowOptions(stationScores, details)
        );
        row.SortSystems(nearestFirst);
        return row;
    }

    private static string BodyDetails(MiningPlanetaryBody body)
    {
        string name = body.Body.StartsWith(body.System + " ", StringComparison.OrdinalIgnoreCase)
            ? body.Body[(body.System.Length + 1)..]
            : body.Body;
        return name
            + ": "
            + body.Subtype
            + ", "
            + body.Gravity.ToString("0.00", CultureInfo.CurrentCulture)
            + " g, "
            + body.ArrivalLs.ToString("0", CultureInfo.CurrentCulture)
            + " ls";
    }

    private void ToggleDistanceSort()
    {
        nearestFirst = !nearestFirst;
        foreach (SurfaceSellRowViewModel row in Rows)
        {
            row.SortSystems(nearestFirst);
        }

        Changed(nameof(DistanceSortLabel));
        Changed(nameof(DistanceSortIndicator));
    }

    private void ToggleSellDistanceSort()
    {
        sellRowSort =
            sellRowSort == SellRowSort.DistanceAscending
                ? SellRowSort.DistanceDescending
                : SellRowSort.DistanceAscending;
        Rows = SortRows(Rows);
        Changed(nameof(SellDistanceSortIndicator));
        Changed(nameof(BestStationSortIndicator));
    }

    private void ToggleBestStationSort()
    {
        sellRowSort =
            sellRowSort == SellRowSort.StationDescending ? SellRowSort.StationAscending : SellRowSort.StationDescending;
        Rows = SortRows(Rows);
        Changed(nameof(SellDistanceSortIndicator));
        Changed(nameof(BestStationSortIndicator));
    }

    private SurfaceSellRowViewModel[] SortRows(IEnumerable<SurfaceSellRowViewModel> source)
    {
        SurfaceSellRowViewModel[] sorted = source.ToArray();
        if (sellRowSort == SellRowSort.Value)
        {
            return sorted;
        }

        Array.Sort(
            sorted,
            (left, right) =>
            {
                int result = sellRowSort switch
                {
                    SellRowSort.DistanceAscending => left.ReferenceDistanceLy.CompareTo(right.ReferenceDistanceLy),
                    SellRowSort.DistanceDescending => right.ReferenceDistanceLy.CompareTo(left.ReferenceDistanceLy),
                    SellRowSort.StationDescending => PowerplayStationRanking.CompareDescending(
                        left.StationRanking,
                        right.StationRanking
                    ),
                    SellRowSort.StationAscending => -PowerplayStationRanking.CompareDescending(
                        left.StationRanking,
                        right.StationRanking
                    ),
                    _ => 0,
                };
                if (result != 0)
                {
                    return result;
                }

                int nameOrder = StringComparer.OrdinalIgnoreCase.Compare(left.Target, right.Target);
                return sellRowSort == SellRowSort.StationAscending ? -nameOrder : nameOrder;
            }
        );
        return sorted;
    }
}
