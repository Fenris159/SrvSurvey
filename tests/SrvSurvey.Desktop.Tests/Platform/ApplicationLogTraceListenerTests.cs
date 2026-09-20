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

    [Fact]
    public void ListenerOmitsMissingLegacyX11SessionManagerNoise()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.WriteLine(
            "[X11Platform] SMLib/ICELib reported a new error: SESSION_MANAGER environment variable not defined\0\0"
        );

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ListenerOmitsKnownIbusDestroyCompatibilityNoise()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);
        const string failure =
            "[IME] Error while destroying the context: 'Tmds.DBus.Protocol.DBusErrorReplyException: "
            + "org.freedesktop.DBus.Error.UnknownMethod: Method Destroy is not implemented on interface "
            + "org.freedesktop.IBus.Service\n"
            + "   at Tmds.DBus.Protocol.InnerConnection.CallMethodAsync(MessageBuffer message)\n"
            + "   at Avalonia.FreeDesktop.DBusIme.DBusTextInputMethodBase.Dispose()'";

        listener.WriteLine(failure);

        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData(
        "org.freedesktop.DBus.Error.UnknownMethod: Object does not exist at path /org/freedesktop/IBus/InputContext_234"
    )]
    [InlineData(
        "org.freedesktop.DBus.Error.UnknownMethod: No such interface org.freedesktop.IBus.InputContext on object at path /org/freedesktop/IBus/InputContext_347"
    )]
    public void ListenerOmitsKnownMultilineIbusDisposedContextNoise(string error)
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.WriteLine("[IME] Error:");
        listener.WriteLine("Tmds.DBus.Protocol.DBusErrorReplyException: " + error);
        listener.WriteLine("   at Avalonia.FreeDesktop.DBusIme.IBus.IBusX11TextInputMethod.SetCapabilitiesCore()");
        listener.WriteLine("   at Avalonia.FreeDesktop.DBusCallQueue.Process() (IBusX11TextInputMethod #16674329)");

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void ListenerPreservesUnexpectedMultilineImeFailures()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.WriteLine("[IME] Error:");
        listener.WriteLine("System.InvalidOperationException: unexpected failure");
        listener.WriteLine("   at Avalonia.FreeDesktop.DBusIme.IBus.IBusX11TextInputMethod.Start()");
        listener.WriteLine("   at Avalonia.FreeDesktop.DBusCallQueue.Process() (IBusX11TextInputMethod #1)");

        Assert.Equal(4, log.Entries.Count);
        Assert.Contains(log.Entries, line => line.EndsWith(": [IME] Error:", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
