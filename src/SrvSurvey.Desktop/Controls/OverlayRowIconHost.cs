using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Reserves an icon's width while letting row text determine height and oversized icons overflow vertically.</summary>
public sealed class OverlayRowIconHost : Decorator
{
    /// <summary>Measures the full icon without contributing its height to the commodity row.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child?.DesiredSize.Width ?? 0, 0);
    }

    /// <summary>Centers the measured icon vertically without shrinking it to the row's height.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child)
        {
            child.Arrange(
                new Rect(
                    0,
                    (finalSize.Height - child.DesiredSize.Height) / 2,
                    finalSize.Width,
                    child.DesiredSize.Height
                )
            );
        }

        return finalSize;
    }
}
