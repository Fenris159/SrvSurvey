using System.Net.Http.Headers;

namespace SrvSurvey.Core.Exploration;

/// <summary>Shares EDAstro's request quota and provider cooldown across catalogue reads and journal writes.</summary>
public sealed class EdAstroTrafficBudget
{
    /// <summary>Process-wide budget used by production EDAstro clients.</summary>
    public static EdAstroTrafficBudget Shared { get; } = new();
    private readonly Lock sync = new();
    private readonly Queue<DateTimeOffset> attempts = new();
    private DateTimeOffset cooldownUntil;

    /// <summary>Reserves one of 100 request attempts per fifteen minutes, returning the next available time.</summary>
    public bool TryAcquire(DateTimeOffset now, out DateTimeOffset retryAt)
    {
        lock (sync)
        {
            while (attempts.TryPeek(out DateTimeOffset first) && now - first >= TimeSpan.FromMinutes(15))
            {
                attempts.Dequeue();
            }
            retryAt = cooldownUntil;
            if (attempts.Count >= 100)
            {
                retryAt = Max(retryAt, attempts.Peek() + TimeSpan.FromMinutes(15));
            }
            if (now < retryAt)
            {
                return false;
            }
            attempts.Enqueue(now);
            return true;
        }
    }

    /// <summary>Extends the shared cooldown from Retry-After and EDAstro's quota response headers.</summary>
    public DateTimeOffset Observe(HttpResponseHeaders headers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(headers);
        lock (sync)
        {
            DateTimeOffset retry = headers.RetryAfter?.Date ?? now + (headers.RetryAfter?.Delta ?? TimeSpan.Zero);
            if (
                headers.TryGetValues("RateLimit-Remaining", out IEnumerable<string>? remaining)
                && remaining.FirstOrDefault() == "0"
                && headers.TryGetValues("RateLimit-Reset", out IEnumerable<string>? reset)
                && int.TryParse(reset.FirstOrDefault(), out int seconds)
            )
            {
                retry = Max(retry, now + TimeSpan.FromSeconds(Math.Clamp(seconds, 10, 86400)));
            }
            cooldownUntil = Max(cooldownUntil, retry);
            return cooldownUntil;
        }
    }

    /// <summary>Returns the later deadline so a shorter delay cannot weaken an existing cooldown.</summary>
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;
}
