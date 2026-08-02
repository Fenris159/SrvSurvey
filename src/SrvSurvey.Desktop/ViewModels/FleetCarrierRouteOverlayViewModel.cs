using System.ComponentModel;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierRouteOverlayViewModel : IDisposable
{
    private readonly RouteWorkspaceViewModel route;

    public FleetCarrierRouteOverlayViewModel(
        RouteWorkspaceViewModel route,
        OverlayPlatformCapabilities capabilities)
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(capabilities);
        route.PropertyChanged += OnRoutePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HopProgress => route.RouteCount == 0
        ? "NO ROUTE"
        : $"HOP {Math.Min(route.ReachedCount + 1, route.RouteCount):N0} / {route.RouteCount:N0}";

    public string SystemName => route.NextHopName;

    public string JumpSummary
    {
        get
        {
            var carrier = route.NextHop?.Carrier;
            return $"{FormatNumber(carrier?.DistanceLy, 2)} LY JUMP  \u2022  "
                + $"{FormatNumber(carrier?.RemainingLy, 2)} LY REMAINING";
        }
    }

    public string JumpsLeft
    {
        get
        {
            var count = Math.Max(
                0,
                route.RouteCount - route.ReachedCount - 1);
            return $"{count:N0} {(count == 1 ? "JUMP" : "JUMPS")} LEFT";
        }
    }

    public string FuelLeft => FormatTonnes(
        route.NextHop?.Carrier?.FuelRemainingTonnes);

    public string TritiumInMarket => FormatTonnes(
        route.NextHop?.Carrier?.TritiumInMarketTonnes);

    public string JumpFuel => FormatTonnes(
        route.NextHop?.Carrier?.FuelUsedTonnes);

    public bool HasIcyRing => route.NextHop?.Carrier?.HasIcyRing == true;

    public string IcyRingLabel => route.NextHop?.Carrier is
        {
            HasIcyRing: true,
            IsSystemPristine: true,
        }
            ? "PRISTINE ICY RING"
            : "ICY RING";

    public bool HasRestockWarning =>
        route.NextHop?.Carrier?.MustRestock == true;

    public string RestockAmount => FormatTonnes(
        route.NextHop?.Carrier?.RestockAmountTonnes);

    public bool HasCountdown => route.HasCarrierJumpCountdown;

    public string CountdownTitle => route.CarrierJumpCountdownTitle;

    public string Countdown => route.CarrierJumpCountdownValue;

    public string CountdownPhase => route.CarrierJumpPhaseLabel;

    public string CountdownPhaseTime => route.CarrierJumpPhaseCountdown;

    public bool HasCountdownPhaseTime => route.HasCarrierJumpPhaseCountdown;

    public void Dispose()
    {
        route.PropertyChanged -= OnRoutePropertyChanged;
    }

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is
            nameof(RouteWorkspaceViewModel.NextHop)
            or nameof(RouteWorkspaceViewModel.NextHopName)
            or nameof(RouteWorkspaceViewModel.RouteCount)
            or nameof(RouteWorkspaceViewModel.ReachedCount))
        {
            RaiseRouteProperties();
        }
        else if (eventArgs.PropertyName is
            nameof(RouteWorkspaceViewModel.HasCarrierJumpCountdown)
            or nameof(RouteWorkspaceViewModel.CarrierJumpCountdownTitle)
            or nameof(RouteWorkspaceViewModel.CarrierJumpCountdownValue)
            or nameof(RouteWorkspaceViewModel.CarrierJumpPhaseLabel)
            or nameof(RouteWorkspaceViewModel.CarrierJumpPhaseCountdown)
            or nameof(RouteWorkspaceViewModel.HasCarrierJumpPhaseCountdown))
        {
            RaiseCountdownProperties();
        }
    }

    private void RaiseRouteProperties()
    {
        Raise(nameof(HopProgress));
        Raise(nameof(SystemName));
        Raise(nameof(JumpSummary));
        Raise(nameof(JumpsLeft));
        Raise(nameof(FuelLeft));
        Raise(nameof(TritiumInMarket));
        Raise(nameof(JumpFuel));
        Raise(nameof(HasIcyRing));
        Raise(nameof(IcyRingLabel));
        Raise(nameof(HasRestockWarning));
        Raise(nameof(RestockAmount));
    }

    private void RaiseCountdownProperties()
    {
        Raise(nameof(HasCountdown));
        Raise(nameof(CountdownTitle));
        Raise(nameof(Countdown));
        Raise(nameof(CountdownPhase));
        Raise(nameof(CountdownPhaseTime));
        Raise(nameof(HasCountdownPhaseTime));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string FormatTonnes(double? value)
    {
        return value is null ? "\u2014" : $"{value:N0} t";
    }

    private static string FormatNumber(double? value, int decimals)
    {
        return value is null
            ? "\u2014"
            : value.Value.ToString($"N{decimals}");
    }
}
