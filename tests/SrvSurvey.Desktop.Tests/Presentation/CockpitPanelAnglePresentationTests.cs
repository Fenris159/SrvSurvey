using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(420, 500));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(500, 200));
            using WriteableBitmap? initial = preview.CaptureRenderedFrame();
            PanelRotationOrb orb = preview.FindControl<PanelRotationOrb>("PanelRotationHandle")!;
            Thumb resize = preview.FindControl<Thumb>("PanelResizeThumb")!;
            Assert.True(orb.IsEffectivelyVisible);
            Assert.True(orb.IsEnabled);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(orb);
            Assert.Equal("Rotate this cockpit panel", peer.GetName());
            Assert.True(peer.IsControlElement());
            Assert.True(peer.IsKeyboardFocusable());
            Assert.Equal(
                "Arrow keys tilt; Control plus Left or Right rolls; Home resets rotation.",
                peer.GetHelpText()
            );
            Rect knobBounds = new(orb.TranslatePoint(default, preview)!.Value, orb.Bounds.Size);
            PixelPoint knobScreenOrigin = preview.PointToScreen(knobBounds.TopLeft);
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
            Assert.Equal("Yaw 30°, pitch 16°, roll -5°", peer.GetItemStatus());
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_COCKPIT_PREVIEW_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame!.Save(Path.Combine(output, $"cockpit-panel-corner-{theme}.png"), PngBitmapEncoderOptions.Default);
            }
            Assert.Equal(knobScreenOrigin, preview.PointToScreen(orb.TranslatePoint(default, preview)!.Value));
            preview.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotRouteBio"));
            Assert.Equal("Yaw 0°, pitch 0°, roll 0°", peer.GetItemStatus());
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

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(100)]
    public void AQuarterTurnShowsTheWholePanelWithoutChangingItsSavedGeometry(int scalePercent)
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-rolled-panel-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        model.SelectPanel("PlotFSSInfo");
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(scalePercent), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 100));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            using WriteableBitmap? before = preview.CaptureRenderedFrame();
            PositionPanel(preview, new PixelPoint(300, 300));
            PixelPoint origin = preview.GetPanelScreenOrigin(preview.RenderScaling);
            PixelSize size = preview.GetPanelMetrics(preview.RenderScaling).PanelSize;
            model.Roll = 90;
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? after = preview.CaptureRenderedFrame();
            Assert.Equal(origin, preview.GetPanelScreenOrigin(preview.RenderScaling));
            Assert.Equal(size, preview.GetPanelMetrics(preview.RenderScaling).PanelSize);
            Assert.True(preview.Bounds.Height >= 299 * (1 + scalePercent / 100d));
            Control panel = preview.RuntimePresentation!;
            Rect clip = Assert.IsType<TransformedBounds>(panel.GetTransformedBounds()).Clip;
            foreach (
                Point corner in new[] { new Point(0, 0), new Point(300, 0), new Point(300, 100), new Point(0, 100) }
            )
            {
                Point visible = panel.TranslatePoint(corner, preview)!.Value;
                Assert.InRange(visible.X, clip.Left - .1, clip.Right + .1);
                Assert.InRange(visible.Y, clip.Top - .1, clip.Bottom + .1);
            }
            Thumb resize = preview.FindControl<Thumb>("PanelResizeThumb")!;
            Point grip = resize.TranslatePoint(new Point(11, 11), preview)!.Value;
            PixelPoint gripScreen = preview.PointToScreen(grip);
            double factor = 1 + scalePercent / 100d;
            Vector delta = new(20 * factor, 10 * factor);
            preview.MouseDown(grip, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(grip + delta, RawInputModifiers.LeftMouseButton);
            preview.MouseUp(grip + delta, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? resized = preview.CaptureRenderedFrame();
            Assert.Equal(new Size(320, 110), panel.Bounds.Size);
            Assert.Equal(origin, preview.GetPanelScreenOrigin(preview.RenderScaling));
            PixelPoint finalGrip = preview.PointToScreen(resize.TranslatePoint(new Point(11, 11), preview)!.Value);
            Assert.InRange(finalGrip.X, gripScreen.X + (int)delta.X - 1, gripScreen.X + (int)delta.X + 1);
            Assert.InRange(finalGrip.Y, gripScreen.Y + (int)delta.Y - 1, gripScreen.Y + (int)delta.Y + 1);
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void PreviewRefreshesItsActualPositionAndCalibratedLens()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-positioned-panel-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        model.SelectPanel("PlotFSSInfo");
        model.Yaw = 45;
        model.CalibratedLens = new CockpitProjectionLens(1.25f, 2.5f, .1f);
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 100));
            preview.ConfigureCockpitViewport(new PixelRect(0, 0, 1000, 1000), 1);
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            using WriteableBitmap? initial = preview.CaptureRenderedFrame();
            PositionPanel(preview, new PixelPoint(100, 200));
            AssertProjectedCorner(preview, 95.593482, 43.525214);
            PositionPanel(preview, new PixelPoint(600, 200));
            AssertProjectedCorner(preview, 23.051458, 43.525214);
            model.CalibratedLens = new CockpitProjectionLens(1.8358436f, 1.8358436f, .025f);
            AssertProjectedCorner(preview, 29.025110, 31.074398);
        }
        finally
        {
            preview.Close();
        }
    }

    private static void PositionPanel(OverlayPositionPreviewWindow preview, PixelPoint origin)
    {
        PixelPoint offset = preview.GetPanelMetrics(preview.RenderScaling).OriginOffset;
        preview.Position = new PixelPoint(origin.X - offset.X, origin.Y - offset.Y);
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = preview.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void DesktopPreviewRemainsVisibleOutsideTheGameViewportUntilCockpitModeIsEnabled()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-desktop-panel-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 100));
            preview.ConfigureCockpitViewport(new PixelRect(0, 0, 1000, 1000), 1);
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(-400, 200));
            Assert.Null(preview.FindControl<Border>("PanelProjectionHost")!.Clip);
            Assert.Equal(1, preview.RuntimePresentation!.Opacity);
            model.Enabled = true;
            Assert.NotNull(preview.FindControl<Border>("PanelProjectionHost")!.Clip);
            model.Enabled = false;
            Assert.Null(preview.FindControl<Border>("PanelProjectionHost")!.Clip);
        }
        finally
        {
            preview.Close();
            if (Directory.Exists(Path.GetDirectoryName(path)))
            {
                Directory.Delete(Path.GetDirectoryName(path)!, true);
            }
        }
    }

    [AvaloniaFact]
    public void ZeroScalingCanBeRestoredWithoutInvalidProjectionGeometry()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-zero-panel-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        model.SelectPanel("PlotFSSInfo");
        model.Yaw = 30;
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            using WriteableBitmap? initial = preview.CaptureRenderedFrame();
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(-100), null, 1);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? hidden = preview.CaptureRenderedFrame();
            Geometry? clip = preview.FindControl<Border>("PanelProjectionHost")!.Clip;
            Assert.True(clip is null || double.IsFinite(clip.Bounds.Width) && double.IsFinite(clip.Bounds.Height));
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? restored = preview.CaptureRenderedFrame();
            Assert.Equal(new CockpitPanelPose(Yaw: 30), model.Resolve("PlotFSSInfo"));
            Assert.Equal(1, preview.RuntimePresentation!.Opacity);
            Assert.True(preview.FindControl<PanelRotationOrb>("PanelRotationHandle")!.IsEffectivelyVisible);
        }
        finally
        {
            preview.Close();
        }
    }

    private static void AssertProjectedCorner(OverlayPositionPreviewWindow preview, double x, double y)
    {
        Control panel = preview.RuntimePresentation!;
        Point actual = panel.RenderTransform!.Value.Transform(default);
        Assert.InRange(actual.X, x - .001, x + .001);
        Assert.InRange(actual.Y, y - .001, y + .001);
    }
}
