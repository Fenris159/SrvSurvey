using SDL3;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class SdlGamepadInputStateTests
{
    [Fact]
    public void MapsStandardButtonsAndSuppressesDuplicateState()
    {
        List<ControllerInputChange> changes = [];
        var state = new SdlGamepadInputState(changes.Add);

        state.UpdateButton(SDL.GamepadButton.South, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.South, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.South, isPressed: false);

        Assert.Equal(
            [
                new ControllerInputChange("B1", IsPressed: true),
                new ControllerInputChange("B1", IsPressed: false),
            ],
            changes);
    }

    [Fact]
    public void ConvertsDPadTransitionsToOnePovDirectionAtATime()
    {
        List<ControllerInputChange> changes = [];
        var state = new SdlGamepadInputState(changes.Add);

        state.UpdateButton(SDL.GamepadButton.DPadUp, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.DPadRight, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.DPadUp, isPressed: false);
        state.UpdateButton(SDL.GamepadButton.DPadRight, isPressed: false);

        Assert.Equal(
            [
                new ControllerInputChange("PovU", IsPressed: true),
                new ControllerInputChange("PovU", IsPressed: false),
                new ControllerInputChange("PovUR", IsPressed: true),
                new ControllerInputChange("PovUR", IsPressed: false),
                new ControllerInputChange("PovR", IsPressed: true),
                new ControllerInputChange("PovR", IsPressed: false),
            ],
            changes);
    }

    [Fact]
    public void CoalescesDPadEventsFromOneSdlUpdateIntoADiagonal()
    {
        List<ControllerInputChange> changes = [];
        var state = new SdlGamepadInputState(changes.Add);

        state.BeginBatch();
        state.UpdateButton(SDL.GamepadButton.DPadUp, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.DPadRight, isPressed: true);
        state.EndBatch();

        Assert.Equal(
            [new ControllerInputChange("PovUR", IsPressed: true)],
            changes);
    }

    [Fact]
    public void DiagonalReleaseProducesTheExistingPovChord()
    {
        var tracker = new ControllerChordTracker();
        string? chord = null;
        var state = new SdlGamepadInputState(change =>
            chord ??= tracker.UpdateToken(change.Token, change.IsPressed));

        state.BeginBatch();
        state.UpdateButton(SDL.GamepadButton.DPadUp, isPressed: true);
        state.UpdateButton(SDL.GamepadButton.DPadRight, isPressed: true);
        state.EndBatch();
        state.BeginBatch();
        state.UpdateButton(SDL.GamepadButton.DPadUp, isPressed: false);
        state.UpdateButton(SDL.GamepadButton.DPadRight, isPressed: false);
        state.EndBatch();

        Assert.Equal("PovUR", chord);
    }

    [Fact]
    public void ClearDoesNotTurnDisconnectIntoAChordRelease()
    {
        List<ControllerInputChange> changes = [];
        var state = new SdlGamepadInputState(changes.Add);
        state.UpdateButton(SDL.GamepadButton.South, isPressed: true);

        state.Clear();

        Assert.Equal(
            [new ControllerInputChange("B1", IsPressed: true)],
            changes);
    }

    [Fact]
    public void MapsTriggerThresholdCrossings()
    {
        List<ControllerInputChange> changes = [];
        var state = new SdlGamepadInputState(changes.Add);

        state.UpdateAxis(SDL.GamepadAxis.LeftTrigger, 29_999);
        state.UpdateAxis(SDL.GamepadAxis.LeftTrigger, 30_000);
        state.UpdateAxis(SDL.GamepadAxis.LeftTrigger, 32_767);
        state.UpdateAxis(SDL.GamepadAxis.LeftTrigger, 0);

        Assert.Equal(
            [
                new ControllerInputChange("LT", IsPressed: true),
                new ControllerInputChange("LT", IsPressed: false),
            ],
            changes);
    }
}
