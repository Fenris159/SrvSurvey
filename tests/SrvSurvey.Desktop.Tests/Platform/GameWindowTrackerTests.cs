using System.Diagnostics;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GameWindowTrackerTests
{
    [Fact]
    public void SnapshotRequiresHandleProcessAndPositiveClientBounds()
    {
        var available = new GameWindowSnapshot(
            (nint)42,
            7,
            new PixelRect(100, 200, 1920, 1080),
            IsVisible: true,
            IsForeground: true
        );
        GameWindowSnapshot missingHandle = available with { NativeHandle = nint.Zero };
        GameWindowSnapshot missingProcess = available with { ProcessId = null };
        GameWindowSnapshot emptyBounds = available with { ClientBounds = default };

        Assert.True(available.IsAvailable);
        Assert.False(missingHandle.IsAvailable);
        Assert.True(missingProcess.IsAvailable);
        Assert.False(emptyBounds.IsAvailable);
    }

    [Fact]
    public void CurrentHostTrackerReturnsAConsistentSnapshot()
    {
        using IGameWindowTracker tracker = GameWindowTracker.CreateCurrent();

        GameWindowSnapshot snapshot = tracker.GetSnapshot();

        if (snapshot.IsAvailable)
        {
            Assert.NotEqual(nint.Zero, snapshot.NativeHandle);
            Assert.NotNull(snapshot.ProcessId);
            Assert.True(snapshot.ClientBounds.Width > 0);
            Assert.True(snapshot.ClientBounds.Height > 0);
        }
        else if (snapshot.NativeHandle == nint.Zero)
        {
            Assert.Null(snapshot.ProcessId);
            Assert.False(snapshot.IsVisible);
            Assert.False(snapshot.IsForeground);
        }
    }

    [Fact]
    public void CachedTrackerSharesOneNativeSampleInsideFreshnessWindow()
    {
        long timestamp = 0L;
        var inner = new CountingGameWindowTracker();
        using var tracker = new CachedGameWindowTracker(inner, TimeSpan.FromMilliseconds(40), () => timestamp);

        GameWindowSnapshot first = tracker.GetSnapshot();
        timestamp += Stopwatch.Frequency / 100;
        GameWindowSnapshot cached = tracker.GetSnapshot();
        timestamp += Stopwatch.Frequency / 20;
        GameWindowSnapshot refreshed = tracker.GetSnapshot();

        Assert.Same(first, cached);
        Assert.NotSame(first, refreshed);
        Assert.Equal(2, inner.GetSnapshotCount);
    }

    [Fact]
    public void OverlayTimerPulsesOnlyWhenItsIntervalIsDue()
    {
        var timer = new OverlayDispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        int ticks = 0;
        timer.Tick += (_, _) => ticks++;
        timer.Arm(TimeSpan.Zero);

        Assert.False(timer.Pulse(TimeSpan.FromMilliseconds(249)));
        Assert.True(timer.Pulse(TimeSpan.FromMilliseconds(250)));
        Assert.False(timer.Pulse(TimeSpan.FromMilliseconds(499)));
        Assert.True(timer.Pulse(TimeSpan.FromMilliseconds(500)));
        Assert.Equal(2, ticks);
    }

    private sealed class CountingGameWindowTracker : IGameWindowTracker
    {
        public int GetSnapshotCount { get; private set; }

        public GameWindowSnapshot GetSnapshot()
        {
            GetSnapshotCount++;
            return new GameWindowSnapshot(
                (nint)GetSnapshotCount,
                GetSnapshotCount,
                new PixelRect(0, 0, 1920, 1080),
                IsVisible: true,
                IsForeground: true
            );
        }

        public void Dispose() { }
    }
}
