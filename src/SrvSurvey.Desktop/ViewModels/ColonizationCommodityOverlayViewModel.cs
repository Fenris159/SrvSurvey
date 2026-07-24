using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ColonizationCommodityOverlayViewModel
    : INotifyPropertyChanged
{
    private ColonizationCommodityPlan plan = EmptyPlan();
    private EliteStatus? status;
    private ColonizationOverlayPreferences preferences =
        ColonizationOverlayPreferences.Default;
    private IReadOnlyList<ColonizationCommodityGroupViewModel> groups = [];
    private bool showSatisfiedGroups;
    private string platformStatus = string.Empty;
    private bool isClickThrough;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ColonizationCommodityPlan Plan
    {
        get => plan;
        private set => SetField(ref plan, value);
    }

    public string Title => Plan.Title;

    public IReadOnlyList<string> ProjectNames => Plan.ProjectNames.Take(8)
        .ToArray();

    public bool HasMultipleProjects => ProjectNames.Count > 1;

    public IReadOnlyList<ColonizationCommodityGroupViewModel> Groups
    {
        get => groups;
        private set => SetField(ref groups, value);
    }

    public bool HasRows => Plan.Rows.Count > 0;

    public bool HasFleetCarriers => preferences.ShowFleetCarrierCargo
        && Plan.FleetCarriers.Count > 0;

    public bool IsConstructionComplete => Plan.IsConstructionComplete;

    public bool IsConstructionFailed => Plan.IsConstructionFailed;

    public bool HasWarning => Plan.IsLocalProjectUntracked
        || IsConstructionFailed;

    public string WarningText => IsConstructionFailed
        ? "Construction failed"
        : Plan.IsLocalProjectUntracked
            ? "This construction site is not in the active project list."
            : string.Empty;

    public string RemainingSummary
    {
        get
        {
            var trips = Plan.TripsInCurrentShip is long tripCount
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

            var trips = Plan.FleetCarrierDeficitTrips is long tripCount
                ? $" | {tripCount:N0} trips"
                : string.Empty;
            var names = string.Join(
                "  •  ",
                Plan.FleetCarriers.Select(carrier =>
                    string.IsNullOrWhiteSpace(carrier.DisplayName)
                        ? carrier.Name
                        : carrier.DisplayName));
            return $"{Plan.FleetCarriers.Count:N0} FCs: "
                + $"{Plan.FleetCarrierDeficit:N0} deficit{trips}\n{names}";
        }
    }

    public bool ShouldAutoShow => preferences.AutoShow
        && Plan.HasContent
        && status is not null
        && !status.FsdChargingJump
        && !status.Flags.HasFlag(StatusFlags.FsdJump)
        && status.GuiFocus is not GuiFocus.GalaxyMap
            and not GuiFocus.ExternalPanel
        && (status.GuiFocus == GuiFocus.StationServices
            || preferences.ShowOnRightPanel
                && status.GuiFocus == GuiFocus.InternalPanel
            || Plan.IsAtConstructionSite
                && status.Docked
                && status.GuiFocus == GuiFocus.NoFocus);

    public string CollapseModeText =>
        preferences.CollapseCoveredGroups ^ showSatisfiedGroups
            ? "Covered Fleet Carrier groups collapse automatically."
            : "Covered Fleet Carrier groups are expanded.";

    public string FleetCarrierColumnHeader =>
        preferences.InlineFleetCarrierCargo
            ? "HAVE"
            : preferences.ShowFleetCarrierDelta
                ? "FC Δ"
                : "FC";

    public string ShipColumnHeader => preferences.InlineFleetCarrierCargo
        ? string.Empty
        : "SHIP";

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

    public string InputMode => IsClickThrough
        ? "CLICK-THROUGH"
        : "PASS-THROUGH UNAVAILABLE";

    public void Apply(
        ColonizationCommodityPlan updatedPlan,
        EliteStatus? updatedStatus)
    {
        ArgumentNullException.ThrowIfNull(updatedPlan);
        Plan = updatedPlan;
        status = updatedStatus;
        RebuildGroups();
        RaisePlanProperties();
    }

    public void ToggleSatisfiedGroups()
    {
        showSatisfiedGroups = !showSatisfiedGroups;
        RebuildGroups();
        OnPropertyChanged(nameof(CollapseModeText));
    }

    public void ApplyPreferences(
        ColonizationOverlayPreferences updatedPreferences)
    {
        ArgumentNullException.ThrowIfNull(updatedPreferences);
        preferences = updatedPreferences;
        RebuildGroups();
        RaisePlanProperties();
        OnPropertyChanged(nameof(CollapseModeText));
        OnPropertyChanged(nameof(FleetCarrierColumnHeader));
        OnPropertyChanged(nameof(ShipColumnHeader));
    }

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PlatformStatus = result.Status;
        IsClickThrough = result.IsClickThrough;
        OnPropertyChanged(nameof(InputMode));
    }

    private void RebuildGroups()
    {
        Groups = Plan.Rows
            .GroupBy(row => row.Category)
            .Select(group =>
            {
                var rows = group.Select(row =>
                        new ColonizationCommodityOverlayRowViewModel(
                            row,
                            preferences.ShowFleetCarrierCargo,
                            preferences.ShowFleetCarrierDelta,
                            preferences.InlineFleetCarrierCargo,
                            preferences
                                .HighlightAlmostCoveredFleetCarrierLoads))
                    .ToArray();
                var canCollapse = !Plan.IsAtConstructionSite
                    && preferences.ShowFleetCarrierCargo
                    && Plan.FleetCarriers.Count > 0
                    && rows.All(row =>
                        row.FleetCarriersHaveEnough && row.InShip == 0);
                var isCollapsed = canCollapse
                    && (preferences.CollapseCoveredGroups
                        ^ showSatisfiedGroups);
                return new ColonizationCommodityGroupViewModel(
                    group.Key,
                    isCollapsed ? [] : rows,
                    isCollapsed,
                    isCollapsed
                        ? $"{rows.Length:N0} commodities covered by linked FCs"
                        : string.Empty);
            })
            .ToArray();
    }

    private void RaisePlanProperties()
    {
        OnPropertyChanged(nameof(Title));
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
    }

    private static ColonizationCommodityPlan EmptyPlan()
    {
        return new ColonizationCommodityPlan(
            "0 projects",
            [],
            [],
            [],
            0,
            null,
            0,
            null,
            false,
            false,
            false,
            false);
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ColonizationCommodityGroupViewModel(
    string Name,
    IReadOnlyList<ColonizationCommodityOverlayRowViewModel> Rows,
    bool IsCollapsed,
    string CollapsedSummary);

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
    bool HighlightAlmostCoveredFleetCarrierLoads)
{
    public ColonizationCommodityOverlayRowViewModel(
        ColonizationCommodityPlanRow row,
        bool showFleetCarrierCargo,
        bool showFleetCarrierDelta,
        bool inlineFleetCarrierCargo,
        bool highlightAlmostCoveredFleetCarrierLoads)
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
            highlightAlmostCoveredFleetCarrierLoads)
    {
        IsAvailableAtCurrentMarket = row.IsAvailableAtCurrentMarket;
        IsUnavailableAtCurrentMarket = row.IsUnavailableAtCurrentMarket;
        CanCompleteFleetCarrierLoad = row.CanCompleteFleetCarrierLoad;
    }

    public bool IsAvailableAtCurrentMarket { get; }

    public bool IsUnavailableAtCurrentMarket { get; }

    public bool CanCompleteFleetCarrierLoad { get; }

    public int FleetCarrierDeficit => Math.Max(0, Needed - OnFleetCarriers);

    public bool IsFleetCarrierLoadHighlighted =>
        HighlightAlmostCoveredFleetCarrierLoads
        && CanCompleteFleetCarrierLoad
        && (!InlineFleetCarrierCargo || InShip == 0);

    public bool IsFleetCarrierValueNormal =>
        !IsFleetCarrierLoadHighlighted;

    public bool HasMarketBadge => IsAvailableAtCurrentMarket;

    public string MarketBadgeText => IsFleetCarrierLoadHighlighted
        ? InShip >= FleetCarrierDeficit
            ? "FC READY"
            : "FC LOAD"
        : "MARKET";

    public double RowOpacity => IsUnavailableAtCurrentMarket ? 0.48 : 1;

    public string NeededText => Needed.ToString("N0");

    public string InShipText => !InlineFleetCarrierCargo && InShip > 0
        ? InShip.ToString("N0")
        : string.Empty;

    public string OnFleetCarriersText
    {
        get
        {
            if (InlineFleetCarrierCargo && InShip > 0)
            {
                return InShip.ToString("N0");
            }

            if (!ShowFleetCarrierCargo || OnFleetCarriers <= 0)
            {
                return string.Empty;
            }

            if (!ShowFleetCarrierDelta
                && !IsFleetCarrierLoadHighlighted)
            {
                return OnFleetCarriers.ToString("N0");
            }

            var difference = OnFleetCarriers - Needed;
            return difference > 0
                ? $"+{difference:N0}"
                : difference.ToString("N0");
        }
    }

    public bool IsSatisfied => ShipHasEnough || FleetCarriersHaveEnough;

    public string AssignmentText => IsAssignedToCommander
        ? "PIN"
        : IsAssignedToOther
            ? "OTHER"
            : string.Empty;

    public bool HasAssignment => IsAssignedToCommander || IsAssignedToOther;
}
