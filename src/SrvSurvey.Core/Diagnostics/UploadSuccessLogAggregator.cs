namespace SrvSurvey.Core.Diagnostics;

internal sealed class UploadSuccessLogAggregator
{
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    private readonly Lock sync = new();
    private readonly Func<DateTimeOffset> utcNow;
    private readonly TimeSpan interval;
    private DateTimeOffset? windowStartedAt;
    private long successfulCount;

    internal UploadSuccessLogAggregator(Func<DateTimeOffset> utcNow, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(utcNow);
        this.utcNow = utcNow;
        this.interval = interval ?? DefaultInterval;
        if (this.interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
    }

    internal long? Record(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        lock (sync)
        {
            DateTimeOffset now = utcNow();
            if (windowStartedAt is { } startedAt && now - startedAt >= interval)
            {
                long completedCount = successfulCount;
                windowStartedAt = now;
                successfulCount = count;
                return completedCount > 0 ? completedCount : null;
            }

            windowStartedAt ??= now;
            successfulCount += count;
            return null;
        }
    }
}
