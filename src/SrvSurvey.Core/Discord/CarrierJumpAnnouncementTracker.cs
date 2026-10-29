using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Discord;

public enum CarrierAnnouncementKind
{
    Scheduled,
    Cancelled,
    Arrived,
}

public sealed record CarrierJumpAnnouncement(
    CarrierAnnouncementKind Event,
    FollowRouteCarrierKind CarrierKind,
    long CarrierId,
    string Name,
    string Callsign,
    string Origin,
    string Destination,
    string Body,
    DateTimeOffset? Departure,
    DateTimeOffset ObservedAt,
    string Route = ""
);

public sealed record CarrierJournalIdentity(string? FrontierId, string? CommanderName);

/// <summary>Separates owned carriers and confirms arrivals without treating passenger jumps or login locations as jumps.</summary>
public sealed class CarrierJumpAnnouncementTracker
{
    private const string CarrierBuyEvent = "CarrierBuy";
    private readonly Dictionary<string, CommanderCarriers> commanders = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CarrierJumpAnnouncement> Apply(
        string commander,
        IReadOnlyList<JournalEventEnvelope> events,
        FrontierAccountSnapshot? account = null,
        CarrierJournalIdentity? initialIdentity = null
    )
    {
        ArgumentNullException.ThrowIfNull(events);
        if (string.IsNullOrWhiteSpace(commander))
        {
            return [];
        }
        if (!commanders.TryGetValue(commander, out CommanderCarriers? state))
        {
            state = new CommanderCarriers();
            commanders[commander] = state;
        }
        SeedAccount(state, account?.Carrier, FollowRouteCarrierKind.Personal);
        SeedAccount(state, account?.SquadronCarrier, FollowRouteCarrierKind.Squadron);
        var announcements = new List<CarrierJumpAnnouncement>();
        foreach (JournalEventEnvelope entry in ForCommander(commander, events, initialIdentity))
        {
            if (!IsRelevant(entry.EventName) || !state.Remember(entry.RawJson))
            {
                continue;
            }
            CarrierJumpAnnouncement? announcement = ApplyEvent(state, entry);
            if (announcement is not null)
            {
                announcements.Add(announcement);
            }
        }
        return announcements;
    }

    private static void SeedAccount(
        CommanderCarriers state,
        FrontierCarrierSnapshot? source,
        FollowRouteCarrierKind kind
    )
    {
        if (source?.FuelData?.CarrierId is not { } id || id <= 0)
        {
            return;
        }
        if (!state.Carriers.TryGetValue(id, out CarrierState? carrier))
        {
            carrier = new CarrierState(kind);
            state.Carriers[id] = carrier;
        }
        carrier.Kind = kind;
        if (string.IsNullOrWhiteSpace(carrier.Name))
        {
            carrier.Name = source.Name;
        }
        if (string.IsNullOrWhiteSpace(carrier.Callsign))
        {
            carrier.Callsign = source.Callsign;
        }
        if (string.IsNullOrWhiteSpace(carrier.System))
        {
            carrier.System = source.System;
        }
        carrier.RefreshPendingIdentity();
        if (
            carrier.Pending is { Origin.Length: 0 } pending
            && !string.Equals(source.System, pending.Destination, StringComparison.OrdinalIgnoreCase)
        )
        {
            carrier.Pending = pending with { Origin = source.System };
        }
    }

