using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationCommodityOverlayViewModel : INotifyPropertyChanged
{
    public const double CompactCommodityListMaxHeight = 300;
    private ColonizationCommodityPlan plan = EmptyPlan();
    private EliteStatus? status;
    private string? musicTrack;
    private ColonizationOverlayPreferences preferences = ColonizationOverlayPreferences.Default;
    private IReadOnlyList<string> projectNames = [];
    private IReadOnlyList<ColonizationCommodityGroupViewModel> groups = [];
    private HashSet<string> pendingCommodities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool hasMarketSinceDocking;
    private bool isSquadronBankOpen;
    private bool showSatisfiedGroups;
    private string platformStatus = string.Empty;
    private bool isClickThrough;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Exposes the saved presentation preferences so editor previews use the live panel's configuration.</summary>
    internal ColonizationOverlayPreferences Preferences => preferences;

    public ColonizationCommodityPlan Plan
    {
        get => plan;
        private set
        {
            if (SetField(ref plan, value))
            {
                projectNames = value.ProjectNames.Take(8).ToArray();
            }
        }
    }

    public string Title => Plan.Title;

    /// <summary>Breaks the displayed project title after its first spaced hyphen without changing the stored name.</summary>
    public string HeaderTitle
    {
        get
        {
            string title = Title.ToUpperInvariant();
            int separator = title.IndexOf(" - ", StringComparison.Ordinal);
            return separator < 0 ? title : $"{title[..(separator + 2)]}\n{title[(separator + 3)..]}";
        }
    }

    public IReadOnlyList<string> ProjectNames => projectNames;

    public bool HasMultipleProjects => ProjectNames.Count > 1;

    public IReadOnlyList<ColonizationCommodityGroupViewModel> Groups
    {
        get => groups;
        private set => SetField(ref groups, value);
    }

    public bool HasRows => Plan.Rows.Count > 0;

    public bool HasPendingCargo => pendingCommodities.Count > 0;

    public bool HasFleetCarriers => preferences.ShowFleetCarrierCargo && Plan.FleetCarriers.Count > 0;

    public bool IsConstructionComplete => Plan.IsConstructionComplete;

    public bool IsConstructionFailed => Plan.IsConstructionFailed;

    public bool HasWarning =>
        Plan.IsLocalProjectUntracked || Plan.IsDockedAtUntrackedFleetCarrier || IsConstructionFailed;

    public string WarningText =>
        IsConstructionFailed
            ? "Construction failed"
            : (Plan.IsLocalProjectUntracked) switch
            {
                true => "This construction site is not in the active project list.",
                false => (Plan.IsDockedAtUntrackedFleetCarrier) switch
                {
                    true => "The current Fleet Carrier is not linked to this " + "commander in Raven Colonial.",
                    false => string.Empty,
                },
            };

    public string RemainingSummary
    {
        get
        {
            string trips = Plan.TripsInCurrentShip is long tripCount
                ? $" | {tripCount:N0} trips in this ship"
                : string.Empty;
            return $"{Plan.TotalRemaining:N0} remaining{trips}";
        }
    }

    public string FleetCarrierSummary
    {
        get
        {
            if (!HasFleetCarriers)
            {
                return string.Empty;
            }

            string trips = Plan.FleetCarrierDeficitTrips is long tripCount ? $" | {tripCount:N0} trips" : string.Empty;
            string names = string.Join(
                "  •  ",
                Plan.FleetCarriers.Select(carrier =>
                    string.IsNullOrWhiteSpace(carrier.DisplayName) ? carrier.Name : carrier.DisplayName
                )
            );
            return $"{Plan.FleetCarriers.Count:N0} FCs: " + $"{Plan.FleetCarrierDeficit:N0} deficit{trips}\n{names}";
        }
    }

    /// <summary>Applies automatic docking context, or persistent eligibility when automatic hiding is disabled.</summary>
    public bool ShouldAutoShow
    {
        get
        {
            if (!preferences.AutoShow)
            {
                return CanShowManually;
            }

            OverlayGameMode mode = OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack);
            return Plan.HasContent
                && mode
                    is not OverlayGameMode.FsdJumping
                        and not OverlayGameMode.GalaxyMap
                        and not OverlayGameMode.ExternalPanel
                && (
                    mode == OverlayGameMode.StationServices
                        && ((hasMarketSinceDocking && Plan.ProjectNames.Count > 0) || Plan.IsAtConstructionSite)
                    || preferences.ShowOnRightPanel
                        && mode == OverlayGameMode.InternalPanel
                        && Plan.ProjectNames.Count > 0
                    || Plan.IsAtConstructionSite && mode == OverlayGameMode.Docked
                    || isSquadronBankOpen
                );
        }
    }

    /// <summary>Allows a loaded panel in an active game independently of its automatic docking preference.</summary>
    public bool CanShowManually
    {
        get
        {
            OverlayGameMode mode = OverlayGameModeResolver.Resolve(status, musicTrack: musicTrack);
            return Plan.HasContent && mode is not OverlayGameMode.Offline and not OverlayGameMode.FsdJumping;
        }
    }

    /// <summary>Describes whether covered groups are expanded, summarized, or hidden.</summary>
    public string CollapseModeText
    {
        get
        {
            if (!(preferences.CollapseCoveredGroups ^ showSatisfiedGroups))
            {
                return "Covered Fleet Carrier groups are expanded.";
            }

            return preferences.HideCollapsedGroups
                ? "Covered Fleet Carrier groups are hidden automatically."
                : "Covered Fleet Carrier groups collapse automatically.";
        }
    }

    public void UpdateMusicTrack(string? currentMusicTrack)
    {
        if (string.Equals(musicTrack, currentMusicTrack, StringComparison.Ordinal))
        {
            return;
        }

        musicTrack = currentMusicTrack;
        OnPropertyChanged(nameof(ShouldAutoShow));
        OnPropertyChanged(nameof(CanShowManually));
    }

    /// <summary>Combines cargo only while Fleet Carrier counts are enabled, retaining the saved preference.</summary>
    private bool CombineShipCargo => preferences.ShowFleetCarrierCargo && preferences.InlineFleetCarrierCargo;

    /// <summary>Identifies whether the cargo column shows carrier or combined quantities and their delta.</summary>
    public string FleetCarrierColumnHeader =>
        (CombineShipCargo, preferences.ShowFleetCarrierDelta) switch
        {
            (true, true) => "HAVE Δ",
            (true, false) => "HAVE",
            (false, true) => "FC Δ",
            _ => "FC",
        };

    /// <summary>Omits the separate ship heading when its cargo contributes to the combined column.</summary>
    public string ShipColumnHeader => CombineShipCargo ? string.Empty : "SHIP";

    public string PlatformStatus
    {
        get => platformStatus;
        private set => SetField(ref platformStatus, value);
    }

    public bool IsClickThrough
    {
        get => isClickThrough;
        private set => SetField(ref isClickThrough, value);
    }

    public string InputMode => IsClickThrough ? "CLICK-THROUGH" : "PASS-THROUGH UNAVAILABLE";

    public bool UseCompactScrollingCommoditiesList => preferences.UseCompactScrollingCommoditiesList;

    public double CommodityListMaxHeight =>
        UseCompactScrollingCommoditiesList ? CompactCommodityListMaxHeight : double.PositiveInfinity;

    public void Apply(
        ColonizationCommodityPlan updatedPlan,
        EliteStatus? updatedStatus,
        bool updatedHasMarketSinceDocking = false,
        bool updatedIsSquadronBankOpen = false
    )
    {
        ArgumentNullException.ThrowIfNull(updatedPlan);
        Plan = updatedPlan;
        status = updatedStatus;
        hasMarketSinceDocking = updatedHasMarketSinceDocking;
        isSquadronBankOpen = updatedIsSquadronBankOpen;
        RebuildGroups();
        RaisePlanProperties();
    }

    public void ToggleSatisfiedGroups()
    {
        showSatisfiedGroups = !showSatisfiedGroups;
        RebuildGroups();
        OnPropertyChanged(nameof(CollapseModeText));
    }

    public void ApplyPreferences(ColonizationOverlayPreferences updatedPreferences)
    {
        ArgumentNullException.ThrowIfNull(updatedPreferences);
        preferences = updatedPreferences;
        RebuildGroups();
        RaisePlanProperties();
        OnPropertyChanged(nameof(CollapseModeText));
        OnPropertyChanged(nameof(FleetCarrierColumnHeader));
        OnPropertyChanged(nameof(ShipColumnHeader));
        OnPropertyChanged(nameof(UseCompactScrollingCommoditiesList));
        OnPropertyChanged(nameof(CommodityListMaxHeight));
        OnPropertyChanged(nameof(Preferences));
    }

    public void ApplyPendingFleetCarrierCargo(IEnumerable<string>? commodities)
    {
        pendingCommodities =
            commodities
                ?.Where(commodity => !string.IsNullOrWhiteSpace(commodity))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        RebuildGroups();
        OnPropertyChanged(nameof(HasPendingCargo));
    }

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        IsClickThrough = result.IsClickThrough;
        OnPropertyChanged(nameof(InputMode));
    }

    /// <summary>Builds category rows using coverage rules and omits collapsed groups when requested.</summary>
    private void RebuildGroups()
    {
        Groups = Plan
            .Rows.GroupBy(row => row.Category)
            .Select(group =>
            {
                ColonizationCommodityOverlayRowViewModel[] rows = group
                    .Select(
                        (row, rowIndex) =>
                            new ColonizationCommodityOverlayRowViewModel(
                                row,
                                preferences.ShowFleetCarrierCargo,
                                preferences.ShowFleetCarrierDelta,
                                CombineShipCargo,
                                preferences.HighlightAlmostCoveredFleetCarrierLoads,
                                pendingCommodities.Contains(row.Commodity),
                                rowIndex % 2 == 0
                            )
                    )
                    .ToArray();
                bool canCollapse =
                    !Plan.IsAtConstructionSite
                    && preferences.ShowFleetCarrierCargo
                    && Plan.FleetCarriers.Count > 0
                    && rows.All(row => row.FleetCarriersHaveEnough && row.InShip == 0);
                bool isCollapsed = canCollapse && (preferences.CollapseCoveredGroups ^ showSatisfiedGroups);
                return new ColonizationCommodityGroupViewModel(
                    group.Key,
                    isCollapsed ? [] : rows,
                    isCollapsed,
                    isCollapsed ? $"{rows.Length:N0} commodities covered by linked FCs" : string.Empty
                );
            })
            .Where(group => !preferences.HideCollapsedGroups || !group.IsCollapsed)
            .ToArray();
    }

    private void RaisePlanProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ProjectNames));
        OnPropertyChanged(nameof(HasMultipleProjects));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasFleetCarriers));
        OnPropertyChanged(nameof(IsConstructionComplete));
        OnPropertyChanged(nameof(IsConstructionFailed));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(WarningText));
        OnPropertyChanged(nameof(RemainingSummary));
        OnPropertyChanged(nameof(FleetCarrierSummary));
        OnPropertyChanged(nameof(ShouldAutoShow));
        OnPropertyChanged(nameof(CanShowManually));
    }

    private static ColonizationCommodityPlan EmptyPlan()
    {
        return new ColonizationCommodityPlan
        {
            Title = "0 projects",
            ProjectNames = [],
            Rows = [],
            FleetCarriers = [],
            TotalRemaining = 0,
            TripsInCurrentShip = null,
            FleetCarrierDeficit = 0,
            FleetCarrierDeficitTrips = null,
            IsAtConstructionSite = false,
            IsLocalProjectUntracked = false,
            IsDockedAtUntrackedFleetCarrier = false,
            IsConstructionComplete = false,
            IsConstructionFailed = false,
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ColonizationCommodityGroupViewModel(
    string Name,
    IReadOnlyList<ColonizationCommodityOverlayRowViewModel> Rows,
    bool IsCollapsed,
    string CollapsedSummary
);

public sealed record ColonizationCommodityOverlayRowViewModel(
    string Commodity,
    string DisplayName,
    int Needed,
    int InShip,
    int OnFleetCarriers,
    bool IsAssignedToCommander,
    bool IsAssignedToOther,
    bool ShipHasEnough,
    bool FleetCarriersHaveEnough,
    bool HasSurplusInShip,
    bool ShowFleetCarrierCargo,
    bool ShowFleetCarrierDelta,
    bool InlineFleetCarrierCargo,
    bool HighlightAlmostCoveredFleetCarrierLoads,
    bool IsPending
)
{
    public ColonizationCommodityOverlayRowViewModel(
        ColonizationCommodityPlanRow row,
        bool showFleetCarrierCargo,
        bool showFleetCarrierDelta,
        bool inlineFleetCarrierCargo,
        bool highlightAlmostCoveredFleetCarrierLoads,
        bool isPending,
        bool isAlternateRow
    )
        : this(
            row.Commodity,
            row.DisplayName,
            row.Needed,
            row.InShip,
            row.OnFleetCarriers,
            row.IsAssignedToCommander,
            row.IsAssignedToOther,
            row.ShipHasEnough,
            row.FleetCarriersHaveEnough,
            row.HasSurplusInShip,
            showFleetCarrierCargo,
            showFleetCarrierDelta,
            inlineFleetCarrierCargo,
            highlightAlmostCoveredFleetCarrierLoads,
            isPending
        )
    {
        IsAvailableAtCurrentMarket = row.IsAvailableAtCurrentMarket;
        IsUnavailableAtCurrentMarket = row.IsUnavailableAtCurrentMarket;
        CanCompleteFleetCarrierLoad = row.CanCompleteFleetCarrierLoad;
        IsAlternateRow = isAlternateRow;
    }

    public bool IsAvailableAtCurrentMarket { get; }

    public bool IsUnavailableAtCurrentMarket { get; }

    public bool CanCompleteFleetCarrierLoad { get; }

    public bool IsAlternateRow { get; }

    public int FleetCarrierDeficit => Math.Max(0, Needed - OnFleetCarriers);

    /// <summary>Highlights carrier-loading guidance without excluding cargo shown in a combined total.</summary>
    public bool IsFleetCarrierLoadHighlighted => HighlightAlmostCoveredFleetCarrierLoads && CanCompleteFleetCarrierLoad;

    public bool HasMarketBadge => IsAvailableAtCurrentMarket;

    public string MarketBadgeText =>
        IsFleetCarrierLoadHighlighted
            ? (InShip >= FleetCarrierDeficit) switch
            {
                true => "FC READY",
                false => "FC LOAD",
            }
            : "MARKET";

    public double RowOpacity => IsUnavailableAtCurrentMarket ? 0.48 : 1;

    public string NeededText => IsPending ? "..." : Needed.ToString("N0", CultureInfo.CurrentCulture);

    public string InShipText =>
        !InlineFleetCarrierCargo && InShip > 0 ? InShip.ToString("N0", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>Adds ship cargo in combined mode without overflowing the individual integer counts.</summary>
    private long DisplayedCargoTotal => InlineFleetCarrierCargo ? (long)OnFleetCarriers + InShip : OnFleetCarriers;

    /// <summary>Formats carrier or combined cargo, preserving pending state and using the same total for delta.</summary>
    public string OnFleetCarriersText
    {
        get
        {
            if (IsPending)
            {
                return "...";
            }

            if (!ShowFleetCarrierCargo)
            {
                return string.Empty;
            }

            if (!ShowFleetCarrierDelta)
            {
                return !InlineFleetCarrierCargo && OnFleetCarriers <= 0
                    ? string.Empty
                    : DisplayedCargoTotal.ToString("N0", CultureInfo.CurrentCulture);
            }

            long difference = DisplayedCargoTotal - Needed;
            return difference > 0
                ? string.Create(CultureInfo.CurrentCulture, $"+{difference:N0}")
                : difference.ToString("N0", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Uses combined coverage when ship cargo is included in the displayed carrier total.</summary>
    public bool IsSatisfied =>
        InlineFleetCarrierCargo ? DisplayedCargoTotal >= Needed : ShipHasEnough || FleetCarriersHaveEnough;

    public bool IsActiveItem => !IsUnavailableAtCurrentMarket;

    public bool IsDimmedItem => IsUnavailableAtCurrentMarket;

    /// <summary>Classifies the displayed delta independently of cargo coverage in the separate ship column.</summary>
    private bool IsDeltaSurplus => DisplayedCargoTotal >= Needed;

    /// <summary>Selects the normal surplus brush only for a nonnegative delta at an available or unknown market.</summary>
    public bool IsActiveSurplus => ShowFleetCarrierDelta && IsDeltaSurplus && IsActiveItem;

    /// <summary>Selects the dim surplus brush for a nonnegative delta unavailable at the current market.</summary>
    public bool IsDimmedSurplus => ShowFleetCarrierDelta && IsDeltaSurplus && IsDimmedItem;

    /// <summary>Selects the normal deficit brush only for a negative delta at an available or unknown market.</summary>
    public bool IsActiveDeficit => ShowFleetCarrierDelta && !IsDeltaSurplus && IsActiveItem;

    /// <summary>Selects the dim deficit brush for a negative delta unavailable at the current market.</summary>
    public bool IsDimmedDeficit => ShowFleetCarrierDelta && !IsDeltaSurplus && IsDimmedItem;

    public string AssignmentText =>
        IsAssignedToCommander
            ? "PIN"
            : (IsAssignedToOther) switch
            {
                true => "OTHER",
                false => string.Empty,
            };

    public bool HasAssignment => IsAssignedToCommander || IsAssignedToOther;
}
