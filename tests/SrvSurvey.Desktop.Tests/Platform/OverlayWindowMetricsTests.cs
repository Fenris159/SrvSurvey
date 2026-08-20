using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayWindowMetricsTests
{
    [AvaloniaFact]
    public void UnmeasuredContentSizedWindowUsesPositiveScaledCatalogFallback()
    {
        var window = new Window
        {
            Width = double.NaN,
            Height = double.NaN,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border(),
        };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(),
            null,
            null);

        var size = OverlayWindowMetrics.PrepareForPlacement(
            window,
            layout,
            "PlotSysStatus",
            1.5d);

        Assert.Equal(new PixelSize(210, 210), size);
        Assert.Equal(
            new PixelPoint(120, 770),
            OverlayWindowPlacement.BottomLeft(
                new PixelRect(100, 200, 1200, 800),
                size));
    }

    [AvaloniaFact]
    public void AbsoluteOverlayScaleKeepsFallbackIndependentOfMonitorScaling()
    {
        var window = new Window
        {
            Width = double.NaN,
            Height = double.NaN,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border(),
        };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>(),
            null,
            null);
        layout.SetScaleIndex(1);

        var size = OverlayWindowMetrics.PrepareForPlacement(
            window,
            layout,
            "PlotSysStatus",
            1.5d);

        Assert.Equal(new PixelSize(140, 140), size);
    }

    [AvaloniaFact]
    public void UnmeasuredRegisteredWindowUsesRenderScaledCatalogFallback()
    {
        var window = new Window
        {
            Width = double.NaN,
            Height = double.NaN,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border(),
        };
        window.SetRenderScaling(1.5d);

        var size = OverlayWindowMetrics.GetPixelSize(
            new RegisteredOverlayWindow(window, "PlotSysStatus"));

        Assert.Equal(new PixelSize(210, 210), size);
    }
}
