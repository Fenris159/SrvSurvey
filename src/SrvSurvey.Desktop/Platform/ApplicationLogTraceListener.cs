using System.Diagnostics;
using System.Text;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Desktop.Platform;

public sealed class ApplicationLogTraceListener : TraceListener
{
    private const string ClosedPresentationSourceWarning =
        "[Control] PlatformImpl is null, couldn't handle input. (PresentationSource #";
    private const string RenderLoopFailurePrefix = "[Visual]Exception in render loop:";
    private const string MissingSessionManagerPrefix =
        "[X11Platform] SMLib/ICELib reported a new error: SESSION_MANAGER environment variable not defined";
    private const string IbusDestroyUnknownMethod =
        "org.freedesktop.DBus.Error.UnknownMethod: Method Destroy is not implemented on interface org.freedesktop.IBus.Service";
    private const string IbusDisposedContext = "Object does not exist at path";
    private const string IbusMissingContextInterface = "No such interface";
    private const string IbusContextInterface = "org.freedesktop.IBus.InputContext";
    private const string AvaloniaIbusDisposeFrame = "Avalonia.FreeDesktop.DBusIme.DBusTextInputMethodBase.Dispose()";
    private const string AvaloniaIbusFrame = "Avalonia.FreeDesktop.DBusIme";
    private const string AvaloniaIbusInstance = "(IBusX11TextInputMethod #";

    private readonly ApplicationLogService applicationLog;
    private readonly Func<DateTimeOffset> getCurrentTime;
    private readonly RepeatedTraceMessageLimiter renderLoopFailureLimiter = new(TimeSpan.FromMinutes(1));
    private readonly Lock syncRoot = new();
    private readonly StringBuilder pending = new();
    private readonly List<string> pendingImeBlock = [];

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
        if (IsExpectedPlatformNoise(message) || TryHandleRepeatedRenderLoopFailure(message))
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
        List<string> linesToAppend;
        lock (syncRoot)
        {
            linesToAppend = FilterPlatformNoiseLine(line);
        }

        foreach (string filteredLine in linesToAppend.Where(line => !IsExpectedClosedPresentationSourceWarning(line)))
        {
            applicationLog.Append(filteredLine);
        }
    }

    private List<string> FilterPlatformNoiseLine(string line)
    {
        var linesToAppend = new List<string>();
        bool startsImeBlock = line.StartsWith("[IME] Error", StringComparison.Ordinal);
        if (pendingImeBlock.Count > 0 && line.Length > 0 && line[0] == '[' && !startsImeBlock)
        {
            linesToAppend.AddRange(pendingImeBlock);
            pendingImeBlock.Clear();
        }

        if (startsImeBlock)
        {
            if (pendingImeBlock.Count > 0)
            {
                linesToAppend.AddRange(pendingImeBlock);
                pendingImeBlock.Clear();
            }

            pendingImeBlock.Add(line);
            return linesToAppend;
        }

        if (pendingImeBlock.Count == 0)
        {
            linesToAppend.Add(line);
            return linesToAppend;
        }

        pendingImeBlock.Add(line);
        if (
            !line.Contains(AvaloniaIbusInstance, StringComparison.Ordinal)
            && !line.Contains(AvaloniaIbusDisposeFrame, StringComparison.Ordinal)
        )
        {
            return linesToAppend;
        }

        string block = string.Join(Environment.NewLine, pendingImeBlock);
        if (!IsExpectedPlatformNoise(block))
        {
            linesToAppend.AddRange(pendingImeBlock);
        }

        pendingImeBlock.Clear();
        return linesToAppend;
    }

    private static bool IsExpectedClosedPresentationSourceWarning(string line) =>
        line.StartsWith(ClosedPresentationSourceWarning, StringComparison.Ordinal) && line.EndsWith(')');

    private static bool IsExpectedPlatformNoise(string? message) =>
        message?.StartsWith(MissingSessionManagerPrefix, StringComparison.Ordinal) == true
        || (
            message?.StartsWith("[IME] Error", StringComparison.Ordinal) == true
            && (
                message.Contains(IbusDestroyUnknownMethod, StringComparison.Ordinal)
                || message.Contains(IbusDisposedContext, StringComparison.Ordinal)
                || message.Contains(IbusMissingContextInterface, StringComparison.Ordinal)
                    && message.Contains(IbusContextInterface, StringComparison.Ordinal)
            )
            && (
                message.Contains(AvaloniaIbusFrame, StringComparison.Ordinal)
                || message.Contains(AvaloniaIbusInstance, StringComparison.Ordinal)
            )
        );

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
