using Avalonia;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class VrOverlayInputRouter
{
    private readonly Pointer pointer = new(1, PointerType.Mouse, true);
    private readonly Dictionary<string, RawInputModifiers> modifiers = new(StringComparer.Ordinal);

    public void Dispatch(RegisteredOverlayWindow registration, VrOverlayPointerEvent pointerEvent)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(pointerEvent);
        Point localPoint = new(pointerEvent.X, pointerEvent.Y);
        Point rootPoint = registration.RenderSource.TranslatePoint(localPoint, registration.Window) ?? localPoint;
        var renderInput = registration.RenderSource as IInputElement;
        IInputElement? target = pointer.Captured ?? renderInput?.InputHitTest(localPoint) ?? renderInput;
        if (target is null)
        {
            return;
        }

        RawInputModifiers current = modifiers.GetValueOrDefault(pointerEvent.PlotterName);
        ulong timestamp = unchecked((ulong)Environment.TickCount64);
        if (pointerEvent.Kind == VrOverlayPointerEventKind.Wheel)
        {
            target.RaiseEvent(
                new PointerWheelEventArgs(
                    target,
                    pointer,
                    registration.Window,
                    rootPoint,
                    timestamp,
                    new PointerPointProperties(current, PointerUpdateKind.Other),
                    KeyModifiers.None,
                    new Vector(pointerEvent.WheelX, pointerEvent.WheelY)
                )
            );
            return;
        }

        (PointerUpdateKind updateKind, RawInputModifiers button, MouseButton? releasedButton) = Map(pointerEvent.Kind);
        current = releasedButton is null && button != RawInputModifiers.None ? current | button : current & ~button;
        modifiers[pointerEvent.PlotterName] = current;
        var properties = new PointerPointProperties(current, updateKind);
        switch (pointerEvent.Kind)
        {
            case VrOverlayPointerEventKind.Move:
                target.RaiseEvent(
                    new PointerEventArgs(
                        InputElement.PointerMovedEvent,
                        target,
                        pointer,
                        registration.Window,
                        rootPoint,
                        timestamp,
                        properties,
                        KeyModifiers.None
                    )
                );
                break;
            case VrOverlayPointerEventKind.LeftButtonDown:
            case VrOverlayPointerEventKind.RightButtonDown:
            case VrOverlayPointerEventKind.MiddleButtonDown:
                target.RaiseEvent(
                    new PointerPressedEventArgs(
                        target,
                        pointer,
                        registration.Window,
                        rootPoint,
                        timestamp,
                        properties,
                        KeyModifiers.None,
                        clickCount: 1
                    )
                );
                break;
            case VrOverlayPointerEventKind.LeftButtonUp:
            case VrOverlayPointerEventKind.RightButtonUp:
            case VrOverlayPointerEventKind.MiddleButtonUp:
                target.RaiseEvent(
                    new PointerReleasedEventArgs(
                        target,
                        pointer,
                        registration.Window,
                        rootPoint,
                        timestamp,
                        properties,
                        KeyModifiers.None,
                        releasedButton!.Value
                    )
                );
                pointer.Capture(null);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(pointerEvent),
                    pointerEvent.Kind,
                    "Unsupported VR pointer event."
                );
        }
    }

    public void Reset()
    {
        modifiers.Clear();
        pointer.Capture(null);
    }

    private static (PointerUpdateKind UpdateKind, RawInputModifiers Button, MouseButton? ReleasedButton) Map(
        VrOverlayPointerEventKind kind
    )
    {
        return kind switch
        {
            VrOverlayPointerEventKind.Move => (PointerUpdateKind.Other, RawInputModifiers.None, null),
            VrOverlayPointerEventKind.LeftButtonDown => (
                PointerUpdateKind.LeftButtonPressed,
                RawInputModifiers.LeftMouseButton,
                null
            ),
            VrOverlayPointerEventKind.LeftButtonUp => (
                PointerUpdateKind.LeftButtonReleased,
                RawInputModifiers.LeftMouseButton,
                MouseButton.Left
            ),
            VrOverlayPointerEventKind.RightButtonDown => (
                PointerUpdateKind.RightButtonPressed,
                RawInputModifiers.RightMouseButton,
                null
            ),
            VrOverlayPointerEventKind.RightButtonUp => (
                PointerUpdateKind.RightButtonReleased,
                RawInputModifiers.RightMouseButton,
                MouseButton.Right
            ),
            VrOverlayPointerEventKind.MiddleButtonDown => (
                PointerUpdateKind.MiddleButtonPressed,
                RawInputModifiers.MiddleMouseButton,
                null
            ),
            VrOverlayPointerEventKind.MiddleButtonUp => (
                PointerUpdateKind.MiddleButtonReleased,
                RawInputModifiers.MiddleMouseButton,
                MouseButton.Middle
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported VR pointer event."),
        };
    }
}
