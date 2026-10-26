using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class FleetCarrierJumpCountdownTracker
{
    private const int JumpLockSeconds = 600;
    private const int PadLockdownSeconds = 200;
    private static readonly TimeSpan PostJumpCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CancellationCooldown = TimeSpan.FromMinutes(1);

    private FleetCarrierJumpCountdownKind kind;
    private string? carrierId;
    private string destination = string.Empty;
    private DateTimeOffset targetTime;
    private long destinationAddress;
    private DateTimeOffset scheduledDeparture;

    public FleetCarrierJumpCountdownState Current { get; private set; } = FleetCarrierJumpCountdownState.Inactive;

    public bool Apply(JournalEventEnvelope journalEvent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        bool changed = journalEvent.EventName switch
        {
            "CarrierJumpRequest" => ApplyJumpRequest(journalEvent),
            "CarrierJumpCancelled" => ApplyCancellation(journalEvent, now),
            "CarrierJump" => ApplyCompletedJump(journalEvent, now),
            "CarrierLocation" => ApplyCarrierLocation(journalEvent, now),
            _ => false,
        };

        return Refresh(now) || changed;
    }

    public bool Refresh(DateTimeOffset now)
    {
        Normalize(now);
        FleetCarrierJumpCountdownState next = CreateState(now);
        if (next == Current)
        {
            return false;
        }

        Current = next;
        return true;
    }

    public bool Reset()
    {
        kind = FleetCarrierJumpCountdownKind.None;
        carrierId = null;
        destination = string.Empty;
        targetTime = default;
        destinationAddress = 0;
        scheduledDeparture = default;
        if (Current == FleetCarrierJumpCountdownState.Inactive)
        {
            return false;
        }

        Current = FleetCarrierJumpCountdownState.Inactive;
        return true;
    }

    private bool ApplyJumpRequest(JournalEventEnvelope journalEvent)
    {
        DateTimeOffset? departure = GetDateTimeOffset(journalEvent.Payload, "DepartureTime");
        if (departure is null)
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.Scheduled;
        carrierId = GetIdentifier(journalEvent.Payload, "CarrierID");
        destination =
            GetString(journalEvent.Payload, "SystemName")
            ?? GetString(journalEvent.Payload, "StarSystem")
            ?? string.Empty;
        targetTime = departure.Value;
        scheduledDeparture = departure.Value;
        destinationAddress = GetSystemAddress(journalEvent.Payload);
        return true;
    }

    private bool ApplyCancellation(JournalEventEnvelope journalEvent, DateTimeOffset now)
    {
        if (kind != FleetCarrierJumpCountdownKind.Scheduled || !MatchesCarrier(journalEvent.Payload))
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.CancellationCooldown;
        targetTime = (journalEvent.Timestamp ?? now) + CancellationCooldown;
        return true;
    }

    private bool ApplyCompletedJump(JournalEventEnvelope journalEvent, DateTimeOffset now)
    {
        DateTimeOffset completedAt = journalEvent.Timestamp ?? now;
        kind = FleetCarrierJumpCountdownKind.PostJumpCooldown;
        targetTime = RoundToNearestMinute(completedAt) + PostJumpCooldown;
        destination = GetString(journalEvent.Payload, "StarSystem") ?? destination;
        destinationAddress = GetSystemAddress(journalEvent.Payload) is > 0 and var address
            ? address
            : destinationAddress;
        return true;
    }

    private bool ApplyCarrierLocation(JournalEventEnvelope journalEvent, DateTimeOffset now)
    {
        if (
            kind is not (FleetCarrierJumpCountdownKind.Scheduled or FleetCarrierJumpCountdownKind.InTransit)
            || !MatchesCarrier(journalEvent.Payload)
        )
        {
            return false;
        }

        DateTimeOffset observedAt = journalEvent.Timestamp ?? now;
        if (observedAt < scheduledDeparture)
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.PostJumpCooldown;
        targetTime = RoundToNearestMinute(observedAt) + PostJumpCooldown;
        destination = GetString(journalEvent.Payload, "StarSystem") ?? destination;
        return true;
    }

    private void Normalize(DateTimeOffset now)
    {
        if (kind == FleetCarrierJumpCountdownKind.Scheduled && now >= targetTime)
        {
            kind = FleetCarrierJumpCountdownKind.InTransit;
            targetTime += PostJumpCooldown;
        }

        if (
            kind
                is FleetCarrierJumpCountdownKind.PostJumpCooldown
                    or FleetCarrierJumpCountdownKind.CancellationCooldown
                    or FleetCarrierJumpCountdownKind.InTransit
            && now >= targetTime
        )
        {
            kind = FleetCarrierJumpCountdownKind.None;
            carrierId = null;
            destination = string.Empty;
            targetTime = default;
            destinationAddress = 0;
        }
    }

    private FleetCarrierJumpCountdownState CreateState(DateTimeOffset now)
    {
        if (kind == FleetCarrierJumpCountdownKind.None)
        {
            return FleetCarrierJumpCountdownState.Inactive;
        }

        int secondsRemaining = Math.Max(0, (int)Math.Ceiling((targetTime - now).TotalSeconds));
        if (kind == FleetCarrierJumpCountdownKind.InTransit)
        {
            return new(true, "JUMP IN PROGRESS", string.Empty, "AWAITING ARRIVAL", string.Empty, false, destination)
            {
                Kind = kind,
                SystemAddress = destinationAddress,
            };
        }
        if (kind == FleetCarrierJumpCountdownKind.PostJumpCooldown)
        {
            return new FleetCarrierJumpCountdownState(
                true,
                "JUMP COOLDOWN",
                FormatCountdown(secondsRemaining),
                "CARRIER ARRIVED",
                string.Empty,
                false,
                destination
            )
            {
                Kind = kind,
                SystemAddress = destinationAddress,
            };
        }

        if (kind == FleetCarrierJumpCountdownKind.CancellationCooldown)
        {
            return new FleetCarrierJumpCountdownState(
                true,
                "CANCELLATION COOLDOWN",
                FormatCountdown(secondsRemaining),
                "JUMP CANCELLED",
                string.Empty,
                false,
                destination
            )
            {
                Kind = kind,
                SystemAddress = destinationAddress,
            };
        }

        string phaseLabel;
        string phaseCountdown;
        bool hasPhaseCountdown;
        if (secondsRemaining > JumpLockSeconds)
        {
            phaseLabel = "JUMP INITIATION IN";
            phaseCountdown = FormatCountdown(secondsRemaining - JumpLockSeconds);
            hasPhaseCountdown = true;
        }
        else if (secondsRemaining > PadLockdownSeconds)
        {
            phaseLabel = "PAD LOCKDOWN IN";
            phaseCountdown = FormatCountdown(secondsRemaining - PadLockdownSeconds);
            hasPhaseCountdown = true;
        }
        else
        {
            phaseLabel = "LANDING PADS LOCKED";
            phaseCountdown = string.Empty;
            hasPhaseCountdown = false;
        }

        return new FleetCarrierJumpCountdownState(
            true,
            string.IsNullOrWhiteSpace(destination)
                ? "CARRIER DEPARTURE"
                : $"DEPARTURE TO {destination.ToUpperInvariant()}",
            FormatCountdown(secondsRemaining),
            phaseLabel,
            phaseCountdown,
            hasPhaseCountdown,
            destination
        )
        {
            Kind = kind,
            SystemAddress = destinationAddress,
        };
    }

    private bool MatchesCarrier(JsonElement payload)
    {
        string? candidate = GetIdentifier(payload, "CarrierID");
        return string.IsNullOrWhiteSpace(carrierId)
            || string.IsNullOrWhiteSpace(candidate)
            || string.Equals(carrierId, candidate, StringComparison.Ordinal);
    }

    private static DateTimeOffset RoundToNearestMinute(DateTimeOffset value)
    {
        var rounded = new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);
        return value.Second >= 30 ? rounded.AddMinutes(1) : rounded;
    }

    private static string FormatCountdown(int seconds)
    {
        int minutes = seconds / 60;
        return $"{minutes:N0}:{seconds % 60:00}";
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static long GetSystemAddress(JsonElement payload)
    {
        foreach (string name in new[] { "SystemAddress", "SystemID" })
        {
            if (
                payload.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out long address)
            )
            {
                return address;
            }
        }
        return 0;
    }

    private static string? GetIdentifier(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement payload, string name)
    {
        string? value = GetString(payload, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : null;
    }
}

public sealed record FleetCarrierJumpCountdownState(
    bool IsActive,
    string Title,
    string Countdown,
    string PhaseLabel,
    string PhaseCountdown,
    bool HasPhaseCountdown,
    string Destination
)
{
    public FleetCarrierJumpCountdownKind Kind { get; init; }
    public long SystemAddress { get; init; }

    public static FleetCarrierJumpCountdownState Inactive { get; } =
        new(false, "CARRIER JUMP", "No jump scheduled", string.Empty, string.Empty, false, string.Empty);
}

public enum FleetCarrierJumpCountdownKind
{
    None,
    Scheduled,
    InTransit,
    PostJumpCooldown,
    CancellationCooldown,
}
