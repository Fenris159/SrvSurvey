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

    private sealed class CallbackDisposable : IDisposable
    {
        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
