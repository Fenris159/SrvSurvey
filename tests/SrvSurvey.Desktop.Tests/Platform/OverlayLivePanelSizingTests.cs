using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayLivePanelSizingTests
{
    private static readonly Dictionary<string, Type> LiveWindows = new()
    {
        ["PlotBioStatus"] = typeof(BiologyStatusOverlayWindow),
        ["PlotBioSystem"] = typeof(BiologySurveyOverlayWindow),
        ["PlotBodyInfo"] = typeof(BodyInformationOverlayWindow),
        ["PlotBuildCommodities"] = typeof(ColonizationCommodityOverlayWindow),
        ["PlotFlightWarning"] = typeof(FlightWarningOverlayWindow),
        ["PlotFloatie"] = typeof(NotificationOverlayWindow),
        ["PlotFootCombat"] = typeof(FootCombatOverlayWindow),
        ["PlotFSS"] = typeof(LastFssBodyOverlayWindow),
        ["PlotFSSInfo"] = typeof(FssInfoOverlayWindow),
        ["PlotGalMap"] = typeof(GalaxyMapOverlayWindow),
        ["PlotMiningNotifications"] = typeof(MiningActivityOverlayWindow),
        ["PlotMiningFiregroups"] = typeof(MiningActivityOverlayWindow),
        ["PlotMiningCargo"] = typeof(MiningCargoOverlayWindow),
        ["PlotMiningReference"] = typeof(MiningReferenceOverlayWindow),
        ["PlotMiningWarning"] = typeof(MiningWarningOverlayWindow),
        ["PlotSurfaceMining"] = typeof(SurfaceMiningOverlayWindow),
        ["PlotMineMap"] = typeof(MineMapOverlayWindow),
        ["PlotSurfaceMiningSurvey"] = typeof(SurfaceMiningSurveyOverlayWindow),
        ["PlotGrounded"] = typeof(SurfaceSurveyOverlayWindow),
        ["PlotGuardians"] = typeof(GuardianOverlayWindow),
        ["PlotGuardianStatus"] = typeof(GuardianStatusOverlayWindow),
        ["PlotGuardianSystem"] = typeof(GuardianSystemOverlayWindow),
        ["PlotHumanSite"] = typeof(HumanSiteOverlayWindow),
        ["PlotJumpInfo"] = typeof(JumpInfoOverlayWindow),
        ["PlotFleetCarrierRoute"] = typeof(FleetCarrierRouteOverlayWindow),
        ["PlotRouteBio"] = typeof(RouteBioOverlayWindow),
        ["PlotMassacre"] = typeof(MassacreMissionsOverlayWindow),
        ["PlotMiniTrack"] = typeof(MiniTrackOverlayWindow),
        ["PlotMultiGameCommander"] = typeof(MultiGameCommanderOverlayWindow),
        ["PlotPriorScans"] = typeof(PriorScansOverlayWindow),
        ["PlotPulse"] = typeof(PulseOverlayWindow),
        ["PlotQuestMini"] = typeof(QuestIndicatorOverlayWindow),
        ["PlotRamTah"] = typeof(RamTahOverlayWindow),
        ["PlotSphericalSearch"] = typeof(SphericalSearchOverlayWindow),
        ["PlotStationInfo"] = typeof(StationInfoOverlayWindow),
        ["PlotSysStatus"] = typeof(SystemStatusOverlayWindow),
        ["PlotTrackTarget"] = typeof(GroundTargetOverlayWindow),
    };

    [AvaloniaTheory]
    [InlineData("default")]
    [InlineData("compact")]
    [InlineData("expanded")]
    [InlineData("typography")]
    [InlineData("scaled")]
    [InlineData("updated")]
    [InlineData("reset")]
    public void EveryLivePanelFitsAndMatchesItsEditorPresentation(string scenario)
    {
        var failures = new List<string>();
        foreach (OverlayLayoutDefinition definition in OverlayLayoutCatalog.Supported)
        {
            for (int state = 0; state < OverlayEditorPreviewCatalog.GetStates(definition.Name).Count; state++)
            {
                AuditPanel(definition, scenario, state, failures);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void AuditPanel(
        OverlayLayoutDefinition definition,
        string scenario,
        int state,
        List<string> failures
    )
    {
        var preview = new OverlayPositionPreviewWindow(definition);
        Window? window = null;
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            for (int index = 0; index < state; index++)
            {
                preview.CycleEditorPreviewState();
            }
            Control expected = Assert.IsType<Control>(preview.RuntimePresentation, exactMatch: false);
            double sizeFactor = scenario == "compact" ? 0.8 : 1.3;
            OverlayPanelSize? size = scenario is "compact" or "expanded" or "reset"
                ? new(
                    Math.Round(definition.PreviewSize.Width * sizeFactor),
                    Math.Round(definition.PreviewSize.Height * sizeFactor)
                )
                : null;
            OverlayTypographyScale typography =
                scenario == "typography" ? new(100, 100, 100, 100, 100, 100, 100) : OverlayTypographyScale.Default;
            var layout = new LegacyOverlayLayout(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    [definition.Name] = new(
                        LegacyHorizontalAnchor.Left,
                        8,
                        LegacyVerticalAnchor.Top,
                        8,
                        null,
                        TypographyScale: typography,
                        SizeOverride: size
                    ),
                },
                null,
                null
            );
            if (scenario == "scaled")
            {
                layout.SetScaleIndex(10);
            }
            preview.ConfigureScale(layout.ScaleIndex, null, 1);
            preview.ConfigureTypography(typography);
            preview.ConfigureSize(size);
            Type windowType = LiveWindows[definition.Name];
            System.Reflection.ConstructorInfo constructor = Assert.Single(
                windowType.GetConstructors(),
                candidate =>
                    candidate.GetParameters() is [var parameter]
                    && parameter.ParameterType.IsInstanceOfType(expected.DataContext)
            );
            window = Assert.IsType<Window>(constructor.Invoke([expected.DataContext]), exactMatch: false);
            OverlayThemeResources.Apply(window, layout, definition.Name, new OverlayWindowRegistry());
            window.Show();
            preview.Show();
            if (scenario is "updated" or "reset")
            {
                using WriteableBitmap? initialLive = window.CaptureRenderedFrame();
                using WriteableBitmap? initialPreview = preview.CaptureRenderedFrame();
                size =
                    scenario == "updated"
                        ? new(definition.PreviewSize.Width * 1.2, definition.PreviewSize.Height * 1.2)
                        : null;
                typography = scenario == "updated" ? new(50, 50, 50, 50, 50, 50, 50) : OverlayTypographyScale.Default;
                layout.SetPlacement(
                    definition.Name,
                    layout.Placements[definition.Name] with
                    {
                        SizeOverride = size,
                        TypographyScale = typography,
                    }
                );
                preview.ConfigureTypography(typography);
                preview.ConfigureSize(size);
            }
            using WriteableBitmap? liveFrame = window.CaptureRenderedFrame();
            using WriteableBitmap? previewFrame = preview.CaptureRenderedFrame();
            Control actual = Assert.Single(
                window.GetVisualDescendants().OfType<Control>(),
                control => control.GetType() == expected.GetType()
            );
            string label = $"{definition.Name}/{state}/{scenario}";
            if (
                Math.Abs(actual.Bounds.Width - expected.Bounds.Width) > 1
                || Math.Abs(actual.Bounds.Height - expected.Bounds.Height) > 1
            )
            {
                failures.Add($"{label}: live {actual.Bounds.Size}, preview {expected.Bounds.Size}");
            }
            Point start = Assert.IsType<Point>(actual.TranslatePoint(default, window));
            Point end = Assert.IsType<Point>(
                actual.TranslatePoint(new Point(actual.Bounds.Width, actual.Bounds.Height), window)
            );
            if (start.X < -1 || start.Y < -1 || end.X > window.Bounds.Width + 1 || end.Y > window.Bounds.Height + 1)
            {
                failures.Add($"{label}: content [{start} - {end}] clipped by live window {window.Bounds.Size}");
            }
        }
        finally
        {
            window?.Close();
            preview.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void RemovingASizeOverrideRestoresCurrentBindingsAndConstraints(bool contentControl)
    {
        var source = new Border { Width = 320, Height = 440 };
        Control panel = contentControl ? new UserControl() : new Border();
        panel.MinWidth = 250;
        panel.MaxWidth = 440;
        using IDisposable widthBinding = panel.Bind(Control.WidthProperty, source.GetObservable(Control.WidthProperty));
        using IDisposable heightBinding = panel.Bind(
            Control.HeightProperty,
            source.GetObservable(Control.HeightProperty)
        );
        OverlayThemeResources.ApplyPanelSize(panel, null);
        OverlayThemeResources.ApplyPanelSize(panel, new(210, 180));
        OverlayThemeResources.ApplyPanelSize(panel, new(210, 180));
        source.Width = 380;
        source.Height = 500;
        Assert.Equal(210, panel.Width);
        Assert.Equal(180, panel.Height);
        Assert.Equal(1, panel.MinWidth);
        Assert.True(double.IsPositiveInfinity(panel.MaxWidth));
        OverlayThemeResources.ApplyPanelSize(panel, new(260, 240));
        Assert.Equal(260, panel.Width);
        Assert.Equal(240, panel.Height);
        OverlayThemeResources.ApplyPanelSize(panel, null);
        Assert.Equal(380, panel.Width);
        Assert.Equal(500, panel.Height);
        Assert.Equal(250, panel.MinWidth);
        Assert.Equal(440, panel.MaxWidth);
        source.Width = 440;
        source.Height = 600;
        Assert.Equal(440, panel.Width);
        Assert.Equal(600, panel.Height);
    }

    [AvaloniaFact]
    public void NextJumpKeepsItsSizeAndTypographyWhenMovedToACombinedHost()
    {
        const string plotter = "PlotJumpInfo";
        var window = new JumpInfoOverlayWindow((JumpInfoOverlayViewModel)OverlayEditorPreviewCatalog.Create(plotter));
        var host = new CombinedOverlayWindow { Width = 1200, Height = 900 };
        var layout = new LegacyOverlayLayout(
            new Dictionary<string, LegacyOverlayPlacement>
            {
                [plotter] = new(
                    LegacyHorizontalAnchor.Left,
                    8,
                    LegacyVerticalAnchor.Top,
                    8,
                    null,
                    TypographyScale: new(100, 100, 100, 100, 100, 100, 100),
                    SizeOverride: new(764, 600)
                ),
            },
            null,
            null
        );
        try
        {
            OverlayThemeResources.Apply(window, layout, plotter, new OverlayWindowRegistry());
            OverlayThemeResources.Apply(host);
            window.Show();
            using WriteableBitmap? firstFrame = window.CaptureRenderedFrame();
            JumpInfoOverlayPresentation presentation = Assert.Single(
                window.GetVisualDescendants().OfType<JumpInfoOverlayPresentation>()
            );
            TextBlock header = Assert.Single(
                presentation.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "NEXT JUMP"
            );
            double headerSize = header.FontSize;
            Assert.Equal(20, headerSize);
            Size originalSize = presentation.Bounds.Size;
            Control content = Assert.IsType<Control>(window.Content, exactMatch: false);
            window.Content = new Border { Width = originalSize.Width, Height = originalSize.Height };
            var presenter = new ContentControl { Content = content, DataContext = window.DataContext };
            host.Add(presenter);
            host.Show();
            using WriteableBitmap? combinedFrame = host.CaptureRenderedFrame();
            Assert.Equal(headerSize, header.FontSize);
            Assert.Equal(originalSize, presentation.Bounds.Size);
            layout.SetPlacement(plotter, layout.Placements[plotter] with { SizeOverride = new(820, 600) });
            using WriteableBitmap? resizedFrame = host.CaptureRenderedFrame();
            Assert.Equal(new Size(820, 600), presentation.Bounds.Size);
            host.Remove(presenter);
            presenter.Content = null;
            // Complete the old host's queued layout before attaching to another window.
            using WriteableBitmap? detachedFrame = host.CaptureRenderedFrame();
            window.Content = content;
            using WriteableBitmap? restoredFrame = window.CaptureRenderedFrame();
            Assert.Equal(headerSize, header.FontSize);
            Assert.Equal(new Size(820, 600), presentation.Bounds.Size);
        }
        finally
        {
            host.Close();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(764, 276)]
    [InlineData(780, 283)]
    [InlineData(500, 283)]
    public void NextJumpLivePanelMatchesItsResizedPreview(double width, double height)
    {
        const string plotter = "PlotJumpInfo";
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-overlay-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plotters.json"), """{"PlotJumpInfo":"left:8, top:8"}""");
        File.WriteAllText(
            Path.Combine(directory, "overlay-size-overrides.json"),
            System.Text.Json.JsonSerializer.Serialize(
                new Dictionary<string, OverlayPanelSize> { [plotter] = new(width, height) },
                System.Text.Json.JsonSerializerOptions.Web
            )
        );
        var window = new JumpInfoOverlayWindow((JumpInfoOverlayViewModel)OverlayEditorPreviewCatalog.Create(plotter));
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(plotter));
        try
        {
            LegacyOverlayLayout layout = new LegacyOverlayLayoutStore(directory).Load();
            Assert.Equal(new OverlayPanelSize(width, height), layout.GetSizeOverride(plotter));
            OverlayThemeResources.Apply(window, layout, plotter, new OverlayWindowRegistry());
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(layout.ScaleIndex, null, 1);
            preview.ConfigureTypography(layout.GetTypographyScale(plotter));
            preview.ConfigureSize(layout.GetSizeOverride(plotter));
            window.Show();
            preview.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            using WriteableBitmap? previewFrame = preview.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.NotNull(previewFrame);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"next-jump-live-{width}.png"), PngBitmapEncoderOptions.Default);
                previewFrame.Save(
                    Path.Combine(output, $"next-jump-preview-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }

            JumpInfoOverlayPresentation live = Assert.Single(
                window.GetVisualDescendants().OfType<JumpInfoOverlayPresentation>()
            );
            JumpInfoOverlayPresentation expected = Assert.IsType<JumpInfoOverlayPresentation>(
                preview.RuntimePresentation
            );
            Point origin = Assert.IsType<Point>(live.TranslatePoint(default, window));
            Assert.True(
                origin.X >= -1 && origin.X + live.Bounds.Width <= window.Bounds.Width + 1,
                $"Next-jump content [{origin.X}, {origin.X + live.Bounds.Width}] is clipped by its {window.Bounds.Width}-wide live window."
            );
            Assert.True(
                Math.Abs(live.Bounds.Width - expected.Bounds.Width) <= 1,
                $"Next-jump live width {live.Bounds.Width} does not match preview width {expected.Bounds.Width}."
            );
            Assert.InRange(Math.Abs(live.Bounds.Height - expected.Bounds.Height), 0, 1);
        }
        finally
        {
            preview.Close();
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }
}
