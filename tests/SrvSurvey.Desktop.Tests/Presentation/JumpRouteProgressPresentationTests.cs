using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class JumpRouteProgressPresentationTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MonochromeRouteHasAThickDistinctNextJumpAndFilledScoopableMarkers(int targetIndex)
    {
        var model = (JumpInfoOverlayViewModel)OverlayEditorPreviewCatalog.Create("PlotJumpInfo");
        var service = new RavenThemeService(
            Assert.IsType<Application>(Application.Current, exactMatch: false),
            new ThemePreferenceStore(Path.Combine(Path.GetTempPath(), $"SrvSurvey-jump-theme-{Guid.NewGuid():N}.json"))
        );
        LegacyOverlayTheme originalTheme = service.CurrentOverlayTheme;
        Assert.True(OverlayThemePresetCatalog.TryGet("Monochrome Companion", out OverlayThemePreset? preset));
        service.ApplyOverlayTheme(new LegacyOverlayTheme(preset.Colors, true, null));
        var window = new JumpInfoOverlayWindow(model);
        try
        {
            model.JumpInfo.InstallEditorPreview(
                new JumpInfoRoutePlan(
                    new JumpTarget("LP 908-11", 3, "M"),
                    JumpInfoRouteSource.NavRoute,
                    targetIndex,
                    [
                        new("Sol", "Alpha", 30, true, false),
                        new("Alpha", "LP 908-11", 30, false, false),
                        new("LP 908-11", "Beta", 30, true, true),
                    ],
                    null
                ),
                new SystemSummary(
                    "LP 908-11",
                    3,
                    null,
                    null,
                    null,
                    21,
                    21,
                    null,
                    null,
                    null,
                    null,
                    new SystemPoiSummary(2, 2, 25, 62, 0, 0, 0),
                    []
                ),
                [new("So-yeon City", "Material Trader - Encoded · Technology Broker - Guardian")],
                dataStatusText: ""
            );
            OverlayThemeResources.Apply(window);
            window.Show();
            using WriteableBitmap frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            SaveEvidence(frame, targetIndex);
            JumpRouteProgressControl route = Assert.Single(
                window.GetVisualDescendants().OfType<JumpRouteProgressControl>()
            );
            Color target = Assert.IsType<ISolidColorBrush>(route.TargetBrush, exactMatch: false).Color;
            Assert.True(
                target.B - target.R > 60 && target.B > 200,
                $"Next-jump color {target} does not stand apart from the monochrome route."
            );
            Point origin = route.TranslatePoint(default, window)!.Value;
            double segment = (route.Bounds.Width - 16) / 3;
            Point middle = origin + new Point(8 + (targetIndex + 0.5) * segment, route.Bounds.Height / 2);
            int thickness = Enumerable
                .Range(-5, 11)
                .Count(offset => IsCyan(Pixel(frame, middle + new Point(0, offset))));
            Assert.True(thickness >= 5, $"The active jump has only {thickness} clearly colored pixels of thickness.");
            Point diamond = origin + new Point(8 + (targetIndex + 1) * segment, route.Bounds.Height / 2 - 5);
            Assert.True(
                IsCyan(Pixel(frame, diamond)),
                "The active destination does not have the taller diamond marker."
            );
            Assert.True(
                IsCyan(Pixel(frame, diamond + new Point(0, 5))),
                "A following route leg covers the active diamond's center."
            );
            Point scoopable = origin + new Point(8 + 3 * segment, route.Bounds.Height / 2 - 12);
            Color fuel = Pixel(frame, scoopable);
            Assert.True(fuel.G > 180 && fuel.G > fuel.R, $"The scoopable marker center is not visibly filled: {fuel}.");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Scoopable star");
        }
        finally
        {
            window.Close();
            model.Dispose();
            model.JumpInfo.Dispose();
            service.ApplyOverlayTheme(originalTheme);
        }
    }

    private static bool IsCyan(Color color) => color.B > 200 && color.B - color.R > 60;

    private static Color Pixel(WriteableBitmap bitmap, Point point)
    {
        using ILockedFramebuffer buffer = bitmap.Lock();
        int x = (int)Math.Round(point.X);
        int y = (int)Math.Round(point.Y);
        byte[] value = new byte[4];
        Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, value, 0, value.Length);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(value[3], value[0], value[1], value[2])
            : Color.FromArgb(value[3], value[2], value[1], value[0]);
    }

    private static void SaveEvidence(WriteableBitmap frame, int targetIndex)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            frame.Save(
                Path.Combine(output, $"next-jump-monochrome-{targetIndex}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
    }
}
