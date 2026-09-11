using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Behaviors;

public static class HorizontalScrollInputBehavior
{
    private const double FallbackWheelStep = 50;
    private static readonly ConditionalWeakTable<ScrollViewer, Subscription>
        Subscriptions = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, ScrollViewer, bool>(
            "Enabled",
            defaultValue: false);

    static HorizontalScrollInputBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnEnabledChanged);
    }

    public static void SetEnabled(ScrollViewer target, bool value)
    {
        target.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(ScrollViewer target)
    {
        return target.GetValue(EnabledProperty);
    }

    private static void OnEnabledChanged(
        ScrollViewer scrollViewer,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.NewValue is true)
        {
            Subscriptions.GetValue(
                scrollViewer,
                static target => new Subscription(target));
            return;
        }

        if (Subscriptions.TryGetValue(scrollViewer, out var subscription))
        {
            subscription.Dispose();
            Subscriptions.Remove(scrollViewer);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ScrollViewer scrollViewer;

        public Subscription(ScrollViewer scrollViewer)
        {
            this.scrollViewer = scrollViewer;
            scrollViewer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnPointerWheelChanged,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            scrollViewer.AddHandler(
                InputElement.ScrollGestureEvent,
                OnScrollGesture,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        public void Dispose()
        {
            scrollViewer.RemoveHandler(
                InputElement.PointerWheelChangedEvent,
                OnPointerWheelChanged);
            scrollViewer.RemoveHandler(
                InputElement.ScrollGestureEvent,
                OnScrollGesture);
        }

        private void OnPointerWheelChanged(
            object? sender,
            PointerWheelEventArgs eventArgs)
        {
            var horizontalDelta = eventArgs.Delta.X;
            var usesShiftFallback = false;
            if (Math.Abs(horizontalDelta) < double.Epsilon
                && eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                horizontalDelta = eventArgs.Delta.Y;
                usesShiftFallback = true;
            }

            if (Math.Abs(horizontalDelta) < double.Epsilon
                || (!eventArgs.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    && Math.Abs(horizontalDelta) < Math.Abs(eventArgs.Delta.Y)))
            {
                return;
            }

            if (!usesShiftFallback
                && scrollViewer.FlowDirection == FlowDirection.RightToLeft)
            {
                horizontalDelta = -horizontalDelta;
            }

            var wheelStep = scrollViewer.SmallChange.Width > 0
                ? scrollViewer.SmallChange.Width
                : FallbackWheelStep;
            ForwardNestedHorizontalInput(
                eventArgs.Source,
                -horizontalDelta * wheelStep,
                eventArgs);
        }

        private void OnScrollGesture(
            object? sender,
            ScrollGestureEventArgs eventArgs)
        {
            if (Math.Abs(eventArgs.Delta.X) < double.Epsilon
                || Math.Abs(eventArgs.Delta.X) < Math.Abs(eventArgs.Delta.Y))
            {
                return;
            }

            var horizontalDelta = scrollViewer.FlowDirection
                == FlowDirection.RightToLeft
                ? -eventArgs.Delta.X
                : eventArgs.Delta.X;
            ForwardNestedHorizontalInput(
                eventArgs.Source,
                horizontalDelta,
                eventArgs);
        }

        private void ForwardNestedHorizontalInput(
            object? eventSource,
            double horizontalDelta,
            RoutedEventArgs eventArgs)
        {
            if (eventSource is not Visual source)
            {
                return;
            }

            var viewers = (source is ScrollViewer sourceScroller
                    ? new[] { sourceScroller }.Concat(
                        source.GetVisualAncestors().OfType<ScrollViewer>())
                    : source.GetVisualAncestors().OfType<ScrollViewer>())
                .ToArray();
            var nearestViewer = viewers.FirstOrDefault();
            var nearestHorizontalViewer = viewers.FirstOrDefault(
                CanScrollHorizontally);
            if (nearestHorizontalViewer is null
                || ReferenceEquals(nearestViewer, nearestHorizontalViewer)
                || !ReferenceEquals(scrollViewer, nearestHorizontalViewer))
            {
                return;
            }

            var maximumOffset = Math.Max(
                0,
                scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
            var requestedOffset = Math.Clamp(
                scrollViewer.Offset.X + horizontalDelta,
                0,
                maximumOffset);
            var moved = Math.Abs(requestedOffset - scrollViewer.Offset.X)
                >= double.Epsilon;
            scrollViewer.Offset = new Vector(
                requestedOffset,
                scrollViewer.Offset.Y);
            eventArgs.Handled = true;
            if (eventArgs is ScrollGestureEventArgs scrollGestureEventArgs)
            {
                scrollGestureEventArgs.ShouldEndScrollGesture = !moved;
            }
        }

        private static bool CanScrollHorizontally(ScrollViewer candidate) =>
            candidate.HorizontalScrollBarVisibility
                != ScrollBarVisibility.Disabled
            && candidate.Extent.Width > candidate.Viewport.Width;
    }
}
