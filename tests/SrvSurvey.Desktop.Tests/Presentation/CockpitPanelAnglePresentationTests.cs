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
    [InlineData(2410, -6)]
    [InlineData(-2410, 6)]
    public void DedicatedRollWrapsSeveralTurnsInOnePointerMove(double horizontalMotion, double expected)
    {
        var handle = new PanelRotationOrb
        {
            Width = 30,
            Height = 30,
            IsRollControl = true,
        };
        var window = new Window
        {
            Width = 200,
            Height = 200,
            Content = handle,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Point start = handle.TranslatePoint(new Point(15, 15), window)!.Value;
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(start + new Vector(horizontalMotion, 400), RawInputModifiers.LeftMouseButton);
            Assert.Equal(expected, handle.Roll, 3);
            window.MouseUp(start + new Vector(horizontalMotion, 400), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(expected, handle.Roll, 3);
            Assert.Equal(0, handle.Yaw);
            Assert.Equal(0, handle.Pitch);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1, 1)]
    [InlineData(false, 47, 47)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 47, 47)]
    public void SeparateTiltAndRollButtonsUseTheirWholeArea(bool roll, double x, double y)
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-separate-handles-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureSize(new OverlayPanelSize(300, 200));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(300, 200));
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Border tilt = preview.FindControl<Border>("PanelRotationHandleBorder")!;
            Border spin = preview.FindControl<Border>("PanelRollHandleBorder")!;
            Assert.Equal(new Size(48, 48), tilt.Bounds.Size);
            Assert.Equal(new Size(48, 48), spin.Bounds.Size);
            Point tiltOrigin = tilt.TranslatePoint(default, preview)!.Value;
            Point rollOrigin = spin.TranslatePoint(default, preview)!.Value;
            Assert.Equal(tiltOrigin.X, rollOrigin.X, 3);
            Assert.Equal(8, rollOrigin.Y - tiltOrigin.Y - 48, 3);
            Border button = roll ? spin : tilt;
            Point start = button.TranslatePoint(new Point(x, y), preview)!.Value;
            PixelPoint target = preview.PointToScreen(start + new Vector(20, -10));
            preview.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(preview.PointToClient(target), RawInputModifiers.LeftMouseButton);
            preview.MouseUp(preview.PointToClient(target), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(
                roll ? new CockpitPanelPose(Roll: -12) : new CockpitPanelPose(12, 6),
                model.Resolve("PlotFSSInfo")
            );
            AssertHandleOutsideProjectedCorner(preview);
            Point reset = button.TranslatePoint(new Point(24, 24), preview)!.Value;
            preview.MouseDown(reset, MouseButton.Right, RawInputModifiers.RightMouseButton);
            preview.MouseUp(reset, MouseButton.Right, RawInputModifiers.None);
            Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotFSSInfo"));
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void OffCenterPanelKeepsItsKnobOutsideWhenProjectionWindingReverses()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-winding-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(200, 200));
            preview.ConfigureCockpitViewport(new PixelRect(0, 0, 1920, 1080), 1);
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(140, 440));
            model.SetPanelPose("PlotFSSInfo", new CockpitPanelPose(Yaw: 60));
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            AssertHandleOutsideProjectedCorner(preview);
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void CameraPlaneCrossingKeepsAReadableRecoveryFrame()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-camera-plane-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 450));
            preview.ConfigureCockpitViewport(new PixelRect(0, 0, 1920, 1080), 1);
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(810, 300));
            model.SetPanelPose("PlotFSSInfo", new CockpitPanelPose(Pitch: -85, Depth: .25));
            Border tab = preview.FindControl<Border>("EditorFolderTab")!;
            Point leftTop = tab.TranslatePoint(default, preview)!.Value;
            Point leftBottom = tab.TranslatePoint(new Point(0, tab.Bounds.Height), preview)!.Value;
            Assert.InRange(Math.Abs(leftTop.X - leftBottom.X), 0, .01);
            Assert.Equal(0, preview.RuntimePresentation!.Opacity);
            model.SetPanelPose("PlotFSSInfo", new CockpitPanelPose());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, preview.RuntimePresentation.Opacity);
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public void VisibleRotatedBorderRetainsItsRightClickAppearanceMenu()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SrvSurvey-border-menu-{Guid.NewGuid():N}", "ui.json");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(path));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 100));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(300, 200));
            model.SetPanelPose("PlotFSSInfo", new CockpitPanelPose(Roll: 90));
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            var requested = new List<string>();
            preview.SettingsRequested += (_, args) => requested.Add(args.PlotterName);
            Border outline = preview.FindControl<Border>("ProjectionReferenceOutline")!;
            Point stroke = outline.TranslatePoint(new Point(1, 1), preview)!.Value;
            preview.MouseDown(stroke, MouseButton.Right, RawInputModifiers.RightMouseButton);
            preview.MouseUp(stroke, MouseButton.Right, RawInputModifiers.None);
            Assert.Equal("PlotFSSInfo", Assert.Single(requested));
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingTheCapturedKnobDoesNotRotateForAStationaryScreenPointer(bool roll)
    {
        var orb = new PanelRotationOrb
        {
            Width = 30,
            Height = 30,
            IsRollControl = roll,
        };
        var canvas = new Canvas
        {
            Width = 400,
            Height = 400,
            Children = { orb },
        };
        Canvas.SetLeft(orb, 100);
        Canvas.SetTop(orb, 100);
        var window = new Window
        {
            Width = 400,
            Height = 400,
            Content = canvas,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Point center = orb.TranslatePoint(new Point(15, 15), window)!.Value;
            PixelPoint held = window.PointToScreen(center);
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Canvas.SetLeft(orb, 125);
            Canvas.SetTop(orb, 120);
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? moved = window.CaptureRenderedFrame();
            window.MouseMove(window.PointToClient(held), RawInputModifiers.LeftMouseButton);
            Assert.Equal(0, orb.Yaw);
            Assert.Equal(0, orb.Pitch);
            Assert.Equal(0, orb.Roll);
            if (roll)
            {
                PixelPoint down = new(held.X + 150, held.Y);
                window.MouseMove(window.PointToClient(down), RawInputModifiers.LeftMouseButton);
                Assert.Equal(-90, orb.Roll, 3);
                held = down;
            }
            window.MouseUp(window.PointToClient(held), MouseButton.Left, RawInputModifiers.None);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("PlotRouteBio", false, 600)]
    [InlineData("PlotRouteBio", true, 600)]
    [InlineData("PlotRouteBio", false, 300)]
    [InlineData("PlotFSSInfo", true, 300)]
    public void HeldRotationUsesScreenMotionOutsideTheKnobWithoutIdleJitter(string name, bool enabled, int height)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-rotation-gesture-{Guid.NewGuid():N}");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(Path.Combine(directory, "ui.json")));
        model.Enabled = enabled;
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(name));
        int positionChanges = 0;
        preview.PositionChanged += (_, _) =>
        {
            if (++positionChanges > 100)
            {
                throw new InvalidOperationException(
                    "Rotation never settled: preview position changed more than 100 times."
                );
            }
        };
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(420, height));
            preview.ConfigureCockpitViewport(new PixelRect(0, 0, 1280, 720), 1);
            preview.MaxHeight = 340;
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(300, 150));
            PanelRotationOrb orb = preview.FindControl<PanelRotationOrb>("PanelRotationHandle")!;
            IPointer? pointer = null;
            orb.AddHandler(
                InputElement.PointerPressedEvent,
                (_, e) => pointer = e.Pointer,
                Avalonia.Interactivity.RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            Point center = orb.TranslatePoint(new Point(15, 15), preview)!.Value;
            PixelPoint start = preview.PointToScreen(center);
            preview.MouseDown(center, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Assert.Same(orb, pointer!.Captured);
            for (int step = 1; step <= 8; step++)
            {
                PixelPoint target = new(start.X + step * 8, start.Y - step * 5);
                preview.MouseMove(preview.PointToClient(target), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap? frame = preview.CaptureRenderedFrame();
                // A second sample at the same screen point must not add rotation after layout settles.
                preview.MouseMove(preview.PointToClient(target), RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(model.Resolve(name).Yaw, step * 4.8 - .01, step * 4.8 + .01);
                Assert.InRange(model.Resolve(name).Pitch, step * 3 - .01, step * 3 + .01);
                Assert.Same(orb, pointer.Captured);
            }
            PixelPoint release = new(start.X + 64, start.Y - 40);
            preview.MouseUp(preview.PointToClient(release), MouseButton.Left, RawInputModifiers.None);
            Assert.Null(pointer.Captured);
            preview.MouseMove(preview.PointToClient(new PixelPoint(release.X + 50, release.Y)), RawInputModifiers.None);
            Assert.InRange(model.Resolve(name).Yaw, 38.39, 38.41);
            Assert.InRange(model.Resolve(name).Pitch, 23.99, 24.01);
        }
        finally
        {
            preview.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [AvaloniaFact]
    public void RotationHandleSitsOutsideThePreviewAndTheBorderRotatesWithItsContents()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-rotation-frame-{Guid.NewGuid():N}");
        var model = new CockpitOverlayViewModel(new CockpitOverlaySettingsStore(Path.Combine(directory, "ui.json")));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFSSInfo"));
        try
        {
            preview.ConfigureScale(OverlayScaleCatalog.GetIndex(0), null, 1);
            preview.ConfigureSize(new OverlayPanelSize(300, 100));
            preview.ConfigureCockpitPanels(model);
            preview.Show();
            PositionPanel(preview, new PixelPoint(300, 200));
            Border outline = preview.FindControl<Border>("ProjectionReferenceOutline")!;
            PanelRotationOrb orb = preview.FindControl<PanelRotationOrb>("PanelRotationHandle")!;
            Rect panel = new(outline.TranslatePoint(default, preview)!.Value, outline.Bounds.Size);
            Rect handle = new(orb.TranslatePoint(default, preview)!.Value, orb.Bounds.Size);
            Assert.False(panel.Intersects(handle));
            model.SetPanelPose("PlotFSSInfo", new CockpitPanelPose(Roll: 30));
            Dispatcher.UIThread.RunJobs();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Point left = outline.TranslatePoint(default, preview)!.Value;
            Point right = outline.TranslatePoint(new Point(outline.Bounds.Width, 0), preview)!.Value;
            Assert.True(Math.Abs(right.Y - left.Y) > 100, "The outer preview border must rotate with the panel.");
        }
        finally
        {
            preview.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

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
            Assert.Equal("Tilt this cockpit panel", peer.GetName());
            Assert.True(peer.IsControlElement());
            Assert.True(peer.IsKeyboardFocusable());
            PanelRotationOrb rollHandle = preview.FindControl<PanelRotationOrb>("PanelRollHandle")!;
            AutomationPeer rollPeer = ControlAutomationPeer.CreatePeerForElement(rollHandle);
            Assert.Equal("Roll this cockpit panel", rollPeer.GetName());
            Assert.Equal(
                "Drag left or right to roll; Left and Right keys roll; Home resets rotation.",
                rollPeer.GetHelpText()
            );
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
            PixelPoint tiltTarget = preview.PointToScreen(center + new Vector(50, -25));
            preview.MouseDown(center, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            preview.MouseMove(preview.PointToClient(tiltTarget), RawInputModifiers.LeftMouseButton);
            preview.MouseUp(preview.PointToClient(tiltTarget), MouseButton.Left, RawInputModifiers.None);
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
            Assert.NotEqual(knobScreenOrigin, preview.PointToScreen(orb.TranslatePoint(default, preview)!.Value));
            AssertHandleOutsideProjectedCorner(preview);
            preview.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotRouteBio"));
            Assert.Equal("Yaw 0°, pitch 0°, roll 0°", peer.GetItemStatus());
            Assert.True(rollHandle.Focus());
            preview.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            preview.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(new CockpitPanelPose(Roll: -1), model.Resolve("PlotRouteBio"));
            preview.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null);
            Assert.Equal(new CockpitPanelPose(Roll: 4), model.Resolve("PlotRouteBio"));
            preview.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
            Assert.Equal(new CockpitPanelPose(), model.Resolve("PlotRouteBio"));
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

    private static void AssertHandleOutsideProjectedCorner(OverlayPositionPreviewWindow preview)
    {
        Border outline = preview.FindControl<Border>("ProjectionReferenceOutline")!;
        StackPanel handles = preview.FindControl<StackPanel>("PanelAngleHandles")!;
        Point top = outline.TranslatePoint(new Point(outline.Bounds.Width, 0), preview)!.Value;
        Point corner = outline.TranslatePoint(new Point(outline.Bounds.Width, outline.Bounds.Height), preview)!.Value;
        Point inside = outline
            .TranslatePoint(new Point(outline.Bounds.Width / 2, outline.Bounds.Height / 2), preview)!
            .Value;
        Vector edge = corner - top;
        Vector inward = inside - top;
        double sign = Math.Sign(edge.X * inward.Y - edge.Y * inward.X);
        double furthestAlongEdge = double.NegativeInfinity;
        foreach (Point local in new[] { new Point(0, 0), new Point(48, 0), new Point(48, 104), new Point(0, 104) })
        {
            Point screen = handles.TranslatePoint(local, preview)!.Value;
            Vector relative = screen - top;
            double distance = sign * (edge.X * relative.Y - edge.Y * relative.X) / edge.Length;
            Assert.True(
                distance <= -7.99,
                "Both complete buttons must remain outside the projected border with an eight-pixel gap."
            );
            Vector fromCorner = screen - corner;
            double along = (fromCorner.X * edge.X + fromCorner.Y * edge.Y) / edge.Length;
            furthestAlongEdge = Math.Max(furthestAlongEdge, along);
        }
        Assert.InRange(furthestAlongEdge, -.01, .01);
    }
}
