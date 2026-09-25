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

    public SurfaceSellRowViewModel(
        string target,
        string distance,
        IReadOnlyList<AcquireStationViewModel> stations,
        IReadOnlyList<SurfaceMiningSystemRowViewModel> systems,
        double referenceDistanceLy,
        long bestViablePrice
    )
    {
        Target = target;
        Distance = distance;
        Stations = stations;
        ReferenceDistanceLy = referenceDistanceLy;
        BestViablePrice = bestViablePrice;
        this.systems = systems;
        ToggleAllCommand = new WorkspaceCommand(() =>
        {
            showAllSystems = !showAllSystems;
            RefreshVisibleSystems();
            Changed(nameof(ShowAllLabel));
        });
        RefreshVisibleSystems();
    }

    public string Target { get; }
    public string Distance { get; }
    public IReadOnlyList<AcquireStationViewModel> Stations { get; }
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
        Changed(nameof(Systems));
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

public sealed class SurfaceMiningSearchViewModel : WorkspaceObservable, IDisposable
{
    private sealed record SurfaceMaterialRule(string Code, PlanetaryBodyCriteria Criteria);

    private sealed record SurfaceBodyMatch(MiningPlanetaryBody Body, IReadOnlyList<string> Codes);

    private sealed record SurfaceBodySearch(IReadOnlyList<SurfaceBodyMatch> Matches, bool Complete);

    private sealed record SurfaceRankedSearch(
        IReadOnlyList<SurfaceSellRowViewModel> Rows,
        bool FoundBody,
        bool Incomplete,
        bool Limited,
        int PagesChecked
    );

    private sealed class SurfaceRequestBudget
    {
        public const int MaximumBodyPages = 40;

        public int BodyPagesRemaining { get; private set; } = MaximumBodyPages;

        public int PagesChecked => MaximumBodyPages - BodyPagesRemaining;

        public bool TryUseBodyPage()
        {
            if (BodyPagesRemaining == 0)
            {
                return false;
            }

            BodyPagesRemaining--;
            return true;
        }
    }

    private sealed record SurfaceStationCandidate(MiningMarketResult[] Quotes, MiningMarketResult Anchor, int Priority);

    private const string RequestFailed = "Request failed. Try again.";
    private readonly MiningSearchClient client;
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
    private IReadOnlyList<SurfaceSellRowViewModel> rows = [];

    public SurfaceMiningSearchViewModel(MiningSearchClient client)
    {
        this.client = client;
        SearchCommand = new WorkspaceCommand(() => _ = SearchAsync());
        CancelCommand = new WorkspaceCommand(Cancel);
        DistanceSortCommand = new WorkspaceCommand(ToggleDistanceSort);
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
    public string DistanceSortLabel => nearestFirst ? "Nearest first" : "Farthest first";
    public string DistanceSortIndicator => nearestFirst ? "↑" : "↓";

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
        set => Set(ref resultLimit, Math.Clamp(value, 1, 5));
    }

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
        List<MiningMarketResult> quotes = [];
        bool usedFallback = false;
        foreach (string material in materials)
        {
            token.ThrowIfCancellationRequested();
            (IReadOnlyList<MiningMarketResult> found, string provider) = await client.FindMarketsPreferringArdentAsync(
                new MiningMarketQuery(
                    Reference.Trim(),
                    material,
                    false,
                    Radius,
                    MinimumDemand: MinimumDemand,
                    MaximumDemand: MaximumDemand,
                    PadSize: PadSize
                ),
                token
            );
            quotes.AddRange(
                found.Where(quote => materials.Any(material => MiningCommodityName.Same(material, quote.Commodity)))
            );
            usedFallback |= provider != "Ardent";
        }

        SurfaceStationCandidate[] candidates = CandidatesFor(quotes, materials, catalogOrder);
        if (candidates.Length == 0)
        {
            return "No sell station matches the selected material, demand, and landing pad within the distance.";
        }

        SurfaceRankedSearch search = await RankStationsAsync(
            candidates,
            materials,
            criteria,
            rules,
            catalogOrder,
            token
        );

