namespace SrvSurvey.Desktop.Platform.Overlay;

internal readonly record struct X11ExpectedErrorSignature(
    nint Display,
    byte ErrorCode,
    byte RequestCode,
    byte MinorCode,
    nuint ResourceId
);

internal readonly record struct X11ExpectedErrorLogDecision(bool ShouldLog, int SuppressedCount);

internal sealed class X11ExpectedErrorLogLimiter(TimeSpan reportInterval)
{
    private readonly Lock gate = new();
    private readonly Dictionary<X11ExpectedErrorSignature, Entry> entries = [];

    public X11ExpectedErrorLogDecision Record(X11ExpectedErrorSignature signature, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(signature, out Entry? entry))
            {
                entries.Add(signature, new Entry(now));
                return new X11ExpectedErrorLogDecision(ShouldLog: true, SuppressedCount: 0);
            }

            if (now - entry.LastReportedAt < reportInterval)
            {
                entry.SuppressedCount++;
                return new X11ExpectedErrorLogDecision(ShouldLog: false, SuppressedCount: 0);
            }

            int suppressedCount = entry.SuppressedCount;
            entry.LastReportedAt = now;
            entry.SuppressedCount = 0;
            return new X11ExpectedErrorLogDecision(ShouldLog: true, suppressedCount);
        }
    }

    public void RemoveDisplay(nint display)
    {
        lock (gate)
        {
            foreach (X11ExpectedErrorSignature signature in entries.Keys.Where(key => key.Display == display).ToArray())
            {
                entries.Remove(signature);
            }
        }
    }

    private sealed class Entry(DateTimeOffset lastReportedAt)
    {
        public DateTimeOffset LastReportedAt { get; set; } = lastReportedAt;

        public int SuppressedCount { get; set; }
    }
}
