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

        Assert.Empty(context.Platform.PreparedWindows);
    }

    [AvaloniaFact]
    public void FocusLossClosesWindowAndReturningToEliteRestoresTopmostWindow()
    {
        using var context = new TestContext(isForeground: true);
        var original = Assert.Single(context.Platform.PreparedWindows);
        var closed = false;
        original.Closed += (_, _) => closed = true;
        Assert.True(original.IsVisible);
        Assert.True(original.Topmost);

        context.Tracker.Snapshot = context.Tracker.Snapshot with { IsForeground = false };
        context.Synchronize();

        Assert.True(closed);
        Assert.False(original.IsVisible);
        Assert.True(context.ViewModel.Enabled);
        context.Synchronize();
        Assert.Single(context.Platform.PreparedWindows);

        context.Tracker.Snapshot = context.Tracker.Snapshot with { IsForeground = true };
        context.Synchronize();

        Assert.Equal(2, context.Platform.PreparedWindows.Count);
        var restored = context.Platform.PreparedWindows[1];
        Assert.True(restored.IsVisible);
        Assert.True(restored.Topmost);
    }

    [AvaloniaFact]
    public void SuppliedTrackerOverrideKeepsWindowUntilOverrideEndsButCannotShowMinimizedGame()
    {
        using var context = new TestContext(isForeground: false, keepVisible: true);
        var window = Assert.Single(context.Platform.PreparedWindows);
        Assert.True(window.IsVisible);
        Assert.True(window.Topmost);

        context.KeepVisible = false;
        context.Synchronize();
        Assert.False(window.IsVisible);

        context.KeepVisible = true;
        context.Synchronize();
        Assert.Equal(2, context.Platform.PreparedWindows.Count);
        var restored = context.Platform.PreparedWindows[1];
        Assert.True(restored.IsVisible);

        context.Tracker.Snapshot = context.Tracker.Snapshot with { IsVisible = false };
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
            Tracker = new StubTracker(new GameWindowSnapshot(
                (nint)42, 123, new PixelRect(0, 0, 1920, 1080),
                IsVisible: true, IsForeground: isForeground));
            ViewModel = new StreamOverlayViewModel(new StreamOverlaySettingsStore(Path.Combine(root, "settings.json")))
            {
                Enabled = true,
            };
            coordinator = new StreamOverlayCoordinator(
                ViewModel, Platform,
                new OverlayGameWindowTracker(Tracker, () => KeepVisible), registry);
        }

        public bool KeepVisible { get; set; }
        public StubTracker Tracker { get; }
        public StubPlatform Platform { get; } = new();
        public StreamOverlayViewModel ViewModel { get; }

        public void Synchronize()
        {
            // Drive the coordinator through its registry notification, without timer delays.
            registry.SetGalaxyMapContextActive(!registry.IsGalaxyMapContextActive);
        }

        public void Dispose()
        {
            coordinator.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubTracker(GameWindowSnapshot snapshot) : IGameWindowTracker
    {
        public GameWindowSnapshot Snapshot { get; set; } = snapshot;
        public GameWindowSnapshot GetSnapshot() => Snapshot;
        public void Dispose() { }
    }

    private sealed class StubPlatform : IOverlayPlatformService
    {
        public OverlayPlatformCapabilities Capabilities { get; } =
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows);
        public List<Window> PreparedWindows { get; } = [];

        public OverlayPreparationResult PreparePassiveWindow(Window window)
        {
            PreparedWindows.Add(window);
            return new OverlayPreparationResult(true, true, "Prepared");
        }

        public OverlayInteractionResult SetInteractive(Window window, bool interactive) =>
            new(true, interactive, "Prepared");

        public void Dispose() { }
    }
}
