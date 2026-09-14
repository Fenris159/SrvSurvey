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
            isInteractionWindow: window => window == (nint)30
        );

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
            isInteractionWindow: _ => false
        );

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
                }
            )
        );

        session.Dispose();
        session.Dispose();

        Assert.Equal([(nuint)20, (nuint)30], undefined.Order().ToArray());
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
                freeCursor: _ => throw new InvalidOperationException()
            )
        );

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
                freeCursor: _ => throw new InvalidOperationException()
            )
        );

        session.Dispose();

        Assert.Empty(restored);
    }

    [Fact]
    public void X11ErrorHandlerSupportsACompatibleDelegateType()
    {
        bool invoked = false;
        var errorEvent = new X11Native.XErrorEvent { ErrorCode = 3 };
        CompatibleX11ErrorHandler handler = (nint display, ref X11Native.XErrorEvent receivedEvent) =>
        {
            invoked = display == (nint)42 && receivedEvent.ErrorCode == 3;
            return 17;
        };
        nint handlerPointer = Marshal.GetFunctionPointerForDelegate(handler);

        Assert.Equal(17, X11Native.InvokeErrorHandler(handlerPointer, (nint)42, ref errorEvent));
        Assert.True(invoked);
        Assert.Equal(0, X11Native.InvokeErrorHandler(nint.Zero, (nint)42, ref errorEvent));
        GC.KeepAlive(handler);
    }

    [Fact]
    public void X11ErrorHandlerOnlySuppressesExpectedRacesOnOwnedDisplays()
    {
        nint ownedDisplay = (nint)42;
        X11OverlayPlatformService.RegisterErrorHandledDisplay(ownedDisplay);
        try
        {
            Assert.True(X11OverlayPlatformService.ShouldSuppressXError(ownedDisplay, X11Native.BadWindow));
            Assert.True(
                X11OverlayPlatformService.ShouldSuppressXError(
                    ownedDisplay,
                    X11Native.BadMatch,
                    X11Native.SetInputFocusRequest
                )
            );
            Assert.True(
                X11OverlayPlatformService.ShouldSuppressXError(
                    ownedDisplay,
                    X11Native.BadDrawable,
                    X11Native.GetImageRequest
                )
            );
            Assert.False(X11OverlayPlatformService.ShouldSuppressXError(ownedDisplay, errorCode: X11Native.BadValue));
            Assert.False(
                X11OverlayPlatformService.ShouldSuppressXError(ownedDisplay, X11Native.BadMatch, requestCode: 12)
            );
            Assert.False(
                X11OverlayPlatformService.ShouldSuppressXError(
                    errorDisplay: 43,
                    errorCode: X11Native.BadMatch,
                    requestCode: X11Native.SetInputFocusRequest
                )
            );
        }
        finally
        {
            X11OverlayPlatformService.UnregisterErrorHandledDisplay(ownedDisplay);
        }

        Assert.False(X11OverlayPlatformService.ShouldSuppressXError(ownedDisplay, X11Native.BadWindow));
    }

    [Fact]
    public void X11ExpectedErrorLoggingReportsFirstFailureAndPeriodicAggregate()
    {
        var limiter = new X11ExpectedErrorLogLimiter(TimeSpan.FromSeconds(30));
        var signature = new X11ExpectedErrorSignature(
            (nint)42,
            X11Native.BadMatch,
            X11Native.GetImageRequest,
            MinorCode: 0,
            ResourceId: 1070
        );
        var now = new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            new X11ExpectedErrorLogDecision(ShouldLog: true, SuppressedCount: 0),
            limiter.Record(signature, now)
        );
        Assert.Equal(
            new X11ExpectedErrorLogDecision(ShouldLog: false, SuppressedCount: 0),
            limiter.Record(signature, now.AddSeconds(1))
        );
        Assert.Equal(
            new X11ExpectedErrorLogDecision(ShouldLog: false, SuppressedCount: 0),
            limiter.Record(signature, now.AddSeconds(2))
        );
        Assert.Equal(
            new X11ExpectedErrorLogDecision(ShouldLog: true, SuppressedCount: 2),
            limiter.Record(signature, now.AddSeconds(30))
        );

        limiter.RemoveDisplay((nint)42);
        Assert.Equal(
            new X11ExpectedErrorLogDecision(ShouldLog: true, SuppressedCount: 0),
            limiter.Record(signature, now.AddSeconds(31))
        );
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CompatibleX11ErrorHandler(nint display, ref X11Native.XErrorEvent errorEvent);

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
