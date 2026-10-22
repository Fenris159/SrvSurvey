using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Behaviors;

/// <summary>
/// Removes the drawn-decoration translation that Avalonia applies twice when
/// positioning a popup inside the owning window's overlay layer.
/// Tracks the placement target through ancestor scrolling without dismissing its popup.
/// </summary>
internal static class OverlayPopupPlacementBehavior
{
    private static readonly ConditionalWeakTable<Popup, Subscription> Subscriptions = [];
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) == 0)
        {
            Popup.IsOpenProperty.Changed.AddClassHandler<Popup>(OnIsOpenChanged);
            Control.RequestBringIntoViewEvent.AddClassHandler<ComboBox>(
                OnComboBoxBringIntoView,
                RoutingStrategies.Bubble
            );
        }
    }

    private static void OnComboBoxBringIntoView(ComboBox comboBox, RequestBringIntoViewEventArgs eventArgs)
    {
        if (
            comboBox.IsDropDownOpen
            && eventArgs.TargetObject is Visual target
            && !ReferenceEquals(target, comboBox)
            && !comboBox.IsVisualAncestorOf(target)
        )
        {
            // Popup rows already scroll within their own menu. Their focus requests must not
            // reach the page scroller through the ComboBox's logical event route.
            eventArgs.Handled = true;
        }
    }

    internal static Vector GetTopLevelRootOffset(Control placementTarget)
    {
        var topLevel = TopLevel.GetTopLevel(placementTarget);
        Visual? root = topLevel?.GetPresentationSource()?.RootVisual;
        if (topLevel is null || root is null)
        {
            return default;
        }

        Matrix? transform = topLevel.TransformToVisual(root);
        Point origin = transform?.Transform(default) ?? default;
        return new Vector(origin.X, origin.Y);
    }

    private static void OnIsOpenChanged(Popup popup, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.GetNewValue<bool>())
        {
            Subscriptions.GetValue(popup, static value => new Subscription(value)).ApplyCorrection();
            return;
        }

        if (Subscriptions.TryGetValue(popup, out Subscription? existing))
        {
            existing.RestoreOffsets();
        }
    }

    private sealed class Subscription(Popup popup)
    {
        private double originalHorizontalOffset;
        private double originalVerticalOffset;
        private bool correctionApplied;
        private Control? trackedTarget;
        private PixelPoint targetPosition;
        private Size targetSize;

        public void ApplyCorrection()
        {
            RestoreOffsets();
            Control? placementTarget = popup.PlacementTarget ?? popup.FindLogicalAncestorOfType<Control>();
            if (placementTarget is not null && TopLevel.GetTopLevel(placementTarget) is not null)
            {
                trackedTarget = placementTarget;
                targetPosition = placementTarget.PointToScreen(default);
                targetSize = placementTarget.Bounds.Size;
                placementTarget.LayoutUpdated += TargetLayoutUpdated;
            }
            if (!popup.IsUsingOverlayLayer)
            {
                return;
            }

            if (placementTarget is null)
            {
                return;
            }

            Vector offset = GetTopLevelRootOffset(placementTarget);
            if (offset == default)
            {
                return;
            }

            originalHorizontalOffset = popup.HorizontalOffset;
            originalVerticalOffset = popup.VerticalOffset;
            correctionApplied = true;
            popup.SetCurrentValue(Popup.HorizontalOffsetProperty, originalHorizontalOffset - offset.X);
            popup.SetCurrentValue(Popup.VerticalOffsetProperty, originalVerticalOffset - offset.Y);
        }

        public void RestoreOffsets()
        {
            if (trackedTarget is not null)
            {
                trackedTarget.LayoutUpdated -= TargetLayoutUpdated;
                trackedTarget = null;
            }
            if (!correctionApplied)
            {
                return;
            }

            correctionApplied = false;
            popup.SetCurrentValue(Popup.HorizontalOffsetProperty, originalHorizontalOffset);
            popup.SetCurrentValue(Popup.VerticalOffsetProperty, originalVerticalOffset);
        }

        private void TargetLayoutUpdated(object? sender, EventArgs args)
        {
            if (!popup.IsOpen || trackedTarget is not { } target)
            {
                return;
            }
            PixelPoint position = target.PointToScreen(default);
            Size size = target.Bounds.Size;
            if (position == targetPosition && size == targetSize)
            {
                return;
            }
            targetPosition = position;
            targetSize = size;
            // Avalonia only checks the target's own Bounds, missing movement caused by its ancestors.
            // Reapply its anchor through the public placement API, preserving the original value/binding.
            Rect? anchor = popup.PlacementRect;
            popup.SetCurrentValue(Popup.PlacementRectProperty, anchor is null ? new Rect(default, size) : null);
            popup.SetCurrentValue(Popup.PlacementRectProperty, anchor);
        }
    }
}
