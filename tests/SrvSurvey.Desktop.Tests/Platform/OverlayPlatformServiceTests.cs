using System.Runtime.InteropServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OverlayPlatformServiceTests
{
    [Fact]
    public void CursorSessionRestoresPreviousForegroundFromOverlayWindow()
    {
        var cursor = new TrackingDisposable();
        var restored = new List<nint>();
        var session = new ForegroundCursorVisibilitySession(
            cursor,
            interactionWindow: (nint)20,
            previousForeground: (nint)10,
            getForegroundWindow: () => (nint)30,
            setForegroundWindow: window =>
            {
                restored.Add(window);
                return true;
            },
            isInteractionWindow: window => window == (nint)30);

        session.Dispose();
        session.Dispose();

        Assert.True(cursor.IsDisposed);
        Assert.Equal([(nint)10], restored);
    }

    [Fact]
    public void CursorSessionDoesNotStealFocusFromAnotherApplication()
    {
        var cursor = new TrackingDisposable();
        var restored = new List<nint>();
        var session = new ForegroundCursorVisibilitySession(
            cursor,
            interactionWindow: (nint)20,
            previousForeground: (nint)10,
            getForegroundWindow: () => (nint)99,
            setForegroundWindow: window =>
            {
                restored.Add(window);
                return true;
            },
            isInteractionWindow: _ => false);

        session.Dispose();

        Assert.True(cursor.IsDisposed);
        Assert.Empty(restored);
    }

    [Fact]
    public void X11CursorSessionRestoresFocusAndRemovesDefinedCursors()
    {
        var undefined = new List<nuint>();
        var freed = new List<nuint>();
        var restored = new List<nuint>();
        var session = new X11CursorVisibilitySession(
            interactionWindows: [(nuint)20, (nuint)30],
            cursor: 40,
            previousActiveWindow: 10,
            new X11CursorSessionOperations(
                getActiveWindow: () => 99,
                getFocusWindow: () => 30,
                activateWindow: window =>
                {
                    restored.Add(window);
                    return true;
                },
                undefineCursor: window =>
                {
                    undefined.Add(window);
                    return 0;
                },
                freeCursor: cursor =>
                {
                    freed.Add(cursor);
                    return 0;
                }));

        session.Dispose();
        session.Dispose();

        Assert.Equal(
            [(nuint)20, (nuint)30],
            undefined.Order().ToArray());
        Assert.Equal([(nuint)40], freed);
        Assert.Equal([(nuint)10], restored);
    }

    [Fact]
    public void X11CursorSessionDoesNotRestoreOverAnotherApplication()
    {
        var restored = new List<nuint>();
        var session = new X11CursorVisibilitySession(
            interactionWindows: [(nuint)20],
            cursor: 0,
            previousActiveWindow: 10,
            new X11CursorSessionOperations(
                getActiveWindow: () => 99,
                getFocusWindow: () => 98,
                activateWindow: window =>
                {
                    restored.Add(window);
                    return true;
                },
                undefineCursor: _ => throw new InvalidOperationException(),
                freeCursor: _ => throw new InvalidOperationException()));

        session.Dispose();

        Assert.Empty(restored);
    }

    [Fact]
    public void X11CursorSessionDoesNotRestoreAnInteractionWindow()
    {
        var restored = new List<nuint>();
        var session = new X11CursorVisibilitySession(
            interactionWindows: [(nuint)20],
            cursor: 0,
            previousActiveWindow: 20,
            new X11CursorSessionOperations(
                getActiveWindow: () => 20,
                getFocusWindow: () => 20,
                activateWindow: window =>
                {
                    restored.Add(window);
                    return true;
                },
                undefineCursor: _ => throw new InvalidOperationException(),
                freeCursor: _ => throw new InvalidOperationException()));

        session.Dispose();

        Assert.Empty(restored);
    }

    [Fact]
    public void X11ErrorHandlerSupportsACompatibleDelegateType()
    {
        var invoked = false;
        var errorEvent = new X11Native.XErrorEvent
        {
            ErrorCode = 3,
        };
        CompatibleX11ErrorHandler handler = (
            nint display,
            ref X11Native.XErrorEvent receivedEvent) =>
        {
            invoked = display == (nint)42 && receivedEvent.ErrorCode == 3;
            return 17;
        };
        var handlerPointer = Marshal.GetFunctionPointerForDelegate(handler);

        Assert.Equal(
            17,
            X11Native.InvokeErrorHandler(
                handlerPointer,
                (nint)42,
                ref errorEvent));
        Assert.True(invoked);
        Assert.Equal(
            0,
            X11Native.InvokeErrorHandler(
                nint.Zero,
                (nint)42,
                ref errorEvent));
        GC.KeepAlive(handler);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CompatibleX11ErrorHandler(
        nint display,
        ref X11Native.XErrorEvent errorEvent);

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
