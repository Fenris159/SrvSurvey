using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SurfaceMiningOverlaySizingTests
{
    [AvaloniaTheory]
    [InlineData("Wille 2 d")]
    [InlineData("Synuefe NL-N c23-4 B 3 with a much longer body name")]
    public async Task EmptyLiveMiningKeepsEditorWidthAndTrackerSizeAtTwoHundredPercent(string bodyName)
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotSurfaceMining"));
        var example = Assert.IsType<SurfaceMiningOverlayViewModel>(preview.RuntimePresentation!.DataContext);
        using var mining = new SurfaceMiningViewModel(new SystemSurfaceStore(
            Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-sizing-{Guid.NewGuid():N}")));
        var state = new SystemScanState();
        foreach (var json in new[]
        {
            """{"event":"Location","StarSystem":"Test","SystemAddress":42}""",
            $$"""{"event":"Scan","StarSystem":"Test","SystemAddress":42,"BodyName":"{{bodyName}}","BodyID":1,"Radius":1000000,"PlanetClass":"Rocky body"}""",
        })
        {
            Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _));
            state.Apply(entry!);
        }
        await mining.ApplyUpdateAsync(new SurfaceSurveySessionContext("F123", "Test", "Test", 42, null),
            state.CreateSnapshot(), new EliteStatus
            {
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                BodyName = bodyName,
                PlanetRadius = 1_000_000,
                Heading = 263,
            }, "mev_rhino");
        var live = new SurfaceMiningOverlayWindow(new SurfaceMiningOverlayViewModel(mining,
            OverlayPlatformCapabilities.DetectCurrent()));
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(0, 13, 1);
            OverlayThemeResources.Apply(live, LegacyOverlayLayout.Empty, "PlotSurfaceMining");
            OverlayThemeResources.ApplyScale(live, 13, 1);
            preview.Show();
            live.Show();
            using var previewFrame = preview.CaptureRenderedFrame();
            using var liveFrame = live.CaptureRenderedFrame();
            Assert.NotNull(previewFrame);
            Assert.NotNull(liveFrame);
            var output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                using var previewFile = File.Create(Path.Combine(output, "mining-editor-200.png"));
                using var liveFile = File.Create(Path.Combine(output, bodyName == "Wille 2 d"
                    ? "mining-live-short-200.png" : "mining-live-long-200.png"));
                previewFrame.Save(previewFile, PngBitmapEncoderOptions.Default);
                liveFrame.Save(liveFile, PngBitmapEncoderOptions.Default);
            }

            Assert.True(example.SurfaceMining.HasResources);
            Assert.False(mining.HasResources);
            Assert.All(mining.Rigs, rig => Assert.False(rig.IsSet));
            var livePresentation = Assert.Single(live.GetVisualDescendants().OfType<SurfaceMiningOverlayPresentation>());
            Assert.Equal(preview.RuntimePresentation.Bounds.Width, livePresentation.Bounds.Width);
            var previewRadar = Assert.Single(preview.GetVisualDescendants().OfType<SurfaceSurveyRadarControl>());
            var liveRadar = Assert.Single(live.GetVisualDescendants().OfType<SurfaceSurveyRadarControl>());
            Assert.Equal(previewRadar.Bounds.Size, liveRadar.Bounds.Size);
            var previewRigs = preview.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("rig")).ToArray();
            var liveRigs = live.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("rig")).ToArray();
            Assert.Equal(6, liveRigs.Length);
            Assert.Equal(previewRigs.Select(rig => rig.Bounds.Size), liveRigs.Select(rig => rig.Bounds.Size));
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }

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
