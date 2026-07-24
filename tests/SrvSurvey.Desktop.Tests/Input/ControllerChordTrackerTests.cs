using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class ControllerChordTrackerTests
{
    [Fact]
    public void EmitsFullChordOnFirstReleaseOnly()
    {
        var tracker = new ControllerChordTracker();
        tracker.UpdateButton(1, isPressed: true);
        tracker.UpdateButton(9, isPressed: true);

        Assert.Equal("B10 B2", tracker.UpdateButton(1, isPressed: false));
        Assert.Null(tracker.UpdateButton(9, isPressed: false));
        Assert.Empty(tracker.Pressed);
    }

    [Fact]
    public void AllowsNextChordAfterAllInputsAreReleased()
    {
        var tracker = new ControllerChordTracker();
        tracker.UpdateButton(0, isPressed: true);
        Assert.Equal("B1", tracker.UpdateButton(0, isPressed: false));

        tracker.UpdateTrigger("RT", isPressed: true);

        Assert.Equal("RT", tracker.UpdateTrigger("RT", isPressed: false));
    }

    [Fact]
    public void MapsDiagonalHatAndHeldButton()
    {
        var tracker = new ControllerChordTracker();
        tracker.UpdateButton(0, isPressed: true);
        tracker.UpdateHat(ControllerHatDirection.UpRight);

        Assert.Equal(
            "B1 PovUR",
            tracker.UpdateHat(ControllerHatDirection.Centered));
        Assert.Null(tracker.UpdateButton(0, isPressed: false));
    }

    [Fact]
    public void ClearDoesNotEmitStaleInputAfterDisconnect()
    {
        var tracker = new ControllerChordTracker();
        tracker.UpdateButton(0, isPressed: true);

        tracker.Clear();

        Assert.Null(tracker.UpdateButton(0, isPressed: false));
        Assert.Empty(tracker.Pressed);
    }
}
