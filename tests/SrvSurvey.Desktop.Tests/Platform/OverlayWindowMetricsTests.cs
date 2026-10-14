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
    /// <summary>Placement uses the rendered panel's monitor scale rather than the game monitor's scale.</summary>
    [AvaloniaTheory]
    [InlineData(1.5, 1.0, false)]
    [InlineData(1.0, 1.5, false)]
    [InlineData(1.5, 1.0, true)]
    [InlineData(1.0, 1.5, true)]
    public void PlacementMatchesRenderedFootprintOnAnotherMonitor(
        double panelScaling,
        double gameScaling,
        bool legacyScale
    )
    {
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired("PlotBuildCommodities");
        var preview = new OverlayPositionPreviewWindow(definition);
        var live = new ColonizationCommodityOverlayWindow(
            (SrvSurvey.Desktop.ViewModels.ColonizationCommodityOverlayViewModel)
                preview.RuntimePresentation!.DataContext!
        );
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [definition.Name] = definition.DefaultPlacement with
                {
                    ScaleIndex = legacyScale ? 7 : OverlayScaleCatalog.GetIndex(50),
                },
            },
            null,
            null
        );
        try
        {
            live.SetRenderScaling(panelScaling);
            preview.SetRenderScaling(panelScaling);
            OverlayThemeResources.Apply(live, layout, definition.Name, new OverlayWindowRegistry());
            OverlayThemeResources.Apply(preview);
            preview.ConfigureScale(layout.ScaleIndex, layout.GetScaleIndex(definition.Name), gameScaling);
            preview.ConfigureTypography(OverlayTypographyScale.Default);
            preview.ConfigureSize(null);
            live.Show();
            preview.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            live.UpdateLayout();
            preview.UpdateLayout();
            PixelSize measured = OverlayWindowMetrics.PrepareForPlacement(live, layout, definition.Name, gameScaling);
            live.UpdateLayout();
            PixelSize actual = OverlayWindowMetrics.GetPixelSize(new RegisteredOverlayWindow(live, definition.Name));
            Assert.InRange(Math.Abs(measured.Width - actual.Width), 0, 1);
            Assert.InRange(Math.Abs(measured.Height - actual.Height), 0, 1);
            PixelSize expected = preview.GetPanelMetrics(panelScaling).PanelSize;
            Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, 1);
            Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, 1);
            live.SetRenderScaling(2);
            preview.SetRenderScaling(2);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            measured = OverlayWindowMetrics.PrepareForPlacement(live, layout, definition.Name, gameScaling);
            preview.UpdateLayout();
            expected = preview.GetPanelMetrics(2).PanelSize;
            Assert.InRange(Math.Abs(measured.Width - expected.Width), 0, 1);
            Assert.InRange(Math.Abs(measured.Height - expected.Height), 0, 1);
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }

    /// <summary>Combined panels use their visible host DPI even when their source window belongs to a different monitor.</summary>
    [AvaloniaTheory]
    [InlineData(1.0, 1.5)]
    [InlineData(1.5, 1.0)]
    public void CombinedPanelMetricsFollowTheVisibleRenderingSurface(double sourceScaling, double hostScaling)
    {
        const string plotter = "PlotBuildCommodities";
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotter));
        var source = new ColonizationCommodityOverlayWindow(
            (SrvSurvey.Desktop.ViewModels.ColonizationCommodityOverlayViewModel)
                preview.RuntimePresentation!.DataContext!
        );
        var host = new CombinedOverlayWindow { Width = 1800, Height = 1800 };
        var layout = new LegacyOverlayLayout(new Dictionary<string, LegacyOverlayPlacement>(), null, null);
        layout.SetScaleIndex(7);
        try
        {
            source.SetRenderScaling(sourceScaling);
            host.SetRenderScaling(hostScaling);
            preview.SetRenderScaling(hostScaling);
            OverlayThemeResources.Apply(source, layout, plotter, new OverlayWindowRegistry());
            OverlayThemeResources.Apply(host);
            OverlayThemeResources.Apply(preview);
            preview.ConfigureScale(7, null, sourceScaling);
            preview.ConfigureTypography(OverlayTypographyScale.Default);
            preview.ConfigureSize(null);
            Control content = Assert.IsType<Control>(source.Content, exactMatch: false);
            source.Content = new Border();
            host.Add(new ContentControl { Content = content, DataContext = source.DataContext });
            host.Show();
            preview.Show();
            OverlayThemeResources.ApplyOpacity(source, layout, plotter);
            host.UpdateLayout();
            preview.UpdateLayout();
            PixelSize measured = OverlayWindowMetrics.GetPixelSize(
                new RegisteredOverlayWindow(source, plotter, content)
            );
            var actual = new PixelSize(
                (int)Math.Ceiling(content.Bounds.Width * hostScaling),
                (int)Math.Ceiling(content.Bounds.Height * hostScaling)
            );
            PixelSize expected = preview.GetPanelMetrics(hostScaling).PanelSize;
            Assert.Equal(actual, measured);
            Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, 1);
            Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, 1);
        }
        finally
        {
            host.Close();
            source.Close();
            preview.Close();
        }
    }

    /// <summary>Unmeasured content-sized windows retain a positive footprint at the requested display scale.</summary>
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
