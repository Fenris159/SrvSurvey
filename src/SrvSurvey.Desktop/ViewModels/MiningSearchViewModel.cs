using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningSearchViewModel(MiningSearchClient client, BookmarksViewModel bookmarks, Action<MiningRing> cacheRing, Func<IReadOnlyList<MiningRing>> localRings, IStarSystemResolver resolver, MiningCommunityCache? community = null) : WorkspaceObservable, IDisposable
{
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
    public IReadOnlyList<string> CommodityCategories => MiningReferenceData.Commodities.Keys.ToArray();
    public IReadOnlyList<string> CommodityOptions => MiningReferenceData.Commodities.GetValueOrDefault(CommodityCategory) ?? [];
    public string CommodityCategory { get => options.CommodityCategory; set { Set(ref options, options with { CommodityCategory = value ?? "Mining" }); Changed(nameof(CommodityOptions)); } }
    public string Reference { get => options.Reference; set { Set(ref options, options with { Reference = value ?? "" }); } }
    public string Mineral { get => options.Mineral; set { Set(ref options, options with { Mineral = value ?? "Platinum" }); } }
    public string RingType { get => options.RingType; set { Set(ref options, options with { RingType = value ?? "All" }); } }
    public string Commodity { get => options.Commodity; set { Set(ref options, options with { Commodity = value ?? "Platinum" }); } }
    public double Radius { get => options.Radius; set { Set(ref options, options with { Radius = double.IsFinite(value) ? Math.Clamp(value, 1, 100000) : 100 }); } }
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
    public int Page { get; set; }
    public static IReadOnlyList<string> Sources { get; } = ["Both", "Local", "Spansh"];
    public static IReadOnlyList<string> RingTypes { get; } = ["All", "Icy", "Metallic", "Metal Rich", "Rocky"];
    public static IReadOnlyList<string> TraderTypes { get; } = ["Raw", "Manufactured", "Encoded"];
    public string Status { get => status; private set => Set(ref status, value); }
    public bool IsBusy { get => busy; private set => Set(ref busy, value); }
    public IReadOnlyList<MiningRing> Rings { get => rings; private set => Set(ref rings, value); }
    public IReadOnlyList<MiningMarketResult> Markets { get => markets; private set => Set(ref markets, value); }
    public IReadOnlyList<MiningSystemResult> Systems { get => systems; private set => Set(ref systems, value); }
    public MiningRing? SelectedRing { get => selectedRing; set => Set(ref selectedRing, value); }
    public MiningMarketResult? SelectedMarket { get => selectedMarket; set => Set(ref selectedMarket, value); }
    public MiningSystemResult? SelectedSystem { get => selectedSystem; set => Set(ref selectedSystem, value); }
    public Task SearchRingsAsync() => Run(async token =>
    {
        var candidates = new List<MiningRing>();
        var offline = false;
        if (Source != "Local")
        {
            try { candidates.AddRange(await client.FindRingsAsync(new MiningRingQuery(Reference, Mineral.Trim(), RingType, Radius, MinimumHotspots, Page), token)); }
            catch (HttpRequestException) when (Source == "Both") { offline = true; }
        }
        if (Source != "Spansh")
        {
            var localPosition = localRings().Concat(MiningReferenceData.Rings).FirstOrDefault(r => r.System.Equals(Reference, StringComparison.OrdinalIgnoreCase) && r.Position is not null)?.Position
                ?? bookmarks.All.FirstOrDefault(b => b.System.Equals(Reference, StringComparison.OrdinalIgnoreCase) && b.Position is not null)?.Position;
            if (localPosition is null)
            {
                try { localPosition = (await resolver.SearchAsync(Reference, token)).FirstOrDefault(s => s.Name.Equals(Reference, StringComparison.OrdinalIgnoreCase))?.Position; }
                catch (HttpRequestException) { offline = true; }
            }
            foreach (var ring in MiningReferenceData.Rings.Concat(localRings()))
            {
                var distance = ring.System.Equals(Reference, StringComparison.OrdinalIgnoreCase) ? 0 : ring.Position is { } location && localPosition is { } origin ? location.DistanceTo(origin) : (double?)null;
                if (distance is null || distance > Radius) continue;
                if (RingType != "All" && !ring.RingType.Replace(" ", "").Equals(RingType.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) continue;
                if (Mineral.Length > 0 && !ring.Hotspots.Any(h => h.Key.Replace(" ", "").Equals(Mineral.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) && h.Value >= MinimumHotspots)) continue;
                var observed = candidates.Find(r => r.System == ring.System && r.Body == ring.Body);
                if (observed is null) candidates.Add(ring with { DistanceLy = distance });
                else
                {
                    var newer = observed.Scanned >= ring.Scanned ? observed : ring;
                    var older = ReferenceEquals(newer, observed) ? ring : observed;
                    candidates.Remove(observed);
                    candidates.Add(newer with
                    {
                        DistanceLy = distance,
                        Overlaps = newer.Overlaps.Length > 0 ? newer.Overlaps : older.Overlaps,
                        ResourceExtractionSites = newer.ResourceExtractionSites.Length > 0 ? newer.ResourceExtractionSites : older.ResourceExtractionSites,
                        Reserve = newer.Reserve.Length > 0 ? newer.Reserve : older.Reserve,
                        Position = newer.Position ?? older.Position
                    });
                }
            }
        }
        var annotated = candidates.Select(r =>
        {
            var bookmark = bookmarks.All.FirstOrDefault(b => b.System.Equals(r.System, StringComparison.OrdinalIgnoreCase) && b.Body.Equals(r.Body, StringComparison.OrdinalIgnoreCase));
            return r with { Overlaps = !string.IsNullOrWhiteSpace(bookmark?.Overlaps) ? bookmark.Overlaps : r.Overlaps, ResourceExtractionSites = !string.IsNullOrWhiteSpace(bookmark?.ResourceExtractionSites) ? bookmark.ResourceExtractionSites : r.ResourceExtractionSites, Power = community?.Power(r.System) is { } power ? $"{power.Power} · {power.State}" : r.Power };
        }).Where(r => (!OnlyOverlaps || r.Overlaps.Length > 0) && (!OnlyRes || r.ResourceExtractionSites.Length > 0)).OrderBy(r => r.DistanceLy ?? double.MaxValue).Take(500).ToArray();
        token.ThrowIfCancellationRequested(); Rings = annotated;
        Status = $"{annotated.Length} matching rings · {Source} · online page {Page + 1}{(offline ? " · Spansh unavailable; local results only" : "")}.";
    });
    public Task SearchMarketsAsync() => Run(async token =>
    {
        var query = new MiningMarketQuery(Reference, Commodity.Trim(), Buying, Radius, GalaxyWide, ExcludeCarriers, LargePads, MaximumAgeDays, StationType.Trim(), Page);
        IReadOnlyList<MiningMarketResult> result;
        var source = "Ardent";
        try { result = await client.FindMarketsAsync(query, token); }
        catch (HttpRequestException) { source = "Spansh fallback"; result = await client.FindSpanshMarketsAsync(query, token); }
        var origin = community?.Position(query.ReferenceSystem) ?? localRings().FirstOrDefault(r => r.System.Equals(query.ReferenceSystem, StringComparison.OrdinalIgnoreCase))?.Position;
        if (community is not null && origin is null && !query.GalaxyWide)
        {
            try { origin = (await resolver.SearchAsync(query.ReferenceSystem, token)).FirstOrDefault(r => r.Name.Equals(query.ReferenceSystem, StringComparison.OrdinalIgnoreCase))?.Position; }
            catch (HttpRequestException) { }
        }
        var merged = result.Concat(community?.Markets(query, origin, DateTimeOffset.UtcNow) ?? []).GroupBy(r => (r.System, r.Station)).Select(g => g.OrderByDescending(r => r.Updated).First());
        result = query.Buying ? merged.OrderBy(r => r.Price).ToArray() : merged.OrderByDescending(r => r.Price).ToArray();
        token.ThrowIfCancellationRequested(); Markets = result;
        Status = $"{result.Count} markets · {source}. Prices and quantities are observations, not guarantees.";
    });
    public Task SearchSystemsAsync() => Run(async token =>
    {
        var result = await client.FindSystemsAsync(new MiningSystemQuery(Reference, Radius, Security.Trim(), Allegiance.Trim(), Government.Trim(), State.Trim(), Economy.Trim(), Power.Trim(), PowerState.Trim(), MinimumPopulation), token);
        token.ThrowIfCancellationRequested(); Systems = result;
        Status = $"{result.Count} nearby systems · Spansh. Blank fields mean the provider has no value.";
    });
    public Task SearchTradersAsync() => Run(async token =>
    {
        var result = await client.FindTradersAsync(Reference, TraderType, token);
        token.ThrowIfCancellationRequested(); Markets = result;
        Status = $"{result.Count} {TraderType.ToLowerInvariant()} material traders · Spansh.";
    });
    public void Bookmark()
    {
        if (SelectedRing is not { } ring) return;
        bookmarks.AddMiningLocation(new GalacticBookmark { System = ring.System, Body = ring.Body, Position = ring.Position, Minerals = ring.Minerals, RingType = ring.RingType, Reserve = ring.Reserve, Overlaps = ring.Overlaps, ResourceExtractionSites = ring.ResourceExtractionSites });
        Status = bookmarks.Status;
    }
    public void CacheSelectedRing() { if (SelectedRing is { } ring) { cacheRing(ring); Status = "Ring saved to your local discoveries."; } }
    public void UseSelectedSystem() { if (SelectedSystem is { } s) Reference = s.System; }
    public MiningSearchPreferences SaveOptions() => options;
    public void LoadOptions(MiningSearchPreferences values)
    {
        pending?.Cancel(); pending = null; IsBusy = false;
        Rings = []; Markets = []; Systems = []; SelectedRing = null; SelectedMarket = null; SelectedSystem = null;
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
        pending?.Cancel();
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        pending = current; IsBusy = true; Status = "Searching…";
        try { await action(current.Token); }
        catch (OperationCanceledException) { if (pending == current) Status = "Search canceled or timed out."; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { if (pending == current) Status = "Search unavailable: " + ex.Message; }
        finally { if (pending == current) { pending = null; IsBusy = false; } }
    }
}
