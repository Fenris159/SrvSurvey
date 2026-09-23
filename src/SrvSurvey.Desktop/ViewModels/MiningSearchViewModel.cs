using System.Globalization;
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
    public static IReadOnlyList<string> CommodityCategories { get; } = MiningReferenceData.Commodities.Keys.ToArray();
    public IReadOnlyList<string> CommodityOptions =>
        MiningReferenceData.Commodities.GetValueOrDefault(CommodityCategory) ?? [];
    public string CommodityCategory
    {
        get => options.CommodityCategory;
        set
        {
            Set(ref options, options with { CommodityCategory = value ?? "Mining" });
            Changed(nameof(CommodityOptions));
        }
    }
    public string Reference
    {
        get => options.Reference;
        set
        {
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
    public string Mineral
    {
        get => options.Mineral;
        set
        {
            if (Set(ref options, options with { Mineral = value ?? PlatinumMineral }))
            {
                Changed(nameof(PowerplaySummary));
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
        set { Set(ref options, options with { Commodity = value ?? PlatinumMineral }); }
    }
    public double Radius
    {
        get => options.Radius;
        set
        {
            if (Set(ref options, options with { Radius = double.IsFinite(value) ? Math.Clamp(value, 1, 500) : 100 }))
            {
                Changed(nameof(PowerplaySummary));
            }
        }
    }
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
        set { Set(ref options, options with { Buying = value }); }
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
        set { Set(ref options, options with { ResultLimit = Math.Clamp(value, 1, 100) }); }
    }
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
        if (next.Length == 0)
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
            if (IsAny(pledgedPower) || pledgedPower.Equals(previousPower, StringComparison.OrdinalIgnoreCase))
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
        if (IsAny(pledgedPower) || pledgedPower.Equals(knownPower, StringComparison.OrdinalIgnoreCase))
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
        .. (MiningReferenceData.Commodities.GetValueOrDefault("Mining") ?? []).Where(name =>
            !PlanetaryMiningPlan.IsSurfaceExclusive(name)
        ),
    ];
    private static readonly string[] PlanetaryMineralChoices = [AnyPower, .. PlanetaryMiningPlan.Materials];
    private static readonly string[] PowerplayMineralChoices =
    [
        MiningMaterialSelection.Default,
        MiningMaterialSelection.Any,
        .. (MiningReferenceData.Commodities.GetValueOrDefault("Mining") ?? []).Where(name =>
            PlanetaryMiningPlan.IsEdpmCommodity(name)
        ),
    ];
    public IReadOnlyList<string> RingMinerals => IsPlanetaryMining ? PlanetaryMineralChoices : RingMineralChoices;
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
    private bool showingSurfaceMaterials;
    public MiningChipBoxViewModel StateChips { get; } = new("System state", FactionStates, "Any");
    private IReadOnlyList<MeritSystemRowViewModel> meritRows = [];
    private IReadOnlyList<AcquireResultRowViewModel> acquireRows = [];
    public IReadOnlyList<AcquireResultRowViewModel> AcquireRows
    {
        get => acquireRows;
        private set
        {
            if (Set(ref acquireRows, value))
            {
                Changed(nameof(HasAcquireRows));
                Changed(nameof(HasMeritRows));
            }
        }
    }
    public bool HasAcquireRows => AcquireRows.Count > 0;
    public bool HasMeritRows => !HasAcquireRows && meritRows.Count > 0;
    public bool ShowMeritRows => !HasAcquireRows;
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

    public Task SearchMarketsAsync() =>
        Run(async token =>
        {
            var query = new MiningMarketQuery(
                Reference,
                Commodity.Trim(),
                Buying,
                Radius,
                GalaxyWide,
                ExcludeCarriers,
                LargePads,
                MaximumAgeDays,
                StationType.Trim(),
                Page,
                SystemOnly,
                MinimumDemand,
                MaximumDemand,
                MarketFreshness,
                PadSize
            );
            (IReadOnlyList<MiningMarketResult> result, string source) = await client.FindMarketsPreferringArdentAsync(
                query,
                token
            );
            GalacticCoordinate? origin =
                community is not null && !query.GalaxyWide ? await ResolveOriginAsync(token) : null;
            IEnumerable<MiningMarketResult> merged = result
                .Concat(community?.Markets(query, origin, DateTimeOffset.UtcNow) ?? [])
                .GroupBy(r => (r.System, r.Station))
                .Select(g => g.OrderByDescending(r => r.Updated).First());
            if (query.SystemOnly && !query.GalaxyWide)
            {
                merged = merged.Where(r => Same(r.System, query.ReferenceSystem));
            }

            result = query.Buying
                ? merged.OrderBy(r => r.Price).ToArray()
                : merged.OrderByDescending(r => r.Price).ToArray();
            token.ThrowIfCancellationRequested();
            Markets = result;
            Status = $"{result.Count} markets · {source}. Prices and quantities are observations, not guarantees.";
        });

    private async Task SearchAcquisitionTargetsAsync(CancellationToken token)
    {
        MiningSystemResult[] supporters = (await CollectPowerSystemsAsync("Fortified", token))
            .Concat(await CollectPowerSystemsAsync("Stronghold", token))
            .Where(system => system.Position is not null)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (supporters.Length == 0)
        {
            Systems = [];
            Status =
                $"No Fortified or Stronghold systems for {PledgedPower} within {Radius:0} ly. Acquire needs one of those in range.";
            return;
        }

        var pairs = new List<(MiningSystemResult Target, MiningSystemResult Miner)>();
        foreach (MiningSystemResult supporter in supporters)
        {
            token.ThrowIfCancellationRequested();
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
                MiningSystemPage bubble = await client.FindSystemPageAsync(query with { Page = pageIndex }, token);
                foreach (
                    MiningSystemResult candidate in bubble.Systems.Where(candidate =>
                        PowerplayPlan.IsAcquisitionTarget(candidate, PowerState)
                    )
                )
                {
                    if (
                        pairs.Any(pair =>
                            Same(pair.Target.System, candidate.System) && Same(pair.Miner.System, supporter.System)
                        )
                    )
                    {
                        continue;
                    }

                    pairs.Add((candidate, supporter));
                }

                hasMore = bubble.HasMore;
                pageIndex++;
            }
        }

        GalacticCoordinate? origin = await ResolveOriginAsync(token);
        pairs = pairs
            .Select(pair =>
                (pair.Target with { Distance = PowerplayPlan.TravelDistance(origin, pair.Target.Position) }, pair.Miner)
            )
            .ToList();

        MiningSystemResult[] result = pairs
            .Select(pair => pair.Target)
            .DistinctBy(system => system.System, StringComparer.OrdinalIgnoreCase)
            .OrderBy(system => system.Distance ?? double.MaxValue)
            .Take(ResultLimit)
            .ToArray();
        token.ThrowIfCancellationRequested();
        Systems = result;
        await PublishAcquisitionRowsAsync(
            pairs.Where(pair => result.Any(system => Same(system.System, pair.Target.System))).ToArray(),
            token
        );
        Status =
            AcquireRows.Count
            + " acquisition targets within 20 ly of a Fortified system or 30 ly of a Stronghold. "
            + Status
            + PriceMarkNote;
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
            foundRings = await client.FindRingsAsync(
                new MiningRingQuery(
                    Reference,
                    "",
                    OnlineRingType,
                    Radius,
                    OnlineMinimumHotspots,
                    Minerals: SelectedMinerals
                ),
                token
            );
            quotes = await TopStationCommoditiesAsync(
                pairs.Select(pair => pair.Target.System).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
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
            .ToArray();
        Changed(nameof(HasAcquireRows));
        Changed(nameof(ShowMeritRows));
    }

    private static AcquireResultRowViewModel DescribeAcquisition(
        MiningSystemResult target,
        MiningSystemResult[] miners,
        IReadOnlyList<MiningRing> rings,
        IReadOnlyList<MiningMarketResult> quotes,
        IReadOnlyDictionary<string, long> averageSellPrices,
        IReadOnlyList<string> materials
    )
    {
        string[] named = MiningMaterialSelection.Named(materials);
        int commodityLimit = MiningMaterialSelection.StationLimit(materials, acquire: true);
        AcquireStationViewModel[] stations = quotes
            .Where(quote => quote.System.Equals(target.System, StringComparison.OrdinalIgnoreCase))
            .GroupBy(quote => quote.Station, StringComparer.OrdinalIgnoreCase)
            .Select(station =>
            {
                MiningMarketResult first = station.First();
                MiningMarketResult[] top = station
                    .Where(quote =>
                        named.Length == 0 || named.Any(name => MiningCommodityName.Same(name, quote.Commodity))
                    )
                    .OrderByDescending(quote => quote.Price)
                    .DistinctBy(quote => MiningCommodityName.Key(quote.Commodity), StringComparer.Ordinal)
                    .Take(commodityLimit)
                    .ToArray();
                return new AcquireStationViewModel(
                    first.Station,
                    first.PadDescription,
                    first.ArrivalLs is { } arrival
                        ? "Distance: " + arrival.ToString("0", CultureInfo.CurrentCulture) + " ls"
                        : "",
                    UpdatedAgo(first.Updated),
                    top.Select(quote => new AcquireQuoteViewModel(
                            MiningCommodityCode.Abbreviate(quote.Commodity),
                            quote.Price.ToString("N0", CultureInfo.CurrentCulture)
                                + " CR "
                                + MiningPriceMarks.For(
                                    quote.Price,
                                    averageSellPrices.TryGetValue(quote.Commodity, out long average) ? average : 0
                                ),
                            quote.Demand.ToString("N0", CultureInfo.CurrentCulture) + " Demand"
                        ))
                        .ToArray()
                );
            })
            .ToArray();
        AcquireMinerViewModel[] minerRows = miners
            .Select(
                (miner, index) =>
                    new AcquireMinerViewModel(
                        miner.System,
                        string.Join(
                            "\n",
                            rings
                                .Where(ring =>
                                    ring.System.Equals(miner.System, StringComparison.OrdinalIgnoreCase)
                                    && MiningMaterialSelection.IncludesHotspot(ring.Hotspots, materials)
                                )
                                .Select(ring =>
                                {
                                    string body = ring.Body.StartsWith(miner.System, StringComparison.OrdinalIgnoreCase)
                                        ? ring.Body[miner.System.Length..].Trim()
                                        : ring.Body;
                                    string hotspots = MiningMaterialSelection.HotspotText(ring.Hotspots, materials);
                                    return body + (hotspots.Length == 0 ? "" : ": " + hotspots);
                                })
                        ),
                        miner.PowerState,
                        miner.Power,
                        AcquireConnector.ForIndex(index, miners.Length)
                    )
            )
            .ToArray();
        return new AcquireResultRowViewModel(
            target.System,
            target.PowerState.Length == 0 ? UnknownState : target.PowerState,
            target.Distance is { } distance ? distance.ToString("N0", CultureInfo.CurrentCulture) + " ly" : "",
            stations,
            minerRows
        );
    }

    private async Task<IReadOnlyList<MiningMarketResult>> TopStationCommoditiesAsync(
        IReadOnlyList<string> systems,
        CancellationToken token
    )
    {
        var quotes = new List<MiningMarketResult>();
        foreach (string system in systems.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                IReadOnlyList<MiningMarketResult> imports = await client.FindSystemImportsAsync(
                    system,
                    MarketFreshness ?? TimeSpan.FromDays(MaximumAgeDays),
                    token
                );
                string[] named = SelectedMinerals;
                quotes.AddRange(
                    imports
                        .Where(market =>
                            market.Price > 0
                            && market.Demand > 0
                            && (
                                IsPlanetaryMining
                                    ? PlanetaryMiningPlan.Materials.Any(material =>
                                        MiningCommodityName.Same(material, market.Commodity)
                                    )
                                    : PlanetaryMiningPlan.IsEdpmCommodity(market.Commodity)
                            )
                            && (
                                named.Length == 0 || named.Any(name => MiningCommodityName.Same(name, market.Commodity))
                            )
                        )
                        .GroupBy(market => market.Station, StringComparer.OrdinalIgnoreCase)
                        .SelectMany(group =>
                            group
                                .OrderByDescending(market => market.Price)
                                .DistinctBy(market => MiningCommodityName.Key(market.Commodity), StringComparer.Ordinal)
                                .Take(MiningMaterialSelection.StationLimit(MineralChips.Selected, acquire: true))
                        )
                );
            }
            catch (Exception ex) when (IsProviderFailure(ex))
            {
                try
                {
                    quotes.AddRange(await client.FindSpanshSystemCommoditiesAsync(Reference, system, token));
                }
                catch (Exception fallback) when (IsProviderFailure(fallback))
                {
                    Status = RequestFailed;
                }
            }
        }

        return quotes;
    }

    private static string UpdatedAgo(DateTimeOffset? updated)
    {
        if (updated is null)
        {
            return "";
        }

        TimeSpan age = DateTimeOffset.UtcNow - updated.Value;
        if (age.TotalMinutes < 1)
        {
            return "Updated: just now";
        }

        if (age.TotalHours < 1)
        {
            return "Updated: " + (int)age.TotalMinutes + "m ago";
        }

        if (age.TotalDays < 1)
        {
            return "Updated: " + (int)age.TotalHours + "h " + age.Minutes + "m ago";
        }

        return "Updated: " + (int)age.TotalDays + "d ago";
    }

    private async Task<IReadOnlyList<MiningSystemResult>> CollectPowerSystemsAsync(
        string state,
        CancellationToken token
    )
    {
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
                resultPage.Systems.Where(system => Same(system.Power, PledgedPower) && Same(system.PowerState, state))
            );
            hasMore = resultPage.HasMore;
            pageIndex++;
        }

        return all;
    }

    public Task SearchSystemsAsync() =>
        Run(async token =>
        {
            string powerFilter = Objective switch
            {
                ReinforceObjective when IsChosenPower(PledgedPower) => PledgedPower,
                UndermineObjective when IsChosenPower(OpposingPower) => OpposingPower,
                _ => "",
            };
            if (IsPlanetaryMining)
            {
                await PublishPlanetaryRowsAsync(token);
                return;
            }

            if (Objective == AcquireObjective && IsChosenPower(PledgedPower))
            {
                await SearchAcquisitionTargetsAsync(token);
                return;
            }

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
            token.ThrowIfCancellationRequested();
            Systems = result;
            await PublishMeritRowsAsync(token);
            Status = MeritRows.Count + " locations, best sell price first. " + source + ". " + Status;
        });

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
            (IReadOnlyList<MiningRing> foundRings, IReadOnlyList<MiningMarketResult> foundMarkets) =
                await RingsForBestPricesAsync(hotspotMinerals, radiusMarkets, token);
            Systems = await CompleteSystemRecordsAsync(SystemsWithRings(Systems, foundRings), token);
            MeritRows = await PresentRowsAsync(
                PowerplayMeritRank.Compose(Systems, foundRings, foundMarkets, ResultLimit),
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
        string[] materials = SelectedMinerals.Length == 0 ? PlanetaryMiningPlan.Materials.ToArray() : SelectedMinerals;
        PlanetaryBodyCriteria? criteria = PlanetaryMiningPlan.For(materials);
        AcquireRows = [];
        Changed(nameof(HasAcquireRows));
        Changed(nameof(ShowMeritRows));
        if (criteria is null)
        {
            MeritRows = [];
            Status = "Choose a Surface Hunt material.";
            return;
        }

        try
        {
            var query = new MiningPlanetaryQuery(
                Reference,
                criteria.BodySubtypes,
                criteria.LandmarkSubtypes,
                "",
                Radius,
                PlanetaryPowers,
                PlanetaryPowerState,
                VolcanismTypes: criteria.VolcanismTypes
            );
            PlanetaryBodyCriteria[] materialRules = materials
                .Select(material => PlanetaryMiningPlan.For([material]))
                .OfType<PlanetaryBodyCriteria>()
                .ToArray();
            var allBodies = new List<MiningPlanetaryBody>();
            int pageIndex = 0;
            bool hasMore = true;
            while (hasMore)
            {
                MiningPlanetaryBodyPage found = await client.FindPlanetaryBodyPageAsync(
                    query with
                    {
                        Page = pageIndex,
                    },
                    token
                );
                MiningPlanetaryBody[] whiteDwarfCandidates = found
                    .Bodies.Where(body =>
                        materialRules.Any(rule =>
                            rule.RequiresWhiteDwarfHost && PlanetaryMiningPlan.Matches(rule, body)
                        )
                    )
                    .ToArray();
                IReadOnlySet<string> hosted =
                    whiteDwarfCandidates.Length > 0
                        ? await client.FindWhiteDwarfHostedBodiesAsync(Reference, whiteDwarfCandidates, token)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                allBodies.AddRange(
                    found.Bodies.Where(body =>
                        materialRules.Any(rule =>
                            PlanetaryMiningPlan.Matches(rule, body)
                            && (!rule.RequiresWhiteDwarfHost || hosted.Contains(body.System + "\u001f" + body.Body))
                        )
                    )
                );
                hasMore =
                    found.HasMore
                    && allBodies.Select(body => body.System).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        < ResultLimit;
                pageIndex++;
            }

            IReadOnlyList<MiningPlanetaryBody> bodies = allBodies;

            (IReadOnlyList<MiningMarketResult> foundMarkets, _) = await ReportedStationPricesAsync(
                bodies.Select(body => body.System).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                materials,
                token
            );
            MeritRows = OrderByDistance(
                await PresentRowsAsync(
                    PowerplayMeritRank.ComposePlanets(Systems, bodies, foundMarkets, ResultLimit),
                    materials.FirstOrDefault() ?? "",
                    token
                )
            );
            Status =
                "Landable planets for "
                + string.Join(", ", materials)
                + " within "
                + Radius.ToString("0", CultureInfo.CurrentCulture)
                + " ly of "
                + Reference
                + "."
                + PriceMarkNote;
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            MeritRows = [];
            Status = RequestFailed;
        }
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
                    MiningMaterialSelection.StationLimit(MineralChips.Selected, acquire: false),
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
            IReadOnlyList<MiningMarketResult> imports = await client.FindSystemImportsAsync(
                row.Name,
                MarketFreshness ?? TimeSpan.FromDays(MaximumAgeDays),
                token
            );
            var stationNames = row.Stations.Select(station => station.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] named = SelectedMinerals;
            int limit = MiningMaterialSelection.StationLimit(MineralChips.Selected, acquire: false);
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
                        .Take(limit)
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
            .OrderByDescending(candidate => candidate.Ceiling)
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

        try
        {
            var reported = new List<MiningMarketResult>();
            foreach (string commodity in wantedCommodities)
            {
                IReadOnlyList<MiningMarketResult> found = await client.FindMarketsAsync(
                    new MiningMarketQuery(
                        Reference,
                        commodity,
                        false,
                        Radius,
                        false,
                        false,
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
                reported.AddRange(
                    anywhereInRadius
                        ? found
                        : found.Where(market => wantedSystems.Contains(market.System, StringComparer.OrdinalIgnoreCase))
                );
            }

            return (reported, "Ardent");
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            IReadOnlyList<MiningSellQuote> quotes = await client.FindSellQuotesAsync(
                Reference,
                Radius,
                wantedSystems,
                wantedCommodities,
                MarketFreshness ?? TimeSpan.FromDays(MaximumAgeDays),
                token
            );
            return (
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
                    .ToArray(),
                "Spansh fallback"
            );
        }
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
        new[] { MiningMaterialSelection.Default, MiningMaterialSelection.Any }
            .Concat(PlanetaryMiningPlan.Materials)
            .ToArray();

    private string[] SelectedMinerals => MiningMaterialSelection.Named(MineralChips.Selected);

    private string PriceMarkNote => client.PriceMarksUnavailable ? " " + RequestFailed : "";

    private IReadOnlyList<string> PlanetaryPowers =>
        IsChosenPower(PledgedPower) ? PlanetaryMiningPlan.OtherPowers(PledgedPower) : [];

    private string PlanetaryPowerState =>
        PowerplayPlan.UsesLiveConflict(Objective, PowerState) || IsAny(PowerState) ? "" : PowerState.Trim();

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
            if (surface)
            {
                MineralChips.ReplaceChoices(SurfaceMinerals, MiningMaterialSelection.Default);
            }
            else
            {
                MineralChips.ReplaceChoices(PowerplayMinerals, MiningMaterialSelection.Default);
            }
        }

        Changed(nameof(UsesRingFilters));
        Changed(nameof(IsPlanetaryMining));
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
        Page = 0;
        SystemOnly = false;
        Systems = [];
        Rings = [];
        Markets = [];
        MeritRows = [];
        AcquireRows = [];
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
        MinimumDemand = values.MinimumDemand;
        MaximumDemand = values.MaximumDemand == 0 ? 90_000 : values.MaximumDemand;
        if (!string.IsNullOrWhiteSpace(values.PledgedPower))
        {
            NoteDetectedPower(values.PledgedPower);
        }
        ResultLimit = values.ResultLimit;
        PlatinumMode = values.PlatinumMode;
        StationType = values.StationType;
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

    public void Cancel() => pending?.Cancel();

    public void Dispose()
    {
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        CancellationTokenSource? previous = pending;
        using var current = new CancellationTokenSource(TimeSpan.FromMinutes(2));
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
