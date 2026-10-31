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
    private const string CarrierIdPropertyName = "CarrierID";
    private const string CarrierTypePropertyName = "CarrierType";
    private const string PersonalCarrierType = "FleetCarrier";
    private const string SquadronCarrierType = "SquadronCarrier";
    private readonly FleetCarrierFuelJournal carrierFuelJournal = new();
    private readonly Dictionary<FollowRouteCarrierKind, long> observedCarrierIds = [];
    private readonly Func<FrontierAccountSnapshot?>? carrierSnapshotSource;
    private readonly Func<Task>? refreshCarrierSnapshot;
    private FollowRouteCarrierKind selectedCarrierKind;
    private FollowRouteCarrierKind OtherCarrierKind =>
        selectedCarrierKind == FollowRouteCarrierKind.Personal
            ? FollowRouteCarrierKind.Squadron
            : FollowRouteCarrierKind.Personal;
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
        carrierAccountBaseline = carrierSnapshotSource?.Invoke() ?? carrierAccountBaseline;
        return ReadObservedCarrierFuel(through);
    }

    private FleetCarrierFuelSnapshot? ReadObservedCarrierFuel(JournalEventEnvelope? through = null)
    {
        latestCarrierFuel = carrierFuelJournal.Read(
            carrierAccountBaseline,
            selectedCarrierKind,
            draftHops,
            through,
            dedicatedCarrier is null ? null : MatchesDedicatedCarrierMovement
        );
        if (!string.Equals(latestCarrierFuel?.SystemName, carrierArrivedFuelSystem, StringComparison.OrdinalIgnoreCase))
        {
            carrierArrivedFuelEstimate = null;
            carrierArrivedFuelSystem = null;
        }
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
        if (dedicatedCarrier is not null)
        {
            // Unsupported types must not teach the personal workspace an unrelated carrier's identity.
            events = events
                .Where(entry =>
                    GetString(entry.Payload, CarrierTypePropertyName)
                        is null
                            or PersonalCarrierType
                            or SquadronCarrierType
                )
                .ToArray();
        }
        carrierFuelJournal.Observe(events);
        foreach (
            System.Text.Json.JsonElement data in events
                .Where(entry => entry.EventName is "CarrierStats" or "CarrierJumpRequest")
                .Select(entry => entry.Payload)
        )
        {
            if (
                GetInt64(data, CarrierIdPropertyName) is { } observedId
                && GetObservedCarrierKind(observedId, GetString(data, CarrierTypePropertyName)) is { } kind
            )
            {
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

    private FollowRouteCarrierKind? GetObservedCarrierKind(long id, string? type)
    {
        if (type is not null)
        {
            return type == SquadronCarrierType ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal;
        }
        if (carrierFuelJournal.GetKnownCarrierKind(id) is { } observed)
        {
            return observed;
        }
        if (latestCarrierFuel is { } known && known.CarrierId == id)
        {
            return known.Kind;
        }
        if (carrierAccountBaseline?.SquadronCarrier?.FuelData?.CarrierId == id)
        {
            return FollowRouteCarrierKind.Squadron;
        }
        return dedicatedCarrier is null || carrierAccountBaseline?.Carrier?.FuelData?.CarrierId == id
            ? FollowRouteCarrierKind.Personal
            : null;
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
        if (dedicatedCarrier is not null)
        {
            return MatchesDedicatedCarrier(entry, id, selectedId);
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
        if (id is not null && observedCarrierIds.Any(pair => pair.Key != selectedCarrierKind && pair.Value == id))
        {
            return false;
        }
        string? type = GetString(entry.Payload, CarrierTypePropertyName);
        return type is null
            ? selectedCarrierKind == FollowRouteCarrierKind.Personal
            : (type == SquadronCarrierType) == (selectedCarrierKind == FollowRouteCarrierKind.Squadron);
    }

    private bool MatchesDedicatedCarrierMovement(JournalEventEnvelope entry, FleetCarrierFuelSnapshot current)
    {
        long? id = GetInt64(entry.Payload, CarrierIdPropertyName) ?? GetInt64(entry.Payload, "MarketID");
        long? selectedId = current.CarrierId;
        if (selectedId is null && observedCarrierIds.TryGetValue(selectedCarrierKind, out long observed))
        {
            selectedId = observed;
        }
        return MatchesDedicatedCarrier(entry, id, selectedId, current.Callsign);
    }

    private bool MatchesDedicatedCarrier(
        JournalEventEnvelope entry,
        long? id,
        long? selectedId,
        string? observedCallsign = null
    )
    {
        string? type = GetString(entry.Payload, CarrierTypePropertyName);
        string expectedType =
            selectedCarrierKind == FollowRouteCarrierKind.Squadron ? SquadronCarrierType : PersonalCarrierType;
        if (type is not null && !string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            return false;
        }
        FrontierCarrierSnapshot? accountCarrier =
            selectedCarrierKind == FollowRouteCarrierKind.Squadron
                ? carrierAccountBaseline?.SquadronCarrier
                : carrierAccountBaseline?.Carrier;
        string? knownCallsign = observedCallsign ?? latestCarrierFuel?.Callsign ?? accountCarrier?.Callsign;
        string? callsign = GetString(entry.Payload, "Callsign");
        string? station = GetString(entry.Payload, "StationName");
        bool? callsignMatches = MatchesKnownCarrierCallsign(callsign, knownCallsign);
        bool? stationMatches = MatchesKnownCarrierCallsign(station, knownCallsign);
        if (
            callsignMatches == false
            || stationMatches == false
            || (string.IsNullOrWhiteSpace(knownCallsign) && MatchesOtherCarrierCallsign(callsign, station))
        )
        {
            return false;
        }
        if (id is { } candidate)
        {
            if (selectedId is { } selected)
            {
                return candidate == selected;
            }
            if (IsKnownOtherCarrier(candidate))
            {
                return false;
            }
        }
        // A destination, even one in an outstanding request, does not identify which carrier arrived.
        return type is not null || callsignMatches == true || stationMatches == true;
    }

    private bool IsKnownOtherCarrier(long candidate)
    {
        FollowRouteCarrierKind other = OtherCarrierKind;
        FrontierCarrierSnapshot? accountCarrier =
            other == FollowRouteCarrierKind.Squadron
                ? carrierAccountBaseline?.SquadronCarrier
                : carrierAccountBaseline?.Carrier;
        return accountCarrier?.FuelData?.CarrierId == candidate
            || (observedCarrierIds.TryGetValue(other, out long observed) && candidate == observed);
    }

    private bool MatchesOtherCarrierCallsign(string? callsign, string? station)
    {
        if (string.IsNullOrWhiteSpace(callsign) && string.IsNullOrWhiteSpace(station))
        {
            return false;
        }
        FrontierCarrierSnapshot? other =
            OtherCarrierKind == FollowRouteCarrierKind.Squadron
                ? carrierAccountBaseline?.SquadronCarrier
                : carrierAccountBaseline?.Carrier;
        string? known =
            carrierFuelJournal.Read(carrierAccountBaseline, OtherCarrierKind, draftHops)?.Callsign ?? other?.Callsign;
        return MatchesKnownCarrierCallsign(callsign, known) == true
            || MatchesKnownCarrierCallsign(station, known) == true;
    }

    // Null means the event and current carrier do not supply comparable callsigns.
    private static bool? MatchesKnownCarrierCallsign(string? candidate, string? known) =>
        string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(known)
            ? null
            : string.Equals(candidate.Trim(), known.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool MatchesCarrierArrival(JournalEventEnvelope entry, FollowRouteHop? nextHop)
    {
        if (
            dedicatedCarrier is not null
            || !IsFleetCarrierWorkspace
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
        string? type = GetString(entry.Payload, CarrierTypePropertyName);
        if (
            type is not null
            && (type == SquadronCarrierType) != (selectedCarrierKind == FollowRouteCarrierKind.Squadron)
        )
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
