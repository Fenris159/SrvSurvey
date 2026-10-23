using System.Text.Json;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Routes;

/// <summary>Records observations cheaply; reconstructs one selected carrier only when fuel inputs are requested.</summary>
public sealed class FleetCarrierFuelJournal
{
    private readonly List<JournalEventEnvelope> events = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);

    public void Reset()
    {
        events.Clear();
        seen.Clear();
    }

    public void Observe(IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        foreach (
            JournalEventEnvelope entry in journalEvents.Where(entry =>
                IsRelevant(entry.EventName) && seen.Add(entry.SourceRecordIdentity ?? entry.RawJson)
            )
        )
        {
            events.Add(entry);
        }
    }

    public FleetCarrierFuelSnapshot? Read(
        FrontierAccountSnapshot? account,
        FollowRouteCarrierKind kind,
        IReadOnlyList<FollowRouteHop> hops,
        JournalEventEnvelope? through = null
    )
    {
        ArgumentNullException.ThrowIfNull(hops);
        FrontierCarrierSnapshot? carrier =
            kind == FollowRouteCarrierKind.Squadron ? account?.SquadronCarrier : account?.Carrier;
        JournalEventEnvelope[] observations = ObservationsThrough(through).ToArray();
        FleetCarrierFuelSnapshot? initial = carrier?.FuelData ?? JournalIdentity(observations, kind);
        if (initial is null)
        {
            return null;
        }
        DateTimeOffset observed =
            (kind == FollowRouteCarrierKind.Squadron ? account?.SquadronCarrierFetchedAt : account?.CarrierFetchedAt)
            ?? initial.ObservedAt;
        var replay = new Replay(
            initial with
            {
                Kind = kind,
                ObservedAt = observed,
                Position = initial.Position ?? FindPosition(hops, initial.SystemName),
            },
            hops
        );
        foreach (JournalEventEnvelope entry in observations)
        {
            replay.Apply(entry);
        }
        return replay.Current;
    }

    private IEnumerable<JournalEventEnvelope> ObservationsThrough(JournalEventEnvelope? through)
    {
        foreach (JournalEventEnvelope entry in events)
        {
            yield return entry;
            if (ReferenceEquals(entry, through) || (through is not null && entry.RawJson == through.RawJson))
            {
                yield break;
            }
        }
    }

    private static FleetCarrierFuelSnapshot? JournalIdentity(
        IEnumerable<JournalEventEnvelope> observations,
        FollowRouteCarrierKind kind
    )
    {
        FleetCarrierFuelSnapshot? identity = null;
        foreach (
            JsonElement data in observations
                .Where(entry => entry.EventName == "CarrierStats")
                .Select(entry => entry.Payload)
        )
        {
            bool squadron =
                data.TryGetProperty("CarrierType", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "SquadronCarrier";
            if (
                squadron != (kind == FollowRouteCarrierKind.Squadron)
                || !data.TryGetProperty("CarrierID", out JsonElement id)
                || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt64(out long number)
                || !data.TryGetProperty("Callsign", out JsonElement callsign)
                || callsign.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(callsign.GetString())
            )
            {
                continue;
            }
            identity = new(
                kind,
                callsign.GetString()!,
                number,
                string.Empty,
                null,
                null,
                null,
                DateTimeOffset.MinValue
            );
        }
        return identity;
    }

    private static bool IsRelevant(string name) =>
        name
            is "CarrierStats"
                or "CarrierDepositFuel"
                or "CarrierJump"
                or "CarrierLocation"
                or "Docked"
                or "Location"
                or "Undocked"
                or "CargoTransfer"
                or "MarketBuy"
                or "MarketSell";

    private static GalacticCoordinate? FindPosition(IReadOnlyList<FollowRouteHop> hops, string system) =>
        hops.FirstOrDefault(hop => string.Equals(hop.Name, system, StringComparison.OrdinalIgnoreCase))?.Position;

    private sealed class Replay(FleetCarrierFuelSnapshot initial, IReadOnlyList<FollowRouteHop> hops)
    {
        private DateTimeOffset fuelAt = initial.ObservedAt;
        private DateTimeOffset cargoAt = initial.ObservedAt;
        private DateTimeOffset capacityAt = initial.ObservedAt;
        private DateTimeOffset locationAt = initial.ObservedAt;
        private long? dockedCarrier;
        public FleetCarrierFuelSnapshot Current { get; private set; } = initial;

        public void Apply(JournalEventEnvelope entry)
        {
            JsonElement data = entry.Payload;
            DateTimeOffset at = entry.Timestamp ?? DateTimeOffset.MinValue;
            switch (entry.EventName)
            {
                case "Docked":
                case "Location":
                    dockedCarrier = Text(data, "StationType") is "FleetCarrier" or "SquadronCarrier"
                        ? Id(data, "MarketID")
                        : null;
                    break;
                case "Undocked":
                    dockedCarrier = null;
                    break;
                case "CarrierStats" when MatchesStats(data):
                    ApplyStats(data, at);
                    break;
                case "CarrierDepositFuel" when Matches(data):
                    SetFuel(Number(data, "Total"), at);
                    break;
                case "CarrierJump" when Matches(data):
                    ApplyJump(data, at);
                    break;
                case "CarrierLocation" when Matches(data):
                    ApplyLocation(data, at);
                    break;
                case "CargoTransfer" when dockedCarrier == Current.CarrierId && dockedCarrier is not null:
                    ApplyTransfer(data, at);
                    break;
                case "MarketBuy" when MatchesMarket(data):
                    ApplyCargo(Text(data, "Type"), -Number(data, "Count"), at);
                    break;
                case "MarketSell" when MatchesMarket(data):
                    ApplyCargo(Text(data, "Type"), Number(data, "Count"), at);
                    break;
            }
        }

        private bool Matches(JsonElement data) =>
            Current.CarrierId is { } id && (Id(data, "CarrierID") ?? Id(data, "MarketID")) == id;

        private bool MatchesMarket(JsonElement data) =>
            Current.CarrierId is { } id && (Id(data, "MarketID") ?? dockedCarrier) == id;

        private bool MatchesStats(JsonElement data)
        {
            if (
                !Matches(data)
                && !string.Equals(Text(data, "Callsign"), Current.Callsign, StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }
            Current = Current with { CarrierId = Id(data, "CarrierID") ?? Current.CarrierId };
            return true;
        }

        private void ApplyStats(JsonElement data, DateTimeOffset at)
        {
            if (Number(data, "FuelLevel") is { } fuel)
            {
                SetFuel(fuel, at);
            }
            if (at >= capacityAt && data.TryGetProperty("SpaceUsage", out JsonElement space))
            {
                double? used = Number(space, "TotalCapacity") - Number(space, "FreeSpace");
                if (used is >= 0)
                {
                    Current = Current with { UsedCapacityTonnes = used, ObservedAt = at };
                    capacityAt = at;
                }
                if (at >= cargoAt && Number(space, "Cargo") == 0)
                {
                    Current = Current with { StoredTritiumTonnes = 0 };
                    cargoAt = at;
                }
            }
        }

        private void SetFuel(double? fuel, DateTimeOffset at)
        {
            if (at < fuelAt)
            {
                return;
            }
            Current = Current with
            {
                TankTonnes = fuel is >= 0 and <= 1000 ? fuel : null,
                ObservedAt = at,
                IsEstimated = false,
            };
            fuelAt = at;
        }

        private void ApplyJump(JsonElement data, DateTimeOffset at)
        {
            if (at <= locationAt)
            {
                return;
            }
            GalacticCoordinate? position =
                ReadPosition(data) ?? FindPosition(hops, Text(data, "StarSystem") ?? string.Empty);
            if (at > fuelAt)
            {
                double? tank =
                    Current.Position is { } from
                    && position is { } to
                    && Current is { TankTonnes: { } fuel, UsedCapacityTonnes: { } used }
                        ? fuel - FleetCarrierFuelPlanner.JumpFuel(Current.Kind, from.DistanceTo(to), used, fuel)
                        : null;
                SetFuel(tank, at);
                Current = Current with { IsEstimated = tank is >= 0 };
            }
            Current = Current with
            {
                Position = position,
                SystemName = Text(data, "StarSystem") ?? Current.SystemName,
                ObservedAt = at,
            };
            locationAt = at;
        }

        private void ApplyLocation(JsonElement data, DateTimeOffset at)
        {
            if (at < locationAt || Text(data, "StarSystem") is not { } system || system == Current.SystemName)
            {
                return;
            }
            if (!string.IsNullOrEmpty(Current.SystemName))
            {
                ApplyJump(data, at);
                return;
            }
            // The login location establishes an origin; it is not evidence of a jump after CarrierStats.
            Current = Current with
            {
                Position = ReadPosition(data) ?? FindPosition(hops, system),
                SystemName = system,
                ObservedAt = at,
            };
            locationAt = at;
        }

        private void ApplyTransfer(JsonElement data, DateTimeOffset at)
        {
            if (
                !data.TryGetProperty("Transfers", out JsonElement transfers)
                || transfers.ValueKind != JsonValueKind.Array
            )
            {
                return;
            }
            foreach (JsonElement transfer in transfers.EnumerateArray())
            {
                double? sign = Text(transfer, "Direction") switch
                {
                    "toship" => -1,
                    "tocarrier" => 1,
                    _ => null,
                };
                ApplyCargo(Text(transfer, "Type"), sign * Number(transfer, "Count"), at);
            }
        }

        private void ApplyCargo(string? commodity, double? delta, DateTimeOffset at)
        {
            if (delta is null || at < cargoAt)
            {
                return;
            }
            if (string.Equals(commodity, "tritium", StringComparison.OrdinalIgnoreCase))
            {
                double? stored = Current.StoredTritiumTonnes + delta;
                Current = Current with { StoredTritiumTonnes = stored is >= 0 ? stored : null, ObservedAt = at };
            }
            if (at >= capacityAt)
            {
                double? used = Current.UsedCapacityTonnes + delta;
                Current = Current with { UsedCapacityTonnes = used is >= 0 ? used : null, ObservedAt = at };
                capacityAt = at;
            }
            cargoAt = at;
        }

        private static long? Id(JsonElement data, string name) =>
            data.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long id)
                ? id
                : null;

        private static string? Text(JsonElement data, string name) =>
            data.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static double? Number(JsonElement data, string name) =>
            data.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out double number)
            && double.IsFinite(number)
                ? number
                : null;

        private static GalacticCoordinate? ReadPosition(JsonElement data)
        {
            if (
                !data.TryGetProperty("StarPos", out JsonElement position)
                || position.ValueKind != JsonValueKind.Array
                || position.GetArrayLength() != 3
            )
            {
                return null;
            }
            double[] xyz = position
                .EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.NaN)
                .ToArray();
            return xyz.All(double.IsFinite) ? new GalacticCoordinate(xyz[0], xyz[1], xyz[2]) : null;
        }
    }
}
