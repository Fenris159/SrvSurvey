using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayDragPolicyTests
{
    private static readonly MainWindowMonitor Ultrawide = new(
        "DP-1",
        "Ultrawide",
        new PixelRect(0, 1080, 5120, 1440),
        default,
        1,
        true
    );
    private static readonly MainWindowMonitor Above = new(
        "HDMI-A-1",
        "Above",
        new PixelRect(1650, 0, 1920, 1080),
        default,
        1,
        false
    );
    private static readonly MainWindowMonitor Right = new(
        "DP-2",
        "Right",
        new PixelRect(5120, 1212, 1920, 1080),
        default,
        2,
        false
    );

    [Fact]
    public void ManualSelectionOverridesGameMonitorForDragBoundary()
    {
        MainWindowMonitor[] monitors = [Above, Ultrawide, Right];
        Assert.Same(Right, OverlayDragPolicy.ResolveMonitor(monitors, "DP-2", Ultrawide.Bounds));
        Assert.Same(Above, OverlayDragPolicy.ResolveMonitor(monitors, "HDMI-A-1", Ultrawide.Bounds));
        Assert.Same(Ultrawide, OverlayDragPolicy.ResolveMonitor(monitors, "disconnected", Right.Bounds));
        Assert.Same(Right, OverlayDragPolicy.ResolveMonitor(monitors, null, new PixelRect(5200, 1300, 1200, 800)));
        Assert.Same(Ultrawide, OverlayDragPolicy.ResolveMonitor(monitors, null, default));
        Assert.Null(OverlayDragPolicy.ResolveMonitor([], null, default));
    }

    [Theory]
    [InlineData(-5000, -5000, -1932, 112)]
    [InlineData(5000, 5000, -312, 992)]
    [InlineData(-1800, 300, -1800, 300)]
    public void LockClampsThePanelBodyUsingScreenPixelsAndNegativeOrigins(int x, int y, int expectedX, int expectedY)
    {
        PixelPoint actual = OverlayDragPolicy.ClampPanel(
            new PixelPoint(x, y),
            new PixelSize(300, 200),
            new PixelPoint(12, 8),
            new PixelRect(-1920, 120, 1920, 1080)
        );
        Assert.Equal(new PixelPoint(expectedX, expectedY), actual);
    }

    [Fact]
    public void OversizedPanelKeepsItsTopLeftVisibleWithoutInvalidClampLimits()
    {
        Assert.Equal(
            new PixelPoint(-1932, 112),
            OverlayDragPolicy.ClampPanel(
                new PixelPoint(9999, 9999),
                new PixelSize(2500, 1400),
                new PixelPoint(12, 8),
                new PixelRect(-1920, 120, 1920, 1080)
            )
        );
    }

    [AvaloniaFact]
    public void PolicyReadsManualChoiceAndLockPreferenceForEachNewGesture()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-drag-policy-{Guid.NewGuid():N}");
        var window = new Window();
        try
        {
            var behavior = new OverlayBehaviorViewModel(
                new OverlayBehaviorSettingsStore(Path.Combine(directory, "ui.json"))
            );
            MainWindowMonitor screen = MainWindowPlacement.DescribeScreens(window.Screens.All)[0];
            var option = new ApplicationMonitorOption(screen.Id, screen.DisplayName);
            behavior.SetAvailableMonitors([option]);
            behavior.SelectedMonitor = option;
            OverlayDragPolicy.SetOptionsFactory(
                window,
                () =>
                    OverlayDragPolicy.CreateOptions(
                        window,
                        behavior,
                        new PixelRect(-5000, -5000, 1200, 800),
                        new PixelSize(300, 200)
                    )
            );
            Assert.Null(OverlayDragPolicy.GetOptions(window).ConstrainPosition);
            behavior.LockToMonitor = true;
            OverlayDragOptions locked = OverlayDragPolicy.GetOptions(window);
            Assert.Equal(screen.Bounds, locked.MonitorBounds);
            Assert.NotNull(locked.ConstrainPosition);
            Assert.Equal(
                new PixelPoint(screen.Bounds.Right - 300, screen.Bounds.Bottom - 200),
                locked.ConstrainPosition(new PixelPoint(9999, 9999))
            );
            behavior.LockToMonitor = false;
            Assert.Null(OverlayDragPolicy.GetOptions(window).ConstrainPosition);
            OverlayDragPolicy.SetOptionsFactory(window, null);
            Assert.Null(OverlayDragPolicy.GetOptions(window).MonitorBounds);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void CombinedLockKeepsPanelsInsideTheHostSurfaceWhenManualDisplayIsOutsideIt()
    {
        var hostBounds = new PixelRect(100, 1200, 1200, 800);
        var size = new PixelSize(300, 200);
        var original = new OverlayDragOptions { MonitorBounds = Right.Bounds };
        OverlayDragOptions fallback = OverlayDragPolicy.RestrictToCombinedHost(original, hostBounds, size);
        Assert.Equal(hostBounds, fallback.MonitorBounds);
        Assert.NotNull(fallback.ConstrainPosition);
        Assert.Equal(new PixelPoint(1000, 1800), fallback.ConstrainPosition(new PixelPoint(9999, 9999)));
        OverlayDragOptions intersected = OverlayDragPolicy.RestrictToCombinedHost(
            original with
            {
                MonitorBounds = Ultrawide.Bounds,
            },
            hostBounds,
            size
        );
        Assert.Equal(hostBounds, intersected.MonitorBounds);
        var unlocked = new OverlayDragOptions();
        Assert.Same(unlocked, OverlayDragPolicy.RestrictToCombinedHost(unlocked, hostBounds, size));
    }

    [AvaloniaFact]
    public void CoordinateTraceComparesNativeAndReportedPixelsAndDisposesOnce()
    {
        var window = new Window();
        var messages = new List<string>();
        var probe = new FakePointerProbe();
        try
        {
            using var diagnostics = new OverlayDragDiagnostics(
                window,
                new PixelPoint(100, 200),
                new OverlayDragOptions
                {
                    Log = messages.Add,
                    PointerProbe = probe,
                    MonitorBounds = Ultrawide.Bounds,
                }
            );
            probe.Sample = new OverlayDragPointerSample(new PixelPoint(5030, 1200), false);
            diagnostics.Observe(new PixelPoint(5000, 1190));
            probe.Sample = new OverlayDragPointerSample(new PixelPoint(5040, 1220), true);
            diagnostics.Observe(new PixelPoint(5000, 1190));
            diagnostics.Complete("button-up", new PixelPoint(5000, 1190));
            diagnostics.Observe(default);
            diagnostics.Complete("duplicate", default);
            Assert.Equal(1, probe.Disposals);
            Assert.Equal(3, probe.Reads);
            Assert.Equal(3, messages.Count);
            Assert.Contains("nativeLeftButton=False", messages[1]);
            Assert.Contains("nativeSamples=2; maxPointerDifferencePx=40,30", messages[2]);
            Assert.Contains("reason=button-up", messages[2]);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class FakePointerProbe : IOverlayDragPointerProbe
    {
        internal OverlayDragPointerSample? Sample { get; set; }
        internal int Reads { get; private set; }
        internal int Disposals { get; private set; }

        public OverlayDragPointerSample? Read()
        {
            Reads++;
            return Sample;
        }

        public void Dispose() => Disposals++;
    }
}
