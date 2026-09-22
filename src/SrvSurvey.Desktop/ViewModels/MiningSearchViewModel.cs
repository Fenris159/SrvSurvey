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
    private const string SpanshSource = "Spansh";
    private const string HotspotRing = "Hotspots";
    private const string WithoutHotspotsRing = "Without Hotspots";
    private CancellationTokenSource? pending;
    private string status = "Choose a reference system to search.";
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
            if (Set(ref options, options with { Reference = value ?? "" }))
            {
                Changed(nameof(PowerplaySummary));
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
    private string planningTarget = "";
    private string planningObjective = "";
    private string miningOrigin = "";
    private string currentSystem = "";
    private bool powerplayPrepared;
    public string CurrentSystem => currentSystem;
    public bool HasPlan => planningTarget.Length > 0;
    public string PlanningContext =>
        planningTarget.Length == 0 ? "" : $"{planningObjective} destination: {planningTarget} · Mining: {miningOrigin}";

    public void UpdateCurrentLocation(string system)
    {
        if (Set(ref currentSystem, system ?? "", nameof(CurrentSystem)) && Reference.Length == 0)
        {
            Reference = currentSystem;
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
        if (currentSystem.Length > 0)
        {
            UseCurrentLocation();
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
                Changed(nameof(PowerplaySummary));
            }
        }
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
            Changed(nameof(PowerplaySummary));
        }
    }
    public static IReadOnlyList<string> Objectives { get; } =
    [AllSystems, ReinforceObjective, UndermineObjective, AcquireObjective];
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
    public static IReadOnlyList<string> Reserves { get; } =
    ["All", "Pristine", "Major", "Common", "Low", "Depleted", "Unknown"];
    public static IReadOnlyList<string> MiningTypes { get; } =
    ["All", "Core", "Laser Surface", "Surface Deposit", "Sub Surface Deposit"];
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
    public IReadOnlyList<string> PowerplayMinerals { get; } =
        new[] { AnyPower }.Concat(MiningReferenceData.Commodities.GetValueOrDefault("Mining") ?? []).ToArray();
    public MiningChipBoxViewModel MineralChips { get; } =
        new(
            "Mineral / metal",
            new[] { AnyPower }.Concat(MiningReferenceData.Commodities.GetValueOrDefault("Mining") ?? []).ToArray(),
            PlatinumMineral
        );
    public MiningChipBoxViewModel MiningTypeChips { get; } = new("Mining type", MiningTypes, "All");
    public MiningChipBoxViewModel StateChips { get; } = new("System state", FactionStates, "Any");
    private IReadOnlyList<MeritSystemRowViewModel> meritRows = [];
    public IReadOnlyList<MeritSystemRowViewModel> MeritRows
    {
        get => meritRows;
        private set => Set(ref meritRows, value);
    }
    public static IReadOnlyList<string> PlatinumModes { get; } = ["Spots++", "RES mapped", "Overlaps", "All platinum"];
    public static IReadOnlyList<string> TraderTypes { get; } = ["Raw", "Manufactured", "Encoded"];
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
            || (reserve == "Unknown" && (ring.Reserve.Length == 0 || Same(ring.Reserve, "Unknown")))
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
        ring.Hotspots.Any(hotspot =>
            Same(hotspot.Key.Replace(" ", ""), mineral.Replace(" ", "")) && hotspot.Value >= minimum
        );

    private bool MatchesMiningType(MiningRing ring, string mineral)
    {
        if (MiningType is "All" or "")
        {
            return true;
        }

        bool core = MiningType == "Core";
        if (mineral.Length > 0)
        {
            return core ? CoreMinerals.Contains(mineral) : !CoreMinerals.Contains(mineral);
        }

        return ring.Hotspots.Keys.Any(name => CoreMinerals.Contains(name) == core);
    }

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
            IReadOnlyList<MiningMarketResult> result;
            string source = query.SystemOnly && !query.GalaxyWide ? SpanshSource : "Ardent";
            try
            {
                result = await client.FindMarketsAsync(query, token);
            }
            catch (Exception ex) when (source == "Ardent" && IsProviderFailure(ex))
            {
                source = "Spansh fallback";
                result = await client.FindSpanshMarketsAsync(query, token);
            }
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

        var found = new Dictionary<string, MiningSystemResult>(StringComparer.OrdinalIgnoreCase);
        foreach (MiningSystemResult supporter in supporters)
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<MiningSystemResult> bubble = await client.FindSystemsAsync(
                new MiningSystemQuery(
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
                ),
                token
            );
            foreach (
                MiningSystemResult candidate in bubble.Where(candidate =>
                    PowerplayPlan.IsAcquisitionTarget(candidate, PowerState)
                )
            )
            {
                found.TryAdd(candidate.System, candidate);
            }
        }

        MiningSystemResult[] result = found
            .Values.OrderBy(system => system.Distance ?? double.MaxValue)
            .Take(ResultLimit)
            .ToArray();
        token.ThrowIfCancellationRequested();
        Systems = result;
        await PublishMeritRowsAsync(token);
        Status =
            MeritRows.Count
            + $" acquisition systems within {PowerplayPlan.FortifiedReachLy:0} ly of a Fortified system or {PowerplayPlan.StrongholdReachLy:0} ly of a Stronghold, best sell price first. "
            + Status;
    }

    private async Task<IReadOnlyList<MiningSystemResult>> CollectPowerSystemsAsync(
        string state,
        CancellationToken token
    )
    {
        var all = new List<MiningSystemResult>();
        for (int pageIndex = 0; pageIndex < 3; pageIndex++)
        {
            IReadOnlyList<MiningSystemResult> rows = await client.FindSystemsAsync(
                new MiningSystemQuery(Reference, Radius, Power: PledgedPower, PowerState: state, Page: pageIndex),
                token
            );
            all.AddRange(rows.Where(system => Same(system.Power, PledgedPower) && Same(system.PowerState, state)));
            if (rows.Count < 100)
            {
                break;
            }
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
            IReadOnlyList<MiningSystemResult> online = await client.FindSystemsAsync(query, token);
            string source = PowerplayPlan.UsesLiveConflict(Objective, PowerState)
                ? "Spansh conflict progress + local Powerplay observations"
                : "Spansh + local Powerplay observations";

            IReadOnlyList<MiningSystemResult> local = community?.FindSystems(query, DateTimeOffset.UtcNow) ?? [];
            MiningSystemResult[] result = local
                .Concat(online)
                .DistinctBy(s => s.System, StringComparer.OrdinalIgnoreCase)
                .Where(system => PowerplayPlan.Matches(Objective, system, PledgedPower, OpposingPower))
                .OrderBy(s => s.Distance ?? double.MaxValue)
                .Take(ResultLimit)
                .ToArray();
            token.ThrowIfCancellationRequested();
            Systems = result;
            await PublishMeritRowsAsync(token);
            Status = MeritRows.Count + " locations, best sell price first. " + source + ". " + Status;
        });

    private async Task PublishMeritRowsAsync(CancellationToken token)
    {
        if (Systems.Count == 0)
        {
            MeritRows = [];
            Status = "No systems matched.";
            return;
        }

        string commodity = MineralChips.Selected.FirstOrDefault(item => !IsAny(item)) ?? PlatinumMineral;
        try
        {
            IReadOnlyList<MiningRing> foundRings = await client.FindRingsAsync(
                new MiningRingQuery(Reference, commodity, OnlineRingType, Radius, OnlineMinimumHotspots, 0, false),
                token
            );
            IReadOnlyList<MiningMarketResult> foundMarkets = await client.FindMarketsAsync(
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
            MeritRows = PowerplayMeritRank
                .Compose(Systems, foundRings, foundMarkets, ResultLimit)
                .Select(MeritSystemRowViewModel.From)
                .ToArray();
            Status = $"Prices are for {commodity}.";
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            MeritRows = [];
            Status = "Station prices are unavailable: " + ex.Message;
        }
    }

    public Task SearchTradersAsync() =>
        Run(async token =>
        {
            IReadOnlyList<MiningMarketResult> result = await client.FindTradersAsync(
                Reference,
                TraderType,
                Radius,
                Page,
                token
            );
            token.ThrowIfCancellationRequested();
            Traders = result;
            Status = $"{result.Count} {TraderType.ToLowerInvariant()} material traders · Spansh.";
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
        Mineral = PlatinumMineral;
        RingType = "All";
        Reserve = "All";
        MinimumHotspots = 1;
        MinimumDemand = 0;
        MaximumDemand = 0;
        LargePads = false;
        MaximumAgeDays = 2;
        ResultLimit = 30;
        Page = 0;
        SystemOnly = false;
        Systems = [];
        Rings = [];
        Markets = [];
        ClearPlan();
        UseCurrentLocation();
        Status = "Powerplay mining filters reset to commander defaults.";
        Changed(nameof(PowerplaySummary));
    }

    public MiningSearchPreferences SaveOptions() => options;

    public void LoadOptions(MiningSearchPreferences values)
    {
        pending?.Cancel();
        pending = null;
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
        PledgedPower = "";
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
        MaximumDemand = values.MaximumDemand;
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
        using var current = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        pending = current;
        IsBusy = true;
        Status = "Searching…";
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
                Status = "Search unavailable: " + ex.Message;
            }
        }
        finally
        {
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
