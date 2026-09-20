using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Configuration;
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
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);

        PixelSize size = OverlayWindowMetrics.PrepareForPlacement(window, layout, "PlotSysStatus", 1.5d);

        Assert.Equal(new PixelSize(210, 210), size);
        Assert.Equal(
            new PixelPoint(120, 770),
            OverlayWindowPlacement.BottomLeft(new PixelRect(100, 200, 1200, 800), size)
        );
    }

    [AvaloniaFact]
    public void RelativeOverlayScaleUsesOperatingSystemScaleAsItsBaseline()
    {
        var window = new Window
        {
            Width = double.NaN,
            Height = double.NaN,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border(),
        };
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        layout.SetScaleIndex(OverlayScaleCatalog.GetIndex(100));

        PixelSize size = OverlayWindowMetrics.PrepareForPlacement(window, layout, "PlotSysStatus", 1.5d);

        Assert.Equal(new PixelSize(420, 420), size);
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

        PixelSize size = OverlayWindowMetrics.GetPixelSize(new RegisteredOverlayWindow(window, "PlotSysStatus"));

        Assert.Equal(new PixelSize(210, 210), size);
    }
}
