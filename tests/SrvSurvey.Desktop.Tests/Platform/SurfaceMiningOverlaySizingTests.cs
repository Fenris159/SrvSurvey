using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SurfaceMiningOverlaySizingTests
{
    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 1)]
    [InlineData(10, 1.5)]
    [InlineData(19, 2)]
    [InlineData(25, 1.25)]
    public void LiveMiningPresentationMatchesEditorAtTheSameScale(int scaleIndex, double renderScaling)
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotSurfaceMining"));
        var presentation = Assert.IsType<SurfaceMiningOverlayPresentation>(preview.RuntimePresentation);
        var viewModel = Assert.IsType<SurfaceMiningOverlayViewModel>(presentation.DataContext);
        var live = new SurfaceMiningOverlayWindow(viewModel);
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(0, scaleIndex, renderScaling);
            OverlayThemeResources.Apply(live, LegacyOverlayLayout.Empty, "PlotSurfaceMining");
            OverlayThemeResources.ApplyScale(live, scaleIndex, renderScaling);
            preview.Show();
            live.Show();
            AssertMatchingPresentations(preview, live);

            var mining = viewModel.SurfaceMining;
            var rigs = mining.RadarMarkers.Where(marker => marker.Kind == SurfaceRadarMarkerKind.MiningRig).ToArray();
            var resources = mining.Resources.Select(resource => resource.Marker).ToArray();
            foreach (var count in new[] { 0, 21, 3 })
            {
                mining.InstallEditorPreview(rigs,
                    Enumerable.Range(0, count).Select(index => resources[index % resources.Length]).ToArray());
                AssertMatchingPresentations(preview, live);
            }

            // A settings change must resize both existing hosts without accumulating scale.
            preview.ConfigureScale(0, 1, renderScaling);
            OverlayThemeResources.ApplyScale(live, 1, renderScaling);
            AssertMatchingPresentations(preview, live);
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }

    private static void AssertMatchingPresentations(OverlayPositionPreviewWindow preview, SurfaceMiningOverlayWindow live)
    {
        using var previewFrame = preview.CaptureRenderedFrame();
        using var liveFrame = live.CaptureRenderedFrame();
        Assert.NotNull(previewFrame);
        Assert.NotNull(liveFrame);
        var previewPresentation = Assert.IsType<SurfaceMiningOverlayPresentation>(preview.RuntimePresentation);
        var livePresentation = Assert.Single(live.GetVisualDescendants().OfType<SurfaceMiningOverlayPresentation>());
        // Compare the shared content, excluding the editor-only folder tab and border.
        var previewBounds = new Rect(previewPresentation.Bounds.Size)
            .TransformToAABB(previewPresentation.TransformToVisual(preview)!.Value);
        var liveBounds = new Rect(livePresentation.Bounds.Size)
            .TransformToAABB(livePresentation.TransformToVisual(live)!.Value);
        Assert.InRange(Math.Abs(previewBounds.Width - liveBounds.Width), 0, 1);
        Assert.InRange(Math.Abs(previewBounds.Height - liveBounds.Height), 0, 1);
    }
}
