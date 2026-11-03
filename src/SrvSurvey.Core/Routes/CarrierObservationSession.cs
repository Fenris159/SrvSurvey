using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Routes;

/// <summary>Owns carrier evidence, reconstruction and countdown attribution for one route workspace.</summary>
public sealed class CarrierObservationSession(bool dedicated)
{
    private const string CarrierIdPropertyName = "CarrierID";
    private const string CarrierTypePropertyName = "CarrierType";
    private const string PersonalCarrierType = "FleetCarrier";
    private const string SquadronCarrierType = "SquadronCarrier";
    private readonly FleetCarrierFuelJournal journal = new();
    private readonly FleetCarrierJumpCountdownTracker countdown = new();
    private readonly Dictionary<FollowRouteCarrierKind, long> observedCarrierIds = [];
    private FrontierAccountSnapshot? account;
    private FollowRouteCarrierKind selectedCarrierKind;
    private IReadOnlyList<FollowRouteHop> hops = [];
    private FleetCarrierJumpCountdownState attributionCountdown = FleetCarrierJumpCountdownState.Inactive;
    private FollowRouteCarrierKind OtherCarrierKind =>
        selectedCarrierKind == FollowRouteCarrierKind.Personal
            ? FollowRouteCarrierKind.Squadron
            : FollowRouteCarrierKind.Personal;

    public FleetCarrierFuelSnapshot? Fuel { get; private set; }
    public FleetCarrierJumpCountdownState Countdown => countdown.Current;

    public void Apply(
        IReadOnlyList<JournalEventEnvelope> events,
        FrontierAccountSnapshot? baseline,
        FollowRouteCarrierKind kind,
        IReadOnlyList<FollowRouteHop> route,
        DateTimeOffset now
    )
    {
        SetContext(baseline, kind, route);
        Observe(events);
        ReadFuel();
        // Legacy inference uses the countdown visible before this batch, as it always has.
        attributionCountdown = countdown.Current;
        foreach (JournalEventEnvelope entry in events.Where(MatchesSelectedCarrier))
        {
            countdown.Apply(entry, now);
        }
    }

    public FleetCarrierFuelSnapshot? Read(
        FrontierAccountSnapshot? baseline,
        FollowRouteCarrierKind kind,
        IReadOnlyList<FollowRouteHop> route,
        JournalEventEnvelope? through = null
    )
    {
        SetContext(baseline, kind, route);
        return ReadFuel(through);
    }

    private void SetContext(
        FrontierAccountSnapshot? baseline,
        FollowRouteCarrierKind kind,
        IReadOnlyList<FollowRouteHop> route
    )
    {
        account = baseline ?? account;
        selectedCarrierKind = kind;
        hops = route;
    }

    private FleetCarrierFuelSnapshot? ReadFuel(JournalEventEnvelope? through = null)
    {
        Fuel = journal.Read(
            account,
            selectedCarrierKind,
            hops,
            through,
            dedicated ? MatchesDedicatedCarrierMovement : null
        );
        return Fuel;
    }

    public bool RefreshCountdown(DateTimeOffset now) => countdown.Refresh(now);

    public bool ResetCountdown() => countdown.Reset();

    public void Reset()
    {
        journal.Reset();
        countdown.Reset();
        observedCarrierIds.Clear();
        Fuel = null;
        account = null;
        hops = [];
        attributionCountdown = FleetCarrierJumpCountdownState.Inactive;
    }

    private void Observe(IReadOnlyList<JournalEventEnvelope> events)
    {
        if (dedicated)
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
        journal.Observe(events);
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
                Fuel is { } known
                && string.Equals(GetString(data, "Callsign"), known.Callsign, StringComparison.OrdinalIgnoreCase)
            )
            {
                Fuel = known with { CarrierId = GetInt64(data, CarrierIdPropertyName) ?? known.CarrierId };
            }
        }
    }

    private FollowRouteCarrierKind? GetObservedCarrierKind(long id, string? type)
    {
        if (type is not null)
        {
            return type == SquadronCarrierType ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal;
        }
        if (journal.GetKnownCarrierKind(id) is { } observed)
        {
            return observed;
        }
        if (Fuel is { } known && known.CarrierId == id)
        {
            return known.Kind;
        }
        if (account?.SquadronCarrier?.FuelData?.CarrierId == id)
        {
            return FollowRouteCarrierKind.Squadron;
        }
        return !dedicated || account?.Carrier?.FuelData?.CarrierId == id ? FollowRouteCarrierKind.Personal : null;
    }

    private bool MatchesSelectedCarrier(JournalEventEnvelope entry)
    {
        long? id = GetInt64(entry.Payload, CarrierIdPropertyName) ?? GetInt64(entry.Payload, "MarketID");
        long? selectedId = Fuel?.CarrierId;
        if (selectedId is null && observedCarrierIds.TryGetValue(selectedCarrierKind, out long observed))
        {
            selectedId = observed;
        }
        if (dedicated)
        {
            return MatchesDedicatedCarrier(entry, id, selectedId);
        }
        if (selectedId is { } selected)
        {
            if (id is null && entry.EventName == "CarrierJump")
            {
                return attributionCountdown.IsActive
                    && string.Equals(
                        GetString(entry.Payload, "StarSystem"),
                        attributionCountdown.Destination,
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
            selectedCarrierKind == FollowRouteCarrierKind.Squadron ? account?.SquadronCarrier : account?.Carrier;
        string? knownCallsign = observedCallsign ?? Fuel?.Callsign ?? accountCarrier?.Callsign;
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
            other == FollowRouteCarrierKind.Squadron ? account?.SquadronCarrier : account?.Carrier;
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
            OtherCarrierKind == FollowRouteCarrierKind.Squadron ? account?.SquadronCarrier : account?.Carrier;
        string? known = journal.Read(account, OtherCarrierKind, hops)?.Callsign ?? other?.Callsign;
        return MatchesKnownCarrierCallsign(callsign, known) == true
            || MatchesKnownCarrierCallsign(station, known) == true;
    }

    // Null means the event and current carrier do not supply comparable callsigns.
    private static bool? MatchesKnownCarrierCallsign(string? candidate, string? known) =>
        string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(known)
            ? null
            : string.Equals(candidate.Trim(), known.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool AcceptsArrival(JournalEventEnvelope entry, FollowRouteHop? nextHop)
    {
        if (
            dedicated
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

    private static string? GetString(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetInt64(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long id)
            ? id
            : null;
}
