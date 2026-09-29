using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed class ManagedOverlayWindowDragSession : IDisposable
{
    private static readonly Dictionary<Window, ManagedOverlayWindowDragSession> ActiveSessions = [];

    private readonly Window window;
    private readonly IPointer pointer;
    private readonly PixelPoint initialWindowPosition;
    private readonly PixelPoint initialPointerPosition;
    private readonly PendingWindowMove pendingMove;
    private readonly OverlayDragOptions options;
    private readonly OverlayDragDiagnostics diagnostics;
    private PixelPoint latestPointerPosition;
    private bool stopped;

    private ManagedOverlayWindowDragSession(
        Window window,
        PointerPressedEventArgs eventArgs,
        Action<PixelPoint>? positionApplied
    )
    {
        this.window = window;
        pointer = eventArgs.Pointer;
        initialWindowPosition = window.Position;
        initialPointerPosition = window.PointToScreen(eventArgs.GetPosition(window));
        latestPointerPosition = initialPointerPosition;
        options = OverlayDragPolicy.GetOptions(window);
        diagnostics = new OverlayDragDiagnostics(window, initialPointerPosition, options);
        pendingMove = new PendingWindowMove(
            position =>
            {
                diagnostics.Observe(latestPointerPosition);
                if (window.Position != position)
                {
                    window.Position = position;
                    positionApplied?.Invoke(window.Position);
                }
            },
            callback => DispatcherTimer.RunOnce(callback, TimeSpan.FromMilliseconds(16), DispatcherPriority.Input)
        );
    }

    internal static void Begin(
        Window window,
        PointerPressedEventArgs eventArgs,
        Action<PixelPoint>? positionApplied = null
    )
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventArgs);

        if (ActiveSessions.Remove(window, out ManagedOverlayWindowDragSession? current))
        {
            current.Dispose();
        }

        var session = new ManagedOverlayWindowDragSession(window, eventArgs, positionApplied);
        ActiveSessions.Add(window, session);
        window.PointerMoved += session.OnPointerMoved;
        window.PointerReleased += session.OnPointerReleased;
        window.PointerCaptureLost += session.OnPointerCaptureLost;
        window.Closed += session.OnWindowClosed;
        window.Deactivated += session.OnWindowDeactivated;
        eventArgs.Pointer.Capture(window);
    }

    internal static bool CanBeginFrom(Control surface, PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(surface).Properties.IsLeftButtonPressed)
        {
            return false;
        }

        // Preserve dragging over handled passive scroll content; inputs and
        // active map gestures receive their own pointer events.
        if (
            eventArgs.Source is Visual source
            && source
                .GetSelfAndVisualAncestors()
                .Any(visual =>
                    visual is Button or Thumb or ScrollBar or Slider or TextBox or SelectingItemsControl or ToggleSwitch
                    || (eventArgs.Handled && visual is MineMapControl or GuardianSiteMapControl)
                )
        )
        {
            return false;
        }

        // Avalonia implicitly captures every mouse press to its hit-test source.
        // A control capturing a different element has taken ownership of the gesture.
        return eventArgs.Pointer.Captured is null
            || ReferenceEquals(eventArgs.Pointer.Captured, eventArgs.Source)
            || ReferenceEquals(eventArgs.Pointer.Captured, TopLevel.GetTopLevel(surface));
    }

    internal static PixelPoint CalculatePosition(
        PixelPoint initialWindowPosition,
        PixelPoint initialPointerPosition,
        PixelPoint currentPointerPosition
    )
    {
        return new PixelPoint(
            initialWindowPosition.X + currentPointerPosition.X - initialPointerPosition.X,
            initialWindowPosition.Y + currentPointerPosition.Y - initialPointerPosition.Y
        );
    }

    private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (stopped || !ReferenceEquals(eventArgs.Pointer, pointer))
        {
            return;
        }

        if (!eventArgs.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
        {
            Stop(releasePointer: true, reason: "button-up without release event");
            eventArgs.Handled = true;
            return;
        }

        PixelPoint currentPointerPosition = window.PointToScreen(eventArgs.GetPosition(window));
        latestPointerPosition = currentPointerPosition;
        PixelPoint position = CalculatePosition(initialWindowPosition, initialPointerPosition, currentPointerPosition);
        pendingMove.Update(options.ConstrainPosition?.Invoke(position) ?? position);
        eventArgs.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (ReferenceEquals(eventArgs.Pointer, pointer))
        {
            Stop(releasePointer: true, reason: "released");
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
    {
        Stop(releasePointer: false, reason: "capture lost");
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        Stop(releasePointer: true, applyPendingMove: false, reason: "closed");
    }

    private void OnWindowDeactivated(object? sender, EventArgs eventArgs) =>
        Stop(releasePointer: true, reason: "deactivated");

    internal static void Cancel(Window window, bool applyPendingMove = true)
    {
        if (ActiveSessions.TryGetValue(window, out ManagedOverlayWindowDragSession? session))
        {
            session.Stop(releasePointer: true, applyPendingMove: applyPendingMove, reason: "interaction ended");
        }
    }

    public void Dispose()
    {
        Stop(releasePointer: true, reason: "disposed");
        diagnostics.Dispose();
    }

    private void Stop(bool releasePointer, bool applyPendingMove = true, string reason = "released or capture lost")
    {
        if (stopped)
        {
            return;
        }

        stopped = true;
        pendingMove.Complete(applyPendingMove);
        diagnostics.Complete(reason, window.Position);
        ActiveSessions.Remove(window);
        window.PointerMoved -= OnPointerMoved;
        window.PointerReleased -= OnPointerReleased;
        window.PointerCaptureLost -= OnPointerCaptureLost;
        window.Closed -= OnWindowClosed;
        window.Deactivated -= OnWindowDeactivated;
        if (releasePointer)
        {
            pointer.Capture(null);
        }
    }
}

internal sealed class PendingWindowMove(Action<PixelPoint> apply, Func<Action, IDisposable> schedule)
{
    private IDisposable? scheduledUpdate;
    private PixelPoint latestPosition;
    private bool hasPendingPosition;
    private bool completed;

    internal void Update(PixelPoint position)
    {
        if (completed)
        {
            return;
        }

        latestPosition = position;
        hasPendingPosition = true;
        scheduledUpdate ??= schedule(ApplyScheduled);
    }

    internal void Complete(bool applyPendingPosition)
    {
        if (completed)
        {
            return;
        }

        completed = true;
        scheduledUpdate?.Dispose();
        scheduledUpdate = null;
        if (applyPendingPosition)
        {
            ApplyLatest();
        }
        else
        {
            hasPendingPosition = false;
        }
    }

    private void ApplyScheduled()
    {
        scheduledUpdate = null;
        if (!completed)
        {
            ApplyLatest();
        }
    }

    private void ApplyLatest()
    {
        if (!hasPendingPosition)
        {
            return;
        }

        hasPendingPosition = false;
        apply(latestPosition);
    }
}
