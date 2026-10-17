namespace SrvSurvey.Core.Colonization;

/// <summary>Enforces a per-carrier interval across local attempts and the server's most recent refresh.</summary>
public sealed class ColonizationCarrierRefreshCooldown(
    Func<long, DateTimeOffset?>? loadAttempt = null,
    Action<long, DateTimeOffset>? saveAttempt = null,
    TimeProvider? timeProvider = null
)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock sync = new();
    private readonly Dictionary<long, DateTimeOffset?> attempts = [];

    public TimeSpan Remaining(ColonizationFleetCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        lock (sync)
        {
            return RemainingAt(carrier, clock.GetUtcNow());
        }
    }

    /// <summary>Records before sending, so failed or cancelled attempts also respect the interval.</summary>
    public bool TryBegin(ColonizationFleetCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);
        lock (sync)
        {
            DateTimeOffset now = clock.GetUtcNow();
            if (RemainingAt(carrier, now) > TimeSpan.Zero)
            {
                return false;
            }
            attempts[carrier.MarketId] = now;
            saveAttempt?.Invoke(carrier.MarketId, now);
            return true;
        }
    }

    private TimeSpan RemainingAt(ColonizationFleetCarrier carrier, DateTimeOffset now)
    {
        if (!attempts.TryGetValue(carrier.MarketId, out DateTimeOffset? last))
        {
            last = loadAttempt?.Invoke(carrier.MarketId);
            attempts[carrier.MarketId] = last;
        }
        if (carrier.LastRefresh is { } refreshed && (last is null || refreshed > last))
        {
            last = refreshed;
        }
        TimeSpan remaining = last is { } attempt ? Interval - (now - attempt) : TimeSpan.Zero;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
