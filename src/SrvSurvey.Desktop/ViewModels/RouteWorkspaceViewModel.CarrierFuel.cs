using System.Globalization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class RouteWorkspaceViewModel
{
    private const string CarrierIdPropertyName = "CarrierID";
    private readonly FleetCarrierFuelJournal carrierFuelJournal = new();
    private readonly Dictionary<FollowRouteCarrierKind, long> observedCarrierIds = [];
    private readonly Func<FrontierAccountSnapshot?>? carrierSnapshotSource;
    private readonly Func<Task>? refreshCarrierSnapshot;
    private FollowRouteCarrierKind selectedCarrierKind;
    private FleetCarrierFuelSnapshot? latestCarrierFuel;
    private FrontierAccountSnapshot? carrierAccountBaseline;
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
            if (value is null || !CarrierOptions.Contains(value) || IsBusy || value.Kind == selectedCarrierKind)
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
        carrierAccountBaseline = carrierSnapshotSource?.Invoke() ?? carrierAccountBaseline;
        return ReadObservedCarrierFuel(through);
    }

    private FleetCarrierFuelSnapshot? ReadObservedCarrierFuel(JournalEventEnvelope? through = null)
    {
        latestCarrierFuel = carrierFuelJournal.Read(carrierAccountBaseline, selectedCarrierKind, draftHops, through);
        OnPropertyChanged(nameof(CarrierFuelSummary));
        OnPropertyChanged(nameof(CarrierDestinationSummary));
        return latestCarrierFuel;
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
        carrierFuelJournal.Reset();
        observedCarrierIds.Clear();
        latestCarrierFuel = null;
        carrierAccountBaseline = null;
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

    private void ObserveCarrierFuel(IReadOnlyList<JournalEventEnvelope> events)
    {
        carrierFuelJournal.Observe(events);
        foreach (
            System.Text.Json.JsonElement data in events
                .Where(entry => entry.EventName == "CarrierStats")
                .Select(entry => entry.Payload)
        )
        {
            if (GetInt64(data, CarrierIdPropertyName) is { } observedId)
            {
                FollowRouteCarrierKind kind =
                    GetString(data, "CarrierType") == "SquadronCarrier"
                        ? FollowRouteCarrierKind.Squadron
                        : FollowRouteCarrierKind.Personal;
                observedCarrierIds[kind] = observedId;
            }
            if (
                latestCarrierFuel is { } known
                && string.Equals(GetString(data, "Callsign"), known.Callsign, StringComparison.OrdinalIgnoreCase)
            )
            {
                latestCarrierFuel = known with { CarrierId = GetInt64(data, CarrierIdPropertyName) ?? known.CarrierId };
            }
        }
    }

    private bool MatchesSelectedCarrier(JournalEventEnvelope entry)
    {
        if (!IsFleetCarrierWorkspace)
        {
            return true;
        }
        long? id = GetInt64(entry.Payload, CarrierIdPropertyName) ?? GetInt64(entry.Payload, "MarketID");
        long? selectedId = latestCarrierFuel?.CarrierId;
        if (selectedId is null && observedCarrierIds.TryGetValue(selectedCarrierKind, out long observed))
        {
            selectedId = observed;
        }
        if (selectedId is { } selected)
        {
            if (id is null && entry.EventName == "CarrierJump")
            {
                return HasCarrierJumpCountdown
                    && string.Equals(
                        GetString(entry.Payload, "StarSystem"),
                        CarrierJumpDestination,
                        StringComparison.OrdinalIgnoreCase
                    );
            }
            return id == selected;
        }
        string? type = GetString(entry.Payload, "CarrierType");
        return type is null
            ? selectedCarrierKind == FollowRouteCarrierKind.Personal
            : (type == "SquadronCarrier") == (selectedCarrierKind == FollowRouteCarrierKind.Squadron);
    }

    private bool MatchesCarrierArrival(JournalEventEnvelope entry, FollowRouteHop? nextHop)
    {
        if (
            !IsFleetCarrierWorkspace
            || GetInt64(entry.Payload, CarrierIdPropertyName) is not null
            || GetInt64(entry.Payload, "MarketID") is not null
        )
        {
            return MatchesSelectedCarrier(entry);
        }
        return MatchesUnidentifiedCarrierArrival(entry, nextHop);
    }

    private bool MatchesUnidentifiedCarrierArrival(JournalEventEnvelope entry, FollowRouteHop? nextHop)
    {
        if (nextHop is null)
        {
            return false;
        }
        string? type = GetString(entry.Payload, "CarrierType");
        if (type is not null && (type == "SquadronCarrier") != (selectedCarrierKind == FollowRouteCarrierKind.Squadron))
        {
            return false;
        }
        return (GetInt64(entry.Payload, "SystemAddress") is { } address && nextHop.SystemAddress == address)
            || string.Equals(nextHop.Name, GetString(entry.Payload, "StarSystem"), StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatFuel(double? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? "—";
}

public sealed record RouteCarrierOption(FollowRouteCarrierKind Kind, string Name)
{
    public override string ToString() => Name;
}
