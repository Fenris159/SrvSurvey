using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class RouteWorkspaceViewModel
{
    private readonly CarrierNextJumpDependencies? carrierNextJump;
    private bool showCarrierNextJumpInformation = true;
    private readonly CarrierDestinationLookup? carrierDestinationLookup;
    private (string Name, long Address)? carrierDestinationKey;
    private SystemSummary? carrierDestinationSummary;
    private GalacticCoordinate? carrierOrigin;
    private double? carrierArrivedFuelEstimate;
    private string? carrierArrivedFuelSystem;
    private string? carrierOriginSystem;

    public Task CarrierDestinationLookup => carrierDestinationLookup?.Pending ?? Task.CompletedTask;

    public bool ShowCarrierNextJumpInformation
    {
        get => showCarrierNextJumpInformation;
        set
        {
            if (!SetField(ref showCarrierNextJumpInformation, value))
            {
                return;
            }
            try
            {
                carrierNextJump?.SettingsStore.Save(value);
                CarrierNextJumpSettingsStatus = string.Empty;
            }
            catch (Exception exception) when (IsExpectedException(exception))
            {
                CarrierNextJumpSettingsStatus = "Carrier next-jump setting could not be saved: " + exception.Message;
            }
            OnPropertyChanged(nameof(CarrierNextJumpSettingsStatus));
            RefreshCarrierDestination();
            OnPropertyChanged(nameof(ShouldShowCarrierNextJumpOverlay));
        }
    }

    public string CarrierNextJumpSettingsStatus { get; private set; } = string.Empty;

    public bool ShouldShowCarrierNextJumpOverlay =>
        IsFleetCarrierWorkspace && HasProfile && ShowCarrierNextJumpInformation && HasCarrierJumpCountdown;

    public bool UseCarrierNextJumpInformation =>
        ShouldShowCarrierNextJumpOverlay
        && (
            !ShouldShowFleetCarrierRouteOverlay
            || !string.Equals(NextHop?.Name, CarrierJumpDestination, StringComparison.OrdinalIgnoreCase)
        );

    public FleetCarrierJumpCountdownKind CarrierJumpKind => carrierJumpCountdownState.Kind;

    public SystemSummary? CarrierDestinationSummary => carrierDestinationSummary;

    public double? CarrierDepotFuel =>
        latestCarrierFuel?.TankTonnes
        ?? (
            string.Equals(latestCarrierFuel?.SystemName, carrierArrivedFuelSystem, StringComparison.OrdinalIgnoreCase)
                ? carrierArrivedFuelEstimate
                : null
        );

    public bool CarrierFuelIsEstimated =>
        latestCarrierFuel?.IsEstimated == true
        || (latestCarrierFuel?.TankTonnes is null && CarrierDepotFuel is not null);

    public double? CarrierStoredTritium => latestCarrierFuel?.StoredTritiumTonnes;

    public double? CarrierNextJumpDistance =>
        CarrierJumpKind is FleetCarrierJumpCountdownKind.Scheduled or FleetCarrierJumpCountdownKind.InTransit
        && carrierOrigin is { } origin
        && carrierDestinationSummary?.Position is { } destination
            ? origin.DistanceTo(destination)
            : null;

    public double? CarrierNextJumpFuel =>
        CarrierJumpKind is FleetCarrierJumpCountdownKind.Scheduled or FleetCarrierJumpCountdownKind.InTransit
        && latestCarrierFuel is { UsedCapacityTonnes: { } used }
        && CarrierDepotFuel is { } tank
        && CarrierNextJumpDistance is { } distance
            ? FleetCarrierFuelPlanner.JumpFuel(selectedCarrierKind, distance, used, tank)
            : null;

    public double? CarrierFuelAfterJump =>
        CarrierNextJumpFuel is { } cost && CarrierDepotFuel is { } tank ? Math.Max(0, tank - cost) : null;

    private void RefreshCarrierDestination()
    {
        (string Name, long Address)? next = null;
        if (UseCarrierNextJumpInformation && !string.IsNullOrWhiteSpace(CarrierJumpDestination))
        {
            next = (CarrierJumpDestination, carrierJumpCountdownState.SystemAddress);
        }
        else if (ShouldShowFleetCarrierRouteOverlay && NextHop is { } hop)
        {
            next = (hop.Name, hop.SystemAddress ?? 0);
        }
        if (next == carrierDestinationKey)
        {
            if (carrierOrigin is null && FindCarrierOrigin() is { } origin)
            {
                carrierOrigin = origin;
                OnPropertyChanged(nameof(CarrierDestinationSummary));
            }
            if (
                carrierOrigin is not null
                || latestCarrierFuel?.SystemName is not { Length: > 0 } from
                || from == carrierOriginSystem
            )
            {
                return;
            }
        }
        carrierDestinationLookup?.Reset();
        carrierDestinationKey = next;
        carrierDestinationSummary = null;
        carrierOrigin = FindCarrierOrigin();
        carrierOriginSystem = latestCarrierFuel?.SystemName;
        OnPropertyChanged(nameof(CarrierDestinationSummary));
        if (next is { } target && carrierNextJump is not null)
        {
            carrierDestinationLookup!.Load(
                target,
                carrierOrigin is null ? latestCarrierFuel?.SystemName : null,
                (summary, origin) =>
                {
                    carrierOrigin ??= origin;
                    carrierDestinationSummary = summary;
                    OnPropertyChanged(nameof(CarrierDestinationSummary));
                }
            );
        }
    }

    public void CancelCarrierDestinationLookup() => carrierDestinationLookup?.Reset();

    private GalacticCoordinate? FindCarrierOrigin() =>
        latestCarrierFuel?.Position
        ?? (
            latestCarrierFuel?.SystemName is { Length: > 0 } system
            && string.Equals(system, currentSystemName, StringComparison.OrdinalIgnoreCase)
                ? currentPosition
                : null
        );

    private void ResetCarrierDestination()
    {
        carrierDestinationLookup?.Reset();
        carrierDestinationKey = null;
        carrierDestinationSummary = null;
        carrierOrigin = null;
        carrierOriginSystem = null;
        carrierArrivedFuelEstimate = null;
        carrierArrivedFuelSystem = null;
        OnPropertyChanged(nameof(CarrierDestinationSummary));
        OnPropertyChanged(nameof(ShouldShowCarrierNextJumpOverlay));
    }
}
