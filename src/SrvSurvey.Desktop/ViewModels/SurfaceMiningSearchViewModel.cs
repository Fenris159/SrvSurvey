using System.ComponentModel;
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

public sealed class SurfaceBodyTag : WorkspaceObservable
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
    private bool visible = true;
    public bool IsVisible
    {
        get => visible;
        private set => Set(ref visible, value);
    }

    public void SetHideIrrelevant(bool hide) => IsVisible = !hide || MatchesStation;

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
        Details = details;
        StationScores = options?.StationScores?.ToArray() ?? [bestViablePrice];
        PowerState = details?.PowerState ?? "";
        FactionState = details?.FactionState ?? "";
        Powers = details?.Powers.Count > 0 ? string.Join("\n", details.Powers) : "";
        PowerLines = PowerplayPowerLineViewModel.From(
            details?.Powers ?? [],
            details?.Conflict ?? [],
            details?.ControllingPower ?? "",
            details?.ControlProgress
        );
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
    public SurfaceSellSystemDetails? Details { get; }
    public IReadOnlyList<long> StationScores { get; }
    public string FactionState { get; }
    public string Powers { get; }
    public IReadOnlyList<PowerplayPowerLineViewModel> PowerLines { get; }
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

public sealed record SurfaceSellRowOptions(IReadOnlyList<long>? StationScores, SurfaceSellSystemDetails? Details);

