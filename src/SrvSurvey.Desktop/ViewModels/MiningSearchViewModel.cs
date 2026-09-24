using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Mining;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningSearchViewModel(
    MiningSearchClient client,
    BookmarksViewModel bookmarks,
    Action<MiningRing> cacheRing,
    Func<IReadOnlyList<MiningRing>> localRings,
    IStarSystemResolver resolver,
    MiningCommunityCache? community = null
) : WorkspaceObservable, IDisposable
{
    private sealed class AcquireMarketCursor(MiningSystemResult[] supporters, GalacticCoordinate? origin)
    {
        public MiningSystemResult[] Supporters { get; } = supporters;
        public GalacticCoordinate? Origin { get; } = origin;
        public Queue<(MiningSystemResult Target, MiningSystemResult Supporter, bool FromCache)> Targets { get; } =
            new();
        public HashSet<string> Seen { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int SupporterIndex { get; set; }
        public MiningSystemResult? CurrentSupporter { get; set; }
        public int BubblePage { get; set; }
        public bool ReferenceChecked { get; set; }
    }

    private const string AllSystems = "All systems";
    private const string PlatinumMineral = "Platinum";
    private const string ReinforceObjective = "Reinforce";
    private const string UndermineObjective = "Undermine";
    private const string AcquireObjective = "Acquire";
    private const string ExpansionState = "Expansion";
    private const string AnyPower = "Any";
    private const string NoPower = "None";
    private const string RequestFailed = "Request failed. Try again.";
    private const string UnknownState = "Unknown";
    private const string SpanshSource = "Spansh";
    private const string HotspotRing = "Hotspots";
    private const string WithoutHotspotsRing = "Without Hotspots";
    private const string IdleStatus = "Choose a reference system to search.";
    private MiningSearchResultCache? resultCache;
    private bool restoringCachedSearch;
    private bool restoredPowerplaySearch;
    private bool preserveRestoredReference;
    private bool preserveRestoredPower;
    private bool planetaryStatusHooked;
    public SurfaceMiningSearchViewModel PlanetarySearch { get; } = new(client, 30);
    private CancellationTokenSource? pending;
    private string status = IdleStatus;
    private MiningSearchPreferences options = new();
    private IReadOnlyList<MiningRing> rings = [];
    private IReadOnlyList<MiningMarketResult> markets = [];
    private IReadOnlyList<MiningSystemResult> systems = [];
    private IReadOnlyList<PlatinumSpotRowViewModel> platinumSpots = [];
    private MiningRing? selectedRing;
    private MiningMarketResult? selectedMarket;
    private MiningSystemResult? selectedSystem;
    private PlatinumSpotRowViewModel? selectedPlatinumSpot;
    private bool busy;
    private readonly WorkspaceTableSorter ringSorter = new();
    private readonly WorkspaceTableSorter marketSorter = new();
    private readonly WorkspaceTableSorter traderSorter = new();
    private readonly WorkspaceTableSorter systemSorter = new();
    private readonly WorkspaceTableSorter platinumSorter = new();
    private ICommand? sortCommand;
    public WorkspaceSortIndicators SortIndicators => new(ActiveSorter.Indicator);
    private const string PlanetaryCommodityCategory = "Planetary Mining";
    private const string MiningCommodityCategory = "Mining";
    private const int MaximumMarketCommodities = 5;
    public static IReadOnlyList<string> MarketStationTypeOptions { get; } =
    [
        "Coriolis",
        "Orbis",
        "Ocellus",
        "Outpost",
        "CraterPort",
        "CraterOutpost",
        "SurfaceStation",
        "AsteroidBase",
        "MegaShip",
        "Bernal",
        "Dodec",
        "OnFootSettlement",
        "FleetCarrier",
        "StrongholdCarrier",
        "PlanetaryConstructionDepot",
        "SpaceConstructionDepot",
    ];
    private bool marketChipsHooked;
    private bool syncingMarketChips;
    public static IReadOnlyList<string> CommodityCategories { get; } =
    [
        MiningCommodityCategory,
        PlanetaryCommodityCategory,
        .. MiningReferenceData.Commodities.Keys.Where(category => category != MiningCommodityCategory),
    ];
    public IReadOnlyList<string> CommodityOptions =>
        CommodityCategory == PlanetaryCommodityCategory
            ? PlanetaryMiningPlan.Materials
            : MiningReferenceData.Commodities.GetValueOrDefault(CommodityCategory) ?? [];
    private readonly MiningChipBoxViewModel marketCategoryChips = new(
        "Commodity category",
        CommodityCategories,
        MiningCommodityCategory,
        options: new(MaximumSelections: 1, ShowFullNames: true)
    );
    private readonly MiningChipBoxViewModel marketCommodityChips = new(
        "Commodities",
        MiningReferenceData.Commodities.GetValueOrDefault(MiningCommodityCategory) ?? [],
        PlatinumMineral,
        options: new(
            MaximumSelections: MaximumMarketCommodities,
            ShowFullNames: true,
            AllowCustom: true,
            AllowEmpty: true
        )
    );
    private readonly MiningChipBoxViewModel marketStationTypeChips = new(
        "Station types",
        MarketStationTypeOptions,
        "",
        options: new(ShowFullNames: true, AllowEmpty: true)
    );
    public MiningChipBoxViewModel MarketStationTypeChips
    {
        get
        {
            HookMarketChips();
            return marketStationTypeChips;
        }
    }
    public MiningChipBoxViewModel MarketCategoryChips
    {
        get
        {
            HookMarketChips();
            return marketCategoryChips;
        }
    }
    public MiningChipBoxViewModel MarketCommodityChips
    {
        get
        {
            HookMarketChips();
            return marketCommodityChips;
        }
    }

    private void HookMarketChips()
    {
        if (marketChipsHooked)
        {
            return;
        }

        marketChipsHooked = true;
        marketCategoryChips.Selected.CollectionChanged += (_, _) =>
        {
            if (!syncingMarketChips && marketCategoryChips.Selected.FirstOrDefault() is { } category)
            {
                CommodityCategory = category;
            }
        };
        marketCommodityChips.Selected.CollectionChanged += (_, _) =>
        {
            if (!syncingMarketChips)
            {
                SyncMarketCommodities();
            }
        };
        marketStationTypeChips.Selected.CollectionChanged += (_, _) =>
        {
            if (!syncingMarketChips)
            {
                options = options with { MarketStationTypes = marketStationTypeChips.Selected.ToArray() };
            }
        };
    }

    private void SyncMarketCommodities()
    {
        string[] selected = marketCommodityChips.Selected.ToArray();
        options = options with { MarketCommodities = selected, Commodity = selected.FirstOrDefault() ?? "" };
        Changed(nameof(Commodity));
    }

    public string CommodityCategory
    {
        get => options.CommodityCategory;
        set
        {
            string category =
                CommodityCategories.FirstOrDefault(choice => choice.Equals(value, StringComparison.OrdinalIgnoreCase))
                ?? MiningCommodityCategory;
            Set(ref options, options with { CommodityCategory = category });
            Changed(nameof(CommodityOptions));
            syncingMarketChips = true;
            if (!marketCategoryChips.Selected.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                marketCategoryChips.Add(category);
            }

            marketCommodityChips.ReplaceChoices(CommodityOptions, "");
            syncingMarketChips = false;
            SyncMarketCommodities();
        }
    }
    public string Reference
    {
        get => options.Reference;
        set
        {
            if (!restoringCachedSearch)
            {
                preserveRestoredReference = false;
            }

            string next = value ?? "";
            if (next.Length == 0 && currentSystem.Length > 0)
            {
                next = currentSystem;
            }

            bool restored = string.IsNullOrEmpty(value) && next.Length > 0;
            referenceTracksCommander = next.Equals(currentSystem, StringComparison.OrdinalIgnoreCase);
            if (Set(ref options, options with { Reference = next }))
            {
                Changed(nameof(PowerplaySummary));
            }
            else if (restored)
            {
                Changed(nameof(Reference));
            }
        }
    }
    public bool ForceIncludeReference
    {
        get => options.ForceIncludeReference;
        set { Set(ref options, options with { ForceIncludeReference = value }); }
    }
    public string Mineral
    {
        get => options.Mineral;
        set
        {
            if (Set(ref options, options with { Mineral = value ?? PlatinumMineral }))
            {
                Changed(nameof(PowerplaySummary));
                if (
                    !syncingPrimaryMineral
                    && !primaryMineralChips.Selected.Contains(options.Mineral, StringComparer.OrdinalIgnoreCase)
                )
                {
                    syncingPrimaryMineral = true;
                    primaryMineralChips.Add(options.Mineral);
                    syncingPrimaryMineral = false;
                }
            }
        }
    }
    public string RingType
    {
        get => options.RingType;
        set
        {
            if (Set(ref options, options with { RingType = value ?? "All" }))
            {
                Changed(nameof(PowerplaySummary));
            }
        }
    }
    public string Reserve
    {
        get => options.Reserve;
        set
        {
            if (Set(ref options, options with { Reserve = value ?? "All" }))
            {
                Changed(nameof(PowerplaySummary));
            }
        }
    }
    public string Commodity
    {
        get => options.Commodity;
        set
        {
            string next = value?.Trim() ?? "";
            syncingMarketChips = true;
            marketCommodityChips.Selected.Clear();
            if (next.Length > 0)
            {
                marketCommodityChips.Add(next);
            }

            syncingMarketChips = false;
            SyncMarketCommodities();
        }
    }
    public double Radius
    {
        get => options.Radius;
        set
        {
            if (Set(ref options, options with { Radius = double.IsFinite(value) ? Math.Clamp(value, 1, 500) : 100 }))
            {
                Changed(nameof(PowerplaySummary));
                Changed(nameof(DistanceWarning));
                Changed(nameof(HasDistanceWarning));
            }
        }
    }
    public string DistanceWarning => CanChooseDistance ? MiningDistanceWarning.For(Radius) : "";
    public bool HasDistanceWarning => DistanceWarning.Length > 0;
    public int MinimumHotspots
    {
        get => options.MinimumHotspots;
        set { Set(ref options, options with { MinimumHotspots = Math.Clamp(value, 0, 100) }); }
    }
    public string Source
    {
        get => options.Source;
        set { Set(ref options, options with { Source = value ?? "Both" }); }
    }
    public bool OnlyOverlaps
    {
        get => options.OnlyOverlaps;
        set { Set(ref options, options with { OnlyOverlaps = value }); }
    }
    public bool OnlyRes
    {
        get => options.OnlyRes;
        set { Set(ref options, options with { OnlyRes = value }); }
    }
    public bool Buying
    {
        get => options.Buying;
        set
        {
            if (Set(ref options, options with { Buying = value }))
            {
                Changed(nameof(TradeMode));
                Changed(nameof(MarketMinimumVolumeLabel));
                Changed(nameof(MarketMaximumVolumeLabel));
            }
        }
    }
    public string MarketMinimumVolumeLabel => Buying ? "Min. supply" : "Min. demand";
    public string MarketMaximumVolumeLabel => Buying ? "Max. supply (0 = no limit)" : "Max. demand (0 = no limit)";
    public string MarketPadSize
    {
        get => options.MarketPadSize;
        set { Set(ref options, options with { MarketPadSize = value ?? AnyPower }); }
    }
    public long MarketMinimumVolume
    {
        get => options.MarketMinimumVolume;
        set { Set(ref options, options with { MarketMinimumVolume = Math.Max(0, value) }); }
    }
    public long MarketMaximumVolume
    {
        get => options.MarketMaximumVolume;
        set { Set(ref options, options with { MarketMaximumVolume = Math.Max(0, value) }); }
    }
    public int MarketMaximumAgeDays
    {
        get => options.MarketMaximumAgeDays;
        set { Set(ref options, options with { MarketMaximumAgeDays = Math.Clamp(value, 1, 3650) }); }
    }
    public bool GalaxyWide
    {
        get => options.GalaxyWide;
        set { Set(ref options, options with { GalaxyWide = value }); }
    }
    public bool ExcludeCarriers
    {
        get => options.ExcludeCarriers;
        set { Set(ref options, options with { ExcludeCarriers = value }); }
    }
    public bool LargePads
    {
        get => options.LargePads;
        set { Set(ref options, options with { LargePads = value }); }
    }
    public int MaximumAgeDays
    {
        get => options.MaximumAgeDays;
        set { Set(ref options, options with { MaximumAgeDays = Math.Clamp(value, 0, 365) }); }
    }
    public long MinimumDemand
    {
        get => options.MinimumDemand;
        set { Set(ref options, options with { MinimumDemand = Math.Max(0, value) }); }
    }
    public long MaximumDemand
    {
        get => options.MaximumDemand;
        set { Set(ref options, options with { MaximumDemand = Math.Max(0, value) }); }
    }
    public int ResultLimit
    {
        get => options.ResultLimit;
        set { Set(ref options, options with { ResultLimit = Math.Clamp(value, 1, MaximumResultLimit) }); }
    }
    public int MaximumResultLimit => Objective == AcquireObjective ? 10 : 30;
    public bool CanChooseDistance => Objective != AcquireObjective;
    public string PlatinumMode
    {
        get => options.PlatinumMode;
        set { Set(ref options, options with { PlatinumMode = value ?? "Spots++" }); }
    }
    public string StationType
    {
        get => options.StationType;
        set { Set(ref options, options with { StationType = value ?? "" }); }
    }
    public string Security
    {
        get => options.Security;
        set { Set(ref options, options with { Security = value ?? "" }); }
    }
    public string Allegiance
    {
        get => options.Allegiance;
        set { Set(ref options, options with { Allegiance = value ?? "" }); }
    }
    public string Government
    {
        get => options.Government;
        set { Set(ref options, options with { Government = value ?? "" }); }
    }
    public string Economy
    {
        get => options.Economy;
        set { Set(ref options, options with { Economy = value ?? "" }); }
    }
    public string State
    {
        get => options.State;
        set { Set(ref options, options with { State = value ?? "" }); }
    }
    public string Power
    {
        get => options.Power;
        set { Set(ref options, options with { Power = value ?? "" }); }
    }
    public string OpposingPower
    {
        get => options.OpposingPower.Length == 0 ? AnyPower : options.OpposingPower;
        set { Set(ref options, options with { OpposingPower = NormalizeAny(value) }); }
    }
    public string MiningType
    {
        get => options.MiningType;
        set { Set(ref options, options with { MiningType = value ?? "All" }); }
    }
    public string AlsoMineral
    {
        get => options.AlsoMineral;
        set { Set(ref options, options with { AlsoMineral = value ?? AnyPower }); }
    }
    public string PadSize
    {
        get => options.PadSize;
        set { Set(ref options, options with { PadSize = value ?? AnyPower }); }
    }
    public bool LimitMarketAge
    {
        get => options.LimitMarketAge;
        set { Set(ref options, options with { LimitMarketAge = value }); }
    }
    public int MarketAge
    {
        get => options.MarketAge;
        set { Set(ref options, options with { MarketAge = Math.Clamp(value, 1, 9999) }); }
    }
    public string MarketAgeUnit
    {
        get => options.MarketAgeUnit;
        set { Set(ref options, options with { MarketAgeUnit = value ?? "Hours" }); }
    }
    private TimeSpan? MarketFreshness =>
        LimitMarketAge
            ? MarketAgeUnit switch
            {
                "Days" => TimeSpan.FromDays(MarketAge),
                "Months" => TimeSpan.FromDays(30d * MarketAge),
                "Years" => TimeSpan.FromDays(365d * MarketAge),
                _ => TimeSpan.FromHours(MarketAge),
            }
            : null;
    public string SystemState
    {
        get => options.State.Length == 0 ? AnyPower : options.State;
        set { Set(ref options, options with { State = NormalizeAny(value) }); }
    }
    public string PowerState
    {
        get => options.PowerState;
        set { Set(ref options, options with { PowerState = value ?? "" }); }
    }
    public long MinimumPopulation
    {
        get => options.MinimumPopulation;
        set { Set(ref options, options with { MinimumPopulation = Math.Max(0, value) }); }
    }
    public string TraderType
    {
        get => options.TraderType;
        set { Set(ref options, options with { TraderType = value ?? "Raw" }); }
    }
    private int destination;
    private int page;
    private bool systemOnly;
    private string objective = AllSystems;
    private string pledgedPower = AnyPower;
    private string detectedPower = "";
    private string planningTarget = "";
    private string planningObjective = "";
    private string miningOrigin = "";
    private string currentSystem = "";
    private bool referenceTracksCommander;
    private bool powerplayPrepared;
    public string CurrentSystem => currentSystem;
    public bool HasPlan => planningTarget.Length > 0;
    public string PlanningContext =>
        planningTarget.Length == 0 ? "" : $"{planningObjective} destination: {planningTarget} · Mining: {miningOrigin}";

    public void UpdateCurrentLocation(string system)
    {
        string next = system ?? "";
        string previous = currentSystem;
        Set(ref currentSystem, next, nameof(CurrentSystem));
        if (next.Length == 0 || preserveRestoredReference)
        {
            return;
        }

        if (
            referenceTracksCommander
            || Reference.Length == 0
            || Reference.Equals(previous, StringComparison.OrdinalIgnoreCase)
        )
        {
            referenceTracksCommander = true;
            Reference = next;
        }
    }

    public void UseCurrentLocation()
    {
        if (currentSystem.Length == 0)
        {
            Status = "Current system is not available from the journal yet.";
            return;
        }

        Reference = currentSystem;
        Page = 0;
        Status = $"Reference system set to {currentSystem}.";
    }

    public void PreparePowerplay()
    {
        if (powerplayPrepared)
        {
            return;
        }

        powerplayPrepared = true;
        if (restoredPowerplaySearch)
        {
            return;
        }

        Objective = ReinforceObjective;
        if (Mineral.Equals(PlatinumMineral, StringComparison.OrdinalIgnoreCase))
        {
            Mineral = AnyPower;
        }

        if (MaximumDemand == 0)
        {
            MaximumDemand = 90_000;
        }

        if (currentSystem.Length > 0)
        {
            referenceTracksCommander = true;
            Reference = currentSystem;
        }
    }

    public void ClearPlan()
    {
        planningTarget = planningObjective = miningOrigin = "";
        Changed(nameof(PlanningContext));
        Changed(nameof(HasPlan));
    }

    private IReadOnlyList<MiningMarketResult> traders = [];
    private MiningMarketResult? selectedTrader;
    public int Page
    {
        get => page;
        set => Set(ref page, Math.Max(0, value));
    }
    public int Destination
    {
        get => destination;
        set
        {
            if (Set(ref destination, value))
            {
                Changed(nameof(SortIndicators));
            }
        }
    }
    public bool SystemOnly
    {
        get => systemOnly;
        set => Set(ref systemOnly, value);
    }
    public string Objective
    {
        get => objective;
        set
        {
            string chosen = !CanChoosePowerGoal && value == AcquireObjective ? ReinforceObjective : value;
            if (Set(ref objective, chosen))
            {
                if (chosen != ReinforceObjective && OpposingPower == NoPower)
                {
                    options = options with { OpposingPower = "" };
                    Changed(nameof(OpposingPower));
                }

                RefreshOpposingChoices();
                Changed(nameof(PowerplaySummary));
                Changed(nameof(IsPlanetaryAcquire));
                Changed(nameof(IsPlanetaryCombined));
                Changed(nameof(CanChooseDistance));
                Changed(nameof(DistanceWarning));
                Changed(nameof(HasDistanceWarning));
                Changed(nameof(MaximumResultLimit));
                ResultLimit = Math.Min(ResultLimit, MaximumResultLimit);
            }
        }
    }
    private string[]? opposingChoices;
    public IReadOnlyList<string> OpposingChoices => opposingChoices ??= CreateOpposingChoices();

    private string[] CreateOpposingChoices() =>
        (Objective == ReinforceObjective ? Powers : Powers.Where(power => power != NoPower))
            .Concat([PowerplayPlan.OneOpposition, PowerplayPlan.TwoOpposition, PowerplayPlan.MultipleOpposition])
            .ToArray();

    private void RefreshOpposingChoices()
    {
        opposingChoices = CreateOpposingChoices();
        Changed(nameof(OpposingChoices));
    }

    public bool CanChoosePowerGoal => IsChosenPower(PledgedPower);
    public string PledgedPower
    {
        get => pledgedPower;
        set
        {
            if (!restoringCachedSearch)
            {
                preserveRestoredPower = false;
            }

            if (!Set(ref pledgedPower, string.IsNullOrWhiteSpace(value) ? AnyPower : value))
            {
                return;
            }

            if (!CanChoosePowerGoal)
            {
                objective = ReinforceObjective;
                options = options with { OpposingPower = "" };
                Changed(nameof(Objective));
                Changed(nameof(OpposingPower));
                Changed(nameof(CanChooseDistance));
                Changed(nameof(DistanceWarning));
                Changed(nameof(HasDistanceWarning));
                Changed(nameof(MaximumResultLimit));
                Changed(nameof(IsPlanetaryAcquire));
                Changed(nameof(IsPlanetaryCombined));
            }

            Changed(nameof(CanChoosePowerGoal));
            RefreshOpposingChoices();
            Changed(nameof(PowerplaySummary));
        }
    }

    public void NoteDetectedPower(string? power)
    {
        if (string.IsNullOrWhiteSpace(power))
        {
            string previousPower = detectedPower;
            detectedPower = "";
            if (
                !preserveRestoredPower
                && (IsAny(pledgedPower) || pledgedPower.Equals(previousPower, StringComparison.OrdinalIgnoreCase))
            )
            {
                PledgedPower = AnyPower;
            }

            return;
        }

        string canonical = CanonicalListedPower(power);
        if (canonical.Length == 0)
        {
            return;
        }

        string knownPower = detectedPower;
        detectedPower = canonical;
        options = options with { PledgedPower = canonical };
        if (
            !preserveRestoredPower
            && (IsAny(pledgedPower) || pledgedPower.Equals(knownPower, StringComparison.OrdinalIgnoreCase))
        )
        {
            PledgedPower = canonical;
        }
    }

    public static string CanonicalListedPower(string? power)
    {
        if (string.IsNullOrWhiteSpace(power))
        {
            return "";
        }

        string name = power.Trim();
        if (name.Equals("A. Lavigny-Duval", StringComparison.OrdinalIgnoreCase))
        {
            name = "Arissa Lavigny-Duval";
        }

        return Powers.FirstOrDefault(item => item.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    public static IReadOnlyList<string> Objectives { get; } =
    [AllSystems, ReinforceObjective, UndermineObjective, AcquireObjective];
    public static IReadOnlyList<string> PowerplayObjectives { get; } =
    [ReinforceObjective, UndermineObjective, AcquireObjective];
    public static IReadOnlyList<string> Powers { get; } =
    [
        AnyPower,
        "Aisling Duval",
        "Archon Delaine",
        "Arissa Lavigny-Duval",
        "Denton Patreus",
        "Edmund Mahon",
        "Felicia Winters",
        "Li Yong-Rui",
        "Nakato Kaine",
        "Pranav Antal",
        "Yuri Grom",
        "Jerome Archer",
        "Zemina Torval",
        NoPower,
    ];
    public static IReadOnlyList<string> PowerStates { get; } =
    ["", "Exploited", "Fortified", "Stronghold", "Unoccupied", ExpansionState, "Contested"];
    public IReadOnlyList<MiningMarketResult> Traders
    {
        get => traderSorter.Apply(traders);
        private set => Set(ref traders, value);
    }
    public MiningMarketResult? SelectedTrader
    {
        get => selectedTrader;
        set => Set(ref selectedTrader, value);
    }
    public string TradeMode
    {
        get => Buying ? "Buy supplies" : "Sell mined cargo";
        set
        {
            Buying = value == "Buy supplies";
            Changed(nameof(TradeMode));
        }
    }
    public static IReadOnlyList<string> TradeModes { get; } = ["Sell mined cargo", "Buy supplies"];
    public static IReadOnlyList<string> Sources { get; } = ["Both", "Local", SpanshSource];
    public static IReadOnlyList<string> RingTypes { get; } =
    ["All", HotspotRing, WithoutHotspotsRing, "Icy", "Metallic", "Metal Rich", "Rocky"];
    private static readonly string[] RingMineralChoices =
    [
        AnyPower,
        .. (MiningReferenceData.Commodities.GetValueOrDefault(MiningCommodityCategory) ?? []).Where(name =>
            !PlanetaryMiningPlan.IsSurfaceExclusive(name)
        ),
    ];
    private static readonly string[] PlanetaryMineralChoices = [AnyPower, .. PlanetaryMiningPlan.Materials];
    private static readonly string[] PowerplayMineralChoices =
    [
        MiningMaterialSelection.Default,
        MiningMaterialSelection.Any,
        .. (MiningReferenceData.Commodities.GetValueOrDefault(MiningCommodityCategory) ?? []).Where(name =>
            PlanetaryMiningPlan.IsEdpmCommodity(name)
        ),
    ];
    public IReadOnlyList<string> RingMinerals => IsPlanetaryMining ? PlanetaryMineralChoices : RingMineralChoices;
    private bool primaryMineralHooked;
    private bool syncingPrimaryMineral;
    private readonly MiningChipBoxViewModel primaryMineralChips = new(
        "Mineral / metal",
        RingMineralChoices,
        PlatinumMineral,
        options: new(MaximumSelections: 1, ShowFullNames: true)
    );
    public MiningChipBoxViewModel PrimaryMineralChips
    {
        get
        {
            if (!primaryMineralHooked)
            {
                primaryMineralHooked = true;
                primaryMineralChips.Selected.CollectionChanged += (_, _) =>
                {
                    if (!syncingPrimaryMineral && primaryMineralChips.Selected.FirstOrDefault() is { } material)
                    {
                        Mineral = material;
                    }
                };
            }

            return primaryMineralChips;
        }
    }
    public static IReadOnlyList<string> PowerplayMinerals => PowerplayMineralChoices;
    public static IReadOnlyList<string> Reserves { get; } =
    ["All", "Pristine", "Major", "Common", "Low", "Depleted", UnknownState];
    public static IReadOnlyList<string> MiningTypes { get; } =
    ["All", "Core", "Laser Surface", "Surface Deposit", "Sub Surface Deposit", PlanetaryMiningPlan.MiningType];
    public static IReadOnlyList<string> PadSizes { get; } = ["Any", "S", "M", "L"];
    public static IReadOnlyList<string> MarketAgeUnits { get; } = ["Hours", "Days", "Months", "Years"];
    public static IReadOnlyList<string> FactionStates { get; } =
    [
        "Any",
        "Blight",
        "Boom",
        "Bust",
        "Civil liberty",
        "Civil unrest",
        "Civil war",
        "Drought",
        "Election",
        "Expansion",
        "Famine",
        "Infrastructure Failure",
        "Investment",
        "Lockdown",
        "Natural Disaster",
        "None",
        "Outbreak",
        "Pirate attack",
        "Public Holiday",
        "Retreat",
        "Terrorist Attack",
        "War",
    ];
    public MiningChipBoxViewModel MineralChips { get; } =
        new(
            "Mineral / metal",
            PowerplayMineralChoices,
            MiningMaterialSelection.Default,
            [MiningMaterialSelection.Default, MiningMaterialSelection.Any]
        );
    private readonly MiningChipBoxViewModel miningTypeChips = new(
        "Mining type",
        MiningTypes,
        "All",
        ["All", PlanetaryMiningPlan.MiningType]
    );
    private bool miningTypeHooked;
    public MiningChipBoxViewModel MiningTypeChips
    {
        get
        {
            if (!miningTypeHooked)
            {
                miningTypeHooked = true;
                miningTypeChips.Selected.CollectionChanged += (_, _) => SyncMineralCatalogue();
            }

            return miningTypeChips;
        }
    }
    public bool UsesRingFilters => !IsPlanetaryMining;
    public bool IsPlanetaryMining =>
        MiningTypeChips.Selected.Any(item =>
            item.Equals(PlanetaryMiningPlan.MiningType, StringComparison.OrdinalIgnoreCase)
        );
    public bool IsPlanetaryAcquire => IsPlanetaryMining && Objective == AcquireObjective;
    public bool IsPlanetaryCombined => IsPlanetaryMining && Objective != AcquireObjective;
    private bool showingSurfaceMaterials;
    public MiningChipBoxViewModel StateChips { get; } = new("System state", FactionStates, "Any");
    private IReadOnlyList<MeritSystemRowViewModel> meritRows = [];
    private IReadOnlyList<AcquireResultRowViewModel> acquireRows = [];
    private IReadOnlyList<PowerplayRingAcquireClusterViewModel> ringAcquireClusters = [];
    private bool acquireNearestFirst = true;
    private bool acquireBestStationSortActive;
    private bool acquireBestStationDescending = true;
    private ICommand? acquireDistanceSortCommand;
    private ICommand? acquireBestStationSortCommand;
    public ICommand AcquireDistanceSortCommand =>
        acquireDistanceSortCommand ??= new WorkspaceCommand(ToggleAcquireDistanceSort);
    public ICommand AcquireBestStationSortCommand =>
        acquireBestStationSortCommand ??= new WorkspaceCommand(ToggleAcquireBestStationSort);
    public string AcquireDistanceSortIndicator
    {
        get
        {
            if (acquireBestStationSortActive)
            {
                return "";
            }

            return acquireNearestFirst ? "↑" : "↓";
        }
    }
    public string AcquireBestStationSortIndicator
    {
        get
        {
            if (!acquireBestStationSortActive)
            {
                return "";
            }

            return acquireBestStationDescending ? "↓" : "↑";
        }
    }
    public IReadOnlyList<PowerplayRingAcquireClusterViewModel> RingAcquireClusters => ringAcquireClusters;
    public IReadOnlyList<AcquireResultRowViewModel> AcquireRows
    {
        get => acquireRows;
        private set
        {
            if (Set(ref acquireRows, OrderAcquireRows(value)))
            {
                ringAcquireClusters = PowerplayRingAcquireClusterViewModel.Group(
                    acquireRows,
                    AcquireDistanceSortCommand,
                    AcquireBestStationSortCommand,
                    AcquireDistanceSortIndicator,
                    AcquireBestStationSortIndicator
                );
                Changed(nameof(RingAcquireClusters));
                Changed(nameof(HasAcquireRows));
                Changed(nameof(HasMeritRows));
            }
        }
    }
    public bool HasAcquireRows => AcquireRows.Count > 0;
    public bool HasMeritRows => !IsPlanetaryMining && !HasAcquireRows && meritRows.Count > 0;
    public bool ShowMeritRows => !HasAcquireRows;

    private AcquireResultRowViewModel[] OrderAcquireRows(IEnumerable<AcquireResultRowViewModel> rows)
    {
        if (acquireBestStationSortActive)
        {
            return rows.OrderBy(
                    row => row,
                    Comparer<AcquireResultRowViewModel>.Create(
                        (left, right) =>
                        {
                            int compare = PowerplayStationRanking.CompareDescending(
                                left.StationRanking,
                                right.StationRanking
                            );
                            if (compare == 0)
                            {
                                compare = (left.DistanceLy ?? double.MaxValue).CompareTo(
                                    right.DistanceLy ?? double.MaxValue
                                );
                            }

                            if (compare == 0)
                            {
                                compare = string.Compare(left.Target, right.Target, StringComparison.OrdinalIgnoreCase);
                            }

                            return acquireBestStationDescending ? compare : -compare;
                        }
                    )
                )
                .ToArray();
        }

        return acquireNearestFirst
            ? rows.OrderBy(row => row.DistanceLy ?? double.MaxValue)
                .ThenBy(row => row.Target, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : rows.OrderByDescending(row => row.DistanceLy ?? double.MaxValue)
                .ThenByDescending(row => row.Target, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private void ToggleAcquireDistanceSort()
    {
        acquireNearestFirst = !acquireNearestFirst;
        acquireBestStationSortActive = false;
        AcquireRows = AcquireRows.ToArray();
        Changed(nameof(AcquireDistanceSortIndicator));
        Changed(nameof(AcquireBestStationSortIndicator));
    }

    private void ToggleAcquireBestStationSort()
    {
        acquireBestStationDescending = !acquireBestStationSortActive || !acquireBestStationDescending;
        acquireBestStationSortActive = true;
        AcquireRows = AcquireRows.ToArray();
        Changed(nameof(AcquireDistanceSortIndicator));
        Changed(nameof(AcquireBestStationSortIndicator));
    }

    public IReadOnlyList<MeritSystemRowViewModel> MeritRows
    {
        get => meritRows;
        private set
        {
            if (Set(ref meritRows, value))
            {
                Changed(nameof(HasMeritRows));
            }
        }
    }
    public static IReadOnlyList<string> PlatinumModes { get; } = ["Spots++", "RES mapped", "Overlaps", "All platinum"];
    public static IReadOnlyList<string> TraderTypes { get; } = ["Raw", "Manufactured", "Encoded"];
    public string Status
    {
        get => status;
        private set
        {
            if (Set(ref status, value))
            {
                Changed(nameof(ShowSearchStatus));
            }
        }
    }

    public bool ShowSearchStatus => status.Length > 0 && status != IdleStatus;
    public bool IsBusy
    {
        get => busy;
        private set => Set(ref busy, value);
    }
    public ICommand SortCommand =>
        sortCommand ??= new WorkspaceParameterCommand(parameter =>
        {
            ActiveSorter.Toggle(parameter);
            Changed(
                Destination switch
                {
                    0 => nameof(Rings),
                    1 => nameof(Markets),
                    2 => nameof(Traders),
                    _ => nameof(PlatinumSpots),
                }
            );
            Changed(nameof(SortIndicators));
        });
    public IReadOnlyList<MiningRing> Rings
    {
        get => ringSorter.Apply(rings);
        private set => Set(ref rings, value);
    }
    public IReadOnlyList<MiningMarketResult> Markets
    {
        get => marketSorter.Apply(markets);
        private set => Set(ref markets, value);
    }
    public IReadOnlyList<MiningSystemResult> Systems
    {
        get => systemSorter.Apply(systems);
        private set => Set(ref systems, value);
    }
    public IReadOnlyList<PlatinumSpotRowViewModel> PlatinumSpots
    {
        get => platinumSorter.Apply(platinumSpots);
        private set => Set(ref platinumSpots, value);
    }
    public MiningRing? SelectedRing
    {
        get => selectedRing;
        set => Set(ref selectedRing, value);
    }
    public MiningMarketResult? SelectedMarket
    {
        get => selectedMarket;
        set => Set(ref selectedMarket, value);
    }
    public MiningSystemResult? SelectedSystem
    {
        get => selectedSystem;
        set => Set(ref selectedSystem, value);
    }
    public PlatinumSpotRowViewModel? SelectedPlatinumSpot
    {
        get => selectedPlatinumSpot;
        set => Set(ref selectedPlatinumSpot, value);
    }
    private WorkspaceTableSorter ActiveSorter =>
        Destination switch
        {
            0 => ringSorter,
            1 => marketSorter,
            2 => traderSorter,
            _ => platinumSorter,
        };

    public Task SearchRingsAsync() =>
        Run(async token =>
        {
            var candidates = new List<MiningRing>();
            bool offline = false;
            if (Source != "Local")
            {
                try
                {
                    candidates.AddRange(
                        await client.FindRingsAsync(
                            new MiningRingQuery(
                                Reference,
                                OnlineMineral,
                                OnlineRingType,
                                Radius,
                                OnlineMinimumHotspots,
                                Page,
                                SystemOnly
                            ),
                            token
                        )
                    );
                }
                catch (Exception ex) when (Source == "Both" && IsProviderFailure(ex))
                {
                    offline = true;
                }
            }
            if (Source != SpanshSource)
            {
                await AddLocalRingsAsync(candidates, Mineral, RingType, MinimumHotspots, Reserve, token, true);
            }

            MiningRing[] annotated = candidates
                .Select(AnnotateRing)
                .Where(r =>
                    MatchesRing(r, Mineral, RingType, MinimumHotspots, Reserve, true)
                    && (!OnlyOverlaps || r.Overlaps.Length > 0)
                    && (!OnlyRes || r.ResourceExtractionSites.Length > 0)
                )
                .OrderBy(r => r.DistanceLy ?? double.MaxValue)
                .Take(SystemOnly ? ResultLimit : 500)
                .ToArray();
            token.ThrowIfCancellationRequested();
            Rings = annotated;
            Status = $"{annotated.Length} matching rings · {Reference} · {Source} · online page {Page + 1}.";
            if (offline)
            {
                Status += " Provider unavailable; showing local results.";
            }
        });

    public Task SearchPlatinumAsync() =>
        Run(async token =>
        {
            SystemOnly = false;
            var candidates = new List<MiningRing>();
            bool offline = false;
            const string mineral = PlatinumMineral;
            const string ringType = "Metallic";
            if (Source != "Local")
            {
                try
                {
                    candidates.AddRange(
                        await client.FindRingsAsync(
                            new MiningRingQuery(Reference, mineral, ringType, Radius, MinimumHotspots, Page),
                            token
                        )
                    );
                }
                catch (Exception ex) when (Source == "Both" && IsProviderFailure(ex))
                {
                    offline = true;
                }
            }

            if (Source != SpanshSource)
            {
                await AddLocalRingsAsync(candidates, mineral, ringType, MinimumHotspots, Reserve, token);
            }

            PlatinumSpotRowViewModel[] result = candidates
                .Select(AnnotateRing)
                .Where(ring => MatchesRing(ring, mineral, ringType, MinimumHotspots, Reserve))
                .Select(PlatinumSpotRowViewModel.Create)
                .Where(MatchesPlatinumMode)
                .OrderByDescending(spot => spot.Score)
                .ThenBy(spot => spot.DistanceLy ?? double.MaxValue)
                .Take(ResultLimit)
                .ToArray();
            token.ThrowIfCancellationRequested();
            PlatinumSpots = result;
            SelectedPlatinumSpot = null;
            Status = $"{result.Length} Platinum Spots++ results · {Reference} · {Source}.";
            if (offline)
            {
                Status += " Spansh unavailable; showing built-in and commander observations.";
            }
        });

    private bool MatchesPlatinumMode(PlatinumSpotRowViewModel spot) =>
        PlatinumMode switch
        {
            "RES mapped" => spot.HasRes,
            "Overlaps" => spot.HasOverlap,
            "All platinum" => true,
            _ => spot.HasRes || spot.HasOverlap || spot.HotspotCount >= 2,
        };

    public void UseSelectedPlatinumSpot()
    {
        if (SelectedPlatinumSpot is not { } spot)
        {
            return;
        }

        SelectedRing = spot.Ring;
        Mineral = PlatinumMineral;
        CommodityCategory = MiningCommodityCategory;
        Commodity = PlatinumMineral;
        Reference = spot.System;
        SystemOnly = false;
        Destination = 1;
    }

    public async Task FindSelectedPlatinumMarketsAsync()
    {
        if (SelectedPlatinumSpot is null)
        {
            return;
        }

        UseSelectedPlatinumSpot();
        await FindSellingStationsAsync();
    }

    public void BookmarkSelectedPlatinumSpot()
    {
        if (SelectedPlatinumSpot is null)
        {
            return;
        }

        SelectedRing = SelectedPlatinumSpot.Ring;
        Bookmark();
    }

    private async Task AddLocalRingsAsync(
        List<MiningRing> candidates,
        string mineral,
        string ringType,
        int minimumHotspots,
        string reserve,
        CancellationToken token,
        bool applyPlannerFilters = false
    )
    {
        GalacticCoordinate? origin = await ResolveOriginAsync(token);
        foreach (MiningRing? ring in MiningReferenceData.Rings.Concat(localRings()))
        {
            double? distance = RingDistance(ring, origin);
            if (
                distance is null
                || distance > Radius
                || !MatchesRing(ring, mineral, ringType, minimumHotspots, reserve, applyPlannerFilters)
            )
            {
                continue;
            }

            MergeRing(candidates, ring, distance.Value);
        }
    }

    private async Task<GalacticCoordinate?> ResolveOriginAsync(CancellationToken token)
    {
        GalacticCoordinate? origin =
            community?.Position(Reference)
            ?? localRings()
                .Concat(MiningReferenceData.Rings)
                .FirstOrDefault(r => Same(r.System, Reference) && r.Position is not null)
                ?.Position
            ?? bookmarks.All.FirstOrDefault(b => Same(b.System, Reference) && b.Position is not null)?.Position;
        if (origin is not null)
        {
            return origin;
        }

        try
        {
            return (await resolver.SearchAsync(Reference, token))
                .FirstOrDefault(s => Same(s.Name, Reference))
                ?.Position;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return null;
        } // Unknown coordinates must not be interpreted as zero distance.
    }

    private double? RingDistance(MiningRing ring, GalacticCoordinate? origin)
    {
        if (Same(ring.System, Reference))
        {
            return 0;
        }

        if (SystemOnly)
        {
            return null;
        }

        return ring.Position is { } location && origin is { } point ? location.DistanceTo(point) : null;
    }

    private bool MatchesPowerplaySystem(MiningSystemResult system) =>
        PowerplayPlan.Matches(Objective, system, PledgedPower, OpposingPower)
        && PowerplayPlan.MatchesOppositionCount(Objective, OpposingPower, system, PledgedPower);

    private MiningSystemResult[] SystemsWithRings(
        IReadOnlyList<MiningSystemResult> known,
        IReadOnlyList<MiningRing> rings
    )
    {
        var byName = known.ToDictionary(system => system.System, StringComparer.OrdinalIgnoreCase);
        foreach (
            IGrouping<string, MiningRing> group in rings.GroupBy(ring => ring.System, StringComparer.OrdinalIgnoreCase)
        )
        {
            if (byName.ContainsKey(group.Key))
            {
                continue;
            }

            MiningRing ring = group.First();
            var candidate = new MiningSystemResult(
                group.Key,
                group.Min(item => item.DistanceLy),
                "",
                "",
                "",
                "",
                "",
                ring.Power,
                ring.PowerState,
                0,
                ring.Position
            );
            if (MatchesPowerplaySystem(candidate))
            {
                byName[group.Key] = candidate;
            }
        }

        return byName
            .Values.Where(MatchesPowerplaySystem)
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .ToArray();
    }

    private async Task<MiningSystemResult[]> CompleteSystemRecordsAsync(
        IReadOnlyList<MiningSystemResult> systems,
        CancellationToken token
    )
    {
        string[] missing = systems
            .Where(system => system.State.Length == 0 || system.NearbyPowers.Count == 0)
            .Select(system => system.System)
            .ToArray();
        if (missing.Length == 0)
        {
            return systems.ToArray();
        }

        IReadOnlyList<MiningSystemResult> details = await client.FindSystemsByNameAsync(Reference, missing, token);
        var byName = details.ToDictionary(system => system.System, StringComparer.OrdinalIgnoreCase);
        return systems
            .Select(system => byName.TryGetValue(system.System, out MiningSystemResult? full) ? full : system)
            .Where(MatchesPowerplaySystem)
            .ToArray();
    }

    private string[] HotspotMinerals(string[] named)
    {
        if (named.Length > 0)
        {
            return named.Where(name => !PlanetaryMiningPlan.IsSurfaceExclusive(name)).ToArray();
        }

        if (!IsAny(Mineral))
        {
            return [Mineral];
        }

        return RingMineralChoices.Where(name => !name.Equals(AnyPower, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private string OnlineMineral => Mineral is AnyPower || RingType == WithoutHotspotsRing ? "" : Mineral.Trim();

    private string OnlineRingType => RingType is HotspotRing or WithoutHotspotsRing ? "All" : RingType;

    private int OnlineMinimumHotspots => RingType == WithoutHotspotsRing ? 0 : MinimumHotspots;

    private static bool IsAny(string value) =>
        value.Length == 0 || value.Equals(AnyPower, StringComparison.OrdinalIgnoreCase);

    private static bool IsChosenPower(string power) =>
        !IsAny(power) && !power.Equals(NoPower, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAny(string? value) => IsAny(value ?? "") ? "" : value!.Trim();

    private bool MatchesRing(
        MiningRing ring,
        string mineral,
        string ringType,
        int minimumHotspots,
        string reserve,
        bool applyPlannerFilters = false
    )
    {
        string wanted = mineral is AnyPower ? "" : mineral;
        bool typeMatches =
            ringType is "All" or HotspotRing or WithoutHotspotsRing
            || Same(ring.RingType.Replace(" ", ""), ringType.Replace(" ", ""));
        bool reserveMatches =
            reserve == "All"
            || (reserve == UnknownState && (ring.Reserve.Length == 0 || Same(ring.Reserve, UnknownState)))
            || Same(ring.Reserve, reserve);
        bool hasMineral = wanted.Length > 0 && HasHotspot(ring, wanted, minimumHotspots);
        bool hotspotMatches = ringType switch
        {
            WithoutHotspotsRing => wanted.Length == 0 ? ring.Hotspots.Count == 0 : !HasHotspot(ring, wanted, 1),
            HotspotRing when wanted.Length == 0 => ring.Hotspots.Count > 0,
            _ => wanted.Length == 0 || hasMineral,
        };
        bool alsoMatches = !applyPlannerFilters || IsAny(AlsoMineral) || HasHotspot(ring, AlsoMineral, 1);
        bool methodMatches = !applyPlannerFilters || MatchesMiningType(ring, wanted);
        return typeMatches && reserveMatches && hotspotMatches && alsoMatches && methodMatches;
    }

    private static bool HasHotspot(MiningRing ring, string mineral, int minimum) =>
        ring.Hotspots.Any(hotspot => MiningCommodityName.Same(hotspot.Key, mineral) && hotspot.Value >= minimum);

    private bool MatchesMiningType(MiningRing ring, string mineral)
    {
        if (MiningType is "All" or "")
        {
            return true;
        }

        bool core = MiningType == "Core";
        if (mineral.Length > 0)
        {
            return core ? IsCoreMineral(mineral) : !IsCoreMineral(mineral);
        }

        return ring.Hotspots.Keys.Any(name => IsCoreMineral(name) == core);
    }

    private bool MatchesSelectedMiningType(MiningRing ring)
    {
        string[] types = MiningTypeChips
            .Selected.Where(item =>
                item.Length > 0
                && !item.Equals("All", StringComparison.OrdinalIgnoreCase)
                && !item.Equals(PlanetaryMiningPlan.MiningType, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        if (types.Length == 0)
        {
            return true;
        }

        return types.Any(type =>
            type.Equals("Core", StringComparison.OrdinalIgnoreCase)
                ? ring.Hotspots.Keys.Any(name => IsCoreMineral(name) && !PlanetaryMiningPlan.IsSurfaceExclusive(name))
                : ring.Hotspots.Keys.Any(name => !PlanetaryMiningPlan.IsSurfaceExclusive(name))
        );
    }

    private static bool IsCoreMineral(string name) =>
        CoreMinerals.Any(mineral => MiningCommodityName.Same(mineral, name));

    private static readonly HashSet<string> CoreMinerals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alexandrite",
        "Benitoite",
        "Bromellite",
        "Grandidierite",
        "Monazite",
        "Musgravite",
        "Rhodplumsite",
        "Serendibite",
        "Void Opal",
        "Void Opals",
    };

    private static void MergeRing(List<MiningRing> candidates, MiningRing ring, double distance)
    {
        MiningRing? observed = candidates.Find(r => Same(r.System, ring.System) && Same(r.Body, ring.Body));
        if (observed is null)
        {
            candidates.Add(ring with { DistanceLy = distance });
            return;
        }
        MiningRing newer = observed.Scanned >= ring.Scanned ? observed : ring;
        MiningRing older = ReferenceEquals(newer, observed) ? ring : observed;
        candidates.Remove(observed);
        candidates.Add(
            newer with
            {
                DistanceLy = distance,
                Overlaps = Prefer(newer.Overlaps, older.Overlaps),
                ResourceExtractionSites = Prefer(newer.ResourceExtractionSites, older.ResourceExtractionSites),
                Reserve = Prefer(newer.Reserve, older.Reserve),
                Position = newer.Position ?? older.Position,
            }
        );
    }

    private MiningRing AnnotateRing(MiningRing ring)
    {
        GalacticBookmark? bookmark = bookmarks.All.FirstOrDefault(b =>
            b.HasCategory(BookmarkCategoryCatalog.Mining)
            && Same(b.System, ring.System)
            && Same(b.CombinedBodyAndRing, ring.Body)
        );
        MiningPowerObservation? power = community?.Power(ring.System);
        return ring with
        {
            Overlaps = Prefer(bookmark?.Overlaps, ring.Overlaps),
            ResourceExtractionSites = Prefer(bookmark?.ResourceExtractionSites, ring.ResourceExtractionSites),
            Power = power is null ? ring.Power : $"{power.Power} · {power.State}",
        };
    }

    private static string Prefer(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderFailure(Exception ex) =>
        ex is HttpRequestException or System.Text.Json.JsonException or IOException or InvalidDataException;

    public Task SearchAllMarketsAsync()
    {
        SystemOnly = false;
        return SearchMarketsAsync();
    }

    public Task SearchMarketsAsync() =>
        Run(async token =>
        {
            if (!TryGetMarketCommodities(out string[] commodities))
            {
                return;
            }

            var quotes = new List<MiningMarketResult>();
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GalacticCoordinate? origin =
                (community is not null || GalaxyWide) && Reference.Length > 0 ? await ResolveOriginAsync(token) : null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (string commodity in commodities)
            {
                token.ThrowIfCancellationRequested();
                Status = $"Checking market prices for {commodity}…";
                var query = new MiningMarketQuery(
                    Reference,
                    commodity,
                    Buying,
                    Radius,
                    GalaxyWide,
                    ExcludeCarriers,
                    false,
                    MarketMaximumAgeDays,
                    "",
                    Page,
                    SystemOnly,
                    MarketMinimumVolume,
                    MarketMaximumVolume,
                    TimeSpan.FromDays(MarketMaximumAgeDays),
                    MarketPadSize
                )
                {
                    StationTypes = MarketStationTypeChips.Selected.ToArray(),
                };
                (IReadOnlyList<MiningMarketResult> found, string source) =
                    await client.FindMarketsPreferringArdentAsync(query, token);
                sources.Add(source);
                quotes.AddRange(
                    found.Select(quote =>
                        quote.Distance is null && origin is { } from && quote.Position is { } at
                            ? quote with
                            {
                                Distance = from.DistanceTo(at),
                            }
                            : quote
                    )
                );
                quotes.AddRange(community?.Markets(query, origin, now) ?? []);
            }

            MiningMarketResult[] result = SortMarketQuotes(quotes);
            token.ThrowIfCancellationRequested();
            Markets = result;
            Status =
                $"{result.Length} prices for {commodities.Length} commodities · {string.Join("/", sources.OrderBy(source => source))}. Prices and quantities are observations, not guarantees.";
        });

    private bool TryGetMarketCommodities(out string[] commodities)
    {
        commodities = MarketCommodityChips
            .Selected.Where(commodity => !string.IsNullOrWhiteSpace(commodity))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumMarketCommodities)
            .ToArray();
        if (commodities.Length == 0)
        {
            Markets = [];
            Status = "Choose at least one commodity.";
            return false;
        }

        if (!GalaxyWide && string.IsNullOrWhiteSpace(Reference))
        {
            Markets = [];
            Status = "Choose a reference system or enable galaxy-wide search.";
            return false;
        }

        if (MarketMaximumVolume > 0 && MarketMinimumVolume > MarketMaximumVolume)
        {
            Markets = [];
            Status = "Minimum volume cannot exceed maximum volume.";
            return false;
        }

        return true;
    }

    private MiningMarketResult[] SortMarketQuotes(IEnumerable<MiningMarketResult> quotes)
    {
        IEnumerable<MiningMarketResult> merged = quotes
            .GroupBy(
                quote => quote.System + "\u001f" + quote.Station + "\u001f" + MiningCommodityName.Key(quote.Commodity),
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.OrderByDescending(quote => quote.Updated).First())
            .Where(quote => !SystemOnly || GalaxyWide || Same(quote.System, Reference));
        return Buying
            ? merged.OrderBy(quote => quote.Price).ThenBy(quote => quote.Distance ?? double.MaxValue).ToArray()
            : merged
                .OrderByDescending(quote => quote.Price)
                .ThenBy(quote => quote.Distance ?? double.MaxValue)
                .ToArray();
    }

    private async Task SearchAcquisitionTargetsAsync(CancellationToken token)
    {
        MiningSystemResult[] supporters = (await CollectPowerSystemsAsync("Fortified", token, galaxyWide: true))
            .Concat(await CollectPowerSystemsAsync("Stronghold", token, galaxyWide: true))
            .Where(system => system.Position is not null)
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (supporters.Length == 0)
        {
            Systems = [];
            Status = $"No Fortified or Stronghold systems for {PledgedPower} were found.";
            return;
        }

        GalacticCoordinate? origin = await ResolveOriginAsync(token);
        var pairs = new List<(MiningSystemResult Target, MiningSystemResult Miner)>();
        int candidateLimit = Math.Min(30, ResultLimit * 3);
        await AddViableReferenceAcquisitionPairsAsync(supporters, pairs, token);
        foreach (MiningSystemResult supporter in supporters)
        {
            token.ThrowIfCancellationRequested();
            await AddAcquisitionPairsForSupporterAsync(supporter, origin, pairs, token);

            if (HasEnoughNearestAcquisitionTargets(pairs, supporter.Distance, candidateLimit))
            {
                break;
            }
        }

        MiningSystemResult[] result = pairs
            .Select(pair => pair.Target)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .Take(candidateLimit)
            .ToArray();
        token.ThrowIfCancellationRequested();
        Systems = result;
        await PublishAcquisitionRowsAsync(
            pairs.Where(pair => result.Any(system => Same(system.System, pair.Target.System))).ToArray(),
            token
        );
        if (!Status.Contains(RequestFailed, StringComparison.Ordinal))
        {
            Status =
                AcquireRows.Count > 0
                    ? AcquireRows.Count
                        + " acquisition targets with matching rings and sell stations within 20 ly of a Fortified system or 30 ly of a Stronghold."
                        + PriceMarkNote
                    : "No acquisition target had both a matching ring and a qualifying sell station." + PriceMarkNote;
        }
    }

    private async Task AddAcquisitionPairsForSupporterAsync(
        MiningSystemResult supporter,
        GalacticCoordinate? origin,
        List<(MiningSystemResult Target, MiningSystemResult Miner)> pairs,
        CancellationToken token
    )
    {
        var query = new MiningSystemQuery(
            supporter.System,
            PowerplayPlan.AcquisitionReachLy(supporter.PowerState),
            Security.Trim(),
            Allegiance.Trim(),
            Government.Trim(),
            IsAny(State) ? "" : State.Trim(),
            Economy.Trim(),
            "",
            "",
            MinimumPopulation,
            0,
            PowerplayPlan.Acquire
        );
        int pageIndex = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningSystemPage bubble = await LoadAcquireBubbleAsync(query with { Page = pageIndex }, token);
            foreach (
                MiningSystemResult candidate in bubble.Systems.Where(candidate =>
                    PowerplayPlan.IsAcquisitionTarget(candidate, PowerState)
                )
            )
            {
                if (ContainsAcquisitionPair(pairs, candidate.System, supporter.System))
                {
                    continue;
                }

                pairs.Add(
                    (candidate with { Distance = PowerplayPlan.TravelDistance(origin, candidate.Position) }, supporter)
                );
            }

            hasMore = bubble.HasMore;
            pageIndex++;
        }
    }

    private static bool ContainsAcquisitionPair(
        IReadOnlyList<(MiningSystemResult Target, MiningSystemResult Miner)> pairs,
        string target,
        string miner
    ) => pairs.Any(pair => Same(pair.Target.System, target) && Same(pair.Miner.System, miner));

    private async Task AddViableReferenceAcquisitionPairsAsync(
        IReadOnlyList<MiningSystemResult> supporters,
        List<(MiningSystemResult Target, MiningSystemResult Miner)> pairs,
        CancellationToken token
    )
    {
        if (!ForceIncludeReference)
        {
            return;
        }

        IReadOnlyList<MiningSystemResult> found = await client.FindSystemsByNameAsync(Reference, [Reference], token);
        MiningSystemResult? target = found.FirstOrDefault(system =>
            Same(system.System, Reference)
            && PowerplayPlan.IsAcquisitionTarget(system, PowerState)
            && MatchesSelectedAcquisitionStates(system)
        );
        if (target?.Position is not { } position)
        {
            return;
        }

        MiningSystemResult[] nearby = supporters
            .Where(supporter =>
                supporter.Position is { } location
                && location.DistanceTo(position) <= PowerplayPlan.AcquisitionReachLy(supporter.PowerState)
            )
            .ToArray();
        if (nearby.Length == 0)
        {
            return;
        }

        string[] materials =
            SelectedMinerals.Length > 0
                ? SelectedMinerals
                : PowerplayMinerals.Where(PlanetaryMiningPlan.IsEdpmCommodity).ToArray();
        MiningMarketResult[] imports = await AcquireImportsAsync(target.System, materials, token);
        if (imports.Length == 0)
        {
            return;
        }

        IReadOnlyList<MiningRing> referenceRings = await client.FindRingsForSystemsAsync(
            new MiningRingQuery(Reference, "", OnlineRingType, Radius, OnlineMinimumHotspots, Minerals: materials)
            {
                GalaxyWide = true,
            },
            nearby.Select(supporter => supporter.System).ToArray(),
            token
        );
        foreach (MiningSystemResult supporter in nearby)
        {
            bool hasMatchingRing = referenceRings.Any(ring =>
                Same(ring.System, supporter.System)
                && MatchesRing(ring, Mineral, RingType, OnlineMinimumHotspots, Reserve, true)
                && imports.Any(quote =>
                    ring.Hotspots.Keys.Any(hotspot => MiningCommodityName.Same(hotspot, quote.Commodity))
                )
            );
            if (hasMatchingRing)
            {
                pairs.Add((target with { Distance = 0 }, supporter));
            }
        }
    }

    private static bool HasEnoughNearestAcquisitionTargets(
        IReadOnlyList<(MiningSystemResult Target, MiningSystemResult Miner)> pairs,
        double? supporterDistance,
        int candidateLimit
    )
    {
        double[] nearest = pairs
            .Select(pair => pair.Target)
            .DistinctBy(target => target.System, StringComparer.OrdinalIgnoreCase)
            .Select(target => target.Distance ?? double.PositiveInfinity)
            .Order()
            .Take(candidateLimit)
            .ToArray();
        return nearest.Length >= candidateLimit
            && (
                supporterDistance is null
                || double.IsPositiveInfinity(nearest[^1])
                || supporterDistance > nearest[^1] + PowerplayPlan.StrongholdReachLy
            );
    }

    private async Task PublishAcquisitionRowsAsync(
        (MiningSystemResult Target, MiningSystemResult Miner)[] pairs,
        CancellationToken token
    )
    {
        MeritRows = [];
        if (pairs.Length == 0)
        {
            AcquireRows = [];
            Changed(nameof(HasAcquireRows));
            Changed(nameof(HasMeritRows));
            Changed(nameof(ShowMeritRows));
            return;
        }

        IReadOnlyList<MiningRing> foundRings = [];
        IReadOnlyList<MiningMarketResult> quotes = [];
        IReadOnlyDictionary<string, long> averages = await AverageSellPricesAsync(token);
        try
        {
            IReadOnlyList<MiningSystemResult> liveSystems = await client.FindSystemsByNameAsync(
                Reference,
                pairs
                    .SelectMany(pair => new[] { pair.Target.System, pair.Miner.System })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                token
            );
            var liveByName = liveSystems.ToDictionary(system => system.System, StringComparer.OrdinalIgnoreCase);
            pairs = pairs
                .Select(pair =>
                    (
                        liveByName.TryGetValue(pair.Target.System, out MiningSystemResult? liveTarget)
                            ? liveTarget with
                            {
                                Distance = pair.Target.Distance,
                            }
                            : pair.Target,
                        liveByName.TryGetValue(pair.Miner.System, out MiningSystemResult? liveMiner)
                            ? liveMiner with
                            {
                                Distance = pair.Miner.Distance,
                            }
                            : pair.Miner
                    )
                )
                .ToArray();
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            // Missing live progress must not discard otherwise valid acquisition pairs.
        }

        try
        {
            foundRings = await client.FindRingsForSystemsAsync(
                new MiningRingQuery(
                    Reference,
                    "",
                    OnlineRingType,
                    Radius,
                    OnlineMinimumHotspots,
                    Minerals: SelectedMinerals
                )
                {
                    GalaxyWide = true,
                },
                pairs.Select(pair => pair.Miner.System).ToArray(),
                token
            );
            var ringSystems = foundRings
                .Where(MatchesSelectedMiningType)
                .Where(ring => MatchesRing(ring, Mineral, RingType, OnlineMinimumHotspots, Reserve, true))
                .Where(ring => MiningMaterialSelection.IncludesHotspot(ring.Hotspots, MineralChips.Selected))
                .Select(ring => ring.System)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            quotes = await TopStationCommoditiesAsync(
                pairs
                    .Where(pair => ringSystems.Contains(pair.Miner.System))
                    .Select(pair => pair.Target.System)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                token
            );
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            Status = RequestFailed;
        }

        AcquireRows = pairs
            .GroupBy(pair => pair.Target.System, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
                DescribeAcquisition(
                    group.First().Target,
                    group.Select(pair => pair.Miner).ToArray(),
                    foundRings,
                    quotes,
                    averages,
                    MineralChips.Selected.ToArray()
                )
            )
            .OfType<AcquireResultRowViewModel>()
            .OrderBy(row =>
                Systems.FirstOrDefault(system => Same(system.System, row.Target))?.Distance ?? double.MaxValue
            )
            .Take(ResultLimit)
            .ToArray();
        Changed(nameof(HasAcquireRows));
        Changed(nameof(ShowMeritRows));
    }

    private AcquireResultRowViewModel? DescribeAcquisition(
        MiningSystemResult target,
        MiningSystemResult[] miners,
        IReadOnlyList<MiningRing> rings,
        IReadOnlyList<MiningMarketResult> quotes,
        IReadOnlyDictionary<string, long> averageSellPrices,
        IReadOnlyList<string> materials
    )
    {
        string[] named = MiningMaterialSelection.Named(materials);
        MiningRing[] eligibleRings = rings
            .Where(ring => miners.Any(miner => Same(miner.System, ring.System)))
            .Where(MatchesSelectedMiningType)
            .Where(ring => MatchesRing(ring, Mineral, RingType, OnlineMinimumHotspots, Reserve, true))
            .Where(ring => MiningMaterialSelection.IncludesHotspot(ring.Hotspots, materials))
            .ToArray();
        PowerplayMeritRing[] miningRings = eligibleRings.Select(PowerplayMeritRank.DescribeRing).ToArray();
        if (miningRings.Length == 0)
        {
            return null;
        }
        PowerplayMeritStation[] stationQuotes = quotes
            .Where(quote => Same(quote.System, target.System))
            .Where(quote => named.Length == 0 || named.Any(name => MiningCommodityName.Same(name, quote.Commodity)))
            .Select(quote => new PowerplayMeritStation(
                quote.Station,
                quote.Type,
                quote.QuotedPad ?? quote.PadDescription,
                quote.Price,
                quote.Demand,
                "",
                quote.Commodity,
                quote.ArrivalLs,
                quote.Updated
            ))
            .ToArray();
        var miningHotspots = miningRings
            .SelectMany(ring => ring.SignalLines ?? [])
            .Select(line => MiningCommodityName.Key(line.Split(':')[0]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!stationQuotes.Any(station => miningHotspots.Contains(MiningCommodityName.Key(station.Commodity))))
        {
            return null;
        }
        var viableStations = stationQuotes
            .GroupBy(station => station.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Quotes = group.ToArray(),
                BestViable = group
                    .Where(station => miningHotspots.Contains(MiningCommodityName.Key(station.Commodity)))
                    .OrderByDescending(station => station.Price)
                    .FirstOrDefault(),
            })
            .Where(station => station.BestViable is not null)
            .OrderByDescending(station => station.BestViable!.Price)
            .ToArray();
        long[] stationScores = viableStations.Select(station => station.BestViable!.Price).ToArray();
        MeritStationBlockViewModel[] stations = viableStations
            .SelectMany(station =>
                MeritSystemRowViewModel.BuildStationBlocks(
                    new PowerplayMeritSystem(
                        target.System,
                        target.Distance,
                        target.Power,
                        target.PowerState,
                        target.State,
                        0,
                        miningRings,
                        station.Quotes
                    ),
                    station.BestViable!.Commodity,
                    averageSellPrices
                )
            )
            .ToArray();
        if (stations.Length == 0)
        {
            return null;
        }
        AcquireMinerViewModel[] minerRows = miners
            .Where(miner => eligibleRings.Any(ring => Same(ring.System, miner.System)))
            .Select(
                (miner, index) =>
                    new AcquireMinerViewModel(
                        miner.System,
                        eligibleRings
                            .Where(ring => Same(ring.System, miner.System))
                            .SelectMany(ring =>
                                MeritSystemRowViewModel.LinesForRing(
                                    miner.System,
                                    PowerplayMeritRank.DescribeRing(ring)
                                )
                            )
                            .ToArray(),
                        miner.PowerState,
                        PowerplayPowerLineViewModel.From(
                            miner.NearbyPowers.Count > 0 ? miner.NearbyPowers : [miner.Power],
                            miner.Conflict,
                            miner.Power,
                            miner.ControlProgress
                        ),
                        AcquireConnector.ForIndex(
                            index,
                            miners.Count(miner => eligibleRings.Any(ring => Same(ring.System, miner.System)))
                        )
                    )
            )
            .ToArray();
        return new AcquireResultRowViewModel(
            target.System,
            target.PowerState.Length == 0 ? UnknownState : target.PowerState,
            target.Distance is { } distance ? distance.ToString("N0", CultureInfo.CurrentCulture) + " ly" : "",
            stations,
            minerRows,
            new AcquireResultMetadata(
                target.State,
                PowerplayPowerLineViewModel.From(
                    target.NearbyPowers.Count > 0 ? target.NearbyPowers : [target.Power],
                    target.Conflict,
                    target.Power,
                    target.ControlProgress
                ),
                target.Distance,
                stationScores
            )
        );
    }

    private async Task<IReadOnlyList<MiningMarketResult>> TopStationCommoditiesAsync(
        IReadOnlyList<string> systems,
        CancellationToken token
    )
    {
        var quotes = new List<MiningMarketResult>();
        string[] named = SelectedMinerals;
        string[] materials =
            named.Length > 0 ? named : PowerplayMinerals.Where(PlanetaryMiningPlan.IsEdpmCommodity).ToArray();
        foreach (string system in systems.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                IReadOnlyList<MiningMarketResult> imports = await AcquireImportsAsync(system, materials, token);
                quotes.AddRange(
                    imports
                        .Where(market =>
                            market.Price > 0
                            && market.Demand > 0
                            && PlanetaryMiningPlan.IsEdpmCommodity(market.Commodity)
                        )
                        .GroupBy(market => market.Station, StringComparer.OrdinalIgnoreCase)
                        .SelectMany(group =>
                            group
                                .OrderByDescending(market => market.Price)
                                .DistinctBy(market => MiningCommodityName.Key(market.Commodity), StringComparer.Ordinal)
                        )
                );
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                Status = RequestFailed;
            }
        }

        return quotes;
    }

    private async Task<IReadOnlyList<MiningSystemResult>> CollectPowerSystemsAsync(
        string state,
        CancellationToken token,
        bool galaxyWide = false
    )
    {
        if (galaxyWide)
        {
            return await client.FindAcquireSupportersAsync(Reference, PledgedPower, state, token);
        }

        var all = new List<MiningSystemResult>();
        int pageIndex = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningSystemPage resultPage = await client.FindSystemPageAsync(
                new MiningSystemQuery(Reference, Radius, Power: PledgedPower, PowerState: state, Page: pageIndex),
                token
            );
            all.AddRange(
                resultPage.Systems.Where(system =>
                    PowerplayPlan.SamePower(system.Power, PledgedPower) && Same(system.PowerState, state)
                )
            );
            hasMore = resultPage.HasMore;
            pageIndex++;
        }

        return all;
    }

    public Task SearchSystemsAsync() =>
        Run(
            async token =>
            {
                if (await SearchSpecializedSystemsAsync(token))
                {
                    return;
                }

                string powerFilter = Objective switch
                {
                    ReinforceObjective when IsChosenPower(PledgedPower) => PledgedPower,
                    UndermineObjective when IsChosenPower(OpposingPower) => OpposingPower,
                    _ => "",
                };
                var query = new MiningSystemQuery(
                    Reference,
                    Radius,
                    Security.Trim(),
                    Allegiance.Trim(),
                    Government.Trim(),
                    IsAny(State) ? "" : State.Trim(),
                    Economy.Trim(),
                    powerFilter,
                    PowerState.Trim(),
                    MinimumPopulation,
                    Page,
                    Objective
                );
                var online = new List<MiningSystemResult>();
                for (int pageIndex = 0; pageIndex < 3; pageIndex++)
                {
                    IReadOnlyList<MiningSystemResult> fetched = await client.FindSystemsAsync(
                        query with
                        {
                            Page = pageIndex,
                        },
                        token
                    );
                    online.AddRange(fetched);
                    if (fetched.Count < 100)
                    {
                        break;
                    }
                }

                string source = PowerplayPlan.UsesLiveConflict(Objective, PowerState)
                    ? "Spansh conflict progress + local Powerplay observations"
                    : "Spansh + local Powerplay observations";

                IReadOnlyList<MiningSystemResult> local = community?.FindSystems(query, DateTimeOffset.UtcNow) ?? [];
                MiningSystemResult[] result = local
                    .Concat(online)
                    .DistinctBy(s => s.System, StringComparer.OrdinalIgnoreCase)
                    .Where(MatchesPowerplaySystem)
                    .OrderBy(s => s.Distance ?? double.MaxValue)
                    .ToArray();
                result = await IncludeReferenceSystemIfEligibleAsync(result, query, token);
                token.ThrowIfCancellationRequested();
                Systems = result;
                await PublishMeritRowsAsync(token);
                Status = MeritRows.Count + " locations, best sell price first. " + source + ". " + Status;
            },
            cacheSearch: true
        );

    private async Task<MiningSystemResult[]> IncludeReferenceSystemIfEligibleAsync(
        MiningSystemResult[] systems,
        MiningSystemQuery query,
        CancellationToken token
    )
    {
        if (!ForceIncludeReference || systems.Any(system => Same(system.System, Reference)))
        {
            return systems;
        }

        IReadOnlyList<MiningSystemResult> found = await client.FindSystemsByNameAsync(Reference, [Reference], token);
        MiningSystemResult? referenceSystem = found.FirstOrDefault(system =>
            Same(system.System, Reference) && MatchesReferenceSystemQuery(system, query)
        );
        return referenceSystem is null ? systems : systems.Append(referenceSystem with { Distance = 0 }).ToArray();
    }

    private bool MatchesReferenceSystemQuery(MiningSystemResult system, MiningSystemQuery query) =>
        MatchesPowerplaySystem(system)
        && MatchesOptional(query.Security, system.Security)
        && MatchesOptional(query.Allegiance, system.Allegiance)
        && MatchesOptional(query.Government, system.Government)
        && MatchesOptional(query.State, system.State)
        && MatchesOptional(query.Economy, system.Economy)
        && MatchesOptional(query.Power, system.Power)
        && MatchesOptional(query.PowerState, system.PowerState)
        && system.Population >= query.MinimumPopulation;

    private static bool MatchesOptional(string expected, string actual) => IsAny(expected) || Same(expected, actual);

    private async Task<bool> SearchSpecializedSystemsAsync(CancellationToken token)
    {
        if (IsPlanetaryMining)
        {
            await PublishPlanetaryRowsAsync(token);
            return true;
        }

        if (Objective == AcquireObjective && IsChosenPower(PledgedPower))
        {
            await SearchAcquisitionTargetsAsync(token);
            return true;
        }

        return false;
    }

    private async Task PublishMeritRowsAsync(CancellationToken token)
    {
        AcquireRows = [];
        Changed(nameof(HasAcquireRows));
        Changed(nameof(ShowMeritRows));
        if (Systems.Count == 0)
        {
            MeritRows = [];
            Status = "No systems matched.";
            return;
        }

        string[] named = SelectedMinerals;
        string commodity = named.FirstOrDefault() ?? "";
        if (IsPlanetaryMining)
        {
            await PublishPlanetaryRowsAsync(token);
            return;
        }

        try
        {
            string[] hotspotMinerals = HotspotMinerals(named);
            string[] pricedCommodities = hotspotMinerals
                .Where(name => IsPlanetaryMining || PlanetaryMiningPlan.IsEdpmCommodity(name))
                .ToArray();
            (IReadOnlyList<MiningMarketResult> radiusMarkets, string priceSource) = await ReportedStationPricesAsync(
                [],
                pricedCommodities,
                token,
                anywhereInRadius: true
            );
            if (ForceIncludeReference && !radiusMarkets.Any(market => Same(market.System, Reference)))
            {
                MiningMarketResult[] referenceQuotes = await AcquireImportsAsync(Reference, pricedCommodities, token);
                radiusMarkets = radiusMarkets
                    .Concat(referenceQuotes.Select(quote => quote with { Distance = 0 }))
                    .ToArray();
            }
            (IReadOnlyList<MiningRing> foundRings, IReadOnlyList<MiningMarketResult> foundMarkets) =
                await RingsForBestPricesAsync(hotspotMinerals, radiusMarkets, token);
            Systems = await CompleteSystemRecordsAsync(SystemsWithRings(Systems, foundRings), token);
            MeritRows = await PresentRowsAsync(
                PowerplayMeritRank.Compose(
                    Systems,
                    foundRings,
                    foundMarkets,
                    ResultLimit,
                    ForceIncludeReference ? Reference : ""
                ),
                commodity,
                token
            );
            Status =
                priceSource
                + " prices for "
                + (commodity.Length == 0 ? "the selected minerals" : commodity)
                + "."
                + PriceMarkNote;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            MeritRows = [];
            Status = RequestFailed;
        }
    }

    private async Task PublishPlanetaryRowsAsync(CancellationToken token)
    {
        AcquireRows = [];
        MeritRows = [];
        Changed(nameof(HasAcquireRows));
        Changed(nameof(ShowMeritRows));
        if (!planetaryStatusHooked)
        {
            PlanetarySearch.PropertyChanged += (_, args) =>
            {
                if (IsPlanetaryMining && args.PropertyName == nameof(SurfaceMiningSearchViewModel.Status))
                {
                    Status = PlanetarySearch.Status;
                }
            };
            planetaryStatusHooked = true;
        }

        PlanetarySearch.Reference = Reference;
        PlanetarySearch.Radius = Radius;
        PlanetarySearch.ResultLimit = ResultLimit;
        PlanetarySearch.ForceIncludeReference = ForceIncludeReference;
        PlanetarySearch.MinimumDemand = MinimumDemand;
        PlanetarySearch.MaximumDemand = MaximumDemand;
        PlanetarySearch.PadSize = PadSize;
        PlanetarySearch.MaximumAge = MarketFreshness;
        PlanetarySearch.GroupStationsBySystem = true;
        bool acquire = Objective == AcquireObjective;
        PlanetarySearch.MarketGalaxyWide = false;
        PlanetarySearch.ExcludeCarrierMarkets = true;
        PlanetarySearch.UseAdditionalMarketsOnly = acquire;
        PlanetarySearch.DefaultSellDistanceSort = Objective == AcquireObjective;
        PlanetarySearch.BodyControllingPowers = [];
        PlanetarySearch.BodySearchRadius = Objective == AcquireObjective ? PowerplayPlan.StrongholdReachLy : 1;
        PlanetarySearch.NoSellStationsMessage =
            Objective == AcquireObjective
                ? "No unoccupied sell station matches the selected demand, landing pad, and supporter reach."
                : "No sell station matches the selected Powerplay goal, power, state, demand, and landing pad within the distance.";
        PlanetarySearch.NoMatchingBodiesMessage =
            Objective == AcquireObjective
                ? "No qualifying landable body was found around a Fortified or Stronghold supporter for the sell systems checked."
                : "No sell station has a matching surface mining body within the distance.";
        var acquisitionSources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var sellSystemDetails = new Dictionary<string, SurfaceSellSystemDetails>(StringComparer.OrdinalIgnoreCase);
        PlanetarySearch.SellSystemDetailsFor = system => sellSystemDetails.GetValueOrDefault(system);
        PlanetarySearch.MiningSystemsForSell = system =>
            Objective == AcquireObjective
                ? acquisitionSources.GetValueOrDefault(system) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>([system], StringComparer.OrdinalIgnoreCase);
        PlanetarySearch.EligibleSellSystemsAsync = async (systems, filterToken) =>
            await EligiblePlanetarySellSystemsAsync(systems, acquisitionSources, sellSystemDetails, filterToken);
        PlanetarySearch.AdditionalMarketQuotesAsync = null;
        if (acquire)
        {
            AcquireMarketCursor cursor = await CreateAcquireMarketCursorAsync(token);
            PlanetarySearch.AdditionalMarketQuotesAsync = (wanted, filterToken) =>
                FindPlanetaryAcquireQuotesAsync(cursor, wanted, acquisitionSources, sellSystemDetails, filterToken);
        }
        PlanetarySearch.Materials.Selected.Clear();
        foreach (string material in SelectedPlanetaryMaterials())
        {
            PlanetarySearch.Materials.Add(material);
        }

        await PlanetarySearch.SearchAsync(token);
        Status = PlanetarySearch.Status;
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindPlanetaryAcquireQuotesAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        if (ForceIncludeReference && !cursor.ReferenceChecked)
        {
            cursor.ReferenceChecked = true;
            IReadOnlyList<MiningMarketResult> forced = await FindForcedAcquireReferenceQuotesAsync(
                cursor,
                materials,
                acquisitionSources,
                sellSystemDetails,
                token
            );
            if (forced.Count > 0)
            {
                return forced;
            }
        }

        return await FindAdditionalAcquireMarketsAsync(cursor, materials, acquisitionSources, sellSystemDetails, token);
    }

    private async Task<AcquireMarketCursor> CreateAcquireMarketCursorAsync(CancellationToken token)
    {
        MiningSystemResult[] supporters = (await CollectPowerSystemsAsync("Fortified", token, galaxyWide: true))
            .Concat(await CollectPowerSystemsAsync("Stronghold", token, galaxyWide: true))
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        GalacticCoordinate? origin = await ResolveOriginAsync(token);
        if (origin is null)
        {
            IReadOnlyList<MiningSystemResult> reference = await client.FindSystemsByNameAsync(
                Reference,
                [Reference],
                token
            );
            origin = reference.FirstOrDefault(system => Same(system.System, Reference))?.Position;
        }

        return new AcquireMarketCursor(supporters, origin);
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindForcedAcquireReferenceQuotesAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        IReadOnlyList<MiningSystemResult> found = await client.FindSystemsByNameAsync(Reference, [Reference], token);
        MiningSystemResult? target = found.FirstOrDefault(system =>
            Same(system.System, Reference)
            && PowerplayPlan.IsAcquisitionTarget(system, PowerState)
            && MatchesSelectedAcquisitionStates(system)
        );
        if (target is null)
        {
            return [];
        }

        HashSet<string> sources = await FindNearbyAcquisitionSourcesAsync(target.System, token);
        if (sources.Count == 0)
        {
            return [];
        }

        MiningMarketResult[] imports = await AcquireImportsAsync(target.System, materials, token);
        if (imports.Length == 0)
        {
            return [];
        }

        acquisitionSources[target.System] = sources;
        sellSystemDetails[target.System] = new SurfaceSellSystemDetails(
            target.PowerState,
            target.State,
            target.NearbyPowers.Count > 0 ? target.NearbyPowers : [target.Power],
            0
        )
        {
            Conflict = target.Conflict,
            ControllingPower = target.Power,
            ControlProgress = target.ControlProgress,
        };
        cursor.Seen.Add(target.System);
        return imports.Select(quote => quote with { Distance = 0 }).ToArray();
    }

    private async Task<IReadOnlyList<MiningMarketResult>> FindAdditionalAcquireMarketsAsync(
        AcquireMarketCursor cursor,
        IReadOnlyList<string> materials,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        List<MiningMarketResult> quotes;
        do
        {
            IReadOnlyList<MiningSystemResult> candidates = await CollectAcquireMarketBatchAsync(
                cursor,
                acquisitionSources,
                sellSystemDetails,
                token
            );
            if (candidates.Count == 0)
            {
                return [];
            }

            quotes = [];
            foreach (string system in candidates.Select(target => target.System))
            {
                MiningMarketResult[] imports = await AcquireImportsAsync(system, materials, token);
                double? distance = sellSystemDetails[system].DistanceLy;
                quotes.AddRange(imports.Select(quote => quote with { Distance = distance }));
            }
        } while (quotes.Count == 0);

        return quotes;
    }

    private async Task<IReadOnlyList<MiningSystemResult>> CollectAcquireMarketBatchAsync(
        AcquireMarketCursor cursor,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        var candidates = new List<MiningSystemResult>();
        var cachedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (candidates.Count < ResultLimit && await FillAcquireTargetQueueAsync(cursor, token))
        {
            (MiningSystemResult target, MiningSystemResult supporter, bool fromCache) = cursor.Targets.Dequeue();
            if (!MatchesSelectedAcquisitionStates(target))
            {
                continue;
            }

            var sources = cursor
                .Supporters.Where(source =>
                    source.Position is { } origin
                    && target.Position is { } position
                    && origin.DistanceTo(position) <= PowerplayPlan.AcquisitionReachLy(source.PowerState)
                )
                .Select(source => source.System)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            sources.Add(supporter.System);
            acquisitionSources[target.System] = sources;
            double? distance = PowerplayPlan.TravelDistance(cursor.Origin, target.Position);
            if (distance is null)
            {
                IReadOnlyList<MiningSystemResult> located = await client.FindSystemsByNameAsync(
                    Reference,
                    [target.System],
                    token
                );
                distance = located.FirstOrDefault(system => Same(system.System, target.System))?.Distance;
            }

            sellSystemDetails[target.System] = new SurfaceSellSystemDetails(
                target.PowerState,
                target.State,
                target.NearbyPowers.Count > 0 ? target.NearbyPowers : [target.Power],
                distance
            )
            {
                Conflict = target.Conflict,
                ControllingPower = target.Power,
                ControlProgress = target.ControlProgress,
            };
            candidates.Add(target);
            if (fromCache)
            {
                cachedCandidates.Add(target.System);
            }
        }

        await RefreshAcquireCandidatesAsync(candidates, cachedCandidates, acquisitionSources, sellSystemDetails, token);
        return candidates;
    }

    private async Task RefreshAcquireCandidatesAsync(
        List<MiningSystemResult> candidates,
        HashSet<string> cachedCandidates,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        if (cachedCandidates.Count == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<MiningSystemResult> live = await client.FindSystemsByNameAsync(
                Reference,
                cachedCandidates.ToArray(),
                token
            );
            var liveByName = live.ToDictionary(system => system.System, StringComparer.OrdinalIgnoreCase);
            for (int index = candidates.Count - 1; index >= 0; index--)
            {
                MiningSystemResult candidate = candidates[index];
                if (!liveByName.TryGetValue(candidate.System, out MiningSystemResult? current))
                {
                    continue;
                }

                if (!PowerplayPlan.IsAcquisitionTarget(current, PowerState))
                {
                    acquisitionSources.Remove(candidate.System);
                    sellSystemDetails.Remove(candidate.System);
                    candidates.RemoveAt(index);
                    continue;
                }

                candidates[index] = current with { Distance = candidate.Distance };
                SurfaceSellSystemDetails details = sellSystemDetails[candidate.System];
                sellSystemDetails[candidate.System] = details with
                {
                    PowerState = current.PowerState,
                    FactionState = current.State,
                    Powers = current.NearbyPowers.Count > 0 ? current.NearbyPowers : [current.Power],
                    Conflict = current.Conflict,
                    ControllingPower = current.Power,
                    ControlProgress = current.ControlProgress,
                };
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            // Candidate geometry remains useful when a live progress lookup fails.
        }
    }

    private bool MatchesSelectedAcquisitionStates(MiningSystemResult target) =>
        !StateChips.Selected.Any(state => !IsAny(state)) || StateChips.Selected.Any(state => Same(state, target.State));

    private async Task<bool> FillAcquireTargetQueueAsync(AcquireMarketCursor cursor, CancellationToken token)
    {
        while (cursor.Targets.Count == 0)
        {
            if (cursor.CurrentSupporter is null)
            {
                if (cursor.SupporterIndex >= cursor.Supporters.Length)
                {
                    return false;
                }

                cursor.CurrentSupporter = cursor.Supporters[cursor.SupporterIndex++];
                cursor.BubblePage = 0;
            }

            MiningSystemResult supporter = cursor.CurrentSupporter;
            var query = new MiningSystemQuery(
                supporter.System,
                PowerplayPlan.AcquisitionReachLy(supporter.PowerState),
                Security.Trim(),
                Allegiance.Trim(),
                Government.Trim(),
                IsAny(State) ? "" : State.Trim(),
                Economy.Trim(),
                "",
                "",
                MinimumPopulation,
                cursor.BubblePage,
                AcquireObjective
            );
            MiningSystemPage bubble = await LoadAcquireBubbleAsync(query, token);
            foreach (
                MiningSystemResult target in bubble.Systems.Where(target =>
                    PowerplayPlan.IsAcquisitionTarget(target, PowerState)
                    && (PowerplayPlan.TravelDistance(supporter.Position, target.Position) ?? target.Distance)
                        <= PowerplayPlan.AcquisitionReachLy(supporter.PowerState)
                    && cursor.Seen.Add(target.System)
                )
            )
            {
                cursor.Targets.Enqueue((target, supporter, bubble.FromCache));
            }

            cursor.BubblePage++;
            if (!bubble.HasMore)
            {
                cursor.CurrentSupporter = null;
            }
        }

        return true;
    }

    private Task<MiningSystemPage> LoadAcquireBubbleAsync(MiningSystemQuery query, CancellationToken token) =>
        IsAny(PowerState) && !StateChips.Selected.Any(state => !IsAny(state))
            ? client.FindAcquireCandidatePageAsync(query, token)
            : client.FindSystemPageAsync(query, token);

    private async Task<MiningMarketResult[]> AcquireImportsAsync(
        string system,
        IReadOnlyList<string> materials,
        CancellationToken token
    )
    {
        var query = new MiningMarketQuery(
            system,
            "Any",
            false,
            ExcludeCarriers: true,
            SystemOnly: true,
            MinimumDemand: MinimumDemand,
            MaximumDemand: MaximumDemand,
            MaximumAge: MarketFreshness,
            PadSize: PadSize
        );
        IReadOnlyList<MiningMarketResult> imports;
        try
        {
            imports = await client.FindSystemImportsAsync(system, query, token);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            imports = [];
        }

        MiningMarketResult[] matching = imports
            .Where(quote => materials.Any(material => MiningCommodityName.Same(material, quote.Commodity)))
            .ToArray();
        if (matching.Length > 0)
        {
            return matching;
        }

        IReadOnlyList<MiningMarketResult> fallback = await client.FindSpanshSystemCommoditiesAsync(
            Reference,
            system,
            token
        );
        return fallback
            .Where(quote =>
                materials.Any(material => MiningCommodityName.Same(material, quote.Commodity))
                && quote.Demand >= MinimumDemand
                && (MaximumDemand == 0 || quote.Demand <= MaximumDemand)
                && (MarketFreshness is null || quote.Updated >= DateTimeOffset.UtcNow - MarketFreshness)
                && MatchesAcquirePad(quote)
            )
            .ToArray();
    }

    private bool MatchesAcquirePad(MiningMarketResult quote) =>
        PadSize switch
        {
            "L" => quote.LargePad == true,
            "M" => quote.QuotedPad is "Medium",
            "S" => quote.QuotedPad is "Small",
            _ => true,
        };

    private async Task<IReadOnlySet<string>> EligiblePlanetarySellSystemsAsync(
        IReadOnlyList<string> names,
        Dictionary<string, HashSet<string>> acquisitionSources,
        Dictionary<string, SurfaceSellSystemDetails> sellSystemDetails,
        CancellationToken token
    )
    {
        var eligible = names.Where(sellSystemDetails.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] unresolved = names.Where(name => !eligible.Contains(name)).ToArray();
        if (unresolved.Length == 0)
        {
            return eligible;
        }

        IReadOnlyList<MiningSystemResult> found = await client.FindSystemsByNameAsync(Reference, unresolved, token);
        string[] states = StateChips.Selected.Where(state => !IsAny(state)).ToArray();
        foreach (MiningSystemResult system in found)
        {
            if (
                Objective == AcquireObjective
                && PowerplayPlan.IsAcquisitionTarget(system, PowerState)
                && !acquisitionSources.ContainsKey(system.System)
            )
            {
                HashSet<string> sources = await FindNearbyAcquisitionSourcesAsync(system.System, token);
                if (sources.Count > 0)
                {
                    acquisitionSources[system.System] = sources;
                }
            }

            if (!IsEligiblePlanetarySellSystem(system, states, acquisitionSources))
            {
                continue;
            }

            eligible.Add(system.System);
            sellSystemDetails[system.System] = new SurfaceSellSystemDetails(
                system.PowerState,
                system.State,
                system.NearbyPowers.Count > 0 ? system.NearbyPowers : [system.Power],
                system.Distance
            )
            {
                Conflict = system.Conflict,
                ControllingPower = system.Power,
                ControlProgress = system.ControlProgress,
            };
        }

        return eligible;
    }

    private bool IsEligiblePlanetarySellSystem(
        MiningSystemResult system,
        string[] states,
        Dictionary<string, HashSet<string>> acquisitionSources
    )
    {
        if (states.Length > 0 && !states.Any(state => Same(state, system.State)))
        {
            return false;
        }

        if (Objective == AcquireObjective)
        {
            return PowerplayPlan.IsAcquisitionTarget(system, PowerState)
                && acquisitionSources.ContainsKey(system.System);
        }

        return MatchesPowerplaySystem(system) && (IsAny(PowerState) || Same(system.PowerState, PowerState));
    }

    private async Task<HashSet<string>> FindNearbyAcquisitionSourcesAsync(string target, CancellationToken token)
    {
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int pageIndex = 0;
        bool hasMore = true;
        while (hasMore)
        {
            MiningSystemPage nearby = await client.FindSystemPageAsync(
                new MiningSystemQuery(target, PowerplayPlan.StrongholdReachLy, Power: PledgedPower, Page: pageIndex),
                token
            );
            foreach (MiningSystemResult supporter in nearby.Systems)
            {
                if (
                    PowerplayPlan.SamePower(supporter.Power, PledgedPower)
                    && supporter.PowerState is "Fortified" or "Stronghold"
                    && supporter.Distance <= PowerplayPlan.AcquisitionReachLy(supporter.PowerState)
                )
                {
                    sources.Add(supporter.System);
                }
            }

            hasMore = nearby.HasMore;
            pageIndex++;
        }

        return sources;
    }

    private async Task<MeritSystemRowViewModel[]> PresentRowsAsync(
        IReadOnlyList<PowerplayMeritSystem> rows,
        string preferredCommodity,
        CancellationToken token
    )
    {
        IReadOnlyDictionary<string, long> averages = await AverageSellPricesAsync(token);
        var presented = new List<MeritSystemRowViewModel>();
        foreach (PowerplayMeritSystem row in rows)
        {
            presented.Add(
                MeritSystemRowViewModel.From(
                    await WithOtherCommoditiesAsync(row, token),
                    preferredCommodity,
                    averages,
                    MiningMaterialSelection.IsAny(MineralChips.Selected),
                    SelectedMinerals
                )
            );
        }

        return presented.ToArray();
    }

    private async Task<IReadOnlyDictionary<string, long>> AverageSellPricesAsync(CancellationToken token)
    {
        try
        {
            return await client.AverageSellPricesAsync(token);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<PowerplayMeritSystem> WithOtherCommoditiesAsync(
        PowerplayMeritSystem row,
        CancellationToken token
    )
    {
        if (row.Stations.Count == 0)
        {
            return row;
        }

        try
        {
            IReadOnlyList<MiningMarketResult> imports = await PowerplaySystemImportsAsync(
                row.Name,
                MarketFreshness ?? TimeSpan.FromDays(MaximumAgeDays),
                token
            );
            var stationNames = row.Stations.Select(station => station.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] named = SelectedMinerals;
            PowerplayMeritStation[] extra = imports
                .Where(market =>
                    stationNames.Contains(market.Station)
                    && market.Price > 0
                    && market.Demand > 0
                    && (
                        IsPlanetaryMining
                            ? PlanetaryMiningPlan.Materials.Any(material =>
                                MiningCommodityName.Same(material, market.Commodity)
                            )
                            : PlanetaryMiningPlan.IsEdpmCommodity(market.Commodity)
                                && (
                                    named.Length == 0
                                    || named.Any(name => MiningCommodityName.Same(name, market.Commodity))
                                )
                    )
                )
                .GroupBy(market => market.Station, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group =>
                    group
                        .OrderByDescending(market => market.Price)
                        .DistinctBy(market => MiningCommodityName.Key(market.Commodity), StringComparer.Ordinal)
                )
                .Select(market => new PowerplayMeritStation(
                    market.Station,
                    market.Type,
                    market.QuotedPad ?? market.PadDescription,
                    market.Price,
                    market.Demand,
                    "",
                    market.Commodity,
                    market.ArrivalLs,
                    market.Updated
                ))
                .ToArray();
            return extra.Length == 0 ? row : row with { Stations = extra };
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return row;
        }
    }

    private async Task<(
        IReadOnlyList<MiningRing> Rings,
        IReadOnlyList<MiningMarketResult> Markets
    )> RingsForBestPricesAsync(
        IReadOnlyList<string> minerals,
        IReadOnlyList<MiningMarketResult> markets,
        CancellationToken token
    )
    {
        PriceCandidate[] candidates = markets
            .GroupBy(market => market.System, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                MiningMarketResult[] quotes = group.ToArray();
                return new PriceCandidate(
                    group.Key,
                    quotes,
                    quotes.Max(quote => quote.Price),
                    quotes.Min(quote => quote.Distance ?? double.MaxValue)
                );
            })
            .OrderByDescending(candidate => ForceIncludeReference && Same(candidate.System, Reference))
            .ThenByDescending(candidate => candidate.Ceiling)
            .ThenBy(candidate => candidate.Distance)
            .ToArray();
        var collected = new List<MiningRing>();
        var kept = new List<MiningMarketResult>();
        var ranked = new List<long>();
        var ringQuery = new MiningRingQuery(
            Reference,
            "",
            OnlineRingType,
            Radius,
            OnlineMinimumHotspots,
            Minerals: minerals
        );
        for (int index = 0; index < candidates.Length; index += 20)
        {
            PriceCandidate[] batch = candidates.Skip(index).Take(20).ToArray();
            long weakest = PowerplayMeritRank.WeakestRankedPrice(ranked, ResultLimit);
            if (!PowerplayMeritRank.MorePricesCanRank(ranked.Count, ResultLimit, weakest, batch[0].Ceiling))
            {
                break;
            }

            IReadOnlyList<MiningRing> found = await client.FindRingsForSystemsAsync(
                ringQuery,
                batch.Select(candidate => candidate.System).ToArray(),
                token
            );
            MiningRing[] matched = found
                .Where(MatchesSelectedMiningType)
                .Where(ring => MatchesRing(ring, Mineral, RingType, OnlineMinimumHotspots, Reserve, true))
                .Where(ring => ring.Hotspots.Keys.Any(name => PlanetaryMiningPlan.IsEdpmCommodity(name)))
                .ToArray();
            collected.AddRange(matched);
            foreach (PriceCandidate candidate in batch)
            {
                HashSet<string> hotspots = HotspotsIn(matched, candidate.System);
                long price = candidate
                    .Quotes.Where(quote => hotspots.Contains(MiningCommodityName.Key(quote.Commodity)))
                    .Select(quote => quote.Price)
                    .DefaultIfEmpty(0)
                    .Max();
                if (price <= 0)
                {
                    continue;
                }

                ranked.Add(price);
                kept.AddRange(candidate.Quotes);
            }
        }

        return (collected, kept);
    }

    private static HashSet<string> HotspotsIn(IReadOnlyList<MiningRing> matches, string system)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            MiningRing ring in matches.Where(ring => ring.System.Equals(system, StringComparison.OrdinalIgnoreCase))
        )
        {
            foreach (string name in ring.Hotspots.Keys.Where(PlanetaryMiningPlan.IsEdpmCommodity))
            {
                names.Add(MiningCommodityName.Key(name));
            }
        }

        return names;
    }

    private sealed record PriceCandidate(string System, MiningMarketResult[] Quotes, long Ceiling, double Distance);

    private async Task<(IReadOnlyList<MiningMarketResult> Markets, string Source)> ReportedStationPricesAsync(
        IReadOnlyList<string> systems,
        IReadOnlyList<string> commodities,
        CancellationToken token,
        bool anywhereInRadius = false
    )
    {
        string[] wantedSystems = systems.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] wantedCommodities = commodities
            .Where(commodity => commodity.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (wantedCommodities.Length == 0 || (!anywhereInRadius && wantedSystems.Length == 0))
        {
            return ([], "Ardent");
        }

        var reported = new List<MiningMarketResult>();
        var missing = new List<string>();
        foreach (string commodity in wantedCommodities)
        {
            IReadOnlyList<MiningMarketResult> found;
            try
            {
                found = await client.FindMarketsAsync(
                    new MiningMarketQuery(
                        Reference,
                        commodity,
                        false,
                        Radius,
                        false,
                        true,
                        PadSize == "L",
                        MaximumAgeDays,
                        "",
                        0,
                        false,
                        MinimumDemand,
                        MaximumDemand,
                        MarketFreshness,
                        PadSize
                    ),
                    token
                );
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                missing.Add(commodity);
                continue;
            }

            MiningMarketResult[] eligible = (
                anywhereInRadius
                    ? found
                    : found.Where(market => wantedSystems.Contains(market.System, StringComparer.OrdinalIgnoreCase))
            ).ToArray();
            if (eligible.Length == 0)
            {
                missing.Add(commodity);
            }
            else
            {
                reported.AddRange(eligible);
            }
        }

        if (missing.Count == 0)
        {
            return (reported, "Ardent");
        }

        IReadOnlyList<MiningSellQuote> quotes;
        try
        {
            quotes = await client.FindSellQuotesAsync(
                Reference,
                Radius,
                wantedSystems,
                missing,
                MarketFreshness ?? TimeSpan.FromDays(MaximumAgeDays),
                token
            );
        }
        catch (Exception ex) when (IsProviderFailure(ex) && reported.Count > 0)
        {
            return (reported, "Ardent; Spansh fallback unavailable");
        }

        bool hadArdent = reported.Count > 0;
        reported.AddRange(
            quotes
                .Where(FitsQuotedStation)
                .Select(quote => new MiningMarketResult(
                    quote.System,
                    quote.Station,
                    quote.StationType,
                    null,
                    quote.ArrivalLs,
                    quote.Price,
                    quote.Demand,
                    0,
                    quote.Updated,
                    0,
                    quote.Pad.Equals("Large", StringComparison.OrdinalIgnoreCase)
                )
                {
                    Commodity = quote.Commodity,
                    QuotedPad = quote.Pad,
                })
        );
        return (reported, hadArdent ? "Ardent/Spansh fallback" : "Spansh fallback");
    }

    private bool FitsQuotedStation(MiningSellQuote quote)
    {
        if (quote.Demand < MinimumDemand || (MaximumDemand > 0 && quote.Demand > MaximumDemand))
        {
            return false;
        }

        return PadSize switch
        {
            "L" => quote.Pad == "Large",
            "M" => quote.Pad == "Medium",
            "S" => quote.Pad == "Small",
            _ => true,
        };
    }

    private IReadOnlyList<string> SurfaceMinerals { get; } =
        new[] { MiningMaterialSelection.Any }.Concat(PlanetaryMiningPlan.Materials).ToArray();

    private string[] SelectedMinerals => MiningMaterialSelection.Named(MineralChips.Selected);

    private Task<IReadOnlyList<MiningMarketResult>> PowerplaySystemImportsAsync(
        string system,
        TimeSpan maximumAge,
        CancellationToken token
    ) =>
        client.FindSystemImportsAsync(
            system,
            new MiningMarketQuery(
                system,
                "Any",
                false,
                ExcludeCarriers: true,
                SystemOnly: true,
                MaximumAge: maximumAge
            ),
            token
        );

    private string[] SelectedPlanetaryMaterials()
    {
        string[] named = SelectedMinerals;
        if (named.Length > 0)
        {
            return named;
        }

        return
            MiningMaterialSelection.IsAny(MineralChips.Selected)
            || !PlanetaryMiningPlan.Materials.Contains(Mineral, StringComparer.OrdinalIgnoreCase)
            ? [MiningMaterialSelection.Any]
            : [Mineral];
    }

    private string PriceMarkNote => client.PriceMarksUnavailable ? " " + RequestFailed : "";

    private bool nearestFirst = true;
    public string DistanceSortLabel => nearestFirst ? "Nearest first" : "Farthest first";
    public string DistanceSortIndicator => nearestFirst ? "↑" : "↓";
    public string ResultOrderLabel
    {
        get
        {
            if (!IsPlanetaryMining)
            {
                return "Results, best sell price first";
            }

            return nearestFirst
                ? "Results, nearest reference distance first"
                : "Results, farthest reference distance first";
        }
    }
    private WorkspaceCommand? distanceSortCommand;
    public ICommand DistanceSortCommand => distanceSortCommand ??= new WorkspaceCommand(ToggleDistanceSort);

    private void ToggleDistanceSort()
    {
        nearestFirst = !nearestFirst;
        MeritRows = OrderByDistance(MeritRows);
        Changed(nameof(DistanceSortLabel));
        Changed(nameof(DistanceSortIndicator));
        Changed(nameof(ResultOrderLabel));
    }

    private MeritSystemRowViewModel[] OrderByDistance(IEnumerable<MeritSystemRowViewModel> rows) =>
        (
            nearestFirst
                ? rows.OrderBy(row => row.DistanceLy ?? double.MaxValue)
                : rows.OrderByDescending(row => row.DistanceLy ?? double.MaxValue)
        ).ToArray();

    private void SyncMineralCatalogue()
    {
        bool surface = IsPlanetaryMining;
        if (surface != showingSurfaceMaterials)
        {
            showingSurfaceMaterials = surface;
            if (!surface && PlanetaryMiningPlan.IsSurfaceExclusive(Mineral))
            {
                Mineral = AnyPower;
            }

            Changed(nameof(RingMinerals));
            syncingPrimaryMineral = true;
            primaryMineralChips.ReplaceChoices(RingMinerals, AnyPower);
            syncingPrimaryMineral = false;
            if (primaryMineralChips.Selected.FirstOrDefault() is { } current && !Same(current, Mineral))
            {
                Mineral = current;
            }
            if (surface)
            {
                MineralChips.ReplaceChoices(SurfaceMinerals, MiningMaterialSelection.Any);
            }
            else
            {
                MineralChips.ReplaceChoices(PowerplayMinerals, MiningMaterialSelection.Default);
            }
        }

        Changed(nameof(UsesRingFilters));
        Changed(nameof(IsPlanetaryMining));
        Changed(nameof(IsPlanetaryAcquire));
        Changed(nameof(IsPlanetaryCombined));
        Changed(nameof(HasMeritRows));
        Changed(nameof(ResultOrderLabel));
    }

    public Task SearchTradersAsync() =>
        Run(async token =>
        {
            (IReadOnlyList<MiningMarketResult> result, string source) = await client.FindTradersPreferringArdentAsync(
                Reference,
                TraderType,
                Radius,
                Page,
                PadSize switch
                {
                    "L" => 3,
                    "M" => 2,
                    "S" => 1,
                    _ => 1,
                },
                token
            );
            token.ThrowIfCancellationRequested();
            Traders = result;
            Status =
                result.Count
                + " material traders · "
                + source
                + (
                    source == "Ardent"
                        ? ". Ardent does not separate Raw, Manufactured, and Encoded."
                        : ". " + TraderType + "."
                );
        });

    public void Bookmark()
    {
        if (SelectedRing is not { } ring)
        {
            return;
        }

        bookmarks.AddMiningLocation(
            new GalacticBookmark
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
            }
        );
        Status = bookmarks.Status;
    }

    public void CacheSelectedRing()
    {
        if (SelectedRing is { } ring)
        {
            cacheRing(ring);
            Status = "Ring saved to your local discoveries.";
        }
    }

    public void UseSelectedSystem()
    {
        if (SelectedSystem is not { } selected)
        {
            return;
        }

        Reference = selected.System;
        SystemOnly = true;
        Page = 0;
        Destination = 0;
        planningTarget = Objective == AllSystems ? "" : selected.System;
        planningObjective = Objective;
        miningOrigin = selected.System;
        Changed(nameof(PlanningContext));
        Changed(nameof(HasPlan));
    }

    public async Task FindSelectedSystemRingsAsync()
    {
        if (SelectedSystem is null)
        {
            return;
        }

        UseSelectedSystem();
        await SearchRingsAsync();
    }

    public async Task FindSellingStationsAsync()
    {
        if (SelectedRing is not { } ring)
        {
            return;
        }

        miningOrigin = ring.System;
        bool hasPlan = planningTarget.Length > 0 && planningObjective == Objective;
        Reference = hasPlan && Objective == AcquireObjective ? planningTarget : ring.System;
        SystemOnly = hasPlan;
        Changed(nameof(PlanningContext));
        Changed(nameof(HasPlan));
        if (Mineral.Length > 0 && !IsAny(Mineral))
        {
            CommodityCategory = MiningCommodityCategory;
            Commodity = Mineral;
        }

        Buying = false;
        Changed(nameof(TradeMode));
        GalaxyWide = false;
        Page = 0;
        Destination = 1;
        await SearchMarketsAsync();
    }

    public string PowerplaySummary =>
        $"{Objective} · {(IsAny(PledgedPower) ? "Any Power" : PledgedPower)} · {Mineral} · {RingType} · {Reserve} reserve · within {Radius:0} ly";

    public void ResetPowerplay()
    {
        Objective = ReinforceObjective;
        PledgedPower = detectedPower.Length > 0 ? detectedPower : AnyPower;
        OpposingPower = AnyPower;
        MiningType = "All";
        AlsoMineral = AnyPower;
        PadSize = AnyPower;
        LimitMarketAge = true;
        MarketAge = 48;
        MarketAgeUnit = "Hours";
        SystemState = AnyPower;
        Power = "";
        PowerState = "";
        Security = "";
        Allegiance = "";
        Government = "";
        Economy = "";
        State = "";
        MinimumPopulation = 0;
        Mineral = AnyPower;
        RingType = "All";
        Reserve = "All";
        MinimumHotspots = 1;
        MinimumDemand = 0;
        MaximumDemand = 90_000;
        LargePads = false;
        MaximumAgeDays = 2;
        Radius = 100;
        ResultLimit = 30;
        ForceIncludeReference = false;
        restoredPowerplaySearch = false;
        preserveRestoredReference = false;
        preserveRestoredPower = false;
        Page = 0;
        SystemOnly = false;
        Systems = [];
        Rings = [];
        Markets = [];
        MeritRows = [];
        AcquireRows = [];
        PlanetarySearch.Cancel();
        RestoreChip(MiningTypeChips, "All");
        RestoreChip(MineralChips, MiningMaterialSelection.Default);
        RestoreChip(StateChips, AnyPower);
        ClearPlan();
        UseCurrentLocation();
        Status = "Powerplay mining filters reset to commander defaults.";
        Changed(nameof(PowerplaySummary));
        Changed(nameof(RingMinerals));
    }

    private static void RestoreChip(MiningChipBoxViewModel chips, string token)
    {
        string[] selected = chips.Selected.ToArray();
        foreach (string item in selected)
        {
            chips.Remove(item);
        }

        chips.Add(token);
    }

    public MiningSearchPreferences SaveOptions() => options;

    public void ConfigureResultCache(MiningSearchResultCache cache)
    {
        resultCache?.HideIrrelevantMaterialTagsChanged -= SyncHideIrrelevantTags;
        resultCache = cache;
        cache.HideIrrelevantMaterialTagsChanged += SyncHideIrrelevantTags;
        PlanetarySearch.HideIrrelevantTagsChanged = value => cache.HideIrrelevantMaterialTags = value;
        PlanetarySearch.HideIrrelevantMaterialTags = cache.HideIrrelevantMaterialTags;
        PropertyChanged += RestorePowerplayWhenFiltersChange;
        MiningTypeChips.Selected.CollectionChanged += (_, _) => TryRestorePowerplaySearch();
        MineralChips.Selected.CollectionChanged += (_, _) => TryRestorePowerplaySearch();
        StateChips.Selected.CollectionChanged += (_, _) => TryRestorePowerplaySearch();
        RestoreLastCompletedPowerplaySearch();
    }

    public void RestoreLastCompletedPowerplaySearch()
    {
        if (resultCache?.LoadLast<PowerplaySearchSnapshot>("powerplay") is { } last)
        {
            RestorePowerplaySnapshot(last, restoreFilters: true);
        }
    }

    private void SyncHideIrrelevantTags(bool value) => PlanetarySearch.HideIrrelevantMaterialTags = value;

    internal string PowerplayCacheKey() =>
        JsonSerializer.Serialize(
            new
            {
                Options = options with
                {
                    Reference = Reference.Trim().ToUpperInvariant(),
                    PledgedPower = PledgedPower,
                },
                PledgedPower,
                Objective,
                MiningTypes = MiningTypeChips
                    .Selected.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Minerals = MineralChips.Selected.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
                States = StateChips.Selected.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).ToArray(),
            }
        );

    private PowerplaySearchSnapshot ExportPowerplaySnapshot() =>
        new(
            SaveOptions(),
            Objective,
            MiningTypeChips.Selected.ToArray(),
            MineralChips.Selected.ToArray(),
            StateChips.Selected.ToArray(),
            Status,
            MeritRows.Select(row => row.ExportSnapshot()).ToArray(),
            AcquireRows.Select(AcquireResultSnapshot.From).ToArray(),
            IsPlanetaryMining ? PlanetarySearch.ExportSnapshot() : null
        )
        {
            SavedAt = DateTimeOffset.UtcNow,
            PledgedPower = PledgedPower,
        };

    private void RestorePowerplaySnapshot(PowerplaySearchSnapshot snapshot, bool restoreFilters)
    {
        restoringCachedSearch = true;
        try
        {
            if (restoreFilters)
            {
                LoadOptions(snapshot.Options);
                PledgedPower = snapshot.PledgedPower.Length > 0 ? snapshot.PledgedPower : snapshot.Options.PledgedPower;
                Objective = snapshot.Objective;
                RestoreSelections(MiningTypeChips, snapshot.MiningTypes);
                RestoreSelections(MineralChips, snapshot.Minerals);
                RestoreSelections(StateChips, snapshot.States);
            }

            MeritRows = snapshot.MeritRows.Select(MeritSystemRowViewModel.RestoreSnapshot).ToArray();
            AcquireRows = snapshot.AcquireRows.Select(row => row.Restore()).ToArray();
            if (snapshot.PlanetaryRows is { } planetary)
            {
                PlanetarySearch.RestoreSnapshot(planetary, restoreFilters: true);
            }
            else
            {
                PlanetarySearch.ClearResults();
            }
            Status = snapshot.SavedAt is { } savedAt
                ? snapshot.Status
                    + " Saved "
                    + savedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    + "; press Search to refresh."
                : snapshot.Status;
            Changed(nameof(HasMeritRows));
            Changed(nameof(HasAcquireRows));
            Changed(nameof(IsPlanetaryAcquire));
            Changed(nameof(IsPlanetaryCombined));
            if (restoreFilters)
            {
                restoredPowerplaySearch = true;
                preserveRestoredReference = true;
                preserveRestoredPower = true;
                referenceTracksCommander = false;
            }
        }
        finally
        {
            restoringCachedSearch = false;
        }
    }

    private static void RestoreSelections(MiningChipBoxViewModel chips, IReadOnlyList<string> selected)
    {
        chips.Selected.Clear();
        foreach (string item in selected)
        {
            chips.Add(item);
        }
    }

    private void RestorePowerplayWhenFiltersChange(object? sender, PropertyChangedEventArgs args)
    {
        if (
            args.PropertyName
            is nameof(Reference)
                or nameof(ForceIncludeReference)
                or nameof(Radius)
                or nameof(ResultLimit)
                or nameof(PledgedPower)
                or nameof(Objective)
                or nameof(OpposingPower)
                or nameof(Mineral)
                or nameof(RingType)
                or nameof(Reserve)
                or nameof(LimitMarketAge)
                or nameof(MarketAge)
                or nameof(MarketAgeUnit)
                or nameof(MinimumDemand)
                or nameof(MaximumDemand)
                or nameof(PadSize)
        )
        {
            TryRestorePowerplaySearch();
        }
    }

    private void TryRestorePowerplaySearch()
    {
        if (restoringCachedSearch || IsBusy || resultCache is null)
        {
            return;
        }

        if (resultCache.Load<PowerplaySearchSnapshot>("powerplay", PowerplayCacheKey()) is { } saved)
        {
            RestorePowerplaySnapshot(saved, restoreFilters: false);
        }
        else
        {
            MeritRows = [];
            AcquireRows = [];
            PlanetarySearch.ClearResults();
        }
    }

    public void LoadOptions(MiningSearchPreferences values)
    {
        pending?.Cancel();
        pending = null;
        detectedPower = "";
        PledgedPower = AnyPower;
        IsBusy = false;
        Rings = [];
        Markets = [];
        Systems = [];
        Traders = [];
        PlatinumSpots = [];
        SelectedPlatinumSpot = null;
        SelectedTrader = null;
        SystemOnly = false;
        powerplayPrepared = false;
        restoredPowerplaySearch = false;
        preserveRestoredReference = false;
        preserveRestoredPower = false;
        Objective = AllSystems;
        SelectedRing = null;
        SelectedMarket = null;
        SelectedSystem = null;
        ClearPlan();
        CommodityCategory = values.CommodityCategory;
        Reference = values.Reference;
        Mineral = values.Mineral;
        RingType = values.RingType;
        Reserve = values.Reserve;
        Commodity = values.Commodity;
        if (values.MarketCommodities is { Count: > 0 })
        {
            syncingMarketChips = true;
            marketCommodityChips.Selected.Clear();
            foreach (string commodity in values.MarketCommodities.Take(MaximumMarketCommodities))
            {
                marketCommodityChips.Add(commodity);
            }

            syncingMarketChips = false;
            SyncMarketCommodities();
        }
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
        MarketPadSize = values.MarketPadSize;
        MarketMinimumVolume = values.MarketMinimumVolume;
        MarketMaximumVolume = values.MarketMaximumVolume;
        MarketMaximumAgeDays = values.MarketMaximumAgeDays;
        MinimumDemand = values.MinimumDemand;
        MaximumDemand = values.MaximumDemand == 0 ? 90_000 : values.MaximumDemand;
        if (!string.IsNullOrWhiteSpace(values.PledgedPower))
        {
            NoteDetectedPower(values.PledgedPower);
        }
        ResultLimit = values.ResultLimit;
        ForceIncludeReference = values.ForceIncludeReference;
        PlatinumMode = values.PlatinumMode;
        syncingMarketChips = true;
        marketStationTypeChips.Selected.Clear();
        foreach (string type in values.MarketStationTypes ?? [])
        {
            marketStationTypeChips.Add(type);
        }

        if (
            marketStationTypeChips.Selected.Count == 0
            && MarketStationTypeOptions.Contains(values.StationType, StringComparer.OrdinalIgnoreCase)
        )
        {
            marketStationTypeChips.Add(values.StationType);
        }

        syncingMarketChips = false;
        options = options with { MarketStationTypes = marketStationTypeChips.Selected.ToArray(), StationType = "" };
        Security = values.Security;
        Allegiance = values.Allegiance;
        Government = values.Government;
        Economy = values.Economy;
        State = values.State;
        Power = values.Power;
        OpposingPower = values.OpposingPower;
        MiningType = values.MiningType;
        AlsoMineral = values.AlsoMineral;
        PadSize = values.PadSize;
        LimitMarketAge = values.LimitMarketAge;
        MarketAge = values.MarketAge;
        MarketAgeUnit = values.MarketAgeUnit;
        PowerState = values.PowerState;
        MinimumPopulation = values.MinimumPopulation;
        TraderType = values.TraderType;
        Page = 0;
    }

    public void UseDiagnosticLog(Action<string>? log) => client.DiagnosticLog = log;

    public void Cancel()
    {
        pending?.Cancel();
        PlanetarySearch.Cancel();
    }

    public void Dispose()
    {
        resultCache?.HideIrrelevantMaterialTagsChanged -= SyncHideIrrelevantTags;
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        PlanetarySearch.Dispose();
    }

    private async Task Run(Func<CancellationToken, Task> action, bool cacheSearch = false)
    {
        CancellationTokenSource? previous = pending;
        using var current = new CancellationTokenSource(SearchTimeout());
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

            token.ThrowIfCancellationRequested();
            await action(token);
            if (
                cacheSearch
                && pending == current
                && resultCache is not null
                && !Status.Contains(RequestFailed, StringComparison.Ordinal)
            )
            {
                resultCache.Save("powerplay", PowerplayCacheKey(), ExportPowerplaySnapshot());
                powerplayPrepared = true;
            }
        }
        catch (OperationCanceledException)
        {
            if (pending == current)
            {
                Status = "Search canceled or timed out.";
            }
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or System.Text.Json.JsonException
                        or ArgumentException
                        or InvalidOperationException
                        or IOException
                        or InvalidDataException
            )
        {
            if (pending == current)
            {
                Status = ex is ArgumentException ? ex.Message : RequestFailed;
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

    private TimeSpan SearchTimeout()
    {
        if (IsPlanetaryMining)
        {
            return TimeSpan.FromMinutes(8);
        }

        return TimeSpan.FromMinutes(Destination == 1 ? 5 : 2);
    }
}

public sealed record PlatinumSpotRowViewModel(
    MiningRing Ring,
    int HotspotCount,
    bool HasRes,
    bool HasOverlap,
    int Score,
    string Features
)
{
    public string System => Ring.System;
    public string Body => Ring.Body;
    public string Reserve => Ring.Reserve;
    public string Power => Ring.Power;
    public string ResourceExtractionSites => Ring.ResourceExtractionSites;
    public string Overlaps => Ring.Overlaps;
    public double? DistanceLy => Ring.DistanceLy;
    public double? ArrivalLs => Ring.ArrivalLs;

    public static PlatinumSpotRowViewModel Create(MiningRing ring)
    {
        int hotspotCount = ring.Hotspots.GetValueOrDefault("Platinum");
        bool hasRes = ring.ResourceExtractionSites.Length > 0;
        bool hasOverlap = ring.Overlaps.Length > 0;
        int score =
            hotspotCount * 10 + (hasRes ? 30 : 0) + (hasOverlap ? 20 : 0) + (ring.Reserve == "Pristine" ? 10 : 0);
        string features = string.Join(
            " · ",
            new[]
            {
                $"{hotspotCount} Platinum hotspot{(hotspotCount == 1 ? "" : "s")}",
                hasRes ? ring.ResourceExtractionSites : "",
                hasOverlap ? ring.Overlaps : "",
                ring.Reserve,
            }.Where(value => value.Length > 0)
        );
        return new(ring, hotspotCount, hasRes, hasOverlap, score, features);
    }
}
