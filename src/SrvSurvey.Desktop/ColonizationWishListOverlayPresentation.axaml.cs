using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop;

public sealed partial class ColonizationWishListOverlayPresentation : UserControl
{
    private Window? host;

    public ColonizationWishListOverlayPresentation()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        host = TopLevel.GetTopLevel(this) as Window;
        if (host is not null)
        {
            host.PositionChanged += OnHostPositionChanged;
            host.ScalingChanged += OnHostGeometryChanged;
            host.Screens.Changed += OnHostGeometryChanged;
        }
        LayoutUpdated += OnHostGeometryChanged;
        UpdateHeightLimit();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (host is not null)
        {
            host.PositionChanged -= OnHostPositionChanged;
            host.ScalingChanged -= OnHostGeometryChanged;
            host.Screens.Changed -= OnHostGeometryChanged;
            host = null;
        }
        LayoutUpdated -= OnHostGeometryChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPositionChanged(object? sender, PixelPointEventArgs e) => UpdateHeightLimit();

    private void OnHostGeometryChanged(object? sender, EventArgs e) => UpdateHeightLimit();

    /// <summary>Caps the entire panel at half its monitor, including display and overlay scaling.</summary>
    private void UpdateHeightLimit()
    {
        Screen? screen = host?.Screens.ScreenFromWindow(host) ?? host?.Screens.Primary;
        double height = screen?.Bounds.Height ?? 1080;
        double scale = host?.RenderScaling ?? 1;
        foreach (LayoutTransformControl ancestor in this.GetVisualAncestors().OfType<LayoutTransformControl>())
        {
            scale *= Math.Abs(ancestor.LayoutTransform?.Value.M22 ?? 1);
        }
        double limit = height / (2 * Math.Max(0.01, scale));
        if (Math.Abs(MaxHeight - limit) > 0.01)
        {
            MaxHeight = limit;
        }
    }
}
