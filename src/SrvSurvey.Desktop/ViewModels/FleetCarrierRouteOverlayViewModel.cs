using System.ComponentModel;
using System.Globalization;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierRouteOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan FinishedLingerDuration = TimeSpan.FromSeconds(3);

    private readonly RouteWorkspaceViewModel route;
    private readonly TimeProvider timeProvider;
    private FleetCarrierRouteEditorPreview? editorPreview;
    private FollowRouteHop? lastPendingHop;
    private FollowRouteHop? finishedHop;
    private DateTimeOffset? finishedUntil;
    private bool wasComplete;

    public FleetCarrierRouteOverlayViewModel(
        RouteWorkspaceViewModel route,
        OverlayPlatformCapabilities capabilities,
        TimeProvider? timeProvider = null
    )
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(capabilities);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        lastPendingHop = route.NextHop;
        wasComplete = route.IsComplete;
        route.PropertyChanged += OnRoutePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPassive => editorPreview?.IsPassive ?? (route.UseCarrierNextJumpInformation && !IsFinished);
    public string Header => IsPassive ? "FLEET CARRIER NEXT JUMP" : "FLEET CARRIER ROUTE";
    public bool HasRouteDetails => !IsPassive;
    public bool HasJumpSummary => !string.IsNullOrEmpty(JumpSummary);
    public string FuelLeftLabel
    {
        get
        {
            if (IsPassive && (editorPreview is not null || route.CarrierFuelAfterJump is not null))
            {
                return "After jump (est.)";
            }
            return IsPassive && route.CarrierFuelIsEstimated ? "Fuel left (est.)" : "Fuel left";
        }
    }
    public string JumpFuelLabel => IsPassive ? "Jump fuel (est.)" : "Jump fuel";
    public bool HasFuelLeft => FuelLeft != "—";
    public bool HasMarketTritium => TritiumInMarket != "—";
    public bool HasJumpFuel => JumpFuel != "—";
    public string DepotFuel => editorPreview?.DepotFuel ?? FormatTonnes(route.CarrierDepotFuel);
    public bool HasDepotFuel =>
        IsPassive && DepotFuel != "—" && (editorPreview is not null || route.CarrierFuelAfterJump is not null);
    public bool HasTritiumDetails => HasFuelLeft || HasMarketTritium || HasJumpFuel;
    public string WarningLabel => IsPassive ? "LOW TRITIUM" : "RESTOCK REQUIRED";
    public bool HasWarningAmount => RestockAmount != "—";
    public bool HasMiningResources => HasIcyBody || HasIcyRing;
    public bool HasIcyBody => editorPreview?.HasIcyBody ?? DestinationSummary?.MiningResources.HasIcyBody == true;
    private SrvSurvey.Core.Navigation.SystemSummary? DestinationSummary =>
        string.Equals(route.CarrierDestinationSummary?.SystemName, SystemName, StringComparison.OrdinalIgnoreCase)
            ? route.CarrierDestinationSummary
            : null;

    public string HopProgress
    {
        get
        {
            if (editorPreview is not null)
            {
                return editorPreview.HopProgress;
            }

            if (IsPassive)
            {
                return route.CarrierJumpKind switch
                {
                    FleetCarrierJumpCountdownKind.Scheduled => "SCHEDULED",
                    FleetCarrierJumpCountdownKind.InTransit => "IN TRANSIT",
                    FleetCarrierJumpCountdownKind.CancellationCooldown => "CANCELLED",
                    _ => "COOLDOWN",
                };
            }

            if (route.RouteCount == 0)
            {
                return "NO ROUTE";
            }

            if (IsFinished)
            {
                return "FINISHED";
            }

            return route.ReachedCount == 0
                ? "START"
                : $"HOP {Math.Min(route.ReachedCount, TotalHops):N0} / {TotalHops:N0}";
        }
    }

    public string SystemName =>
        editorPreview?.SystemName
        ?? (IsPassive ? route.CarrierJumpDestination : DisplayedHop?.Name ?? route.NextHopName);

    public bool ShouldShow =>
        route.ShouldShowFleetCarrierRouteOverlay || route.ShouldShowCarrierNextJumpOverlay || IsFinished;

    public string JumpSummary
    {
        get
        {
            if (editorPreview is not null)
            {
                return editorPreview.JumpSummary;
            }

            if (IsPassive)
            {
                return route.CarrierNextJumpDistance is { } distance ? $"{distance:N2} LY JUMP (EST.)" : string.Empty;
            }
            FollowRouteCarrierHop? carrier = DisplayedHop?.Carrier;
            return string.Join(
                "  •  ",
                new[]
                {
                    carrier?.DistanceLy is { } jump ? $"{FormatNumber(jump, 2)} LY JUMP" : null,
                    carrier?.RemainingLy is { } remaining ? $"{FormatNumber(remaining, 2)} LY REMAINING" : null,
                }.Where(value => value is not null)
            );
        }
    }

    public string JumpsLeft
    {
        get
        {
            int count = editorPreview?.RemainingJumps ?? route.RemainingJumps;
            string jumps = $"{count:N0} {(count == 1 ? "JUMP" : "JUMPS")} LEFT";
            string estimate = RouteWorkspaceViewModel.FormatCarrierEstimatedTimeRemaining(count);
            return estimate.Length == 0 ? jumps : $"{jumps} • {estimate}";
        }
    }

    public string FuelLeft =>
        editorPreview?.FuelLeft
        ?? FormatTonnes(
            IsPassive
                ? route.CarrierFuelAfterJump ?? route.CarrierDepotFuel
                : DisplayedHop?.Carrier?.FuelRemainingTonnes
        );

    public string TritiumInMarket =>
        editorPreview?.TritiumInMarket
        ?? FormatTonnes(IsPassive ? route.CarrierStoredTritium : DisplayedHop?.Carrier?.TritiumInMarketTonnes);

    public string JumpFuel =>
        editorPreview?.JumpFuel
        ?? FormatTonnes(IsPassive ? route.CarrierNextJumpFuel : DisplayedHop?.Carrier?.FuelUsedTonnes);

    public bool HasIcyRing =>
        editorPreview?.HasIcyRing
        ?? (
            DisplayedHop?.Carrier?.HasIcyRing == true && !IsPassive
            || DestinationSummary?.MiningResources.HasIcyRing == true
        );

    public string IcyRingLabel =>
        editorPreview?.IcyRingLabel
        ?? (
            DisplayedHop?.Carrier is { HasIcyRing: true, IsSystemPristine: true } && !IsPassive
            || DestinationSummary?.MiningResources.HasPristineIcyRing == true
                ? "PRISTINE ICY RING"
                : "ICY RING"
        );

    public bool HasRestockWarning =>
        editorPreview?.HasRestockWarning
        ?? (IsPassive ? route.CarrierFuelAfterJump is < 200 : DisplayedHop?.Carrier?.MustRestock == true);

    public string RestockAmount =>
        editorPreview?.RestockAmount
        ?? FormatTonnes(IsPassive ? route.CarrierFuelAfterJump : DisplayedHop?.Carrier?.RestockAmountTonnes);

    public bool HasCountdown => editorPreview?.HasCountdown ?? route.HasCarrierJumpCountdown;

    public string CountdownTitle =>
        editorPreview?.CountdownTitle
        ?? (
            IsPassive && route.CarrierJumpKind == FleetCarrierJumpCountdownKind.Scheduled
                ? "JUMP DEPARTURE"
                : route.CarrierJumpCountdownTitle
        );

    public string Countdown => editorPreview?.Countdown ?? route.CarrierJumpCountdownValue;

    public string CountdownPhase => editorPreview?.CountdownPhase ?? route.CarrierJumpPhaseLabel;

    public string CountdownPhaseTime => editorPreview?.CountdownPhaseTime ?? route.CarrierJumpPhaseCountdown;

    public bool HasCountdownPhaseTime => editorPreview?.HasCountdownPhaseTime ?? route.HasCarrierJumpPhaseCountdown;

    /// <summary>
    /// Installs representative fleet-carrier route content for the position editor.
    /// </summary>
    internal void InstallEditorPreview(FleetCarrierRouteEditorPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        editorPreview = preview;
        RaiseRouteProperties();
        RaiseCountdownProperties();
    }

    internal void AdvanceTimedTransitions()
    {
        if (finishedUntil is not { } expiry || expiry > timeProvider.GetUtcNow())
        {
            return;
        }

        finishedUntil = null;
        finishedHop = null;
        RaiseRouteProperties();
    }

    public void Dispose()
    {
        route.PropertyChanged -= OnRoutePropertyChanged;
    }

    private void OnRoutePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        UpdateCompletionTransition();
        if (
            eventArgs.PropertyName
            is nameof(RouteWorkspaceViewModel.NextHop)
                or nameof(RouteWorkspaceViewModel.NextHopName)
                or nameof(RouteWorkspaceViewModel.RouteCount)
                or nameof(RouteWorkspaceViewModel.ReachedCount)
                or nameof(RouteWorkspaceViewModel.IsComplete)
                or nameof(RouteWorkspaceViewModel.ShouldShowFleetCarrierRouteOverlay)
                or nameof(RouteWorkspaceViewModel.ShouldShowCarrierNextJumpOverlay)
                or nameof(RouteWorkspaceViewModel.CarrierDestinationSummary)
                or nameof(RouteWorkspaceViewModel.CarrierJumpDestination)
                or nameof(RouteWorkspaceViewModel.CarrierJumpKind)
        )
        {
            RaiseRouteProperties();
        }
        else if (
            eventArgs.PropertyName
            is nameof(RouteWorkspaceViewModel.HasCarrierJumpCountdown)
                or nameof(RouteWorkspaceViewModel.CarrierJumpCountdownTitle)
                or nameof(RouteWorkspaceViewModel.CarrierJumpCountdownValue)
                or nameof(RouteWorkspaceViewModel.CarrierJumpPhaseLabel)
                or nameof(RouteWorkspaceViewModel.CarrierJumpPhaseCountdown)
                or nameof(RouteWorkspaceViewModel.HasCarrierJumpPhaseCountdown)
        )
        {
            RaiseCountdownProperties();
        }
    }

    private void RaiseRouteProperties()
    {
        Raise(nameof(IsPassive));
        Raise(nameof(Header));
        Raise(nameof(HasRouteDetails));
        Raise(nameof(HasJumpSummary));
        Raise(nameof(FuelLeftLabel));
        Raise(nameof(JumpFuelLabel));
        Raise(nameof(HasFuelLeft));
        Raise(nameof(HasMarketTritium));
        Raise(nameof(HasJumpFuel));
        Raise(nameof(DepotFuel));
        Raise(nameof(HasDepotFuel));
        Raise(nameof(HasTritiumDetails));
        Raise(nameof(WarningLabel));
        Raise(nameof(HasWarningAmount));
        Raise(nameof(HasMiningResources));
        Raise(nameof(HasIcyBody));
        Raise(nameof(HopProgress));
        Raise(nameof(SystemName));
        Raise(nameof(ShouldShow));
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

    private FollowRouteHop? DisplayedHop => IsFinished ? finishedHop : route.NextHop;

    private bool IsFinished => finishedUntil > timeProvider.GetUtcNow() && finishedHop is not null;

    private int TotalHops => Math.Max(0, route.RouteCount - 1);

    private void UpdateCompletionTransition()
    {
        if (!wasComplete && route.IsComplete && lastPendingHop is not null)
        {
            finishedHop = lastPendingHop;
            finishedUntil = timeProvider.GetUtcNow() + FinishedLingerDuration;
        }
        else if (wasComplete && !route.IsComplete)
        {
            finishedHop = null;
            finishedUntil = null;
        }

        wasComplete = route.IsComplete;
        if (route.NextHop is { } pendingHop)
        {
            lastPendingHop = pendingHop;
        }
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
            : value.Value.ToString(
                string.Create(CultureInfo.InvariantCulture, $"N{decimals}"),
                CultureInfo.CurrentCulture
            );
    }
}

internal sealed record FleetCarrierRouteEditorPreview(
    string HopProgress,
    string SystemName,
    string JumpSummary,
    int RemainingJumps,
    string FuelLeft,
    string TritiumInMarket,
    string JumpFuel,
    bool HasIcyRing,
    string IcyRingLabel,
    bool HasRestockWarning,
    string RestockAmount,
    bool HasCountdown,
    string CountdownTitle,
    string Countdown,
    string CountdownPhase,
    string CountdownPhaseTime,
    bool HasCountdownPhaseTime,
    bool IsPassive = false,
    bool HasIcyBody = false,
    string? DepotFuel = null
);

internal enum FleetCarrierRouteEditorPreviewState
{
    Cooldown,
    Scheduled,
    RouteOnly,
    Passive,
}
