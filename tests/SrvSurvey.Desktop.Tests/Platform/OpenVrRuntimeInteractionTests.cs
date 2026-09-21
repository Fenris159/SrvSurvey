using SrvSurvey.Desktop.Platform.Overlay;
using Valve.VR;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OpenVrRuntimeInteractionTests
{
    [Fact]
    public void InactiveRuntimeRejectsInteractionAndReturnsNoEvents()
    {
        using var runtime = new OpenVrRuntime();

        VrRuntimeResult result = runtime.SetInteractionEnabled(true);

        Assert.False(result.Succeeded);
        Assert.Contains("not active", result.Message);
        Assert.Empty(runtime.PollPointerEvents());
    }

    [Fact]
    public void InteractionUpdateRollsBackHandlesChangedBeforeFailure()
    {
        var calls = new List<(ulong Handle, VROverlayInputMethod Method)>();

        (bool succeeded, string? error, bool rollbackFailed) = OpenVrRuntime.UpdateOverlayInputMethods(
            [11, 22, 33],
            VROverlayInputMethod.None,
            VROverlayInputMethod.Mouse,
            (handle, method) =>
            {
                calls.Add((handle, method));
                return handle == 22 && method == VROverlayInputMethod.Mouse
                    ? EVROverlayError.InvalidHandle
                    : EVROverlayError.None;
            }
        );

        Assert.False(succeeded);
        Assert.Equal(EVROverlayError.InvalidHandle.ToString(), error);
        Assert.False(rollbackFailed);
        Assert.Equal(
            [(11UL, VROverlayInputMethod.Mouse), (22UL, VROverlayInputMethod.Mouse), (11UL, VROverlayInputMethod.None)],
            calls
        );
    }

    [Fact]
    public void InteractionUpdateReportsFailedRollback()
    {
        (bool succeeded, string? error, bool rollbackFailed) = OpenVrRuntime.UpdateOverlayInputMethods(
            [11, 22],
            VROverlayInputMethod.Mouse,
            VROverlayInputMethod.None,
            (handle, method) =>
            {
                if (handle == 22)
                {
                    return EVROverlayError.InvalidHandle;
                }

                return method == VROverlayInputMethod.Mouse ? EVROverlayError.PermissionDenied : EVROverlayError.None;
            }
        );

        Assert.False(succeeded);
        Assert.Equal(EVROverlayError.InvalidHandle.ToString(), error);
        Assert.True(rollbackFailed);
    }

    [Fact]
    public void MouseMoveUpdatesThePointerPosition()
    {
        VREvent_t source = CreateMouseEvent(EVREventType.VREvent_MouseMove, 12.5f, 34.25f, 0);
        (double X, double Y) position = (10, 11);

        bool converted = OpenVrRuntime.TryCreatePointerEvent(
            "PlotJumpInfo",
            source,
            ref position,
            out VrOverlayPointerEvent? result
        );

        Assert.True(converted);
        Assert.Equal(VrOverlayPointerEventKind.Move, result!.Kind);
        Assert.Equal((12.5, 34.25), position);
        Assert.Equal(12.5, result.X);
        Assert.Equal(34.25, result.Y);
    }

    [Theory]
    [InlineData(EVREventType.VREvent_MouseButtonDown, EVRMouseButton.Left, VrOverlayPointerEventKind.LeftButtonDown)]
    [InlineData(EVREventType.VREvent_MouseButtonUp, EVRMouseButton.Left, VrOverlayPointerEventKind.LeftButtonUp)]
    [InlineData(EVREventType.VREvent_MouseButtonDown, EVRMouseButton.Right, VrOverlayPointerEventKind.RightButtonDown)]
    [InlineData(EVREventType.VREvent_MouseButtonUp, EVRMouseButton.Right, VrOverlayPointerEventKind.RightButtonUp)]
    [InlineData(
        EVREventType.VREvent_MouseButtonDown,
        EVRMouseButton.Middle,
        VrOverlayPointerEventKind.MiddleButtonDown
    )]
    [InlineData(EVREventType.VREvent_MouseButtonUp, EVRMouseButton.Middle, VrOverlayPointerEventKind.MiddleButtonUp)]
    public void MouseButtonsMapToAvaloniaFriendlyEvents(
        EVREventType eventType,
        EVRMouseButton button,
        VrOverlayPointerEventKind expected
    )
    {
        VREvent_t source = CreateMouseEvent(eventType, 2, 3, (uint)button);
        (double X, double Y) position = (10, 11);

        bool converted = OpenVrRuntime.TryCreatePointerEvent(
            "PlotJumpInfo",
            source,
            ref position,
            out VrOverlayPointerEvent? result
        );

        Assert.True(converted);
        Assert.Equal(expected, result!.Kind);
        Assert.Equal((10, 11), position);
        Assert.Equal(10, result.X);
        Assert.Equal(11, result.Y);
    }

    [Fact]
    public void ScrollKeepsTheLastPointerPositionAndPreservesBothAxes()
    {
        var source = new VREvent_t { eventType = (uint)EVREventType.VREvent_ScrollSmooth };
        source.data.scroll = new VREvent_Scroll_t { xdelta = -1.5f, ydelta = 2.25f };
        (double X, double Y) position = (40, 50);

        bool converted = OpenVrRuntime.TryCreatePointerEvent(
            "PlotJumpInfo",
            source,
            ref position,
            out VrOverlayPointerEvent? result
        );

        Assert.True(converted);
        Assert.Equal(VrOverlayPointerEventKind.Wheel, result!.Kind);
        Assert.Equal((40, 50), position);
        Assert.Equal(-1.5, result.WheelX);
        Assert.Equal(2.25, result.WheelY);
    }

    [Fact]
    public void UnrelatedOpenVrEventsAreIgnored()
    {
        var source = new VREvent_t { eventType = (uint)EVREventType.VREvent_OverlayShown };
        (double X, double Y) position = (4, 5);

        bool converted = OpenVrRuntime.TryCreatePointerEvent(
            "PlotJumpInfo",
            source,
            ref position,
            out VrOverlayPointerEvent? result
        );

        Assert.False(converted);
        Assert.Null(result);
        Assert.Equal((4, 5), position);
    }

    [Fact]
    public void UnknownOpenVrMouseButtonIsIgnored()
    {
        VREvent_t source = CreateMouseEvent(EVREventType.VREvent_MouseButtonDown, 2, 3, uint.MaxValue);
        (double X, double Y) position = (4, 5);

        bool converted = OpenVrRuntime.TryCreatePointerEvent(
            "PlotJumpInfo",
            source,
            ref position,
            out VrOverlayPointerEvent? result
        );

        Assert.False(converted);
        Assert.Null(result);
        Assert.Equal((4, 5), position);
    }

    [Fact]
    public void HeadsetUnavailableProbePreservesTheRuntimeAvailability()
    {
        var probe = VrRuntimeProbe.HeadsetUnavailable("Wake the headset.");

        Assert.True(probe.RuntimeAvailable);
        Assert.False(probe.HeadsetPresent);
        Assert.Equal("Wake the headset.", probe.Message);
    }

    private static VREvent_t CreateMouseEvent(EVREventType eventType, float x, float y, uint button)
    {
        var source = new VREvent_t { eventType = (uint)eventType };
        source.data.mouse = new VREvent_Mouse_t
        {
            x = x,
            y = y,
            button = button,
        };
        return source;
    }
}