internal static class MiningSearchStatusText
{
    public static string Saved(string status, DateTimeOffset? savedAt) =>
        savedAt is { } saved
            ? status
                + " Saved "
                + saved.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                + "; press Search to refresh."
            : status;
}

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

    private sealed class SearchProgress(Action<SurfaceSellSearchProgress> report) : IProgress<SurfaceSellSearchProgress>
    {
        public void Report(SurfaceSellSearchProgress value) => report(value);
    }

    private const string RequestFailed = "Request failed. Try again.";
    private const string IdleStatus = "Choose a reference system and a surface material.";
    private readonly MiningSearchClient client;
    private readonly MiningSearchSession<SurfaceMiningSearchSnapshot> session;
    private readonly SurfaceSellSearch sellSearch;
    private readonly int maximumResults;
    private string reference = "";
    private bool forceIncludeReference;
    private double radius = 100;
    private double mineSellRadius = 50;
    private int resultLimit = 1;
    private string padSize = "Any";
    private long minimumDemand;
    private long maximumDemand = 90_000;
    private string status = IdleStatus;
    private bool nearestFirst = true;
    private bool hideIrrelevantMaterialTags;
    private SellRowSort sellRowSort;
    private IReadOnlyList<SurfaceSellRowViewModel> rows = [];
    private IReadOnlyList<PowerplayAcquireClusterViewModel> acquireClusters = [];

    public SurfaceMiningSearchViewModel(MiningSearchClient client, int maximumResults = 5)
    {
        this.client = client;
        this.maximumResults = maximumResults;
        session = new MiningSearchSession<SurfaceMiningSearchSnapshot>(client, "surface", CacheFilters);
        session.BusyChanged += () => Changed(nameof(IsBusy));
        sellSearch = new SurfaceSellSearch(client);
        SearchCommand = new WorkspaceCommand(() => _ = SearchAsync(CancellationToken.None));
        ResetCommand = new WorkspaceCommand(Reset);
        CancelCommand = new WorkspaceCommand(Cancel);
        DistanceSortCommand = new WorkspaceCommand(ToggleDistanceSort);
        SellDistanceSortCommand = new WorkspaceCommand(ToggleSellDistanceSort);
        BestStationSortCommand = new WorkspaceCommand(ToggleBestStationSort);
        PropertyChanged += RestoreWhenFiltersChange;
        Materials.Selected.CollectionChanged += (_, _) => TryRestoreCachedResults();
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
    public ICommand ResetCommand { get; }
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
            string next = session.Reference.Edit(value, out bool refilled);
            if (!Set(ref reference, next) && refilled)
            {
                Changed(nameof(Reference));
            }
        }
    }

    public bool ForceIncludeReference
    {
        get => forceIncludeReference;
        set => Set(ref forceIncludeReference, value);
    }

    public bool HideIrrelevantMaterialTags
    {
        get => hideIrrelevantMaterialTags;
        set
        {
            if (Set(ref hideIrrelevantMaterialTags, value))
            {
                ApplyTagVisibility();
                HideIrrelevantTagsChanged?.Invoke(value);
            }
        }
    }

    public Action<bool>? HideIrrelevantTagsChanged { get; set; }

    private void ApplyTagVisibility()
    {
        foreach (
            SurfaceBodyTag tag in Rows.SelectMany(row => row.Systems)
                .SelectMany(system => system.Bodies)
                .SelectMany(body => body.Tags)
        )
        {
            tag.SetHideIrrelevant(HideIrrelevantMaterialTags);
        }
        foreach (PowerplayAcquireClusterViewModel cluster in acquireClusters)
        {
            cluster.SetHideIrrelevant(HideIrrelevantMaterialTags);
        }
    }

    public void Reset() =>
        session.Restore(() =>
        {
            session.Abandon();
            Materials.Selected.Clear();
            ForceIncludeReference = false;
            session.Reference.Unpin();
            Radius = 100;
            ResultLimit = 1;
            MineSellRadius = 50;
            PadSize = "Any";
            MinimumDemand = 0;
            MaximumDemand = 90_000;
            Reference = session.Reference.CurrentSystem;
            Rows = [];
            Status = IdleStatus;
        });

    public void ClearResults()
    {
        Rows = [];
        Status = IdleStatus;
    }

    public void ConfigureCache(IMiningSearchResultStore cache, string workspace = "surface", bool restoreLast = true)
    {
        session.Store?.HideIrrelevantMaterialTagsChanged -= SyncHideIrrelevantTags;
        session.UseStore(cache, workspace);
        cache.HideIrrelevantMaterialTagsChanged += SyncHideIrrelevantTags;
        HideIrrelevantTagsChanged = value => cache.HideIrrelevantMaterialTags = value;
        HideIrrelevantMaterialTags = cache.HideIrrelevantMaterialTags;
        if (restoreLast && session.LoadLast() is { } last)
        {
            RestoreSnapshot(last, restoreFilters: true);
            if (workspace == "surface" && last.SurfaceSearchVersion != 1)
            {
                ClearResults();
            }
        }
    }

    private void SyncHideIrrelevantTags(bool value) => HideIrrelevantMaterialTags = value;

    public SurfaceMiningSearchSnapshot ExportSnapshot() =>
        new(
            Reference,
            Radius,
            ResultLimit,
            Materials.Selected.ToArray(),
            PadSize,
            MinimumDemand,
            MaximumDemand,
            Status,
            Rows.Select(SurfaceSellSnapshot.From).ToArray()
        )
        {
            SavedAt = DateTimeOffset.UtcNow,
            ForceIncludeReference = ForceIncludeReference,
            MineSellRadius = MineSellRadius,
            SurfaceSearchVersion = 1,
        };

    public void RestoreSnapshot(SurfaceMiningSearchSnapshot snapshot, bool restoreFilters) =>
        session.Restore(() =>
        {
            try
            {
                if (restoreFilters)
                {
                    Reference = snapshot.Reference;
                    ForceIncludeReference = snapshot.ForceIncludeReference;
                    Radius = snapshot.Radius;
                    MineSellRadius = snapshot.MineSellRadius;
                    ResultLimit = snapshot.ResultLimit;
                    Materials.Selected.Clear();
                    foreach (string material in snapshot.Materials)
                    {
                        Materials.Selected.Add(material);
                    }
                    PadSize = snapshot.PadSize;
                    MinimumDemand = snapshot.MinimumDemand;
                    MaximumDemand = snapshot.MaximumDemand;
                }

                Rows = snapshot.Rows.Select(row => row.Restore()).ToArray();
                Status = MiningSearchStatusText.Saved(snapshot.Status, snapshot.SavedAt);
            }
            finally
            {
                if (restoreFilters)
                {
                    session.Reference.Pin();
                }
            }
        });

    private object CacheFilters() =>
        new
        {
            Reference = Reference.Trim().ToUpperInvariant(),
            ForceIncludeReference,
            Radius,
            MineSellRadius,
            ResultLimit,
            Materials = Materials.Selected.OrderBy(material => material, StringComparer.OrdinalIgnoreCase).ToArray(),
            PadSize,
            MinimumDemand,
            MaximumDemand,
            MaximumAge,
        };

    private void RestoreWhenFiltersChange(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName
            is nameof(Reference)
                or nameof(ForceIncludeReference)
                or nameof(Radius)
                or nameof(MineSellRadius)
                or nameof(ResultLimit)
                or nameof(PadSize)
                or nameof(MinimumDemand)
                or nameof(MaximumDemand)
        )
        {
            TryRestoreCachedResults();
        }
    }

    private void TryRestoreCachedResults() =>
        session.RestoreSaved(
            saved => RestoreSnapshot(saved, restoreFilters: false),
            () =>
            {
                Rows = [];
                Status = IdleStatus;
            }
        );

    public void UpdateCurrentLocation(string? system)
    {
        if (session.Reference.Move(system, Reference, out string next))
        {
            Reference = next;
        }
    }

    public double Radius
    {
        get => radius;
        set
        {
            if (Set(ref radius, value))
            {
                Changed(nameof(DistanceWarning));
                Changed(nameof(HasDistanceWarning));
            }
        }
    }

    public string DistanceWarning => MiningDistanceWarning.For(Radius);
    public bool HasDistanceWarning => DistanceWarning.Length > 0;

    public double MineSellRadius
    {
        get => mineSellRadius;
        set => Set(ref mineSellRadius, Math.Clamp(value, 1, 500));
    }

    public int ResultLimit
    {
        get => resultLimit;
        set => Set(ref resultLimit, Math.Clamp(value, 1, maximumResults));
    }

    public TimeSpan? MaximumAge { get; set; } = TimeSpan.FromDays(2);

    public IReadOnlyList<string> BodyControllingPowers { get; set; } = [];

    public double? BodySearchRadius { get; set; }

    public SurfaceSellMarketRules SellMarketRules { get; set; } = SurfaceSellMarketRules.Open;

    public bool GroupStationsBySystem { get; set; }

    public bool MarketGalaxyWide { get; set; }

    public bool ExcludeCarrierMarkets { get; set; }

    public bool UseAdditionalMarketsOnly { get; set; }

    public bool DefaultSellDistanceSort { get; set; }

    public string NoSellStationsMessage { get; set; } =
        "No sell station matches the selected material, demand, and landing pad within the distance.";

    public string NoMatchingBodiesMessage { get; set; } =
        "No sell station has a matching surface mining body within the mine–sell distance.";

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

    public bool IsBusy => session.IsBusy;

    public IReadOnlyList<SurfaceSellRowViewModel> Rows
    {
        get => rows;
        private set
        {
            if (Set(ref rows, value))
            {
                acquireClusters = PowerplayAcquireClusterViewModel.Group(
                    rows,
                    SellDistanceSortCommand,
                    BestStationSortCommand,
                    SellDistanceSortIndicator,
                    BestStationSortIndicator
                );
                ApplyTagVisibility();
                Changed(nameof(HasRows));
                Changed(nameof(AcquireClusters));
            }
        }
    }

    public bool HasRows => Rows.Count > 0;
    public IReadOnlyList<PowerplayAcquireClusterViewModel> AcquireClusters => acquireClusters;

    public void UseDiagnosticLog(Action<string>? log) => client.DiagnosticLog = log;

    public Task SearchAsync(CancellationToken cancellationToken = default)
    {
        SurfaceSellSearchRequest request = SearchRequest();
        bool defaultDistanceSort = DefaultSellDistanceSort;
        return session.RunAsync(
            async token =>
            {
                string result = await FindSurfaceSalesAsync(request, defaultDistanceSort, token);
                session.Publish(() => Status = result, token);
            },
            ReportSearch,
            cancellationToken: cancellationToken
        );
    }

    private void ReportSearch(MiningSearchOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case MiningSearchOutcomeKind.Started:
                Status = "Searching…";
                break;
            case MiningSearchOutcomeKind.Completed:
                if (!Status.Contains(RequestFailed, StringComparison.Ordinal))
                {
                    session.Save(ExportSnapshot());
                }
                break;
            case MiningSearchOutcomeKind.Canceled:
                Status = "Search canceled.";
                break;
            case MiningSearchOutcomeKind.Failed:
                Status = Rows.Count > 0 ? "Showing partial results. Request failed. Try again." : RequestFailed;
                break;
            default:
                Status = outcome.Message;
                break;
        }
    }

    private async Task<string> FindSurfaceSalesAsync(
        SurfaceSellSearchRequest request,
        bool defaultDistanceSort,
        CancellationToken token
    )
    {
        var presented = new Dictionary<SurfaceSellMatch, SurfaceSellRowViewModel>(ReferenceEqualityComparer.Instance);
        SurfaceSellSearchResult result = await sellSearch.FindAsync(
            request,
            new SearchProgress(progress =>
                session.Publish(() => ShowProgress(progress, presented, defaultDistanceSort), token)
            ),
            token
        );
        switch (result.Kind)
        {
            case SurfaceSellSearchResultKind.NoReference:
                session.Publish(() => Rows = [], token);
                return IdleStatus;
            case SurfaceSellSearchResultKind.NoMaterial:
                session.Publish(() => Rows = [], token);
                return "Choose a surface material.";
            case SurfaceSellSearchResultKind.NoSellStations:
                return NoSellStationsMessage;
            case SurfaceSellSearchResultKind.NoMatchingBodies:
                return NoMatchingBodiesMessage;
            default:
                return ResultMessage(result, request);
        }
    }

    private SurfaceSellSearchRequest SearchRequest() =>
        new(Reference, Materials.Selected.ToArray())
        {
            ForceIncludeReference = ForceIncludeReference,
            Radius = Radius,
            MineSellRadius = MineSellRadius,
            ResultLimit = ResultLimit,
            PadSize = PadSize,
            MinimumDemand = MinimumDemand,
            MaximumDemand = MaximumDemand,
            MaximumAge = MaximumAge,
            BodyControllingPowers = BodyControllingPowers.ToArray(),
            BodySearchRadius = BodySearchRadius,
            GroupStationsBySystem = GroupStationsBySystem,
            MarketGalaxyWide = MarketGalaxyWide,
            ExcludeCarrierMarkets = ExcludeCarrierMarkets,
            UseAdditionalMarketsOnly = UseAdditionalMarketsOnly,
            Rules = SellMarketRules,
        };

    private void ShowProgress(
        SurfaceSellSearchProgress progress,
        Dictionary<SurfaceSellMatch, SurfaceSellRowViewModel> presented,
        bool defaultDistanceSort
    )
    {
        switch (progress.Stage)
        {
            case SurfaceSellSearchStage.Ranking:
                Rows = [];
                sellRowSort =
                    defaultDistanceSort || progress.ProximityFirst ? SellRowSort.DistanceAscending : SellRowSort.Value;
                Changed(nameof(SellDistanceSortIndicator));
                Changed(nameof(BestStationSortIndicator));
                break;
            case SurfaceSellSearchStage.CheckingEligibility:
                Status = "Checking sell-system eligibility…";
                break;
            case SurfaceSellSearchStage.CheckingSellSystem:
                Status = $"Checking sell systems {progress.Index + 1}/{progress.Count}…";
                break;
            default:
                Rows = SortRows(
                    progress.Matches.Select(match =>
                    {
                        if (!presented.TryGetValue(match, out SurfaceSellRowViewModel? row))
                        {
                            row = Describe(match);
                            presented.Add(match, row);
                        }

                        return row;
                    })
                );
                break;
        }
    }

    private string ResultMessage(SurfaceSellSearchResult result, SurfaceSellSearchRequest request)
    {
        int bodyCount = result.Matches.Sum(match => match.Bodies.Count);
        string rankingDescription = result.CatalogOrder
            ? " sell systems, daily commodity value order. Best sell from "
            : " sell systems, best viable price first. Best sell from ";
        if (result.ProximityFirst)
        {
            rankingDescription = " nearby sell systems with short mining loops. Best sell from ";
        }

        return bodyCount
            + " landable bodies for "
            + (
                MiningMaterialSelection.IsAny(request.SelectedMaterials)
                    ? "Any surface material"
                    : string.Join(", ", result.Materials)
            )
            + ". "
            + result.Matches.Count
            + rankingDescription
            + (result.UsedFallback ? "Ardent/Spansh fallback" : "Ardent")
            + "."
            + (client.PriceMarksUnavailable ? " " + RequestFailed : "");
    }

    public void Cancel() => session.Cancel();

    public void Dispose()
    {
        session.Store?.HideIrrelevantMaterialTagsChanged -= SyncHideIrrelevantTags;
        session.Dispose();
    }

    private static AcquireStationViewModel StationFor(SurfaceSellStation station) =>
        new(
            station.Market.Station,
            station.Market.PadDescription,
            station.Market.ArrivalLs is { } arrival
                ? "Distance: " + arrival.ToString("0", CultureInfo.CurrentCulture) + " ls"
                : "",
            "",
            station
                .Quotes.Select(quote =>
                {
                    string code = MiningCommodityCode.Abbreviate(quote.Commodity);
                    return new AcquireQuoteViewModel(
                        code,
                        quote.Price.ToString("N0", CultureInfo.CurrentCulture)
                            + " CR "
                            + MiningPriceMarks.For(
                                quote.Price,
                                station.AverageSellPrices.TryGetValue(quote.Commodity, out long average) ? average : 0
                            ),
                        quote.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand",
                        station.IsUnavailable(code),
                        true
                    );
                })
                .ToArray()
        )
        {
            CanToggle = station.CanToggle,
        };

    private SurfaceSellRowViewModel Describe(SurfaceSellMatch match)
    {
        SurfaceMiningSystemRowViewModel[] systems = match
            .Bodies.GroupBy(body => body.Body.System, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SurfaceMiningSystemRowViewModel(
                group.Key,
                group.First().Body.DistanceLy ?? double.MaxValue,
                group
                    .Select(body => new SurfaceBodyLine(body.Codes, BodyDetails(body.Body), match.StationCodes))
                    .ToArray()
            ))
            .ToArray();
        var row = new SurfaceSellRowViewModel(
            match.System,
            match.DistanceLy is { } distance ? distance.ToString("0", CultureInfo.CurrentCulture) + " ly" : "",
            match.Stations.Select(StationFor).ToArray(),
            systems,
            match.ReferenceDistanceLy,
            match.BestViablePrice,
            new SurfaceSellRowOptions(match.StationScores, match.Details)
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
