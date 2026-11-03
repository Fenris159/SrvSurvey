using System.Globalization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class RouteWorkspaceViewModel
{
    private readonly FollowRouteCarrierKind? dedicatedCarrier;
    public bool CanSelectCarrier => IsFleetCarrierWorkspace && dedicatedCarrier is null;
    public string CarrierIdentityLabel => IsSquadronCarrierWorkspace ? "Squadron carrier" : "Personal fleet carrier";
    private readonly CarrierObservationSession carrierObservations;
    private readonly Func<FrontierAccountSnapshot?>? carrierSnapshotSource;
    private readonly Func<Task>? refreshCarrierSnapshot;
    private FollowRouteCarrierKind selectedCarrierKind;
    private FleetCarrierFuelSnapshot? latestCarrierFuel;
    private FleetCarrierFuelSnapshot? passFuel;
    private int fuelStartIndex;
    private FleetCarrierFuelPlan? capturedFuelPlan;
    private FollowRouteHop[] forecastSource = [];
    private bool forecastRestockLow;
    private bool isFuelWarningVisible;
    private string fuelWarningMessage = string.Empty;

    public IReadOnlyList<RouteCarrierOption> CarrierOptions { get; } =
    [
        new(FollowRouteCarrierKind.Personal, "Personal fleet carrier"),
        new(FollowRouteCarrierKind.Squadron, "Squadron carrier"),
    ];

    public RouteCarrierOption SelectedCarrier
    {
        get => CarrierOptions.Single(option => option.Kind == selectedCarrierKind);
        set
        {
            if (
                !CanSelectCarrier
                || value is null
                || !CarrierOptions.Contains(value)
                || IsBusy
                || value.Kind == selectedCarrierKind
            )
            {
                return;
            }
            profileRevision++;
            SpanshSearch?.Cancel();
            ResetCarrierJumpCountdown();
            ResetCarrierDestination();
            ApplyCarrierKind(value.Kind);
            if (HasRoute)
            {
                CaptureCarrierFuel();
            }
            RefreshPresentation();
            _ = RetainWorkspaceAsync();
        }
    }

    public FollowRouteCarrierKind SelectedCarrierKind => selectedCarrierKind;

    private void ApplyCarrierKind(FollowRouteCarrierKind kind)
    {
        kind = dedicatedCarrier ?? kind;
        if (kind == selectedCarrierKind)
        {
            return;
        }
        selectedCarrierKind = kind;
        ClearCarrierForecast();
        OnPropertyChanged(nameof(SelectedCarrier));
        OnPropertyChanged(nameof(SelectedCarrierKind));
        PrepareCarrierSearch();
    }

    public string CarrierFuelSummary
    {
        get
        {
            if (latestCarrierFuel is not { } fuel)
            {
                return "Refresh to load fuel data for the selected carrier.";
            }
            string estimate = fuel.IsEstimated ? " (fuel estimated)" : string.Empty;
            return $"{fuel.Callsign} · Depot: {FormatFuel(fuel.TankTonnes)} t · Hold: {FormatFuel(fuel.StoredTritiumTonnes)} t · {fuel.ObservedAt.LocalDateTime:t}{estimate}";
        }
    }

    public bool IsFuelWarningVisible
    {
        get => isFuelWarningVisible;
        private set => SetDialogField(ref isFuelWarningVisible, value, nameof(IsFuelWarningVisible));
    }

    public string FuelWarningMessage
    {
        get => fuelWarningMessage;
        private set => SetField(ref fuelWarningMessage, value);
    }

    public double? AdditionalTritiumNeeded => capturedFuelPlan?.AdditionalFuelTonnes;

    /// <summary>Prefills an opening search from the selected carrier without changing a generated pass.</summary>
    public void PrepareCarrierSearch()
    {
        if (!IsFleetCarrierWorkspace)
        {
            return;
        }
        FleetCarrierFuelSnapshot? snapshot = ReadCarrierFuel();
        SpanshSearch?.ApplyCarrierContext(snapshot);
    }

    /// <summary>Initializes an opening workspace once, after its journal bootstrap has supplied observations.</summary>
    public void PrepareCarrierWorkspace()
    {
        PrepareCarrierSearch();
        if (passFuel is null)
        {
            CaptureCarrierFuel();
        }
    }

    private FleetCarrierFuelSnapshot? ReadCarrierFuel(JournalEventEnvelope? through = null)
    {
        latestCarrierFuel = carrierObservations.Read(
            carrierSnapshotSource?.Invoke(),
            selectedCarrierKind,
            draftHops,
            through
        );
        PresentCarrierFuel();
        return latestCarrierFuel;
    }

    private void PresentCarrierFuel()
    {
        if (!string.Equals(latestCarrierFuel?.SystemName, carrierArrivedFuelSystem, StringComparison.OrdinalIgnoreCase))
        {
            carrierArrivedFuelEstimate = null;
            carrierArrivedFuelSystem = null;
        }
        OnPropertyChanged(nameof(CarrierFuelSummary));
        OnPropertyChanged(nameof(CarrierDestinationSummary));
    }

    private void ClearCarrierForecast()
    {
        passFuel = null;
        capturedFuelPlan = null;
        forecastSource = [];
        IsFuelWarningVisible = false;
        OnPropertyChanged(nameof(AdditionalTritiumNeeded));
    }

    private void ResetCarrierFuelContext()
    {
        carrierObservations.Reset();
        latestCarrierFuel = null;
        SpanshSearch?.ApplyCarrierContext(null);
        ClearCarrierForecast();
        OnPropertyChanged(nameof(CarrierFuelSummary));
    }

    /// <summary>Called on generation, refresh, selection and pass boundaries, never on an ordinary arrival.</summary>
    private void CaptureCarrierFuel(JournalEventEnvelope? through = null)
    {
        if (!IsFleetCarrierWorkspace || !HasRoute)
        {
            return;
        }
        passFuel = ReadCarrierFuel(through);
        fuelStartIndex = Math.Clamp(lastReachedIndex, 0, draftHops.Length - 1);
        forecastSource = [];
        capturedFuelPlan = null;
        RefreshPresentation();
        UpdateFuelWarning();
    }

    private FleetCarrierFuelPlan? GetCapturedFuelPlan()
    {
        if (passFuel is null)
        {
            return null;
        }
        if (
            capturedFuelPlan is null
            || forecastRestockLow != RestockOnlyWhenLow
            || !forecastSource.SequenceEqual(draftHops)
        )
        {
            capturedFuelPlan = FleetCarrierFuelPlanner.Calculate(
                draftHops,
                passFuel,
                RestockOnlyWhenLow,
                fuelStartIndex
            );
            forecastSource = draftHops.ToArray();
            forecastRestockLow = RestockOnlyWhenLow;
            UpdateFuelWarning();
        }
        return capturedFuelPlan;
    }

    private void UpdateFuelWarning()
    {
        OnPropertyChanged(nameof(AdditionalTritiumNeeded));
        if (
            capturedFuelPlan
            is not { UnfundedHopIndex: not null, AdditionalFuelTonnes: > 0, FuelRequiredTonnes: { } required } plan
        )
        {
            IsFuelWarningVisible = false;
            return;
        }
        double additional = plan.AdditionalFuelTonnes.Value;
        FuelWarningMessage =
            $"Your carrier needs approximately {required:N0} t of tritium for the remaining route and is short by {additional:N0} t. Mine or buy at least {additional:N0} t, then select Refresh to recalculate from this waypoint.";
        if (plan.RequiresFuelStops)
        {
            FuelWarningMessage +=
                " The additional fuel exceeds available cargo space, so plan resupply stops along the way.";
        }
        IsFuelWarningVisible = true;
    }

    private static string FormatFuel(double? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
}

public sealed record RouteCarrierOption(FollowRouteCarrierKind Kind, string Name)
{
    public override string ToString() => Name;
}
