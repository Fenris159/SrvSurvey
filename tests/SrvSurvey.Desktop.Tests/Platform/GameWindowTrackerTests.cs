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
            IsForeground: true);
        var missingHandle = available with { NativeHandle = nint.Zero };
        var missingProcess = available with { ProcessId = null };
        var emptyBounds = available with { ClientBounds = default };

        Assert.True(available.IsAvailable);
        Assert.False(missingHandle.IsAvailable);
        Assert.True(missingProcess.IsAvailable);
        Assert.False(emptyBounds.IsAvailable);
    }

    [Fact]
    public void CurrentHostTrackerReturnsAConsistentSnapshot()
    {
        using var tracker = GameWindowTracker.CreateCurrent();

        var snapshot = tracker.GetSnapshot();

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
}
