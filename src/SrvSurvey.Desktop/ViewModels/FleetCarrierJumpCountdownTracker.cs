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

    public FleetCarrierJumpCountdownState Current { get; private set; } =
        FleetCarrierJumpCountdownState.Inactive;

    public bool Apply(
        JournalEventEnvelope journalEvent,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
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
        var next = CreateState(now);
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
        if (Current == FleetCarrierJumpCountdownState.Inactive)
        {
            return false;
        }

        Current = FleetCarrierJumpCountdownState.Inactive;
        return true;
    }

    private bool ApplyJumpRequest(JournalEventEnvelope journalEvent)
    {
        var departure = GetDateTimeOffset(
            journalEvent.Payload,
            "DepartureTime");
        if (departure is null)
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.Scheduled;
        carrierId = GetIdentifier(journalEvent.Payload, "CarrierID");
        destination = GetString(journalEvent.Payload, "SystemName")
            ?? GetString(journalEvent.Payload, "StarSystem")
            ?? string.Empty;
        targetTime = departure.Value;
        return true;
    }

    private bool ApplyCancellation(
        JournalEventEnvelope journalEvent,
        DateTimeOffset now)
    {
        if (kind != FleetCarrierJumpCountdownKind.Scheduled
            || !MatchesCarrier(journalEvent.Payload))
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.CancellationCooldown;
        targetTime = (journalEvent.Timestamp ?? now) + CancellationCooldown;
        destination = string.Empty;
        return true;
    }

    private bool ApplyCompletedJump(
        JournalEventEnvelope journalEvent,
        DateTimeOffset now)
    {
        var completedAt = journalEvent.Timestamp ?? now;
        kind = FleetCarrierJumpCountdownKind.PostJumpCooldown;
        targetTime = RoundToNearestMinute(completedAt) + PostJumpCooldown;
        destination = GetString(journalEvent.Payload, "StarSystem")
            ?? destination;
        return true;
    }

    private bool ApplyCarrierLocation(
        JournalEventEnvelope journalEvent,
        DateTimeOffset now)
    {
        if (kind != FleetCarrierJumpCountdownKind.Scheduled
            || !MatchesCarrier(journalEvent.Payload))
        {
            return false;
        }

        var observedAt = journalEvent.Timestamp ?? now;
        if (observedAt < targetTime)
        {
            return false;
        }

        kind = FleetCarrierJumpCountdownKind.PostJumpCooldown;
        targetTime = RoundToNearestMinute(observedAt) + PostJumpCooldown;
        destination = GetString(journalEvent.Payload, "StarSystem")
            ?? destination;
        return true;
    }

    private void Normalize(DateTimeOffset now)
    {
        if (kind == FleetCarrierJumpCountdownKind.Scheduled
            && now >= targetTime)
        {
            kind = FleetCarrierJumpCountdownKind.PostJumpCooldown;
            targetTime = RoundToNearestMinute(targetTime) + PostJumpCooldown;
        }

        if (kind is FleetCarrierJumpCountdownKind.PostJumpCooldown
                or FleetCarrierJumpCountdownKind.CancellationCooldown
            && now >= targetTime)
        {
            kind = FleetCarrierJumpCountdownKind.None;
            carrierId = null;
            destination = string.Empty;
            targetTime = default;
        }
    }

    private FleetCarrierJumpCountdownState CreateState(DateTimeOffset now)
    {
        if (kind == FleetCarrierJumpCountdownKind.None)
        {
            return FleetCarrierJumpCountdownState.Inactive;
        }

        var secondsRemaining = Math.Max(
            0,
            (int)Math.Ceiling((targetTime - now).TotalSeconds));
        if (kind == FleetCarrierJumpCountdownKind.PostJumpCooldown)
        {
            return new FleetCarrierJumpCountdownState(
                true,
                "JUMP COOLDOWN",
                FormatCountdown(secondsRemaining),
                "CARRIER ARRIVED",
                string.Empty,
                false,
                destination);
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
                string.Empty);
        }

        string phaseLabel;
        string phaseCountdown;
        bool hasPhaseCountdown;
        if (secondsRemaining > JumpLockSeconds)
        {
            phaseLabel = "JUMP INITIATION IN";
            phaseCountdown = FormatCountdown(
                secondsRemaining - JumpLockSeconds);
            hasPhaseCountdown = true;
        }
        else if (secondsRemaining > PadLockdownSeconds)
        {
            phaseLabel = "PAD LOCKDOWN IN";
            phaseCountdown = FormatCountdown(
                secondsRemaining - PadLockdownSeconds);
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
            destination);
    }

    private bool MatchesCarrier(JsonElement payload)
    {
        var candidate = GetIdentifier(payload, "CarrierID");
        return string.IsNullOrWhiteSpace(carrierId)
            || string.IsNullOrWhiteSpace(candidate)
            || string.Equals(carrierId, candidate, StringComparison.Ordinal);
    }

    private static DateTimeOffset RoundToNearestMinute(DateTimeOffset value)
    {
        var rounded = new DateTimeOffset(
            value.Year,
            value.Month,
            value.Day,
            value.Hour,
            value.Minute,
            0,
            value.Offset);
        return value.Second >= 30 ? rounded.AddMinutes(1) : rounded;
    }

    private static string FormatCountdown(int seconds)
    {
        var minutes = seconds / 60;
        return $"{minutes:N0}:{seconds % 60:00}";
    }

    private static string? GetString(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string? GetIdentifier(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var property))
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

    private static DateTimeOffset? GetDateTimeOffset(
        JsonElement payload,
        string name)
    {
        var value = GetString(payload, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
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
    string Destination)
{
    public static FleetCarrierJumpCountdownState Inactive { get; } = new(
        false,
        "CARRIER JUMP",
        "No jump scheduled",
        string.Empty,
        string.Empty,
        false,
        string.Empty);
}

public enum FleetCarrierJumpCountdownKind
{
    None,
    Scheduled,
    PostJumpCooldown,
    CancellationCooldown,
}
