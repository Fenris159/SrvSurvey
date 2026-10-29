using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Wraps readout sections into columns while keeping each row's natural height.</summary>
public sealed class ResponsiveColumnsPanel : Panel
{
    public static readonly StyledProperty<double> MinimumColumnWidthProperty = AvaloniaProperty.Register<
        ResponsiveColumnsPanel,
        double
    >(nameof(MinimumColumnWidth), 340, validate: value => double.IsFinite(value) && value > 0);

    public static readonly StyledProperty<int> MaximumColumnsProperty = AvaloniaProperty.Register<
        ResponsiveColumnsPanel,
        int
    >(nameof(MaximumColumns), 2, validate: value => value > 0);

    public static readonly StyledProperty<double> SpacingProperty = AvaloniaProperty.Register<
        ResponsiveColumnsPanel,
        double
    >(nameof(Spacing), 20, validate: value => double.IsFinite(value) && value >= 0);

    static ResponsiveColumnsPanel()
    {
        AffectsMeasure<ResponsiveColumnsPanel>(MinimumColumnWidthProperty, MaximumColumnsProperty, SpacingProperty);
    }

    public double MinimumColumnWidth
    {
        get => GetValue(MinimumColumnWidthProperty);
        set => SetValue(MinimumColumnWidthProperty, value);
    }

    public int MaximumColumns
    {
        get => GetValue(MaximumColumnsProperty);
        set => SetValue(MaximumColumnsProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Measures visible sections in responsive rows, using each row's tallest section for its height.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Control[] children = Children.Where(child => child.IsVisible).ToArray();
        int columns = GetColumns(availableSize.Width);
        double height = 0;
        double width = 0;
        for (int start = 0; start < children.Length; start += columns)
        {
            int count = Math.Min(columns, children.Length - start);
            double cellWidth = Math.Max(0, (availableSize.Width - Spacing * (count - 1)) / count);
            double rowHeight = 0;
            double rowWidth = Spacing * (count - 1);
            for (int index = start; index < start + count; index++)
            {
                children[index].Measure(new Size(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
                rowWidth += children[index].DesiredSize.Width;
            }
            height += rowHeight + (start == 0 ? 0 : Spacing);
            width = Math.Max(width, rowWidth);
        }
        return new Size(width, height);
    }

    /// <summary>Arranges visible sections into equal-width columns, stretching the final partial row to fit.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Control[] children = Children.Where(child => child.IsVisible).ToArray();
        int columns = GetColumns(finalSize.Width);
        double top = 0;
        for (int start = 0; start < children.Length; start += columns)
        {
            int count = Math.Min(columns, children.Length - start);
            double cellWidth = Math.Max(0, (finalSize.Width - Spacing * (count - 1)) / count);
            double rowHeight = children.Skip(start).Take(count).Max(child => child.DesiredSize.Height);
            for (int index = 0; index < count; index++)
            {
                children[start + index].Arrange(new Rect(index * (cellWidth + Spacing), top, cellWidth, rowHeight));
            }
            top += rowHeight + Spacing;
        }
        return finalSize;
    }

    /// <summary>Chooses a bounded column count from the available width and the minimum section width.</summary>
    private int GetColumns(double width) =>
        double.IsPositiveInfinity(width)
            ? MaximumColumns
            : Math.Clamp((int)((width + Spacing) / (MinimumColumnWidth + Spacing)), 1, MaximumColumns);
}
