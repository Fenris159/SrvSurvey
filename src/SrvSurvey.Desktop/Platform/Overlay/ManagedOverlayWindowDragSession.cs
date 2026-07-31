using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class ManagedOverlayWindowDragSession
{
    private static readonly Dictionary<Window, ManagedOverlayWindowDragSession>
        ActiveSessions = [];

    private readonly Window window;
    private readonly IPointer pointer;
    private readonly PixelPoint initialWindowPosition;
    private readonly PixelPoint initialPointerPosition;
    private bool stopped;

    private ManagedOverlayWindowDragSession(
        Window window,
        PointerPressedEventArgs eventArgs)
    {
        this.window = window;
        pointer = eventArgs.Pointer;
        initialWindowPosition = window.Position;
        initialPointerPosition = window.PointToScreen(
            eventArgs.GetPosition(window));
    }

    internal static void Begin(
        Window window,
        PointerPressedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (ActiveSessions.Remove(window, out var current))
        {
            current.Stop(releasePointer: true);
        }

        var session = new ManagedOverlayWindowDragSession(window, eventArgs);
        ActiveSessions.Add(window, session);
        window.PointerMoved += session.OnPointerMoved;
        window.PointerReleased += session.OnPointerReleased;
        window.PointerCaptureLost += session.OnPointerCaptureLost;
        window.Closed += session.OnWindowClosed;
        eventArgs.Pointer.Capture(window);
    }

    internal static PixelPoint CalculatePosition(
        PixelPoint initialWindowPosition,
        PixelPoint initialPointerPosition,
        PixelPoint currentPointerPosition)
    {
        return new PixelPoint(
            initialWindowPosition.X
                + currentPointerPosition.X
                - initialPointerPosition.X,
            initialWindowPosition.Y
                + currentPointerPosition.Y
                - initialPointerPosition.Y);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (stopped || !ReferenceEquals(eventArgs.Pointer, pointer))
        {
            return;
        }

        var currentPointerPosition = window.PointToScreen(
            eventArgs.GetPosition(window));
        window.Position = CalculatePosition(
            initialWindowPosition,
            initialPointerPosition,
            currentPointerPosition);
        eventArgs.Handled = true;
    }

    private void OnPointerReleased(
        object? sender,
        PointerReleasedEventArgs eventArgs)
    {
        if (ReferenceEquals(eventArgs.Pointer, pointer))
        {
            Stop(releasePointer: true);
        }
    }

    private void OnPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs eventArgs)
    {
        Stop(releasePointer: false);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        Stop(releasePointer: true);
    }

    private void Stop(bool releasePointer)
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        ActiveSessions.Remove(window);
        window.PointerMoved -= OnPointerMoved;
        window.PointerReleased -= OnPointerReleased;
        window.PointerCaptureLost -= OnPointerCaptureLost;
        window.Closed -= OnWindowClosed;
        if (releasePointer)
        {
            pointer.Capture(null);
        }
    }
}
