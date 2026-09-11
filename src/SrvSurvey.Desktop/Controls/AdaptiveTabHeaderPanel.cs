using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Keeps headers in one row while readable; content typography stays unchanged.</summary>
public sealed class AdaptiveTabHeaderPanel : WrapPanel
{
    public const double PreferredFontSize = 22;
    public const double MinimumFontSize = 14;

    protected override Size MeasureOverride(Size constraint)
    {
        var tabs = Children.OfType<TabItem>().Where(t => t.Header is TextBlock).ToArray();
        if (tabs.Length == 0)
        {
            return base.MeasureOverride(constraint);
        }

        var headers = tabs.Select(t => (TextBlock)t.Header!).ToArray();
        var chrome = new double[tabs.Length];
        for (var i = 0; i < tabs.Length; i++)
        {
            tabs[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            chrome[i] = Math.Max(0, tabs[i].DesiredSize.Width - headers[i].DesiredSize.Width);
        }
        var size = PreferredFontSize;
        // Measure detached text rather than changing live headers repeatedly during layout.
        for (; size > MinimumFontSize; size--)
        {
            double width = 0;
            for (var i = 0; i < headers.Length; i++)
            {
                var header = headers[i];
                var probe = new TextBlock
                {
                    Text = header.Text,
                    FontFamily = header.FontFamily,
                    FontWeight = header.FontWeight,
                    FontStyle = header.FontStyle,
                    FontSize = size,
                };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width += probe.DesiredSize.Width + chrome[i];
            }
            if (width <= constraint.Width)
            {
                break;
            }
        }
        foreach (var header in headers)
        {
            header.FontSize = size;
        }

        return base.MeasureOverride(constraint);
    }
}
