using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Lets the location determine card width, wrapping commodity readouts within that width.</summary>
public sealed class MiningMissionDestinationPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 2)
        {
            return base.MeasureOverride(availableSize);
        }
        Children[0].Measure(new Size(availableSize.Width, double.PositiveInfinity));
        double width = Math.Min(availableSize.Width, Math.Max(240, Children[0].DesiredSize.Width));
        Children[1].Measure(new Size(width, double.PositiveInfinity));
        return new Size(width, Children[0].DesiredSize.Height + 6 + Children[1].DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 2)
        {
            double headerHeight = Children[0].DesiredSize.Height;
            Children[0].Arrange(new Rect(0, 0, finalSize.Width, headerHeight));
            Children[1]
                .Arrange(
                    new Rect(0, headerHeight + 6, finalSize.Width, Math.Max(0, finalSize.Height - headerHeight - 6))
                );
        }
        return finalSize;
    }
}