    // Desktop callers supply the identity preceding the batch: a later Commander event cannot own earlier records.
    private static IEnumerable<JournalEventEnvelope> ForCommander(
        string commander,
        IEnumerable<JournalEventEnvelope> events,
        CarrierJournalIdentity? initialIdentity
    )
    {
        CarrierJournalIdentity identity = initialIdentity ?? new(commander, commander);
        foreach (JournalEventEnvelope entry in events)
        {
            if (entry.EventName == "Fileheader")
            {
                identity = new(null, null);
            }
            else if (entry.EventName is "Commander" or "LoadGame")
            {
                identity = ReadIdentity(entry, identity);
            }
            else if (
                string.Equals(
                    identity.FrontierId ?? identity.CommanderName,
                    commander,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                yield return entry;
            }
        }
    }

    private static CarrierJournalIdentity ReadIdentity(JournalEventEnvelope entry, CarrierJournalIdentity previous)
    {
        string? name = Text(entry.Payload, entry.EventName == "Commander" ? "Name" : "Commander");
        if (entry.Payload.TryGetProperty("FID", out _))
        {
            string? frontierId = Text(entry.Payload, "FID");
            if (name is null && string.Equals(frontierId, previous.FrontierId, StringComparison.OrdinalIgnoreCase))
            {
                name = previous.CommanderName;
            }
            return new(frontierId, name);
        }
        return name is not null && !string.Equals(name, previous.CommanderName, StringComparison.OrdinalIgnoreCase)
            ? new(null, name)
            : previous with
            {
                CommanderName = name ?? previous.CommanderName,
            };
    }

    private static CarrierJumpAnnouncement? ApplyEvent(CommanderCarriers state, JournalEventEnvelope entry)
    {
        JsonElement data = entry.Payload;
        long? id = Identifier(data, "CarrierID") ?? Identifier(data, "MarketID");
        if (id is null or <= 0 || entry.Timestamp is not { } at)
        {
            return null;
        }
        state.Carriers.TryGetValue(id.Value, out CarrierState? carrier);
        if (carrier is not null && at < carrier.LastEvent)
        {
            return null;
        }
        carrier = DiscoverOwnedCarrier(state, id.Value, carrier, entry);
        if (carrier is null)
        {
            return null;
        }
        carrier.LastEvent = at;
        switch (entry.EventName)
        {
            case "CarrierStats":
            case CarrierBuyEvent:
            case "CarrierNameChanged":
                carrier.Name = Text(data, "Name") ?? carrier.Name;
                carrier.Callsign = Text(data, "Callsign") ?? carrier.Callsign;
                if (entry.EventName == CarrierBuyEvent)
                {
                    carrier.System = Text(data, "Location") ?? carrier.System;
                }
                carrier.RefreshPendingIdentity();
                return null;
            case "CarrierJumpRequest":
                return Schedule(carrier, id.Value, data, at);
            case "CarrierJumpCancelled":
                CarrierJumpAnnouncement? cancelled = carrier.Pending is { } pending
                    ? pending with
                    {
                        Event = CarrierAnnouncementKind.Cancelled,
                        ObservedAt = at,
                    }
                    : null;
                carrier.Pending = null;
                return cancelled;
            default:
                return ObserveLocation(carrier, id.Value, data, at, entry.EventName == "CarrierJump");
        }
    }

    private static CarrierState? DiscoverOwnedCarrier(
        CommanderCarriers state,
        long id,
        CarrierState? carrier,
        JournalEventEnvelope entry
    )
    {
        JsonElement data = entry.Payload;
        if (entry.EventName is "CarrierStats" or CarrierBuyEvent or "CarrierJumpRequest" or "CarrierLocation")
        {
            FollowRouteCarrierKind? kind = Text(data, "CarrierType") switch
            {
                "SquadronCarrier" => FollowRouteCarrierKind.Squadron,
                "FleetCarrier" => FollowRouteCarrierKind.Personal,
                null => carrier?.Kind ?? FollowRouteCarrierKind.Personal,
                _ => null,
            };
            if (kind is null)
            {
                return null;
            }
            carrier ??= new CarrierState(kind.Value);
            carrier.Kind = kind.Value;
            state.Carriers[id] = carrier;
        }
        return carrier;
    }

    private static CarrierJumpAnnouncement? Schedule(CarrierState carrier, long id, JsonElement data, DateTimeOffset at)
    {
        string? destination = Text(data, "SystemName");
        if (string.IsNullOrWhiteSpace(destination))
        {
            return null;
        }
        DateTimeOffset? departure =
            data.TryGetProperty("DepartureTime", out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out DateTimeOffset time)
                ? time
                : null;
        carrier.DestinationAddress = Identifier(data, "SystemAddress") ?? Identifier(data, "SystemID");
        carrier.DestinationBodyId = Identifier(data, "BodyID");
        carrier.Pending = new CarrierJumpAnnouncement(
            CarrierAnnouncementKind.Scheduled,
            carrier.Kind,
            id,
            carrier.Name,
            carrier.Callsign,
            carrier.System,
            destination,
            Text(data, "Body") ?? "",
            departure,
            at
        );
        return carrier.Pending;
    }

    private static CarrierJumpAnnouncement? ObserveLocation(
        CarrierState carrier,
        long id,
        JsonElement data,
        DateTimeOffset at,
        bool completedJump
    )
    {
        string? system = Text(data, "StarSystem");
        if (string.IsNullOrWhiteSpace(system))
        {
            return null;
        }
        CarrierJumpAnnouncement? pending = carrier.Pending;
        if (pending is null && completedJump)
        {
            return CompleteWithoutRequest(carrier, id, data, system, at);
        }
        long? address = Identifier(data, "SystemAddress");
        bool matches =
            pending is not null
            && at >= (pending.Departure ?? pending.ObservedAt)
            && string.Equals(system, pending.Destination, StringComparison.OrdinalIgnoreCase)
            && (address is null || carrier.DestinationAddress is null || address == carrier.DestinationAddress)
            && (
                completedJump
                || Identifier(data, "BodyID") is not { } body
                || carrier.DestinationBodyId is null
                || body == carrier.DestinationBodyId
            );
        // A pre-departure destination report must not replace the known departure system.
        if (pending is null || matches)
        {
            carrier.System = system;
        }
        if (!matches)
        {
            return null;
        }
        carrier.Pending = null;
        carrier.LastArrival = at;
        return pending! with { Event = CarrierAnnouncementKind.Arrived, ObservedAt = at };
    }

    private static CarrierJumpAnnouncement? CompleteWithoutRequest(
        CarrierState carrier,
        long id,
        JsonElement data,
        string system,
        DateTimeOffset at
    )
    {
        // CarrierLocation and CarrierJump can both confirm the same arrival seconds apart.
        if (
            carrier.LastArrival is { } last
            && at - last <= TimeSpan.FromMinutes(1)
            && string.Equals(carrier.System, system, StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }
        var arrival = new CarrierJumpAnnouncement(
            CarrierAnnouncementKind.Arrived,
            carrier.Kind,
            id,
            carrier.Name,
            carrier.Callsign,
            carrier.System,
            system,
            Text(data, "Body") ?? "",
            null,
            at
        );
        carrier.System = system;
        carrier.LastArrival = at;
        return arrival;
    }

    private static bool IsRelevant(string name) =>
        name
            is "CarrierStats"
                or CarrierBuyEvent
                or "CarrierNameChanged"
                or "CarrierJumpRequest"
                or "CarrierJumpCancelled"
                or "CarrierJump"
                or "CarrierLocation";

    private static string? Text(JsonElement data, string name) =>
        data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? Identifier(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
        {
            return number;
        }
        return
            value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private sealed class CommanderCarriers
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly Queue<string> recent = new();
        public Dictionary<long, CarrierState> Carriers { get; } = [];

        public bool Remember(string identity)
        {
            if (!seen.Add(identity))
            {
                return false;
            }
            recent.Enqueue(identity);
            if (recent.Count > 512)
            {
                seen.Remove(recent.Dequeue());
            }
            return true;
        }
    }

    private sealed class CarrierState(FollowRouteCarrierKind kind)
    {
        public FollowRouteCarrierKind Kind { get; set; } = kind;
        public string Name { get; set; } = "";
        public string Callsign { get; set; } = "";
        public string System { get; set; } = "";
        public DateTimeOffset LastEvent { get; set; }
        public DateTimeOffset? LastArrival { get; set; }
        public CarrierJumpAnnouncement? Pending { get; set; }
        public long? DestinationAddress { get; set; }
        public long? DestinationBodyId { get; set; }

        public void RefreshPendingIdentity()
        {
            if (Pending is { } pending)
            {
                Pending = pending with { Name = Name, Callsign = Callsign, CarrierKind = Kind };
            }
        }
    }
}
