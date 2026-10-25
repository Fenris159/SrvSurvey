using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class JumpInfoOverlaySizingTests
{
    [AvaloniaFact]
    public void NextJumpRolesMatchTheEditorCategories()
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotJumpInfo"));
        try
        {
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            TextBlock caption = preview
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "Route and destination intelligence");
            Assert.Contains("type-caption", caption.Classes);
            foreach (string label in new[] { "Next jump", "Scoopable star" })
            {
                Assert.Contains(
                    "type-header",
                    preview.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == label).Classes
                );
            }
        }
        finally
        {
            preview.Close();
        }
    }

    /// <summary>Verifies that editor resizing changes Next Jump width while content determines its height.</summary>
    [AvaloniaFact]
    public void EditorResizeChangesWidthWhileHeightFollowsContent()
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotJumpInfo"));
        try
        {
            preview.ConfigureSize(new(760, 600));
            preview.Show();
            using WriteableBitmap? initial = preview.CaptureRenderedFrame();
            Control panel = preview.RuntimePresentation!;
            Size original = panel.Bounds.Size;
            Thumb grip = preview.FindControl<Thumb>("PanelResizeThumb")!;
            Point start = grip.TranslatePoint(new Point(11, 11), preview)!.Value;
            preview.MouseMove(start, RawInputModifiers.None);
            preview.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(start + new Vector(0, 120), RawInputModifiers.LeftMouseButton);
            using WriteableBitmap? vertical = preview.CaptureRenderedFrame();
            Assert.Equal(original, panel.Bounds.Size);
            preview.MouseMove(start + new Vector(100, 120), RawInputModifiers.LeftMouseButton);
            using WriteableBitmap? horizontal = preview.CaptureRenderedFrame();
            Assert.InRange(Math.Abs(panel.Bounds.Width - original.Width - 100), 0, 1);
            Assert.True(double.IsNaN(panel.Height));
            preview.MouseUp(start + new Vector(100, 120), MouseButton.Left, RawInputModifiers.None);
        }
        finally
        {
            preview.Close();
        }
    }

    /// <summary>Verifies that changing destination details resizes panel height without changing its configured width.</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HeightExpandsAndContractsWithContentWhileKeepingTheConfiguredWidth(bool combined, bool savedSize)
    {
        const string plotter = "PlotJumpInfo";
        var model = (JumpInfoOverlayViewModel)OverlayEditorPreviewCatalog.Create(plotter);
        var window = new JumpInfoOverlayWindow(model);
        var host = new CombinedOverlayWindow { Width = 1200, Height = 1000 };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = new(
                    LegacyHorizontalAnchor.Left,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    SizeOverride: savedSize ? new(760, 600) : null
                ),
            },
            null,
            null
        );
        try
        {
            OverlayThemeResources.Apply(window, layout, plotter, new OverlayWindowRegistry());
            Window surface = window;
            if (combined)
            {
                Control content = Assert.IsType<Control>(window.Content, exactMatch: false);
                window.Content = null;
                host.Add(new ContentControl { Content = content, DataContext = model });
                OverlayThemeResources.Apply(host);
                surface = host;
            }
            surface.Show();
            InstallDetails(model, 8);
            using WriteableBitmap? expanded = surface.CaptureRenderedFrame();
            JumpInfoOverlayPresentation panel = Assert.Single(
                surface.GetVisualDescendants().OfType<JumpInfoOverlayPresentation>()
            );
            double expandedHeight = panel.Bounds.Height;
            double width = panel.Bounds.Width;
            InstallDetails(model, 0);
            using WriteableBitmap? compact = surface.CaptureRenderedFrame();
            Assert.True(
                panel.Bounds.Height < expandedHeight - 80,
                $"Removing detail rows left the panel at {panel.Bounds.Height}, previously {expandedHeight}."
            );
            Assert.Equal(width, panel.Bounds.Width);
            Assert.Equal(savedSize ? 760 : 600, width);
            InstallDetails(model, 8);
            using WriteableBitmap? restored = surface.CaptureRenderedFrame();
            Assert.InRange(Math.Abs(panel.Bounds.Height - expandedHeight), 0, 1);
        }
        finally
        {
            host.Close();
            window.Close();
            model.Dispose();
            model.JumpInfo.Dispose();
        }
    }

    /// <summary>Installs destination-detail fixtures for overlay measurement and resizing checks.</summary>
    private static void InstallDetails(JumpInfoOverlayViewModel model, int count)
    {
        model.JumpInfo.InstallEditorPreview(
            new JumpInfoRoutePlan(new JumpTarget("LP 908-11", 3, "M"), JumpInfoRouteSource.Direct, 0, [], null),
            new SystemSummary(
                "LP 908-11",
                3,
                null,
                null,
                null,
                0,
                0,
                null,
                null,
                null,
                null,
                new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
                []
            ),
            Enumerable
                .Range(0, count)
                .Select(index => new JumpInfoDetailLineViewModel(
                    "Station " + index,
                    "Material Trader - Encoded · Technology Broker - Guardian"
                ))
                .ToArray(),
            dataStatusText: ""
        );
    }

    /// <summary>Verifies that a long lookup warning wraps within both live and editor-preview widths.</summary>
    [AvaloniaFact]
    public void LongLookupWarningCannotExpandRuntimeOrPreviewWidth()
    {
        var window = new JumpInfoOverlayWindow();
        JumpInfoOverlayViewModel model = Assert.IsType<JumpInfoOverlayViewModel>(window.DataContext);
        model.JumpInfo.InstallEditorPreview(
            new JumpInfoRoutePlan(new JumpTarget("Beta", 3), JumpInfoRouteSource.Direct, 0, [], null),
            new SystemSummary(
                "Beta",
                3,
                null,
                null,
                null,
                0,
                0,
                null,
                null,
                null,
                null,
                new SystemPoiSummary(0, 0, 0, 0, 0, 0, 0),
                []
            ),
            [],
            dataStatusText: string.Concat(
                Enumerable.Repeat("EDSM data is unavailable: a very long network error. ", 20)
            )
        );
        var preview = new JumpInfoOverlayPresentation { DataContext = model };
        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.Equal(600, preview.DesiredSize.Width);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(600, window.Bounds.Width);
            OverlayThemeResources.ApplyScale(window, 13, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1200, window.Bounds.Width);
        }
        finally
        {
            window.Close();
            model.Dispose();
            model.JumpInfo.Dispose();
        }
    }
}
