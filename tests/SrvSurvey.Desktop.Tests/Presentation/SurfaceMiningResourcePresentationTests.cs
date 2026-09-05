using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SurfaceMiningResourcePresentationTests
{
    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(21)]
    public void ResourcesFillTwoColumnsAndKeepOverflowAccessible(int count)
    {
        var viewModel = OverlayEditorPreviewFactories.CreateSurfaceMining();
        using var mining = viewModel.SurfaceMining;
        var examples = mining.Resources.Select(resource => resource.Marker).ToArray();
        mining.InstallEditorPreview(mining.RadarMarkers.Where(marker => marker.Kind == SurfaceRadarMarkerKind.MiningRig).ToArray(),
            Enumerable.Range(0, count).Select(index => examples[index % examples.Length]).ToArray());
        var service = new RavenThemeService(Assert.IsAssignableFrom<Application>(Application.Current),
            new ThemePreferenceStore(Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-theme-{Guid.NewGuid():N}.json")));
        Assert.True(OverlayThemePresetCatalog.TryGet("Monochrome Companion", out var preset));
        service.ApplyOverlayTheme(new LegacyOverlayTheme(preset.Colors, true, null));
        var window = new SurfaceMiningOverlayWindow(viewModel);
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var cells = window.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("resource")).ToArray();
            Assert.Equal(count, cells.Length);
            var first = cells[0].TranslatePoint(default, window)!.Value;
            var second = cells[1].TranslatePoint(default, window)!.Value;
            var third = cells[2].TranslatePoint(default, window)!.Value;
            Assert.Equal(first.Y, second.Y);
            Assert.True(second.X > first.X);
            Assert.Equal(first.X, third.X);
            Assert.True(third.Y > first.Y);
            Assert.Equal(cells[0].Bounds.Width, cells[1].Bounds.Width);
            var chevrons = cells.Select(cell => Assert.Single(cell.GetVisualDescendants()
                .OfType<SrvSurvey.Desktop.Controls.DirectionalChevronControl>())).ToArray();
            Assert.True(chevrons[0].IsFar);
            Assert.False(chevrons[1].IsFar);
            Assert.NotEqual(chevrons[0].Stroke, chevrons[1].Stroke);
            var scroll = Assert.Single(window.GetVisualDescendants().OfType<ScrollViewer>());
            if (count == 21)
            {
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            }
            var cargo = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                block => block.Text == mining.CargoText);
            Assert.True(cargo.TranslatePoint(default, window)!.Value.Y + cargo.Bounds.Height <= window.Bounds.Height);
            var output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                using var stream = File.Create(Path.Combine(output, $"mining-resources-{count}.png"));
                frame.Save(stream, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            service.ApplyOverlayTheme(LegacyOverlayThemeStore.CreateDefault());
        }
    }
}
