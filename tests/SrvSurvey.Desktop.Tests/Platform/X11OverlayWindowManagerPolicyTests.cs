using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class X11OverlayWindowManagerPolicyTests
{
    [Fact]
    public void StackingPolicyLoggingReportsEachPolicyOnlyOnce()
    {
        var limiter = new X11StackingPolicyLogLimiter();

        Assert.True(limiter.ShouldLog(X11OverlayStackingMode.StandardTopmost));
        Assert.False(limiter.ShouldLog(X11OverlayStackingMode.StandardTopmost));
        Assert.True(limiter.ShouldLog(X11OverlayStackingMode.KdeOnScreenDisplay));
        Assert.False(limiter.ShouldLog(X11OverlayStackingMode.KdeOnScreenDisplay));
    }

    [Fact]
    public void AdvertisedKdeOnScreenDisplayAtomEnablesKdePolicy()
    {
        X11OverlayStackingMode mode = X11OverlayWindowManagerPolicy.Select(kdeOnScreenDisplayAtom: 42, [4, 17, 42, 93]);

        Assert.Equal(X11OverlayStackingMode.KdeOnScreenDisplay, mode);
    }

    [Theory]
    [InlineData(0, new uint[] { 42 })]
    [InlineData(42, new uint[] { 4, 17, 93 })]
    public void MissingKdeCapabilityKeepsStandardTopmostPolicy(uint kdeOnScreenDisplayAtom, uint[] supportedAtoms)
    {
        X11OverlayStackingMode mode = X11OverlayWindowManagerPolicy.Select(
            kdeOnScreenDisplayAtom,
            supportedAtoms.Select(atom => (nuint)atom).ToArray()
        );

        Assert.Equal(X11OverlayStackingMode.StandardTopmost, mode);
    }

    [Fact]
    public void KdePolicyWritesOsdTypeWithNormalFallback()
    {
        nuint[] windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.KdeOnScreenDisplay,
            kdeOnScreenDisplayAtom: 42,
            notificationWindowAtom: 23,
            normalWindowAtom: 17
        );

        Assert.Equal([(nuint)42, (nuint)17], windowTypes);
    }

    [Fact]
    public void StandardPolicyUsesNotificationTypeWithNormalFallback()
    {
        nuint[] windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.StandardTopmost,
            kdeOnScreenDisplayAtom: 42,
            notificationWindowAtom: 23,
            normalWindowAtom: 17
        );

        Assert.Equal([(nuint)23, (nuint)17], windowTypes);
    }

    [Theory]
    [InlineData(0, 17)]
    [InlineData(42, 0)]
    public void IncompleteKdeAtomPairDoesNotReplaceAvaloniaWindowType(
        uint kdeOnScreenDisplayAtom,
        uint normalWindowAtom
    )
    {
        nuint[] windowTypes = X11OverlayWindowManagerPolicy.CreateWindowTypes(
            X11OverlayStackingMode.KdeOnScreenDisplay,
            kdeOnScreenDisplayAtom,
            notificationWindowAtom: 23,
            normalWindowAtom
        );

        Assert.Empty(windowTypes);
    }

    [Fact]
    public void ManagedDragUsesScreenPixelDelta()
    {
        PixelPoint position = ManagedOverlayWindowDragSession.CalculatePosition(
            initialWindowPosition: new PixelPoint(100, 200),
            initialPointerPosition: new PixelPoint(125, 240),
            currentPointerPosition: new PixelPoint(165, 225)
        );

        Assert.Equal(new PixelPoint(140, 185), position);
    }

    [Fact]
    public void ManagedDragAllowsWindowToCrossTheTopScreenEdge()
    {
        PixelPoint position = ManagedOverlayWindowDragSession.CalculatePosition(
            initialWindowPosition: new PixelPoint(100, 5),
            initialPointerPosition: new PixelPoint(125, 40),
            currentPointerPosition: new PixelPoint(165, -20)
        );

        Assert.Equal(new PixelPoint(140, -55), position);
    }

    [Fact]
    public void ManagedDragAppliesOnlyTheLatestMoveInEachScheduledUpdate()
    {
        var appliedPositions = new List<PixelPoint>();
        var scheduledUpdates = new List<Action>();
        var pendingMove = new PendingWindowMove(
            appliedPositions.Add,
            update =>
            {
                scheduledUpdates.Add(update);
                return new CallbackDisposable();
            }
        );

        pendingMove.Update(new PixelPoint(110, 210));
        pendingMove.Update(new PixelPoint(120, 220));
        pendingMove.Update(new PixelPoint(130, 230));

        Action scheduledUpdate = Assert.Single(scheduledUpdates);
        Assert.Empty(appliedPositions);

        scheduledUpdate();

        Assert.Equal([new PixelPoint(130, 230)], appliedPositions);
    }

    [Fact]
    public void ManagedDragAppliesTheLatestPendingMoveWhenReleased()
    {
        var appliedPositions = new List<PixelPoint>();
        Action? scheduledUpdate = null;
        CallbackDisposable? scheduledUpdateCancellation = null;
        var pendingMove = new PendingWindowMove(
            appliedPositions.Add,
            update =>
            {
                scheduledUpdate = update;
                scheduledUpdateCancellation = new CallbackDisposable();
                return scheduledUpdateCancellation;
            }
        );

        pendingMove.Update(new PixelPoint(110, 210));
        pendingMove.Update(new PixelPoint(150, 250));
        pendingMove.Complete(applyPendingPosition: true);

        Assert.Equal([new PixelPoint(150, 250)], appliedPositions);
        Assert.NotNull(scheduledUpdate);
        Assert.NotNull(scheduledUpdateCancellation);
        Assert.True(scheduledUpdateCancellation.IsDisposed);
        scheduledUpdate();
        Assert.Equal([new PixelPoint(150, 250)], appliedPositions);
    }

    [Fact]
    public void ManagedDragDiscardsPendingMoveWhenWindowCloses()
    {
        var appliedPositions = new List<PixelPoint>();
        Action? scheduledUpdate = null;
        var pendingMove = new PendingWindowMove(
            appliedPositions.Add,
            update =>
            {
                scheduledUpdate = update;
                return new CallbackDisposable();
            }
        );

        pendingMove.Update(new PixelPoint(110, 210));
        pendingMove.Complete(applyPendingPosition: false);
        pendingMove.Update(new PixelPoint(150, 250));
        pendingMove.Complete(applyPendingPosition: true);

        Assert.NotNull(scheduledUpdate);
        scheduledUpdate();
        Assert.Empty(appliedPositions);
    }

    [AvaloniaFact]
    public void ManagedDragFlushesTheLatestPointerPositionOnRelease()
    {
        var window = new Window { Width = 200, Height = 120 };
        window.PointerPressed += (_, eventArgs) => ManagedOverlayWindowDragSession.Begin(window, eventArgs);
        try
        {
            window.Show();
            window.Position = new PixelPoint(100, 200);
            window.MouseMove(new Point(20, 25), RawInputModifiers.None);
            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);

            window.MouseMove(new Point(55, 70), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(55, 70), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(new PixelPoint(135, 245), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ManagedDragStopsIfMotionReportsLeftButtonUp()
    {
        var window = new Window { Width = 200, Height = 120 };
        window.PointerPressed += (_, eventArgs) => ManagedOverlayWindowDragSession.Begin(window, eventArgs);
        try
        {
            window.Show();
            window.Position = new PixelPoint(100, 200);
            window.MouseMove(new Point(20, 25), RawInputModifiers.None);
            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(55, 70), RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(400, 300), RawInputModifiers.None);
            window.MouseUp(new Point(400, 300), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(new PixelPoint(135, 245), window.Position);
            window.MouseMove(new Point(500, 400), RawInputModifiers.None);
            Assert.Equal(new PixelPoint(135, 245), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Flushes a delayed motion packet using the current desktop pointer instead of trailing it after release.</summary>
    [AvaloniaFact]
    public void ManagedDragDoesNotTrailTheNativePointerWhenMotionEventsAreDelayed()
    {
        var window = new Window { Width = 200, Height = 120 };
        var probe = new DragPointerProbe(new PixelPoint(4028, 1491));
        OverlayDragPolicy.SetOptionsFactory(window, () => new OverlayDragOptions { PointerProbe = probe });
        window.PointerPressed += (_, args) => ManagedOverlayWindowDragSession.Begin(window, args);
        try
        {
            window.Show();
            window.Position = new PixelPoint(3904, 1472);
            window.MouseDown(new Point(124, 19), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(140, 19), RawInputModifiers.LeftMouseButton);
            probe.Sample = new OverlayDragPointerSample(new PixelPoint(4228, 1491), false);
            window.MouseUp(new Point(140, 19), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(new PixelPoint(4104, 1472), window.Position);
            window.MouseMove(new Point(500, 19), RawInputModifiers.None);
            Assert.Equal(new PixelPoint(4104, 1472), window.Position);
            Assert.Equal(1, probe.Disposals);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Uses the current pointer during a scheduled move and ends a native release before queued motion can replay.</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScheduledDragUsesCurrentPointerAndHonorsItsButtonState(bool nativeButtonPressed)
    {
        var window = new Window { Width = 200, Height = 120 };
        var probe = new DragPointerProbe(new PixelPoint(4048, 1491));
        var applied = new TaskCompletionSource<PixelPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        OverlayDragPolicy.SetOptionsFactory(window, () => new OverlayDragOptions { PointerProbe = probe });
        window.PointerPressed += (_, args) =>
            ManagedOverlayWindowDragSession.Begin(window, args, position => applied.TrySetResult(position));
        try
        {
            window.Show();
            window.Position = new PixelPoint(3904, 1472);
            window.MouseDown(new Point(124, 19), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(140, 19), RawInputModifiers.LeftMouseButton);
            probe.Sample = new OverlayDragPointerSample(new PixelPoint(4228, 1491), nativeButtonPressed);

            Assert.Equal(new PixelPoint(4104, 1472), await applied.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            if (!nativeButtonPressed)
            {
                Assert.Equal(1, probe.Disposals);
                window.MouseMove(new Point(500, 19), RawInputModifiers.LeftMouseButton);
            }
            window.MouseUp(new Point(124, 19), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(new PixelPoint(4104, 1472), window.Position);
            Assert.Equal(1, probe.Disposals);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Keeps fresh native coordinates within the captured monitor and falls back safely when sampling is unavailable.</summary>
    [AvaloniaTheory]
    [InlineData(10000, 10000, 4920, 2400)]
    [InlineData(-10000, -10000, 0, 1080)]
    [InlineData(4028, 1491, 3904, 1472)]
    [InlineData(null, null, 3920, 1472)]
    public void NativeDragPreservesMonitorLockAndEventFallback(int? x, int? y, int expectedX, int expectedY)
    {
        var window = new Window { Width = 200, Height = 120 };
        var probe = new DragPointerProbe(new PixelPoint(4028, 1491));
        var boundary = new PixelRect(0, 1080, 5120, 1440);
        OverlayDragPolicy.SetOptionsFactory(
            window,
            () =>
                new OverlayDragOptions
                {
                    PointerProbe = probe,
                    MonitorBounds = boundary,
                    ConstrainPosition = position =>
                        OverlayDragPolicy.ClampPanel(position, new PixelSize(200, 120), default, boundary),
                }
        );
        window.PointerPressed += (_, args) => ManagedOverlayWindowDragSession.Begin(window, args);
        try
        {
            window.Show();
            window.Position = new PixelPoint(3904, 1472);
            window.MouseDown(new Point(124, 19), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(140, 19), RawInputModifiers.LeftMouseButton);
            probe.Sample =
                x is { } nativeX && y is { } nativeY
                    ? new OverlayDragPointerSample(new PixelPoint(nativeX, nativeY), false)
                    : null;
            window.MouseUp(new Point(140, 19), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(new PixelPoint(expectedX, expectedY), window.Position);
            Assert.Equal(1, probe.Disposals);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Does not drop a return move when the desktop still reports the position from before the previous request.</summary>
    [AvaloniaFact]
    public async Task DragBackToReportedPositionStillUpdatesTheRequestedPlacement()
    {
        var window = new Window { Width = 200, Height = 120 };
        var origin = new PixelPoint(3904, 1472);
        var probe = new DragPointerProbe(new PixelPoint(4028, 1491));
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<PixelPoint>();
        OverlayDragPolicy.SetOptionsFactory(window, () => new OverlayDragOptions { PointerProbe = probe });
        window.PointerPressed += (_, args) =>
            ManagedOverlayWindowDragSession.Begin(
                window,
                args,
                position =>
                {
                    requests.Add(position);
                    // Model an X11 configure acknowledgement still reporting the original position.
                    window.Position = origin;
                    applied.TrySetResult();
                }
            );
        try
        {
            window.Show();
            window.Position = origin;
            window.MouseDown(new Point(124, 19), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            probe.Sample = new OverlayDragPointerSample(new PixelPoint(4228, 1491), true);
            window.MouseMove(new Point(140, 19), RawInputModifiers.LeftMouseButton);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

            probe.Sample = new OverlayDragPointerSample(new PixelPoint(4028, 1491), false);
            window.MouseMove(new Point(140, 19), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(140, 19), MouseButton.Left, RawInputModifiers.None);

            Assert.Equal([new PixelPoint(4104, 1472), origin], requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ManagedDragRetainsMonitorBoundaryForTheGestureAndCancelFlushesItsLastValidMove()
    {
        var window = new Window { Width = 200, Height = 120 };
        var boundary = new PixelRect(0, 1080, 5120, 1440);
        int factoryCalls = 0;
        var messages = new List<string>();
        OverlayDragPolicy.SetOptionsFactory(
            window,
            () =>
            {
                factoryCalls++;
                PixelRect captured = boundary;
                return new OverlayDragOptions
                {
                    MonitorBounds = captured,
                    ConstrainPosition = position =>
                        OverlayDragPolicy.ClampPanel(position, new PixelSize(200, 120), default, captured),
                    Log = messages.Add,
                };
            }
        );
        window.PointerPressed += (_, args) => ManagedOverlayWindowDragSession.Begin(window, args);
        try
        {
            window.Show();
            window.Position = new PixelPoint(4800, 2200);
            window.MouseDown(new Point(20, 25), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            boundary = new PixelRect(-1920, -1080, 1920, 1080);
            window.MouseMove(new Point(1000, 1000), RawInputModifiers.LeftMouseButton);
            ManagedOverlayWindowDragSession.Cancel(window);
            Assert.Equal(new PixelPoint(4920, 2400), window.Position);
            Assert.Equal(1, factoryCalls);
            Assert.Contains(
                messages,
                message => message.Contains("reason=interaction ended", StringComparison.Ordinal)
            );
            window.MouseUp(new Point(1000, 1000), MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(2000, 2000), RawInputModifiers.None);
            Assert.Equal(new PixelPoint(4920, 2400), window.Position);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class CallbackDisposable : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    /// <summary>Supplies current screen coordinates independently of queued local motion packets.</summary>
    private sealed class DragPointerProbe(PixelPoint initialPosition) : IOverlayDragPointerProbe
    {
        internal OverlayDragPointerSample? Sample { get; set; } = new(initialPosition, true);
        internal int Disposals { get; private set; }

        /// <summary>Returns the most recent desktop sample without consuming queued local events.</summary>
        public OverlayDragPointerSample? Read() => Sample;

        /// <summary>Records when the gesture releases ownership of the pointer source.</summary>
        public void Dispose() => Disposals++;
    }
}
