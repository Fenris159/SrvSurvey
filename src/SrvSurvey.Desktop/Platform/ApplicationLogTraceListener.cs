using System.Diagnostics;
using System.Text;
using SrvSurvey.Core.Diagnostics;

namespace SrvSurvey.Desktop.Platform;

public sealed class ApplicationLogTraceListener(
    ApplicationLogService applicationLog) : TraceListener
{
    private const string ClosedPresentationSourceWarning =
        "[Control] PlatformImpl is null, couldn't handle input. (PresentationSource #";

    private readonly object syncRoot = new();
    private readonly StringBuilder pending = new();

    public override void Write(string? message)
    {
        WriteCore(message, terminateLine: false);
    }

    public override void WriteLine(string? message)
    {
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
            var startIndex = 0;
            for (var index = 0; index < pending.Length; index++)
            {
                if (pending[index] != '\n')
                {
                    continue;
                }

                completeLines.Add(pending
                    .ToString(startIndex, index - startIndex)
                    .TrimEnd('\r'));
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

        foreach (var line in completeLines)
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
        line.StartsWith(ClosedPresentationSourceWarning, StringComparison.Ordinal)
        && line.EndsWith(')');
}
