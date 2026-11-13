using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CockpitPanelAnglePresentationTests
{
    [AvaloniaTheory]
    [InlineData("monochrome-dark")]
    [InlineData("blue-light")]
    [InlineData("orange-dark")]
    public void PersistentCornerOrbEditsTheRenderedPanelAcrossThemes(string theme)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-cockpit-preview-{Guid.NewGuid():N}");
        Application application = Assert.IsType<Application>(Application.Current, exactMatch: false);
        var themes = new RavenThemeService(
            application,
            new ThemePreferenceStore(Path.Combine(directory, "theme.json"))
        );
        Color originalFill = Assert
            .IsType<ISolidColorBrush>(application.Resources["RavenWindowBrush"], exactMatch: false)
            .Color;
        string previousTheme = RavenThemeCatalog
            .All.Single(palette => Color.Parse(palette.WindowColor) == originalFill)
            .Key;
        var model = new CockpitOverlayViewModel(
            new CockpitOverlaySettingsStore(Path.Combine(directory, "settings.json"))
        );
        model.BeginEdit();
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotRouteBio"));
        try
        {
            themes.Select(theme == "blue-dark" ? "monochrome-dark" : "blue-dark");
            themes.Select(theme);
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureSize(new OverlayPanelSize(420, 500));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            using WriteableBitmap? initial = preview.CaptureRenderedFrame();
            PanelRotationOrb orb = preview.FindControl<PanelRotationOrb>("PanelRotationHandle")!;
            Thumb resize = preview.FindControl<Thumb>("PanelResizeThumb")!;
            Assert.True(orb.IsEffectivelyVisible);
            Assert.True(orb.IsEnabled);
            Assert.Equal("Rotate this cockpit panel", AutomationProperties.GetName(orb));
            Rect knobBounds = new(orb.TranslatePoint(default, preview)!.Value, orb.Bounds.Size);
            Rect resizeBounds = new(resize.TranslatePoint(default, preview)!.Value, resize.Bounds.Size);
            Assert.False(knobBounds.Intersects(resizeBounds));
            Assert.InRange(knobBounds.Left, 0, preview.Bounds.Width - knobBounds.Width);
            Assert.InRange(knobBounds.Bottom, 1, preview.Bounds.Height);
            Control panel = preview.RuntimePresentation!;
            Point corner = panel.RenderTransform!.Value.Transform(new Point(0, 0));
            Point center = orb.TranslatePoint(new Point(15, 15), preview)!.Value;
            preview.MouseDown(center, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(center + new Vector(50, -25), RawInputModifiers.LeftMouseButton);
            preview.MouseUp(center + new Vector(50, -25), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(new CockpitPanelPose(30, 15), model.Resolve("PlotRouteBio"));
            Assert.NotEqual(corner, panel.RenderTransform.Value.Transform(new Point(0, 0)));
            Assert.True(orb.Focus());
            preview.KeyPress(
                Key.Right,
                RawInputModifiers.Control | RawInputModifiers.Shift,
                PhysicalKey.ArrowRight,
                null
            );
            preview.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(new CockpitPanelPose(30, 16, -5), model.Resolve("PlotRouteBio"));
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_COCKPIT_PREVIEW_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame!.Save(Path.Combine(output, $"cockpit-panel-corner-{theme}.png"), PngBitmapEncoderOptions.Default);
            }
            Assert.Equal(knobBounds, new Rect(orb.TranslatePoint(default, preview)!.Value, orb.Bounds.Size));
        }
        finally
        {
            preview.Close();
            model.CancelEdit();
            themes.Select(previousTheme);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
