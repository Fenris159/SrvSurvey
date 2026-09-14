using System.Diagnostics;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationLogTraceListenerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-trace-{Guid.NewGuid():N}"
    );

    [Fact]
    public void ListenerCombinesFragmentsAndSeparatesCompleteLines()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.Write("Avalonia ");
        listener.WriteLine("started");
        listener.Write("first\nsecond");
        listener.Flush();

        Assert.Collection(
            log.Entries,
            line => Assert.EndsWith(": Avalonia started", line),
            line => Assert.EndsWith(": first", line),
            line => Assert.EndsWith(": second", line)
        );
    }

    [Fact]
    public void ListenerOmitsLateInputForAnAlreadyClosedAvaloniaWindow()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.WriteLine("[Control] PlatformImpl is null, couldn't handle input. " + "(PresentationSource #12345)");
        listener.Write("[Control] PlatformImpl is null, couldn't handle input. " + "(PresentationSource #67890)");
        listener.Flush();
        listener.WriteLine("[Control] A different warning");

        Assert.DoesNotContain(log.Entries, line => line.Contains("PlatformImpl is null", StringComparison.Ordinal));
        Assert.Contains(
            log.Entries,
            line => line.EndsWith(": [Control] A different warning", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void HandAuthoredSectorProbeDoesNotPolluteTheApplicationLog()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        using var listener = new ApplicationLogTraceListener(log);
        Trace.Listeners.Add(listener);

        try
        {
            Assert.True(BoxelAddress.TryFromSystemAddress(684107179361, "Col 359 Sector JX-K b24-0", out _));
            Trace.Flush();
            listener.Flush();
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        Assert.DoesNotContain(
            log.Entries,
            line => line.Contains("Sector fragment not matched", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ListenerAggregatesRepeatedGlxRenderLoopFailures()
    {
        var now = new DateTimeOffset(2026, 9, 14, 17, 51, 51, TimeSpan.Zero);
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log, () => now);
        const string failure =
            "[Visual]Exception in render loop: 'Avalonia.OpenGL.OpenGlException: glXMakeContextCurrent failed\n"
            + "   at Avalonia.X11.Glx.GlxContext.MakeCurrent(IntPtr xid)'";

        listener.WriteLine(failure);
        listener.WriteLine(failure);
        listener.WriteLine(failure);

        Assert.Single(log.Entries, line => line.Contains("glXMakeContextCurrent failed", StringComparison.Ordinal));

        now = now.AddMinutes(1);
        listener.WriteLine(failure);

        Assert.Contains(
            log.Entries,
            line => line.Contains("Suppressed 2 repeated Avalonia render-loop failures", StringComparison.Ordinal)
        );
        Assert.Equal(
            2,
            log.Entries.Count(line => line.Contains("glXMakeContextCurrent failed", StringComparison.Ordinal))
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
