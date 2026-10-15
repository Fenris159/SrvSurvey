using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Draws notification expiry without making the fill width participate in content-sized layout.</summary>
public sealed class NotificationLifetimeBar : Control
{
    public static readonly StyledProperty<double> PercentProperty = AvaloniaProperty.Register<
        NotificationLifetimeBar,
        double
    >(nameof(Percent));
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<
        NotificationLifetimeBar,
        IBrush?
    >(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<
        NotificationLifetimeBar,
        IBrush?
    >(nameof(FillBrush));

    /// <summary>Invalidates only painting when expiry or theme brushes change.</summary>
    static NotificationLifetimeBar() =>
        AffectsRender<NotificationLifetimeBar>(PercentProperty, TrackBrushProperty, FillBrushProperty);

    /// <summary>The remaining lifetime, clamped during rendering to a percentage of the existing bounds.</summary>
    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }
    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }
    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>Paints the fixed-size track and its remaining fraction without resizing a template child.</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double fraction = double.IsFinite(Percent) ? Math.Clamp(Percent, 0, 100) / 100 : 0;
        context.DrawRectangle(TrackBrush, null, new Rect(Bounds.Size));
        context.DrawRectangle(FillBrush, null, new Rect(0, 0, Bounds.Width * fraction, Bounds.Height));
    }
}
