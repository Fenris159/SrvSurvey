using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Behaviors;

/// <summary>
/// Removes the drawn-decoration translation that Avalonia applies twice when
/// positioning a popup inside the owning window's overlay layer.
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

        public void ApplyCorrection()
        {
            RestoreOffsets();
            if (!popup.IsUsingOverlayLayer)
            {
                return;
            }

            Control? placementTarget = popup.PlacementTarget ?? popup.FindLogicalAncestorOfType<Control>();
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
            if (!correctionApplied)
            {
                return;
            }

            correctionApplied = false;
            popup.SetCurrentValue(Popup.HorizontalOffsetProperty, originalHorizontalOffset);
            popup.SetCurrentValue(Popup.VerticalOffsetProperty, originalVerticalOffset);
        }
    }
}
