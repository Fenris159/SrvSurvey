using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class OverlayGameWindowTrackerTests
{
    [Fact]
    public void KeepPreferenceOnlyOverridesForegroundForVisibleGameWindow()
    {
        var keep = false;
        var inner = new StubTracker(new GameWindowSnapshot(
            (nint)42,
            123,
            new PixelRect(10, 20, 1920, 1080),
            IsVisible: true,
            IsForeground: false));
        using var tracker = new OverlayGameWindowTracker(inner, () => keep);

        Assert.False(tracker.GetSnapshot().IsForeground);

        keep = true;
        Assert.True(tracker.GetSnapshot().IsForeground);

        inner.Snapshot = GameWindowSnapshot.Unavailable;
        Assert.False(tracker.GetSnapshot().IsForeground);
    }

    [Fact]
    public void DisposeOwnsThePlatformTracker()
    {
        var inner = new StubTracker(GameWindowSnapshot.Unavailable);
        var tracker = new OverlayGameWindowTracker(inner, () => false);

        tracker.Dispose();

        Assert.True(inner.IsDisposed);
    }

    private sealed class StubTracker(GameWindowSnapshot snapshot)
        : IGameWindowTracker
    {
        public GameWindowSnapshot Snapshot { get; set; } = snapshot;

        public bool IsDisposed { get; private set; }

        public GameWindowSnapshot GetSnapshot() => Snapshot;

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
