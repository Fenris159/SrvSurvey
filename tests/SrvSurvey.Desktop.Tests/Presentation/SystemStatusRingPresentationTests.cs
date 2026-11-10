using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SystemStatusRingPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveAndEditorRenderConcentricBordersAndFollowGeneralBrushChanges(bool editor)
    {
        Application application = Assert.IsAssignableFrom<Application>(Application.Current);
        var themeService = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "theme.json"))
        );
        LegacyOverlayTheme defaultTheme = LegacyOverlayThemeStore.CreateDefault();
        var comparisonColors = defaultTheme.Colors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal
        );
        comparisonColors["orangeDark"] = Color.Parse("#343A40");
        comparisonColors["black"] = Color.Parse("#071725");
        comparisonColors["orange"] = Color.Parse("#C6D4DF");
        LegacyOverlayTheme theme = defaultTheme with { Colors = comparisonColors };
        themeService.ApplyOverlayTheme(theme);
        Window window = editor
            ? new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotSysStatus"))
            : new SystemStatusOverlayWindow(
                (SystemSurveyOverlayViewModel)OverlayEditorPreviewCatalog.Create("PlotSysStatus")
            );
        try
        {
            OverlayThemeResources.Apply(window);
            if (window is OverlayPositionPreviewWindow preview)
            {
                preview.ApplyRuntimePresentationTheme();
            }

            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Border[] rings = window
                .GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("ring-layer"))
                .ToArray();
            Assert.Equal(9, rings.Length);
            Assert.Equal(6, rings.Count(border => border.BorderThickness == new Thickness(1)));
            AssertRingColors(rings, "#FFFF00", "#00FF00", "#008B00");
            Border[] triple = rings
                .Where(border => ((SurveyBodyReferenceViewModel)border.DataContext!).Name == "C 3")
                .ToArray();
            Border outer = Assert.Single(triple, border => border.Classes.Contains("ring-c"));
            Border middle = Assert.Single(triple, border => border.Classes.Contains("ring-b"));
            Border inner = Assert.Single(triple, border => border.Classes.Contains("ring-a"));
            Assert.Equal(new Point(1, 1), middle.TranslatePoint(default, outer));
            Assert.Equal(new Point(2, 2), inner.TranslatePoint(default, outer));
            Assert.Equal(outer.Bounds.Width - 4, inner.Bounds.Width);
            Assert.Equal(new CornerRadius(6), outer.CornerRadius);
            Assert.Equal(new CornerRadius(5), middle.CornerRadius);
            double fullWidth = outer.Bounds.Width;
            TextBlock destination = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "B 2 a" && text.IsVisible
            );
            Assert.Equal(FontWeight.Bold, destination.FontWeight);
            Assert.Equal(
                Color.Parse("#FFFF00"),
                Assert.IsAssignableFrom<ISolidColorBrush>(destination.Foreground).Color
            );
            SaveRender(window, editor ? "system-status-editor" : "system-status-live");

            outer.DataContext = ((SurveyBodyReferenceViewModel)outer.DataContext!) with { HasUnscannedRingA = false };
            using WriteableBitmap? updated = window.CaptureRenderedFrame();
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(inner.BorderBrush).Color);
            Assert.Equal(new Thickness(0), inner.BorderThickness);
            Assert.Equal(new Point(1, 1), middle.TranslatePoint(default, outer));
            Assert.Equal(new Point(2, 2), inner.TranslatePoint(default, outer));
            Assert.Equal(fullWidth - 2, outer.Bounds.Width);
            Assert.Equal(new CornerRadius(5), outer.CornerRadius);
            Assert.Equal(new CornerRadius(4), middle.CornerRadius);
            SaveRender(window, editor ? "system-status-editor-ring-a-mapped" : "system-status-live-ring-a-mapped");

            var colors = theme.Colors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            colors["yellow"] = Color.Parse("#FF8844");
            colors["green"] = Color.Parse("#88AAFF");
            colors["greenDark"] = Color.Parse("#4455AA");
            themeService.ApplyOverlayTheme(theme with { Colors = colors });
            OverlayThemeResources.RefreshAll();
            using WriteableBitmap? recolored = window.CaptureRenderedFrame();
            AssertRingColors(rings, "#FF8844", "#88AAFF", "#4455AA");
        }
        finally
        {
            window.Close();
            themeService.ApplyOverlayTheme(defaultTheme);
            OverlayThemeResources.RefreshAll();
        }
    }

    private static void AssertRingColors(IEnumerable<Border> rings, string warning, string success, string dim)
    {
        foreach (Border border in rings.Where(border => border.Classes.Contains("unscanned")))
        {
            string expected = dim;
            if (border.Classes.Contains("ring-a"))
            {
                expected = warning;
            }
            else if (border.Classes.Contains("ring-b"))
            {
                expected = success;
            }
            Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(border.BorderBrush).Color);
        }
    }

    private static void SaveRender(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(window.Bounds.Width * 4), (int)Math.Ceiling(window.Bounds.Height * 4)),
            new Vector(384, 384)
        );
        bitmap.Render(window);
        using FileStream stream = File.Create(Path.Combine(directory, name + ".png"));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
