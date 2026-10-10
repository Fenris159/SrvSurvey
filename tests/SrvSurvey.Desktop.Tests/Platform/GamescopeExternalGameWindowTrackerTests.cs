using Avalonia;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopeExternalGameWindowTrackerTests
{
    [Theory]
    [InlineData(0, 1280, 720, 0, 40)]
    [InlineData(1, 960, 540, 160, 130)]
    [InlineData(2, 1280, 720, 0, 40)]
    [InlineData(3, 1422, 800, -71, 0)]
    [InlineData(4, 1280, 800, 0, 0)]
    public void OutputViewportMatchesGamescopeScaling(uint mode, int width, int height, int x, int y)
    {
        var focus = new GamescopeFocusSnapshot(":7", 42, 359320, 359320, new PixelRect(0, 0, 1280, 800), mode);
        Assert.Equal(
            new PixelRect(x, y, width, height),
            GamescopeViewport.Project(new PixelRect(0, 0, 960, 540), new PixelRect(0, 0, 960, 600), focus)
        );
    }

    [Fact]
    public void NestedAndOutputAspectRatiosAreAppliedSeparately()
    {
        var focus = new GamescopeFocusSnapshot(":7", 42, 1, 1, new PixelRect(0, 0, 1280, 800));
        Assert.Equal(
            new PixelRect(106, 100, 1067, 600),
            GamescopeViewport.Project(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 800, 600), focus)
        );
        Assert.Equal(
            new PixelRect(0, 40, 1280, 720),
            GamescopeViewport.Project(new PixelRect(0, 0, 1920, 1080), null, focus)
        );
        Assert.Equal(default, GamescopeViewport.Project(default, null, focus));
        Assert.Equal(default, GamescopeViewport.Project(new PixelRect(0, 0, 10, 10), default(PixelRect), focus));
        Assert.Equal(
            default,
            GamescopeViewport.Project(new PixelRect(0, 0, 10, 10), null, focus with { OutputBounds = default })
        );
    }

    [Fact]
    public void FollowsStartupFocusOutputResizeAndProcessRestartWithoutLauncherHints()
    {
        var focus = new GamescopeFocusSnapshot(":7", 42, 359320, 359320, new PixelRect(0, 0, 1280, 800));
        EliteKeyboardDisplay? discovered = null;
        long now = 0;
        int discoveries = 0;
        var first = new StubTracker(new(42, 100, new PixelRect(0, 0, 960, 600), true, false));
        var second = new StubTracker(new(43, 200, new PixelRect(0, 0, 1920, 1080), true, false));
        var connection = new StubTracker(GameWindowSnapshot.Unavailable);
        using var tracker = new GamescopeExternalGameWindowTracker(
            () => focus,
            preferred =>
            {
                Assert.Equal(focus.Display, preferred);
                discoveries++;
                return discovered;
            },
            display => display == ":7" ? first : second,
            connection,
            () => now
        );
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        discovered = new(100, ":7");
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        now = 500;
        GameWindowSnapshot shown = tracker.GetSnapshot();
        Assert.True(shown.IsVisible);
        Assert.True(shown.IsForeground);
        Assert.Equal(focus.OutputBounds, shown.OverlayCanvasBounds);
        Assert.Equal(new PixelRect(0, 0, 1280, 800), shown.ClientBounds);
        Assert.Equal(2, discoveries);
        focus = focus with { InputApp = 0, OutputBounds = new PixelRect(0, 0, 1920, 1200) };
        shown = tracker.GetSnapshot();
        Assert.True(shown.IsVisible);
        Assert.False(shown.IsForeground);
        Assert.Equal(focus.OutputBounds, shown.ClientBounds);
        focus = focus with { Window = 99 };
        Assert.False(tracker.GetSnapshot().IsVisible);
        focus = focus with { Window = 42, Display = ":8" }; // Same numeric XID on another server must not match.
        Assert.False(tracker.GetSnapshot().IsVisible);
        now = 1000;
        discovered = new(200, ":8");
        focus = focus with { Window = 43, InputApp = 359320 };
        Assert.True(tracker.GetSnapshot().IsForeground);
        Assert.Equal(1, first.Disposals);
        second.Snapshot = GameWindowSnapshot.Unavailable;
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        Assert.Equal(1, second.Disposals);
        tracker.Dispose();
        tracker.Dispose();
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        Assert.Equal(1, connection.Disposals);
    }

    [Fact]
    public void MissingTrackerRetriesAndDefaultClockIsUsable()
    {
        var focus = new GamescopeFocusSnapshot(null, 0, null, null, default);
        using var tracker = new GamescopeExternalGameWindowTracker(
            () => focus,
            _ => new(100, ":7"),
            _ => null,
            new StubTracker(GameWindowSnapshot.Unavailable)
        );
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        Assert.True(GamescopeExternalGameWindowTracker.SameDisplay(":7.0", ":7"));
        Assert.False(GamescopeExternalGameWindowTracker.SameDisplay(":7", null));
    }

    private sealed class StubTracker(GameWindowSnapshot snapshot) : IGameWindowTracker
    {
        public GameWindowSnapshot Snapshot { get; set; } = snapshot;
        public int Disposals { get; private set; }

        public GameWindowSnapshot GetSnapshot() => Snapshot;

        public void Dispose() => Disposals++;
    }
}
