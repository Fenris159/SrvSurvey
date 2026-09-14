using System.Diagnostics;
using System.Text;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Desktop.Platform;

public sealed class ApplicationLogTraceListener : TraceListener
{
    private const string ClosedPresentationSourceWarning =
        "[Control] PlatformImpl is null, couldn't handle input. (PresentationSource #";
    private const string RenderLoopFailurePrefix = "[Visual]Exception in render loop:";

    private readonly ApplicationLogService applicationLog;
    private readonly Func<DateTimeOffset> getCurrentTime;
    private readonly RepeatedTraceMessageLimiter renderLoopFailureLimiter = new(TimeSpan.FromMinutes(1));
    private readonly Lock syncRoot = new();
    private readonly StringBuilder pending = new();

    public ApplicationLogTraceListener(ApplicationLogService applicationLog)
        : this(applicationLog, () => DateTimeOffset.UtcNow) { }

    internal ApplicationLogTraceListener(ApplicationLogService applicationLog, Func<DateTimeOffset> getCurrentTime)
    {
        this.applicationLog = applicationLog ?? throw new ArgumentNullException(nameof(applicationLog));
        this.getCurrentTime = getCurrentTime ?? throw new ArgumentNullException(nameof(getCurrentTime));
    }

    public override void Write(string? message)
    {
        WriteCore(message, terminateLine: false);
    }

    public override void WriteLine(string? message)
    {
        if (TryHandleRepeatedRenderLoopFailure(message))
        {
            return;
        }

        WriteCore(message, terminateLine: true);
    }

    public override void Flush()
    {
        string? line = null;
        lock (syncRoot)
        {
            if (pending.Length > 0)
            {
                line = pending.ToString();
                pending.Clear();
            }
        }

        if (line is not null)
        {
            AppendLine(line.TrimEnd('\r'));
        }
    }

    private void WriteCore(string? message, bool terminateLine)
    {
        List<string> completeLines = [];
        lock (syncRoot)
        {
            pending.Append(message);
            int startIndex = 0;
            for (int index = 0; index < pending.Length; index++)
            {
                if (pending[index] != '\n')
                {
                    continue;
                }

                completeLines.Add(pending.ToString(startIndex, index - startIndex).TrimEnd('\r'));
                startIndex = index + 1;
            }

            if (startIndex > 0)
            {
                pending.Remove(0, startIndex);
            }

            if (terminateLine)
            {
                completeLines.Add(pending.ToString().TrimEnd('\r'));
                pending.Clear();
            }
        }

        foreach (string line in completeLines)
        {
            AppendLine(line);
        }
    }

    private void AppendLine(string line)
    {
        if (!IsExpectedClosedPresentationSourceWarning(line))
        {
            applicationLog.Append(line);
        }
    }

    private static bool IsExpectedClosedPresentationSourceWarning(string line) =>
        line.StartsWith(ClosedPresentationSourceWarning, StringComparison.Ordinal) && line.EndsWith(')');

    private bool TryHandleRepeatedRenderLoopFailure(string? message)
    {
        if (message is null || !message.StartsWith(RenderLoopFailurePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string signature = message.Split(['\r', '\n'], 2)[0];
        RepeatedTraceMessageLogDecision decision = renderLoopFailureLimiter.Record(signature, getCurrentTime());
        if (!decision.ShouldLog)
        {
            return true;
        }

        if (decision.SuppressedCount > 0)
        {
            applicationLog.Append(
                $"Suppressed {decision.SuppressedCount} repeated Avalonia render-loop failures; latest follows."
            );
        }

        return false;
    }
}

internal readonly record struct RepeatedTraceMessageLogDecision(bool ShouldLog, int SuppressedCount);

internal sealed class RepeatedTraceMessageLimiter(TimeSpan reportInterval)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public RepeatedTraceMessageLogDecision Record(string signature, DateTimeOffset now)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(signature, out Entry? entry))
            {
                entries.Add(signature, new Entry(now));
                return new RepeatedTraceMessageLogDecision(ShouldLog: true, SuppressedCount: 0);
            }

            if (now - entry.LastReportedAt < reportInterval)
            {
                entry.SuppressedCount++;
                return new RepeatedTraceMessageLogDecision(ShouldLog: false, SuppressedCount: 0);
            }

            int suppressedCount = entry.SuppressedCount;
            entry.LastReportedAt = now;
            entry.SuppressedCount = 0;
            return new RepeatedTraceMessageLogDecision(ShouldLog: true, suppressedCount);
        }
    }

    private sealed class Entry(DateTimeOffset lastReportedAt)
    {
        public DateTimeOffset LastReportedAt { get; set; } = lastReportedAt;

        public int SuppressedCount { get; set; }
    }
}
