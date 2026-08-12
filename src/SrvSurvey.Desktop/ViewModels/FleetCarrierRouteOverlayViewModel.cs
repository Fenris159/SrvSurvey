using System.ComponentModel;
using SrvSurvey.Core.Routes;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierRouteOverlayViewModel : IDisposable
{
    private static readonly TimeSpan FinishedLingerDuration =
        TimeSpan.FromSeconds(3);

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
        TimeProvider? timeProvider = null)
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(capabilities);
        this.timeProvider = timeProvider ?? TimeProvider.System;
        lastPendingHop = route.NextHop;
        wasComplete = route.IsComplete;
        route.PropertyChanged += OnRoutePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string HopProgress => editorPreview?.HopProgress
        ?? (route.RouteCount == 0
            ? "NO ROUTE"
            : IsFinished
                ? "FINISHED"
                : route.ReachedCount == 0
                    ? "START"
                    : $"HOP {Math.Min(route.ReachedCount, TotalHops):N0} / {TotalHops:N0}");

    public string SystemName => editorPreview?.SystemName
        ?? DisplayedHop?.Name
        ?? route.NextHopName;

    public bool ShouldShow => route.ShouldShowFleetCarrierRouteOverlay
        || IsFinished;

    public string JumpSummary
    {
        get
        {
            if (editorPreview is not null)
            {
                return editorPreview.JumpSummary;
            }

            var carrier = DisplayedHop?.Carrier;
            return $"{FormatNumber(carrier?.DistanceLy, 2)} LY JUMP  \u2022  "
                + $"{FormatNumber(carrier?.RemainingLy, 2)} LY REMAINING";
        }
    }

    public string JumpsLeft
    {
        get
        {
            if (editorPreview is not null)
            {
                return editorPreview.JumpsLeft;
            }

            var count = Math.Max(
                0,
                route.RouteCount - route.ReachedCount - 1);
            return $"{count:N0} {(count == 1 ? "JUMP" : "JUMPS")} LEFT";
        }
    }

    public string FuelLeft => editorPreview?.FuelLeft
        ?? FormatTonnes(DisplayedHop?.Carrier?.FuelRemainingTonnes);

    public string TritiumInMarket => editorPreview?.TritiumInMarket
        ?? FormatTonnes(DisplayedHop?.Carrier?.TritiumInMarketTonnes);

    public string JumpFuel => editorPreview?.JumpFuel
        ?? FormatTonnes(DisplayedHop?.Carrier?.FuelUsedTonnes);

    public bool HasIcyRing => editorPreview?.HasIcyRing
        ?? DisplayedHop?.Carrier?.HasIcyRing == true;

    public string IcyRingLabel => editorPreview?.IcyRingLabel
        ?? (DisplayedHop?.Carrier is
        {
            HasIcyRing: true,
            IsSystemPristine: true,
        }
                ? "PRISTINE ICY RING"
                : "ICY RING");

    public bool HasRestockWarning => editorPreview?.HasRestockWarning
        ?? DisplayedHop?.Carrier?.MustRestock == true;

    public string RestockAmount => editorPreview?.RestockAmount
        ?? FormatTonnes(DisplayedHop?.Carrier?.RestockAmountTonnes);

    public bool HasCountdown => editorPreview?.HasCountdown
        ?? route.HasCarrierJumpCountdown;

    public string CountdownTitle => editorPreview?.CountdownTitle
        ?? route.CarrierJumpCountdownTitle;

    public string Countdown => editorPreview?.Countdown
        ?? route.CarrierJumpCountdownValue;

    public string CountdownPhase => editorPreview?.CountdownPhase
        ?? route.CarrierJumpPhaseLabel;

    public string CountdownPhaseTime => editorPreview?.CountdownPhaseTime
        ?? route.CarrierJumpPhaseCountdown;

    public bool HasCountdownPhaseTime => editorPreview?.HasCountdownPhaseTime
        ?? route.HasCarrierJumpPhaseCountdown;

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
        if (finishedUntil is not { } expiry
            || expiry > timeProvider.GetUtcNow())
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

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        UpdateCompletionTransition();
        if (eventArgs.PropertyName is
            nameof(RouteWorkspaceViewModel.NextHop)
            or nameof(RouteWorkspaceViewModel.NextHopName)
            or nameof(RouteWorkspaceViewModel.RouteCount)
            or nameof(RouteWorkspaceViewModel.ReachedCount)
            or nameof(RouteWorkspaceViewModel.IsComplete)
            or nameof(RouteWorkspaceViewModel.ShouldShowFleetCarrierRouteOverlay))
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

    private FollowRouteHop? DisplayedHop => IsFinished
        ? finishedHop
        : route.NextHop;

    private bool IsFinished => finishedUntil > timeProvider.GetUtcNow()
        && finishedHop is not null;

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
            : value.Value.ToString($"N{decimals}");
    }
}

internal sealed record FleetCarrierRouteEditorPreview(
    string HopProgress,
    string SystemName,
    string JumpSummary,
    string JumpsLeft,
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
    bool HasCountdownPhaseTime);

internal enum FleetCarrierRouteEditorPreviewState
{
    Cooldown,
    Scheduled,
    RouteOnly,
}
