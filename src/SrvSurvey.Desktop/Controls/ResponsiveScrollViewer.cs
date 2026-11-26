using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace SrvSurvey.Desktop.Controls;

/// <summary>
/// Fits scrollable content to the viewport width when wide and switches to horizontal scrolling
/// below <see cref="MinimumContentWidth"/> without binding child width to <see cref="ScrollViewer.Viewport"/>.
/// </summary>
public sealed class ResponsiveScrollViewer : ScrollViewer
{
    private const double VerticalScrollBarReserve = 16;

    public static readonly StyledProperty<double> MinimumContentWidthProperty = AvaloniaProperty.Register<
        ResponsiveScrollViewer,
        double
    >(nameof(MinimumContentWidth), 0d, validate: value => double.IsFinite(value) && value >= 0);

    static ResponsiveScrollViewer()
    {
        AffectsMeasure<ResponsiveScrollViewer>(MinimumContentWidthProperty);
    }

    public double MinimumContentWidth
    {
        get => GetValue(MinimumContentWidthProperty);
        set => SetValue(MinimumContentWidthProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override Size MeasureOverride(Size availableSize)
    {
        double minWidth = MinimumContentWidth;
        if (minWidth > 0 && double.IsFinite(availableSize.Width))
        {
            ApplyResponsiveWidthMode(availableSize.Width, minWidth);
        }

        return base.MeasureOverride(availableSize);
    }

    private void ApplyResponsiveWidthMode(double availableWidth, double minWidth)
    {
        double reserve = VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? 0 : VerticalScrollBarReserve;
        bool useHorizontalScroll = availableWidth < minWidth + reserve;
        ScrollBarVisibility desiredVisibility = useHorizontalScroll
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;
        if (HorizontalScrollBarVisibility != desiredVisibility)
        {
            SetCurrentValue(HorizontalScrollBarVisibilityProperty, desiredVisibility);
        }

        UpdateContentWidth(useHorizontalScroll ? minWidth : double.NaN);
    }

    private void UpdateContentWidth(double desiredWidth)
    {
        if (Content is Layoutable content && !SameWidth(content.Width, desiredWidth))
        {
            content.Width = desiredWidth;
        }
    }

    private static bool SameWidth(double current, double desired) =>
        double.IsNaN(desired) ? double.IsNaN(current) : Math.Abs(current - desired) < 0.01;
}
