using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class StreamOverlayCoordinatorTests
{
    /// <summary>The real stream definition must prepare each capture before mapping, including through borrowed platforms.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureNativePolicyRunsBeforeShowWithoutRegisteringAsAGameplayPanel(bool useSharedPlatform)
    {
        using var context = new TestContext(isForeground: true, useSharedPlatform: useSharedPlatform);
        (Window capture, bool wasVisible) = Assert.Single(context.Harness.PreShowWindows);
        Assert.IsType<StreamOverlayWindow>(capture);
        Assert.False(wasVisible);
        Assert.True(capture.IsVisible);
        Assert.Empty(context.Registry.Snapshot());
        context.ViewModel.Enabled = false;
        context.ViewModel.Enabled = true;
        Assert.Equal(2, context.Harness.PreShowWindows.Count);
        Assert.NotSame(capture, context.Harness.PreShowWindows[1].Window);
        Assert.False(context.Harness.PreShowWindows[1].WasVisible);
    }

    /// <summary>The joined stream canvas uses background capture policy rather than gameplay overlay stacking.</summary>
    [AvaloniaFact]
    public void CaptureWindowDoesNotJoinTheAlwaysOnTopGameplayPanels()
    {
        using var context = new TestContext(isForeground: true);
        Window capture = Assert.Single(context.Harness.PreparedWindows);
        Assert.False(capture.Topmost);
        Assert.False(capture.ShowActivated);
    }

    /// <summary>Capture raster dimensions preserve physical panel size across differing window and monitor scales.</summary>
    [AvaloniaTheory]
    [InlineData(1, 2)]
    [InlineData(2, 1.5)]
    [InlineData(1.5, 1)]
    public void CapturedPanelKeepsItsPhysicalSizeWhenWindowAndMonitorDpiDiffer(
        double panelScaling,
        double captureScaling
    )
    {
        using var context = new TestContext(isForeground: true);
        Window capture = Assert.Single(context.Harness.PreparedWindows);
        capture.SetRenderScaling(captureScaling);
        var panel = new Window
        {
            Width = 80,
            Height = 40,
            Background = Brushes.Red,
            Content = new Border
            {
                Width = 20,
                Height = 10,
                Background = Brushes.Blue,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(8, 6, 0, 0),
            },
        };
        try
        {
            panel.SetRenderScaling(panelScaling);
            context.Registry.Register(panel, "PlotBuildCommodities");
            panel.Show();
            using WriteableBitmap? sourceFrame = panel.CaptureRenderedFrame();
            context.Harness.Tick();
            Assert.Equal(context.Harness.GameWindow.ClientBounds.Width, capture.Width * capture.RenderScaling);
            Assert.Equal(context.Harness.GameWindow.ClientBounds.Height, capture.Height * capture.RenderScaling);
            Canvas canvas = Assert.IsType<Canvas>(capture.FindControl<Canvas>("OverlayCanvas"));
            Image image = Assert.IsType<Image>(Assert.Single(canvas.Children));
            Assert.Equal(panel.Bounds.Width * panel.RenderScaling, image.Width * capture.RenderScaling);
            Assert.Equal(panel.Bounds.Height * panel.RenderScaling, image.Height * capture.RenderScaling);
            Assert.NotNull(sourceFrame);
            Assert.Equal(new Size(sourceFrame.PixelSize.Width, sourceFrame.PixelSize.Height), image.Source!.Size);
            using WriteableBitmap? joinedFrame = capture.CaptureRenderedFrame();
            VrOverlayFrame joined = VrOverlayFrameRenderer.Render(capture);
            Assert.Equal(context.Harness.GameWindow.ClientBounds.Width, joined.Width);
            Assert.Equal(context.Harness.GameWindow.ClientBounds.Height, joined.Height);
            int outside = ((int)(panel.Bounds.Height * panel.RenderScaling + 2) * joined.Width + 2) * 4;
            Assert.Equal(255, joined.RgbaBytes[((2 * joined.Width) + 2) * 4]);
            Assert.Equal(0, joined.RgbaBytes[outside + 3]);
            int marker = (((int)(8 * panelScaling) * joined.Width) + (int)(10 * panelScaling)) * 4;
            Assert.Equal(255, joined.RgbaBytes[marker + 2]);
            Assert.Equal(0, joined.RgbaBytes[marker]);
        }
        finally
        {
            panel.Close();
        }
    }

    /// <summary>Transparent pixels and zero window opacity both exclude a panel from the live capture count.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullyTransparentPanelsAreNotCountedAsLiveOverlays(bool zeroOpacity)
    {
        using var context = new TestContext(isForeground: true);
        var visible = new Window
        {
            Width = 80,
            Height = 40,
            Background = Brushes.Red,
        };
        var transparent = new Window
        {
            Width = 80,
            Height = 40,
            Background = zeroOpacity ? Brushes.Blue : Brushes.Transparent,
            TransparencyBackgroundFallback = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowDecorations = WindowDecorations.None,
            Opacity = zeroOpacity ? 0 : 1,
        };
        try
        {
            context.Registry.Register(visible, "PlotBuildCommodities");
            context.Registry.Register(transparent, "PlotBuildCommodities");
            visible.Show();
            transparent.Show();
            using WriteableBitmap? visibleFrame = visible.CaptureRenderedFrame();
            using WriteableBitmap? transparentFrame = transparent.CaptureRenderedFrame();
            context.Harness.Tick();

            Assert.Equal("Compositing 1 live overlays into SrvSurveyWindowOne.", context.ViewModel.StatusMessage);
            Assert.Equal("Colonization commodities", Assert.Single(context.ViewModel.ActiveOverlayNames));
            Assert.True(context.ViewModel.HasActiveOverlays);
            context.ViewModel.Enabled = false;
            Assert.False(context.ViewModel.HasActiveOverlays);
            Assert.Empty(context.ViewModel.ActiveOverlayNames);
        }
        finally
        {
            visible.Close();
            transparent.Close();
        }
    }

    /// <summary>The status names identify every visible source, including the small journal indicator.</summary>
    [AvaloniaFact]
    public void ListsTheJournalIndicatorAlongsideTheGameplayPanelItActuallyCaptures()
    {
        using var context = new TestContext(isForeground: true);
        var panel = new Window
        {
            Width = 80,
            Height = 40,
            Background = Brushes.Red,
        };
        var journalIndicator = new Window
        {
            Width = 32,
            Height = 32,
            Background = Brushes.Blue,
        };
        try
        {
            context.Registry.Register(panel, "PlotBuildCommodities");
            context.Registry.Register(journalIndicator, "PlotPulse");
            panel.Show();
            journalIndicator.Show();
            using WriteableBitmap? panelFrame = panel.CaptureRenderedFrame();
            using WriteableBitmap? indicatorFrame = journalIndicator.CaptureRenderedFrame();
            context.Harness.Tick();

            Assert.Equal("Compositing 2 live overlays into SrvSurveyWindowOne.", context.ViewModel.StatusMessage);
            Assert.Collection(
                context.ViewModel.ActiveOverlayNames,
                name => Assert.Equal("Colonization commodities", name),
                name => Assert.Equal("Journal activity and SCO status", name)
            );
            journalIndicator.Close();
            context.Harness.Tick();
            Assert.Equal("Colonization commodities", Assert.Single(context.ViewModel.ActiveOverlayNames));
        }
        finally
        {
            panel.Close();
            journalIndicator.Close();
        }
    }

    /// <summary>The capture retains source alpha and follows live panel changes without applying opacity twice.</summary>
    [AvaloniaFact]
    public void NativePanelPixelsKeepTheirOpacityAndUpdateInsideTheJoinedCapture()
    {
        using var context = new TestContext(isForeground: true);
        var panel = new Window
        {
            Width = 80,
            Height = 40,
            Background = Brushes.Red,
            TransparencyBackgroundFallback = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            WindowDecorations = WindowDecorations.None,
            Opacity = 0.5,
        };
        try
        {
            context.Registry.Register(panel, "PlotBuildCommodities");
            panel.Show();
            using WriteableBitmap? sourceFrame = panel.CaptureRenderedFrame();
            context.Harness.Tick();
            StreamOverlayWindow capture = Assert.IsType<StreamOverlayWindow>(
                Assert.Single(context.Harness.PreparedWindows)
            );
            using WriteableBitmap? firstCapture = capture.CaptureRenderedFrame();
            VrOverlayFrame first = VrOverlayFrameRenderer.Render(capture);
            int offset = ((10 * first.Width) + 10) * 4;
            Assert.Equal(255, first.RgbaBytes[offset]);
            Assert.InRange(first.RgbaBytes[offset + 3], (byte)127, (byte)128);

            panel.Background = Brushes.Blue;
            panel.Opacity = 0.25;
            using WriteableBitmap? updatedSource = panel.CaptureRenderedFrame();
            context.Harness.Tick();
            using WriteableBitmap? updatedCapture = capture.CaptureRenderedFrame();
            VrOverlayFrame second = VrOverlayFrameRenderer.Render(capture);
            Assert.Equal(255, second.RgbaBytes[offset + 2]);
            Assert.InRange(second.RgbaBytes[offset + 3], (byte)63, (byte)64);
            panel.Close();
            context.Harness.Tick();
            Assert.Equal("Compositing 0 live overlays into SrvSurveyWindowOne.", context.ViewModel.StatusMessage);
            Assert.Empty(capture.FindControl<Canvas>("OverlayCanvas")!.Children);
            Assert.False(context.ViewModel.HasActiveOverlays);
        }
        finally
        {
            panel.Close();
        }
    }

    [AvaloniaFact]
    public void TransientUnavailableEliteRetainsTheSameCaptureWindow()
    {
        using var context = new TestContext(isForeground: true);
        Window original = Assert.Single(context.Harness.PreparedWindows);
        GameWindowSnapshot available = context.Harness.GameWindow;
        context.Harness.GameWindow = GameWindowSnapshot.Unavailable;

        context.Synchronize();

        Assert.True(original.IsVisible);
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        context.Harness.GameWindow = available;
        context.Synchronize();
        Assert.Same(original, Assert.Single(context.Harness.PreparedWindows));
        context.Clock.Advance(TimeSpan.FromSeconds(3));
        context.Harness.GameWindow = GameWindowSnapshot.Unavailable;
        context.Synchronize();
        Assert.True(original.IsVisible);
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        context.Synchronize();
        Assert.False(original.IsVisible);
    }

    [AvaloniaFact]
    public void StartingWithMinimizedEliteWaitsForAUsableCanvas()
    {
        using var context = new TestContext(isForeground: false, isVisible: false);
        Assert.Empty(context.Harness.PreparedWindows);
        context.Harness.GameWindow = context.Harness.GameWindow with { IsVisible = true };

        context.Synchronize();

        Assert.True(Assert.Single(context.Harness.PreparedWindows).IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimizedEliteRetainsTheCaptureCanvasUntilUsableBoundsReturn(bool emptyBounds)
    {
        using var context = new TestContext(isForeground: true);
        Window original = Assert.Single(context.Harness.PreparedWindows);
        var size = new Size(original.Width, original.Height);
        PixelPoint position = original.Position;
        context.Harness.GameWindow = context.Harness.GameWindow with
        {
            IsVisible = false,
            IsForeground = false,
            ClientBounds = emptyBounds ? default : new PixelRect(-32000, -32000, 33920, 33080),
        };

        context.Synchronize();
        context.Clock.Advance(TimeSpan.FromSeconds(10));
        context.Synchronize();

        Assert.True(original.IsVisible);
        Assert.Equal(size, new Size(original.Width, original.Height));
        Assert.Equal(position, original.Position);
        context.Harness.GameWindow = context.Harness.GameWindow with
        {
            IsVisible = true,
            IsForeground = true,
            ClientBounds = new PixelRect(10, 20, 1280, 720),
        };
        context.Synchronize();
        Assert.Same(original, Assert.Single(context.Harness.PreparedWindows));
        Assert.Equal(new PixelPoint(10, 20), original.Position);
        Assert.Equal(1280, original.Width);
        Assert.Equal(720, original.Height);
    }

    [AvaloniaFact]
    public void EnablingWhileEliteIsInBackgroundOpensTheCaptureWindow()
    {
        using var context = new TestContext(isForeground: false);

        Assert.True(Assert.Single(context.Harness.PreparedWindows).IsVisible);
    }

    /// <summary>Losing Elite focus preserves the existing OBS capture window and its resources.</summary>
    [AvaloniaFact]
    public void FocusLossKeepsTheSameCaptureWindowAvailableToObs()
    {
        using var context = new TestContext(isForeground: true);
        Window original = Assert.Single(context.Harness.PreparedWindows);
        bool closed = false;
        original.Closed += (_, _) => closed = true;
        Assert.True(original.IsVisible);
        Assert.False(original.Topmost);

        context.Harness.GameWindow = context.Harness.GameWindow with { IsForeground = false };
        context.Synchronize();

        Assert.False(closed);
        Assert.True(original.IsVisible);
        Assert.True(context.ViewModel.Enabled);
        context.Synchronize();
        Assert.Single(context.Harness.PreparedWindows);

        context.Harness.GameWindow = context.Harness.GameWindow with { IsForeground = true };
        context.Synchronize();

        Assert.Same(original, Assert.Single(context.Harness.PreparedWindows));
        Assert.True(original.IsVisible);
        Assert.False(original.Topmost);
    }

    /// <summary>Temporary game visibility changes retain capture identity until the native game window actually disappears.</summary>
    [AvaloniaFact]
    public void CaptureRemainsDiscoverableWithHiddenEliteAndValidBoundsUntilTheGameWindowIsGone()
    {
        using var context = new TestContext(isForeground: false, keepVisible: true);
        Window window = Assert.Single(context.Harness.PreparedWindows);
        Assert.True(window.IsVisible);
        Assert.False(window.Topmost);

        context.KeepVisible = false;
        context.Synchronize();
        Assert.True(window.IsVisible);

        context.KeepVisible = true;
        context.Synchronize();
        Assert.Same(window, Assert.Single(context.Harness.PreparedWindows));

        context.Harness.GameWindow = context.Harness.GameWindow with { IsVisible = false };
        context.Synchronize();
        Assert.True(window.IsVisible);
        context.Harness.GameWindow = GameWindowSnapshot.Unavailable;
        context.Synchronize();
        Assert.True(window.IsVisible);
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        context.Synchronize();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void DisablingCaptureInTheBackgroundHidesItsWindow()
    {
        using var context = new TestContext(isForeground: false);
        Window window = Assert.Single(context.Harness.PreparedWindows);
        context.ViewModel.Enabled = false;
        context.Synchronize();
        Assert.False(window.IsVisible);
        Assert.Single(context.Harness.PreparedWindows);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan elapsed) => ticks += elapsed.Ticks;
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-stream-{Guid.NewGuid():N}");
        private readonly OverlayWindowRegistry registry = new();
        private readonly StreamOverlayCoordinator coordinator;

        public OverlayWindowRegistry Registry => registry;

        public ManualClock Clock { get; } = new();

        public TestContext(
            bool isForeground,
            bool keepVisible = false,
            bool isVisible = true,
            bool useSharedPlatform = false
        )
        {
            KeepVisible = keepVisible;
            Harness = new HostedOverlayTestHarness(
                registry,
                () => new OverlayGameWindowTracker(new SnapshotTracker(this), () => KeepVisible),
                useSharedPlatform: useSharedPlatform
            )
            {
                GameWindow = new GameWindowSnapshot(
                    (nint)42,
                    123,
                    new PixelRect(0, 0, 1920, 1080),
                    IsVisible: isVisible,
                    IsForeground: isForeground
                ),
            };
            ViewModel = new StreamOverlayViewModel(new StreamOverlaySettingsStore(Path.Combine(root, "settings.json")))
            {
                Enabled = true,
            };
            coordinator = new StreamOverlayCoordinator(ViewModel, Harness.Session, Clock);
        }

        public bool KeepVisible { get; set; }

        public HostedOverlayTestHarness Harness { get; }

        public StreamOverlayViewModel ViewModel { get; }

        public void Synchronize()
        {
            registry.SetGalaxyMapContextActive(!registry.IsGalaxyMapContextActive);
            Harness.Tick();
        }

        public void Dispose()
        {
            coordinator.Dispose();
            Harness.Dispose();
            Directory.Delete(root, recursive: true);
        }

        private sealed class SnapshotTracker(TestContext context) : IGameWindowTracker
        {
            public GameWindowSnapshot GetSnapshot() => context.Harness.GameWindow;

            public void Dispose() { }
        }
    }
}
