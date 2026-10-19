using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class StreamOverlayCoordinatorTests
{
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

    [AvaloniaFact]
    public void FocusLossKeepsTheSameCaptureWindowAvailableToObs()
    {
        using var context = new TestContext(isForeground: true);
        Window original = Assert.Single(context.Harness.PreparedWindows);
        bool closed = false;
        original.Closed += (_, _) => closed = true;
        Assert.True(original.IsVisible);
        Assert.True(original.Topmost);

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
        Assert.True(original.Topmost);
    }

    [AvaloniaFact]
    public void CaptureRemainsDiscoverableWithHiddenEliteAndValidBoundsUntilTheGameWindowIsGone()
    {
        using var context = new TestContext(isForeground: false, keepVisible: true);
        Window window = Assert.Single(context.Harness.PreparedWindows);
        Assert.True(window.IsVisible);
        Assert.True(window.Topmost);

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
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void DisablingCaptureInTheBackgroundClosesItsWindow()
    {
        using var context = new TestContext(isForeground: false);
        Window window = Assert.Single(context.Harness.PreparedWindows);
        context.ViewModel.Enabled = false;
        context.Synchronize();
        Assert.False(window.IsVisible);
        Assert.Single(context.Harness.PreparedWindows);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-stream-{Guid.NewGuid():N}");
        private readonly OverlayWindowRegistry registry = new();
        private readonly StreamOverlayCoordinator coordinator;

        public TestContext(bool isForeground, bool keepVisible = false, bool isVisible = true)
        {
            KeepVisible = keepVisible;
            Harness = new HostedOverlayTestHarness(
                registry,
                () => new OverlayGameWindowTracker(new SnapshotTracker(this), () => KeepVisible)
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
            coordinator = new StreamOverlayCoordinator(ViewModel, Harness.Session);
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
