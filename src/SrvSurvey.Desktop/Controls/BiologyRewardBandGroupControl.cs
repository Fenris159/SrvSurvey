using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

/// <summary>
/// Draws the legacy dotted frame around the reward PIPs that correspond to
/// the body's reported biological signal count. Alternative predictions are
/// intentionally arranged outside this control by the presentation.
/// </summary>
public sealed class BiologyRewardBandGroupControl : Decorator
{
    public static readonly StyledProperty<IBrush?> FrameBrushProperty =
        AvaloniaProperty.Register<BiologyRewardBandGroupControl, IBrush?>(
            nameof(FrameBrush));

    private const double HorizontalInset = 2;
    private const double VerticalInset = 1;

    static BiologyRewardBandGroupControl()
    {
        AffectsRender<BiologyRewardBandGroupControl>(FrameBrushProperty);
    }

    public IBrush? FrameBrush
    {
        get => GetValue(FrameBrushProperty);
        set => SetValue(FrameBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            return new Size(HorizontalInset * 2, VerticalInset * 2);
        }

        var childAvailable = new Size(
            Math.Max(0, availableSize.Width - HorizontalInset * 2),
            Math.Max(0, availableSize.Height - VerticalInset * 2));
        Child.Measure(childAvailable);
        return new Size(
            Child.DesiredSize.Width + HorizontalInset * 2,
            Child.DesiredSize.Height + VerticalInset * 2);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(
            HorizontalInset,
            VerticalInset,
            Math.Max(0, finalSize.Width - HorizontalInset * 2),
            Math.Max(0, finalSize.Height - VerticalInset * 2)));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 1 || Bounds.Height <= 1 || FrameBrush is null)
        {
            return;
        }

        var frame = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
        context.DrawRectangle(
            Brushes.Transparent,
            new Pen(
                FrameBrush,
                1,
                DashStyle.Dot,
                PenLineCap.Round,
                PenLineJoin.Round),
            frame,
            2,
            2);
    }
}
