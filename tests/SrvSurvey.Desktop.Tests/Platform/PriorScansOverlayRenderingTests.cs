using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class PriorScansOverlayRenderingTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CanonnCheckboxSharesAvailabilityShortcutAndPersistedState(bool available)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-prior-availability-{Guid.NewGuid():N}");
        var paths = new AppDataPaths(
            Path.Combine(directory, "config"),
            Path.Combine(directory, "data"),
            Path.Combine(directory, "cache"),
            []
        );
        new SystemSurveySettingsStore(paths.UiSettingsPath).Save(
            SystemSurveyPreferences.Default with
            {
                ShowCanonnSignalsOnRadar = !available,
            }
        );
        new OverlayPanelVisibilitySettingsStore(paths.UiSettingsPath).Save(
            new Dictionary<string, bool> { ["PlotPriorScans"] = available }
        );
        var window = new Window { Width = 1100, Height = 900 };
        MainWindowViewModel? main = null;
        try
        {
            main = MainWindowViewModelTestBuilder.Create(null, builder => builder.WithAppDataPaths(paths));
            OverlayPanelVisibilityEntryViewModel panel = main.OverlayPanelVisibility.Panels.Single(item =>
                item.PlotterName == "PlotPriorScans"
            );
            Assert.Equal(available, panel.IsEnabled);
            Assert.Equal(available, main.SystemSurvey.ShowCanonnSignalsOnRadar);
            var consistentChanges = new List<bool>();
            main.SystemSurvey.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemSurveyViewModel.ShowCanonnSignalsOnRadar))
                {
                    consistentChanges.Add(
                        main.SystemSurvey.ShowCanonnSignalsOnRadar
                            == OverlayWindowRegistry.Shared.IsUserVisible("PlotPriorScans")
                    );
                }
            };
            var view = new Views.OverlaySettingsView(OverlaySettingsCategory.Exobiology) { DataContext = main };
            window.Content = view;
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            CheckBox checkbox = Assert.IsType<CheckBox>(view.FindControl<CheckBox>("CanonnPriorScansEnabledToggle"));
            Assert.Equal(available, checkbox.IsChecked);
            checkbox.IsChecked = true;
            checkbox.IsChecked = false;
            Assert.False(panel.IsEnabled);
            Assert.False(main.SystemSurvey.ShowCanonnSignalsOnRadar);
            Assert.False(OverlayWindowRegistry.Shared.IsUserVisible("PlotPriorScans"));
            Assert.False(new OverlayPanelVisibilitySettingsStore(paths.UiSettingsPath).Load()["PlotPriorScans"]);
            Assert.False(new SystemSurveySettingsStore(paths.UiSettingsPath).Load().ShowCanonnSignalsOnRadar);
            Assert.True(
                GlobalInputActionCatalog.TryGetOverlayPlotterName(
                    GlobalInputAction.TogglePriorScansVisibility,
                    out string plotter
                )
            );
            Assert.Same(
                main.InputSettings.Bindings.Single(binding =>
                    binding.Definition.Action == GlobalInputAction.TogglePriorScansVisibility
                ),
                panel.Shortcut
            );
            Assert.True(main.OverlayPanelVisibility.Toggle(plotter));
            Assert.True(checkbox.IsChecked);
            Assert.True(main.SystemSurvey.ShowCanonnSignalsOnRadar);
            Assert.True(new OverlayPanelVisibilitySettingsStore(paths.UiSettingsPath).Load()[plotter]);
            Assert.True(new SystemSurveySettingsStore(paths.UiSettingsPath).Load().ShowCanonnSignalsOnRadar);
            panel.IsEnabled = false;
            Assert.False(checkbox.IsChecked);
            main.SystemSurvey.AutoShowPriorScans = false;
            main.SystemSurvey.UseExternalData = false;
            Assert.True(checkbox.IsEnabled);
            checkbox.IsChecked = true;
            Assert.True(panel.IsEnabled);
            Assert.True(main.SystemSurvey.ShowCanonnSignalsOnRadar);
            Assert.NotEmpty(consistentChanges);
            Assert.All(consistentChanges, Assert.True);
        }
        finally
        {
            window.Close();
            main?.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(true, 0, 1)]
    [InlineData(false, 0, 1)]
    [InlineData(false, 0, 1.5)]
    [InlineData(true, 220, 1.5)]
    [InlineData(false, 520, 2)]
    public async Task LiveCanonnDataFitsDefaultAndSavedWidths(bool panelEnabled, double savedWidth, double scaling)
    {
        const string name = "PlotPriorScans";
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-prior-layout-{Guid.NewGuid():N}");
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(name));
        try
        {
            var survey = new SystemSurveyViewModel(new SystemSurveySettingsStore(Path.Combine(directory, "ui.json")))
            {
                ShowCanonnSignalsOnRadar = panelEnabled,
            };
            survey.ApplyUpdate(
                [
                    Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                    Parse(
                        """{"event":"Scan","ScanType":"Detailed","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"PlanetClass":"Rocky body","Radius":2500000}"""
                    ),
                ],
                new EliteStatus
                {
                    Flags = StatusFlags.HasLatLong | StatusFlags.InMainShip,
                    BodyName = "Test 1",
                    PlanetRadius = 2_500_000,
                    Heading = 86,
                    Altitude = 1_000,
                }
            );
            string[] names =
            [
                "Fonticulua Digitos - Amethyst",
                "Bacterium Bullaris - Aquamarine",
                "Stratum Tectonicas - Emerald",
            ];
            ExobiologyReference[] entries = names
                .Select(
                    (species, index) =>
                        new ExobiologyReference(
                            2310101 + index,
                            $"$Codex_Ent_Test_{index}_Variant;",
                            $"$Codex_Ent_Test_{index}_Species;",
                            species,
                            1_800_000,
                            HudCategory: "Biology"
                        )
                )
                .ToArray();
            var client = new CanonnClient(
                new(
                    "Test",
                    entries
                        .Select(entry => new CanonnSurfaceBiologySignal(
                            "1",
                            entry.DisplayName!,
                            entry.EntryId,
                            new SurfaceCoordinate(0, 180),
                            false
                        ))
                        .ToArray()
                )
            );
            using var model = new PriorScansOverlayViewModel(
                survey,
                client,
                new ExobiologyReferenceCatalog(entries),
                () => "Test",
                OverlayPlatformCapabilities.ForHost(OverlayHostKind.Windows)
            );
            await model.RefreshAsync();
            Assert.Equal(3, model.Species.Count);
            OverlayPanelSize? size = savedWidth > 0 ? new(savedWidth, 560) : null;
            var layout = new LegacyOverlayLayout(
                new Dictionary<string, LegacyOverlayPlacement>
                {
                    [name] = new(
                        LegacyHorizontalAnchor.Left,
                        8,
                        LegacyVerticalAnchor.Top,
                        8,
                        1,
                        ScaleIndex: OverlayScaleCatalog.GetIndex(10),
                        SizeOverride: size
                    ),
                },
                null,
                null
            );
            using var overlays = new HostedOverlayTestHarness(new OverlayWindowRegistry(), overlayLayout: layout);
            using HostedOverlayWindow host = overlays.Session.HostPassiveWindow(
                new PassiveOverlayWindowDefinition(
                    name,
                    _ =>
                    {
                        var live = new PriorScansOverlayWindow(model);
                        live.SetRenderScaling(scaling);
                        return live;
                    },
                    (_, _) => default
                )
            );
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.SetRenderScaling(scaling);
            preview.ConfigureScale(layout.ScaleIndex, layout.Placements[name].ScaleIndex, scaling);
            preview.ConfigureSize(size);
            preview.Show();
            host.Reconcile(model.ShouldShow);
            Assert.Equal(panelEnabled, model.ShouldShow);
            if (!panelEnabled)
            {
                Assert.Null(host.CurrentWindow);
                survey.ShowCanonnSignalsOnRadar = true;
                host.Reconcile(model.ShouldShow);
            }
            Window window = Assert.IsType<PriorScansOverlayWindow>(host.CurrentWindow);
            using WriteableBitmap? liveFrame = window.CaptureRenderedFrame();
            using WriteableBitmap? previewFrame = preview.CaptureRenderedFrame();
            if (savedWidth == 0)
            {
                liveFrame?.Save(
                    Path.Combine(Path.GetTempPath(), $"srvsurvey-prior-scans-enabled-{panelEnabled}-dpi-{scaling}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            PriorScansOverlayPresentation actual = Assert.Single(
                window.GetVisualDescendants().OfType<PriorScansOverlayPresentation>()
            );
            PriorScansOverlayPresentation expected = Assert.IsType<PriorScansOverlayPresentation>(
                preview.RuntimePresentation
            );
            double width = savedWidth > 0 ? savedWidth : OverlayLayoutCatalog.GetRequired(name).PreviewSize.Width;
            Assert.InRange(actual.Bounds.Width, width - 1, width + 1);
            Assert.InRange(expected.Bounds.Width, width - 1, width + 1);
            Assert.True(
                Assert.Single(actual.GetVisualDescendants().OfType<PriorScanRadarControl>()).IsEffectivelyVisible
            );
            TextBlock[] labels = actual
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => names.Contains(text.Text))
                .ToArray();
            Assert.Equal(3, labels.Length);
            Assert.All(
                labels,
                label =>
                {
                    Assert.True(label.TextLayout.Width <= label.Bounds.Width + 0.1);
                    Point origin = Assert.IsType<Point>(label.TranslatePoint(default, actual));
                    Assert.InRange(origin.X + label.Bounds.Width, 0, actual.Bounds.Width + 0.1);
                }
            );
            survey.ShowCanonnSignalsOnRadar = false;
            host.Reconcile(model.ShouldShow);
            Assert.False(model.ShouldShow);
            Assert.Null(host.CurrentWindow);
            survey.ShowCanonnSignalsOnRadar = true;
            host.Reconcile(model.ShouldShow);
            Window reopened = Assert.IsType<PriorScansOverlayWindow>(host.CurrentWindow);
            using WriteableBitmap? updated = reopened.CaptureRenderedFrame();
            PriorScansOverlayPresentation restored = Assert.Single(
                reopened.GetVisualDescendants().OfType<PriorScansOverlayPresentation>()
            );
            Assert.InRange(restored.Bounds.Width, width - 1, width + 1);
            Assert.True(
                Assert.Single(restored.GetVisualDescendants().OfType<PriorScanRadarControl>()).IsEffectivelyVisible
            );
            layout.SetPlacement(name, layout.Placements[name] with { SizeOverride = null });
            using WriteableBitmap? reset = reopened.CaptureRenderedFrame();
            Assert.Equal(OverlayLayoutCatalog.GetRequired(name).PreviewSize.Width, restored.Bounds.Width);
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
    [InlineData(false)]
    [InlineData(true)]
    public void LiveSpeciesNamesKeepTheEditorPanelWidth(bool longNames)
    {
        const string name = "PlotPriorScans";
        using var model = (PriorScansOverlayViewModel)OverlayEditorPreviewCatalog.Create(name);
        if (longNames)
        {
            string[] names =
            [
                "Fonticulua Digitos - Amethyst",
                "Bacterium Bullaris - Aquamarine",
                "Stratum Tectonicas - Emerald",
            ];
            model.InstallEditorPreview(
                "Robigo 2 f",
                "HEADING 086°",
                model
                    .Species.Select(
                        (row, index) =>
                            row with
                            {
                                DisplayName = names[index],
                                Targets = row
                                    .Targets.Select(target =>
                                        target with
                                        {
                                            DistanceMeters = 7_800_000,
                                            DistanceText = "7.8 Mm",
                                            IsClose = false,
                                            IsFar = true,
                                        }
                                    )
                                    .ToArray(),
                            }
                    )
                    .ToArray(),
                []
            );
        }
        using var overlays = new HostedOverlayTestHarness(new OverlayWindowRegistry());
        using HostedOverlayWindow host = overlays.Session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(name, _ => new PriorScansOverlayWindow(model), (_, _) => default)
        );
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired(name));
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();
            host.Reconcile(true);
            Window live = Assert.IsType<PriorScansOverlayWindow>(host.CurrentWindow);
            using WriteableBitmap? liveFrame = live.CaptureRenderedFrame();
            using WriteableBitmap? previewFrame = preview.CaptureRenderedFrame();
            PriorScansOverlayPresentation expected = Assert.IsType<PriorScansOverlayPresentation>(
                preview.RuntimePresentation
            );
            PriorScansOverlayPresentation actual = Assert.Single(
                live.GetVisualDescendants().OfType<PriorScansOverlayPresentation>()
            );

            Assert.InRange(actual.Bounds.Width, expected.Bounds.Width - 1, expected.Bounds.Width + 1);
        }
        finally
        {
            preview.Close();
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return Assert.IsType<JournalEventEnvelope>(entry);
    }

    private sealed class CanonnClient(CanonnSystemPoiResult result) : ICanonnSystemPoiClient
    {
        public Task<CanonnSystemPoiResult> GetAsync(
            string systemName,
            string commanderName,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }
}
