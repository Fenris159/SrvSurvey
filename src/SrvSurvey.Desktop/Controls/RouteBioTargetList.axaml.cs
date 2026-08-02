using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Controls;

public sealed partial class RouteBioTargetList : UserControl
{
    internal const int MaxVisibleItemCount = 3;
    private const double MinimumThumbHeight = 12;
    private readonly TranslateTransform scrollThumbTransform = new();
    private bool viewportUpdatePending;

    public static readonly StyledProperty<
        IReadOnlyList<RouteBioTargetItemViewModel>?> ItemsSourceProperty =
        AvaloniaProperty.Register<
            RouteBioTargetList,
            IReadOnlyList<RouteBioTargetItemViewModel>?>(nameof(ItemsSource));

    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<RouteBioTargetList, bool>(
            nameof(IsInteractive));

    static RouteBioTargetList()
    {
        ItemsSourceProperty.Changed.AddClassHandler<RouteBioTargetList>(
            static (control, eventArgs) =>
                control.ApplyItemsSource(
                    eventArgs.NewValue as
                        IReadOnlyList<RouteBioTargetItemViewModel>));
    }

    public RouteBioTargetList()
    {
        InitializeComponent();
        ScrollThumb.RenderTransform = scrollThumbTransform;
        BodyItems.LayoutUpdated += (_, _) => ScheduleViewportUpdate();
        AttachedToVisualTree += (_, _) => ScheduleViewportUpdate();
    }

    public event EventHandler<RouteBioCompletionRequestedEventArgs>?
        CompletionRequested;

    public IReadOnlyList<RouteBioTargetItemViewModel>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    private void ApplyItemsSource(
        IReadOnlyList<RouteBioTargetItemViewModel>? items)
    {
        BodyItems.ItemsSource = items;
        BodyScroller.Offset = default;
        ScheduleViewportUpdate();
    }

    private void ScheduleViewportUpdate()
    {
        if (viewportUpdatePending)
        {
            return;
        }

        viewportUpdatePending = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                viewportUpdatePending = false;
                UpdateViewport();
            },
            DispatcherPriority.Loaded);
    }

    private void UpdateViewport()
    {
        var count = ItemsSource?.Count ?? 0;
        if (count <= MaxVisibleItemCount)
        {
            BodyScroller.MaxHeight = double.PositiveInfinity;
            UpdateScrollIndicator();
            return;
        }

        var visibleHeight = 0d;
        for (var index = 0; index < MaxVisibleItemCount; index++)
        {
            if (BodyItems.ContainerFromIndex(index) is not Control container
                || container.Bounds.Height <= 0)
            {
                ScheduleViewportUpdate();
                return;
            }

            visibleHeight += container.Bounds.Height;
        }

        visibleHeight = Math.Ceiling(visibleHeight);
        if (Math.Abs(BodyScroller.MaxHeight - visibleHeight) > 0.5)
        {
            BodyScroller.MaxHeight = visibleHeight;
        }

        UpdateScrollIndicator();
    }

    private void BodyScroller_ScrollChanged(
        object? sender,
        ScrollChangedEventArgs eventArgs)
    {
        UpdateScrollIndicator();
    }

    private void UpdateScrollIndicator()
    {
        var hasOverflowItems = (ItemsSource?.Count ?? 0)
            > MaxVisibleItemCount;
        ScrollIndicator.IsVisible = hasOverflowItems;
        if (!hasOverflowItems)
        {
            return;
        }

        var extent = BodyScroller.Extent.Height;
        var viewport = BodyScroller.Viewport.Height;
        var overflow = extent - viewport;
        var indicatorHeight = ScrollIndicator.Bounds.Height;
        var isScrollable = extent > 0
            && viewport > 0
            && overflow > 0.5
            && indicatorHeight > 0;
        ScrollThumb.IsVisible = isScrollable;
        if (!isScrollable)
        {
            return;
        }

        var thumbHeight = Math.Clamp(
            indicatorHeight * viewport / extent,
            MinimumThumbHeight,
            indicatorHeight);
        var availableTravel = Math.Max(0, indicatorHeight - thumbHeight);
        var progress = Math.Clamp(BodyScroller.Offset.Y / overflow, 0, 1);
        ScrollThumb.Height = thumbHeight;
        scrollThumbTransform.Y = availableTravel * progress;
    }

    private void RouteBioTargetRow_CompletionRequested(
        object? sender,
        RouteBioCompletionRequestedEventArgs eventArgs)
    {
        CompletionRequested?.Invoke(this, eventArgs);
    }
}
