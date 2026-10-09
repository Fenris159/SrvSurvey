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
    public void EnablingWhileEliteIsInBackgroundDoesNotOpenWindow()
    {
        using var context = new TestContext(isForeground: false);

        Assert.Empty(context.Harness.PreparedWindows);
    }

    [AvaloniaFact]
    public void FocusLossClosesWindowAndReturningToEliteRestoresTopmostWindow()
    {
        using var context = new TestContext(isForeground: true);
        Window original = Assert.Single(context.Harness.PreparedWindows);
        bool closed = false;
        original.Closed += (_, _) => closed = true;
        Assert.True(original.IsVisible);
        Assert.True(original.Topmost);

        context.Harness.GameWindow = context.Harness.GameWindow with { IsForeground = false };
        context.Synchronize();

        Assert.True(closed);
        Assert.False(original.IsVisible);
        Assert.True(context.ViewModel.Enabled);
        context.Synchronize();
        Assert.Single(context.Harness.PreparedWindows);

        context.Harness.GameWindow = context.Harness.GameWindow with { IsForeground = true };
        context.Synchronize();

        Assert.Equal(2, context.Harness.PreparedWindows.Count);
        Window restored = context.Harness.PreparedWindows[1];
        Assert.True(restored.IsVisible);
        Assert.True(restored.Topmost);
    }

    [AvaloniaFact]
    public void SuppliedTrackerOverrideKeepsWindowUntilOverrideEndsButCannotShowMinimizedGame()
    {
        using var context = new TestContext(isForeground: false, keepVisible: true);
        Window window = Assert.Single(context.Harness.PreparedWindows);
        Assert.True(window.IsVisible);
        Assert.True(window.Topmost);

        context.KeepVisible = false;
        context.Synchronize();
        Assert.False(window.IsVisible);

        context.KeepVisible = true;
        context.Synchronize();
        Assert.Equal(2, context.Harness.PreparedWindows.Count);
        Window restored = context.Harness.PreparedWindows[1];
        Assert.True(restored.IsVisible);

        context.Harness.GameWindow = context.Harness.GameWindow with { IsVisible = false };
        context.Synchronize();
        Assert.False(restored.IsVisible);
    }

    private sealed class TestContext : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-stream-{Guid.NewGuid():N}");
        private readonly OverlayWindowRegistry registry = new();
        private readonly StreamOverlayCoordinator coordinator;

        public TestContext(bool isForeground, bool keepVisible = false)
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
                    IsVisible: true,
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