        return ResultMessage(search, materials, catalogOrder, usedFallback);
    }

    private string ResultMessage(
        SurfaceRankedSearch search,
        IReadOnlyList<string> materials,
        bool catalogOrder,
        bool usedFallback
    )
    {
        if (search.Rows.Count == 0 && search.Limited)
        {
            return $"No matching bodies were found after checking the highest-paying sell stations. Search stopped at its request limit ({search.PagesChecked} Spansh body pages); narrow the distance or select a material.";
        }

        if (search.Rows.Count == 0)
        {
            return "No sell station has a matching surface mining body within the distance.";
        }

        int bodyCount = search.Rows.Sum(
            (SurfaceSellRowViewModel row) =>
                row.Systems.Sum((SurfaceMiningSystemRowViewModel system) => system.Bodies.Count)
        );
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
            + (
                search.Incomplete
                    ? " Some body searches reached the page limit; unmatched prices were left unmarked."
                    : ""
            )
            + (
                search.Limited
                    ? $" Search stopped at its request limit ({search.PagesChecked} Spansh body pages); narrow the distance for more coverage."
                    : ""
            )
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
        CancellationToken token
    )
    {
        var bodyCache = new Dictionary<string, SurfaceBodySearch>(StringComparer.OrdinalIgnoreCase);
        var budget = new SurfaceRequestBudget();
        var ranked = new List<SurfaceSellRowViewModel>();
        bool foundAnyBody = false;
        bool incomplete = false;
        bool limited = false;
        var selectedStations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < candidates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            Status = $"Checking sell systems {index + 1}/{candidates.Length}…";
            SurfaceStationCandidate candidate = candidates[index];
            string stationKey = candidate.Anchor.System + "\u001f" + candidate.Anchor.Station;
            if (selectedStations.Contains(stationKey))
            {
                continue;
            }

            if (budget.BodyPagesRemaining == 0)
            {
                limited = true;
                break;
            }

            SurfaceBodySearch bodySearch = await CachedBodySearchAsync(
                bodyCache,
                candidate.Anchor.System,
                criteria,
                rules,
                budget,
                token
            );
            foundAnyBody |= bodySearch.Matches.Count > 0;
            incomplete |= !bodySearch.Complete;
            if (bodySearch.Matches.Count == 0)
            {
                continue;
            }

            SurfaceSellRowViewModel? row = await DescribeCandidateAsync(
                candidate,
                bodySearch,
                materials,
                catalogOrder,
                token
            );
            if (row is null)
            {
                continue;
            }

            selectedStations.Add(stationKey);
            ranked.Add(row);
            ranked = KeepBestRows(ranked, catalogOrder, ResultLimit);

            Rows = ranked.ToArray();
            if (CanStopRanking(catalogOrder, ranked, candidates, index, ResultLimit))
            {
                break;
            }
        }

        return new SurfaceRankedSearch(ranked, foundAnyBody, incomplete, limited, budget.PagesChecked);
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
        SurfaceRequestBudget budget,
        CancellationToken token
    )
    {
        if (!cache.TryGetValue(system, out SurfaceBodySearch? result))
        {
            result = await FindBodyMatchesAsync(system, criteria, rules, budget, token);
            cache.Add(system, result);
        }

        return result;
    }

    private static List<SurfaceSellRowViewModel> KeepBestRows(
        List<SurfaceSellRowViewModel> ranked,
        bool catalogOrder,
        int resultLimit
    ) =>
        catalogOrder
            ? ranked
            : ranked
                .OrderByDescending(item => item.BestViablePrice)
                .ThenBy(item => item.ReferenceDistanceLy)
                .Take(resultLimit)
                .ToList();

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
        List<SurfaceSellRowViewModel> ranked,
        IReadOnlyList<SurfaceStationCandidate> candidates,
        int index,
        int resultLimit
    ) =>
        ranked.Count == resultLimit
        && (
            catalogOrder
            || (index + 1 < candidates.Count && candidates[index + 1].Anchor.Price < ranked[^1].BestViablePrice)
        );

    public async Task SearchAsync()
    {
        CancellationTokenSource? previous = pending;
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(90));
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
                Status = "Search canceled or timed out.";
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
        CancellationToken token
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
            IReadOnlyList<MiningMarketResult> imports = await client.FindSystemImportsAsync(
                best.System,
                TimeSpan.FromDays(2),
                token
            );
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
        SurfaceRequestBudget budget,
        CancellationToken token
    )
    {
        var matches = new List<SurfaceBodyMatch>();
        bool complete = false;
        for (int page = 0; page < 20; page++)
        {
            if (!budget.TryUseBodyPage())
            {
                break;
            }

            IReadOnlyList<MiningPlanetaryBody> bodies = await client.FindPlanetaryBodiesAsync(
                new MiningPlanetaryQuery(
                    system,
                    criteria.BodySubtypes,
                    criteria.LandmarkSubtypes,
                    "",
                    Radius,
                    Page: page,
                    VolcanismTypes: criteria.VolcanismTypes
                ),
                token
            );
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
            if (bodies.Count < SpanshRoutes.PageSize(SpanshRoutes.Bodies))
            {
                complete = true;
                break;
            }
        }

        return new SurfaceBodySearch(
            matches
                .DistinctBy(match => match.Body.System + "\u001f" + match.Body.Body, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            complete
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
        return representatives
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
        IReadOnlyList<MiningMarketResult> stationQuotes
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
        var row = new SurfaceSellRowViewModel(
            best.System,
            best.Distance is { } distance ? distance.ToString("0", CultureInfo.CurrentCulture) + " ly" : "",
            stations,
            systems,
            best.Distance ?? double.MaxValue,
            bestViablePrice
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
}
