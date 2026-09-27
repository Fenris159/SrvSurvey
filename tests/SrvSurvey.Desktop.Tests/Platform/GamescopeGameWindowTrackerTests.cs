using System.Globalization;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopeGameWindowTrackerTests
{
    [Fact]
    public void ReadsLiveGamescopeMarkerAndDesktopBounds()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            WriteMarker(directory, Environment.ProcessId, ":7", "3840 0 5120 2880");

            var bridge = GamescopeGameWindowBridge.TryRead(directory);

            Assert.NotNull(bridge);
            Assert.Equal(Environment.ProcessId, bridge.ProcessId);
            Assert.Equal(":7", bridge.Display);
            Assert.Equal(new PixelRect(3840, 0, 5120, 2880), bridge.HostBounds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("wrong-start-time", ":7", "3840 0 5120 2880")]
    [InlineData("valid", "wayland-0", "3840 0 5120 2880")]
    [InlineData("valid", ":7", "3840 0 0 2880")]
    [InlineData("valid", ":7", "bad bounds")]
    public void RejectsInvalidGamescopeMarkers(string startTime, string display, string bounds)
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            WriteMarker(directory, Environment.ProcessId, display, bounds, startTime);

            Assert.Null(GamescopeGameWindowBridge.TryRead(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingRuntimeDirectoryHasNoBridge()
    {
        Assert.Null(GamescopeGameWindowBridge.TryRead(null));
        Assert.Null(GamescopeGameWindowBridge.TryRead(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ProjectsNestedGameOntoDesktopAndReleasesTrackerWhenSessionEnds()
    {
        var host = new StubTracker(GameWindowSnapshot.Unavailable);
        var nested = new StubTracker(
            new GameWindowSnapshot((nint)42, 123, new PixelRect(0, 0, 3840, 2160), true, true)
        );
        GamescopeGameWindowBridge? bridge = new(123, ":7", new PixelRect(3840, 0, 5120, 2880));
        using var tracker = new GamescopeGameWindowTracker(host, () => bridge, _ => nested);

        GameWindowSnapshot projected = tracker.GetSnapshot();
        Assert.Equal((nint)42, projected.NativeHandle);
        Assert.Equal(new PixelRect(3840, 0, 5120, 2880), projected.ClientBounds);
        Assert.True(projected.IsForeground);

        bridge = null;
        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        Assert.True(nested.Disposed);
    }

    [Fact]
    public void PrefersRealDesktopGameWindow()
    {
        var desktopGame = new GameWindowSnapshot((nint)7, 1, new PixelRect(10, 20, 100, 80), true, true);
        var host = new StubTracker(desktopGame);
        var bridge = new GamescopeGameWindowBridge(123, ":7", new PixelRect(3840, 0, 5120, 2880));
        bool nestedWasCreated = false;
        using var tracker = new GamescopeGameWindowTracker(
            host,
            () => bridge,
            _ =>
            {
                nestedWasCreated = true;
                return new StubTracker(GameWindowSnapshot.Unavailable);
            }
        );

        Assert.Same(desktopGame, tracker.GetSnapshot());
        Assert.False(nestedWasCreated);
    }

    [Fact]
    public void ReconnectsWhenGamescopeSessionChanges()
    {
        var first = new StubTracker(GameWindowSnapshot.Unavailable);
        var second = new StubTracker(new GameWindowSnapshot((nint)9, 3, new PixelRect(0, 0, 1920, 1080), true, false));
        GamescopeGameWindowBridge? bridge = new(1, ":2", new PixelRect(0, 0, 1920, 1080));
        using var tracker = new GamescopeGameWindowTracker(
            new StubTracker(GameWindowSnapshot.Unavailable),
            () => bridge,
            display => display == ":2" ? first : second
        );

        Assert.Same(GameWindowSnapshot.Unavailable, tracker.GetSnapshot());
        bridge = new GamescopeGameWindowBridge(2, ":3", new PixelRect(3840, 0, 5120, 2880));
        Assert.Equal(bridge.HostBounds, tracker.GetSnapshot().ClientBounds);
        Assert.True(first.Disposed);
    }

    private static string CreateTemporaryDirectory() => Directory.CreateTempSubdirectory().FullName;

    private static void WriteMarker(
        string directory,
        int processId,
        string display,
        string bounds,
        string startTime = "valid"
    )
    {
        ulong actualStartTime = X11OverlayInteractionMarker.TryReadProcessStartTime(processId)!.Value;
        string value = startTime == "valid" ? actualStartTime.ToString(CultureInfo.InvariantCulture) : "0";
        File.WriteAllText(
            Path.Combine(directory, GamescopeGameWindowBridge.MarkerPrefix + processId),
            $"{value}\n{display}\n{bounds}\n"
        );
    }

    private sealed class StubTracker(GameWindowSnapshot snapshot) : IGameWindowTracker
    {
        public bool Disposed { get; private set; }

        public GameWindowSnapshot GetSnapshot() => snapshot;

        public void Dispose() => Disposed = true;
    }
}
