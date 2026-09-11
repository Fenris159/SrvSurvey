using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using System.Windows.Input;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningSearchViewModel(MiningSearchClient client, BookmarksViewModel bookmarks, Action<MiningRing> cacheRing, Func<IReadOnlyList<MiningRing>> localRings, IStarSystemResolver resolver, MiningCommunityCache? community = null) : WorkspaceObservable, IDisposable
{
    private const string AllSystems = "All systems";
    private const string ExpansionState = "Expansion";
    private CancellationTokenSource? pending;
    private string status = "Choose a reference system to search.";
    private MiningSearchPreferences options = new();
    private IReadOnlyList<MiningRing> rings = [];
    private IReadOnlyList<MiningMarketResult> markets = [];
    private IReadOnlyList<MiningSystemResult> systems = [];
    private MiningRing? selectedRing;
    private MiningMarketResult? selectedMarket;
    private MiningSystemResult? selectedSystem;
    private bool busy;
    private readonly WorkspaceTableSorter ringSorter = new();
    private readonly WorkspaceTableSorter marketSorter = new();
    private readonly WorkspaceTableSorter traderSorter = new();
    private readonly WorkspaceTableSorter systemSorter = new();
    private ICommand? sortCommand;
    public WorkspaceSortIndicators SortIndicators => new(ActiveSorter.Indicator);
    public static IReadOnlyList<string> CommodityCategories { get; } = MiningReferenceData.Commodities.Keys.ToArray();
    public IReadOnlyList<string> CommodityOptions => MiningReferenceData.Commodities.GetValueOrDefault(CommodityCategory) ?? [];
    public string CommodityCategory { get => options.CommodityCategory; set { Set(ref options, options with { CommodityCategory = value ?? "Mining" }); Changed(nameof(CommodityOptions)); } }
    public string Reference { get => options.Reference; set { Set(ref options, options with { Reference = value ?? "" }); } }
    public string Mineral { get => options.Mineral; set { Set(ref options, options with { Mineral = value ?? "Platinum" }); } }
    public string RingType { get => options.RingType; set { Set(ref options, options with { RingType = value ?? "All" }); } }
    public string Commodity { get => options.Commodity; set { Set(ref options, options with { Commodity = value ?? "Platinum" }); } }
    public double Radius { get => options.Radius; set { Set(ref options, options with { Radius = double.IsFinite(value) ? Math.Clamp(value, 1, 500) : 100 }); } }
    public int MinimumHotspots { get => options.MinimumHotspots; set { Set(ref options, options with { MinimumHotspots = Math.Clamp(value, 0, 100) }); } }
    public string Source { get => options.Source; set { Set(ref options, options with { Source = value ?? "Both" }); } }
    public bool OnlyOverlaps { get => options.OnlyOverlaps; set { Set(ref options, options with { OnlyOverlaps = value }); } }
    public bool OnlyRes { get => options.OnlyRes; set { Set(ref options, options with { OnlyRes = value }); } }
    public bool Buying { get => options.Buying; set { Set(ref options, options with { Buying = value }); } }
    public bool GalaxyWide { get => options.GalaxyWide; set { Set(ref options, options with { GalaxyWide = value }); } }
    public bool ExcludeCarriers { get => options.ExcludeCarriers; set { Set(ref options, options with { ExcludeCarriers = value }); } }
    public bool LargePads { get => options.LargePads; set { Set(ref options, options with { LargePads = value }); } }
    public int MaximumAgeDays { get => options.MaximumAgeDays; set { Set(ref options, options with { MaximumAgeDays = Math.Clamp(value, 0, 365) }); } }
    public string StationType { get => options.StationType; set { Set(ref options, options with { StationType = value ?? "" }); } }
    public string Security { get => options.Security; set { Set(ref options, options with { Security = value ?? "" }); } }
    public string Allegiance { get => options.Allegiance; set { Set(ref options, options with { Allegiance = value ?? "" }); } }
    public string Government { get => options.Government; set { Set(ref options, options with { Government = value ?? "" }); } }
    public string Economy { get => options.Economy; set { Set(ref options, options with { Economy = value ?? "" }); } }
    public string State { get => options.State; set { Set(ref options, options with { State = value ?? "" }); } }
    public string Power { get => options.Power; set { Set(ref options, options with { Power = value ?? "" }); } }
    public string PowerState { get => options.PowerState; set { Set(ref options, options with { PowerState = value ?? "" }); } }
    public long MinimumPopulation { get => options.MinimumPopulation; set { Set(ref options, options with { MinimumPopulation = Math.Max(0, value) }); } }
    public string TraderType { get => options.TraderType; set { Set(ref options, options with { TraderType = value ?? "Raw" }); } }
    private int destination;
    private int page;
    private bool systemOnly;
    private string objective = AllSystems;
    private string pledgedPower = "";
    private string planningTarget = "";
    private string planningObjective = "";
    private string miningOrigin = "";
    public bool HasPlan => planningTarget.Length > 0;
    public string PlanningContext => planningTarget.Length == 0 ? "" : $"{planningObjective} destination: {planningTarget} · Mining: {miningOrigin}";
    public void ClearPlan() { planningTarget = planningObjective = miningOrigin = ""; Changed(nameof(PlanningContext)); Changed(nameof(HasPlan)); }
    private IReadOnlyList<MiningMarketResult> traders = [];
    private MiningMarketResult? selectedTrader;
    public int Page { get => page; set => Set(ref page, Math.Max(0, value)); }
    public int Destination
    {
        get => destination;
        set
        {
            if (Set(ref destination, value)) Changed(nameof(SortIndicators));
        }
    }
    public bool SystemOnly { get => systemOnly; set => Set(ref systemOnly, value); }
    public string Objective { get => objective; set => Set(ref objective, value); }
    public string PledgedPower { get => pledgedPower; set => Set(ref pledgedPower, value ?? ""); }
    public static IReadOnlyList<string> Objectives { get; } = [AllSystems, "Reinforce", "Undermine", "Acquire"];
    public static IReadOnlyList<string> Powers { get; } = ["", "Aisling Duval", "Archon Delaine", "Arissa Lavigny-Duval", "Denton Patreus", "Edmund Mahon", "Felicia Winters", "Li Yong-Rui", "Nakato Kaine", "Pranav Antal", "Yuri Grom", "Jerome Archer", "Zemina Torval"];
    public static IReadOnlyList<string> PowerStates { get; } = ["", "Exploited", "Fortified", "Stronghold", "Unoccupied", ExpansionState, "Contested"];
    public IReadOnlyList<MiningMarketResult> Traders { get => traderSorter.Apply(traders); private set => Set(ref traders, value); }
    public MiningMarketResult? SelectedTrader { get => selectedTrader; set => Set(ref selectedTrader, value); }
    public string TradeMode { get => Buying ? "Buy supplies" : "Sell mined cargo"; set { Buying = value == "Buy supplies"; Changed(nameof(TradeMode)); } }
    public static IReadOnlyList<string> TradeModes { get; } = ["Sell mined cargo", "Buy supplies"];
    public static IReadOnlyList<string> Sources { get; } = ["Both", "Local", "Spansh"];
    public static IReadOnlyList<string> RingTypes { get; } = ["All", "Icy", "Metallic", "Metal Rich", "Rocky"];
    public static IReadOnlyList<string> TraderTypes { get; } = ["Raw", "Manufactured", "Encoded"];
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => busy; private set => Set(ref busy, value); }
    public ICommand SortCommand => sortCommand ??= new WorkspaceParameterCommand(parameter =>
    {
        ActiveSorter.Toggle(parameter);
        Changed(Destination switch
        {
            0 => nameof(Rings),
            1 => nameof(Markets),
            2 => nameof(Traders),
            _ => nameof(Systems),
        });
        Changed(nameof(SortIndicators));
    });
    public IReadOnlyList<MiningRing> Rings { get => ringSorter.Apply(rings); private set => Set(ref rings, value); }
    public IReadOnlyList<MiningMarketResult> Markets { get => marketSorter.Apply(markets); private set => Set(ref markets, value); }
    public IReadOnlyList<MiningSystemResult> Systems { get => systemSorter.Apply(systems); private set => Set(ref systems, value); }
    public MiningRing? SelectedRing { get => selectedRing; set => Set(ref selectedRing, value); }
    public MiningMarketResult? SelectedMarket { get => selectedMarket; set => Set(ref selectedMarket, value); }
    public MiningSystemResult? SelectedSystem { get => selectedSystem; set => Set(ref selectedSystem, value); }
    private WorkspaceTableSorter ActiveSorter => Destination switch
    {
        0 => ringSorter,
        1 => marketSorter,
        2 => traderSorter,
        _ => systemSorter,
    };
    public Task SearchRingsAsync() => Run(async token =>
    {
        var candidates = new List<MiningRing>();
        var offline = false;
        if (Source != "Local")
        {
            try { candidates.AddRange(await client.FindRingsAsync(new MiningRingQuery(Reference, Mineral.Trim(), RingType, Radius, MinimumHotspots, Page, SystemOnly), token)); }
            catch (Exception ex) when (Source == "Both" && IsProviderFailure(ex)) { offline = true; }
        }
        if (Source != "Spansh") await AddLocalRingsAsync(candidates, token);
        var annotated = candidates.Select(AnnotateRing)
            .Where(r => (!OnlyOverlaps || r.Overlaps.Length > 0) && (!OnlyRes || r.ResourceExtractionSites.Length > 0))
            .OrderBy(r => r.DistanceLy ?? double.MaxValue).Take(500).ToArray();
        token.ThrowIfCancellationRequested();
        Rings = annotated;
        Status = $"{annotated.Length} matching rings · {Reference} · {Source} · online page {Page + 1}.";
        if (offline) Status += " Provider unavailable; showing local results.";
    });
    private async Task AddLocalRingsAsync(List<MiningRing> candidates, CancellationToken token)
    {
        var origin = await ResolveOriginAsync(token);
        foreach (var ring in MiningReferenceData.Rings.Concat(localRings()))
        {
            var distance = RingDistance(ring, origin);
            if (distance is null || distance > Radius || !MatchesRing(ring)) continue;
            MergeRing(candidates, ring, distance.Value);
        }
    }
    private async Task<GalacticCoordinate?> ResolveOriginAsync(CancellationToken token)
    {
        var origin = community?.Position(Reference)
            ?? localRings().Concat(MiningReferenceData.Rings).FirstOrDefault(r => Same(r.System, Reference) && r.Position is not null)?.Position
            ?? bookmarks.All.FirstOrDefault(b => Same(b.System, Reference) && b.Position is not null)?.Position;
        if (origin is not null) return origin;
        try { return (await resolver.SearchAsync(Reference, token)).FirstOrDefault(s => Same(s.Name, Reference))?.Position; }
        catch (Exception ex) when (IsProviderFailure(ex)) { return null; } // Unknown coordinates must not be interpreted as zero distance.
    }
    private double? RingDistance(MiningRing ring, GalacticCoordinate? origin)
    {
        if (Same(ring.System, Reference)) return 0;
        if (SystemOnly) return null;
        return ring.Position is { } location && origin is { } point ? location.DistanceTo(point) : null;
    }
    private bool MatchesRing(MiningRing ring) =>
        (RingType == "All" || Same(ring.RingType.Replace(" ", ""), RingType.Replace(" ", "")))
        && (Mineral.Length == 0 || ring.Hotspots.Any(h => Same(h.Key.Replace(" ", ""), Mineral.Replace(" ", "")) && h.Value >= MinimumHotspots));
    private static void MergeRing(List<MiningRing> candidates, MiningRing ring, double distance)
    {
        var observed = candidates.Find(r => Same(r.System, ring.System) && Same(r.Body, ring.Body));
        if (observed is null) { candidates.Add(ring with { DistanceLy = distance }); return; }
        var newer = observed.Scanned >= ring.Scanned ? observed : ring;
        var older = ReferenceEquals(newer, observed) ? ring : observed;
        candidates.Remove(observed);
        candidates.Add(newer with
        {
            DistanceLy = distance,
            Overlaps = Prefer(newer.Overlaps, older.Overlaps),
            ResourceExtractionSites = Prefer(newer.ResourceExtractionSites, older.ResourceExtractionSites),
            Reserve = Prefer(newer.Reserve, older.Reserve),
            Position = newer.Position ?? older.Position
        });
    }
    private MiningRing AnnotateRing(MiningRing ring)
    {
        var bookmark = bookmarks.All.FirstOrDefault(b =>
            b.HasCategory(BookmarkCategoryCatalog.Mining)
            && Same(b.System, ring.System)
            && Same(b.CombinedBodyAndRing, ring.Body));
        var power = community?.Power(ring.System);
        return ring with
        {
            Overlaps = Prefer(bookmark?.Overlaps, ring.Overlaps),
            ResourceExtractionSites = Prefer(bookmark?.ResourceExtractionSites, ring.ResourceExtractionSites),
            Power = power is null ? ring.Power : $"{power.Power} · {power.State}"
        };
    }
    private static string Prefer(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static bool IsProviderFailure(Exception ex) => ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException;
    public Task SearchMarketsAsync() => Run(async token =>
    {
        var query = new MiningMarketQuery(Reference, Commodity.Trim(), Buying, Radius, GalaxyWide, ExcludeCarriers, LargePads, MaximumAgeDays, StationType.Trim(), Page, SystemOnly);
        IReadOnlyList<MiningMarketResult> result;
        var source = query.SystemOnly && !query.GalaxyWide ? "Spansh" : "Ardent";
        try { result = await client.FindMarketsAsync(query, token); }
        catch (Exception ex) when (source == "Ardent" && IsProviderFailure(ex)) { source = "Spansh fallback"; result = await client.FindSpanshMarketsAsync(query, token); }
        var origin = community is not null && !query.GalaxyWide ? await ResolveOriginAsync(token) : null;
        var merged = result.Concat(community?.Markets(query, origin, DateTimeOffset.UtcNow) ?? []).GroupBy(r => (r.System, r.Station)).Select(g => g.OrderByDescending(r => r.Updated).First());
        if (query.SystemOnly && !query.GalaxyWide) merged = merged.Where(r => Same(r.System, query.ReferenceSystem));
        result = query.Buying ? merged.OrderBy(r => r.Price).ToArray() : merged.OrderByDescending(r => r.Price).ToArray();
        token.ThrowIfCancellationRequested(); Markets = result;
        Status = $"{result.Count} markets · {source}. Prices and quantities are observations, not guarantees.";
    });
    public Task SearchSystemsAsync() => Run(async token =>
    {
        if (Objective is "Reinforce" or "Undermine" && string.IsNullOrWhiteSpace(PledgedPower))
            throw new ArgumentException("Choose your pledged Power before searching for this objective.");
        var query = new MiningSystemQuery(Reference, Radius, Security.Trim(), Allegiance.Trim(), Government.Trim(), State.Trim(), Economy.Trim(), Power.Trim(), PowerState.Trim(), MinimumPopulation, Page);
        IReadOnlyList<MiningSystemResult> online = [];
        var source = "Spansh + local Powerplay observations";
        if (PowerState is ExpansionState or "Contested") source = "Local Powerplay observations; this state is not indexed by Spansh";
        else online = await client.FindSystemsAsync(query, token);
        var local = community?.FindSystems(query, DateTimeOffset.UtcNow) ?? [];
        var result = local.Concat(online).DistinctBy(s => s.System, StringComparer.OrdinalIgnoreCase).Where(MatchesObjective).OrderBy(s => s.Distance ?? double.MaxValue).ToArray();
        token.ThrowIfCancellationRequested(); Systems = result;
        Status = $"{Systems.Count} matching systems · {source} · {Objective}. Unknown Powerplay data is not treated as eligible.";
    });
    public Task SearchTradersAsync() => Run(async token =>
    {
        var result = await client.FindTradersAsync(Reference, TraderType, Radius, Page, token);
        token.ThrowIfCancellationRequested(); Traders = result;
        Status = $"{result.Count} {TraderType.ToLowerInvariant()} material traders · Spansh.";
    });
    public void Bookmark()
    {
        if (SelectedRing is not { } ring) return;
        bookmarks.AddMiningLocation(new GalacticBookmark
        {
            System = ring.System,
            Body = ring.Body,
            Position = ring.Position,
            Category = BookmarkCategoryCatalog.Mining,
            CategoryAssignments = [BookmarkCategoryCatalog.Mining],
            Minerals = ring.Minerals,
            RingType = ring.RingType,
            Reserve = ring.Reserve,
            Overlaps = ring.Overlaps,
            ResourceExtractionSites = ring.ResourceExtractionSites,
        });
        Status = bookmarks.Status;
    }
    public void CacheSelectedRing() { if (SelectedRing is { } ring) { cacheRing(ring); Status = "Ring saved to your local discoveries."; } }
    public void UseSelectedSystem()
    {
        if (SelectedSystem is not { } selected) return;
        Reference = selected.System; SystemOnly = true; Page = 0; Destination = 0;
        planningTarget = Objective == AllSystems ? "" : selected.System;
        planningObjective = Objective; miningOrigin = selected.System; Changed(nameof(PlanningContext)); Changed(nameof(HasPlan));
    }
    public async Task FindSelectedSystemRingsAsync()
    {
        if (SelectedSystem is null) return;
        UseSelectedSystem(); await SearchRingsAsync();
    }
    public async Task FindSellingStationsAsync()
    {
        if (SelectedRing is not { } ring) return;
        miningOrigin = ring.System;
        var hasPlan = planningTarget.Length > 0 && planningObjective == Objective;
        Reference = hasPlan && Objective == "Acquire" ? planningTarget : ring.System;
        SystemOnly = hasPlan;
        Changed(nameof(PlanningContext)); Changed(nameof(HasPlan));
        if (Mineral.Length > 0) Commodity = Mineral;
        Buying = false; Changed(nameof(TradeMode)); GalaxyWide = false; Page = 0; Destination = 1;
        await SearchMarketsAsync();
    }
    private bool MatchesObjective(MiningSystemResult system) => Objective switch
    {
        "Reinforce" => !Same(system.PowerState, ExpansionState) && system.Power.Length > 0 && Same(system.Power, PledgedPower),
        "Undermine" => !Same(system.PowerState, ExpansionState) && system.Power.Length > 0 && !Same(system.Power, PledgedPower),
        "Acquire" => system.PowerState is "Unoccupied" or ExpansionState,
        _ => true
    };
    public MiningSearchPreferences SaveOptions() => options;
    public void LoadOptions(MiningSearchPreferences values)
    {
        pending?.Cancel(); pending = null; IsBusy = false;
        Rings = []; Markets = []; Systems = []; Traders = []; SelectedTrader = null; SystemOnly = false; Objective = AllSystems; PledgedPower = ""; SelectedRing = null; SelectedMarket = null; SelectedSystem = null;
        ClearPlan();
        CommodityCategory = values.CommodityCategory;
        Reference = values.Reference;
        Mineral = values.Mineral;
        RingType = values.RingType;
        Commodity = values.Commodity;
        Radius = values.Radius;
        MinimumHotspots = values.MinimumHotspots;
        Source = values.Source;
        OnlyOverlaps = values.OnlyOverlaps;
        OnlyRes = values.OnlyRes;
        Buying = values.Buying;
        GalaxyWide = values.GalaxyWide;
        ExcludeCarriers = values.ExcludeCarriers;
        LargePads = values.LargePads;
        MaximumAgeDays = values.MaximumAgeDays;
        StationType = values.StationType;
        Security = values.Security;
        Allegiance = values.Allegiance;
        Government = values.Government;
        Economy = values.Economy;
        State = values.State;
        Power = values.Power;
        PowerState = values.PowerState;
        MinimumPopulation = values.MinimumPopulation;
        TraderType = values.TraderType;
        Page = 0;
    }
    public void Cancel() => pending?.Cancel();
    public void Dispose() { pending?.Cancel(); pending?.Dispose(); pending = null; }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        var previous = pending;
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        pending = current; IsBusy = true; Status = "Searching…";
        var token = current.Token;
        try
        {
            if (previous is not null) await previous.CancelAsync();
            token.ThrowIfCancellationRequested();
            await action(token);
        }
        catch (OperationCanceledException) { if (pending == current) Status = "Search canceled or timed out."; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException or IOException or InvalidDataException)
        { if (pending == current) Status = "Search unavailable: " + ex.Message; }
        finally { if (pending == current) { pending = null; IsBusy = false; } }
    }
}
