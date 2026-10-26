using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CarrierRouteFuelPresentationTests
{
    [AvaloniaFact]
    public void TravelSettingDefaultsOnAndSavesTheSameLivePreference()
    {
        string directory = Path.Combine(Path.GetTempPath(), "carrier-next-jump-ui-" + Guid.NewGuid().ToString("N"));
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
                builder.WithAppDataPaths(
                    new(
                        Path.Combine(directory, "config"),
                        Path.Combine(directory, "data"),
                        Path.Combine(directory, "cache"),
                        []
                    )
                )
        );
        var view = new Views.OverlaySettingsView(OverlaySettingsCategory.Travel) { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 800,
        };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            CheckBox checkbox = view.GetVisualDescendants()
                .OfType<CheckBox>()
                .Single(box => Equals(box.Content, "Show Carrier Next-Jump Information"));
            Assert.True(checkbox.IsEffectivelyVisible);
            Assert.True(checkbox.IsChecked);
            checkbox.IsChecked = false;
            Assert.False(main.FleetCarrierRoute.ShowCarrierNextJumpInformation);
            Assert.False(new CarrierNextJumpSettingsStore(main.AppDataPaths.UiSettingsPath).Load());
            main.FleetCarrierRoute.ShowCarrierNextJumpInformation = true;
            Assert.True(checkbox.IsChecked);
            var global = new Views.OverlaySettingsView(OverlaySettingsCategory.Global) { DataContext = main };
            Assert.False(global.FindControl<Border>("CarrierNextJumpCard")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PassiveEditorVariationUsesLowTritiumAndBothResourceTags()
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFleetCarrierRoute"));
        try
        {
            for (int index = 0; index < 3; index++)
            {
                Assert.True(preview.CycleEditorPreviewState());
            }
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Assert.Equal("Carrier next jump", preview.CurrentEditorPreviewStateName);
            TextBlock[] visible = preview
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();
            Assert.Contains(visible, text => text.Text == "LOW TRITIUM");
            Assert.Contains(visible, text => text.Text == "[ ICY BODY ]");
            Assert.Contains(visible, text => text.Text == "[ PRISTINE ICY RING ]");
            Assert.DoesNotContain(visible, text => text.Text?.Contains("JUMPS LEFT", StringComparison.Ordinal) == true);
            frame!.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-next-jump-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaFact]
    public async Task PassiveCoordinatorOpensWithoutARouteAndOmitsUnavailableFuelRows()
    {
        string directory = Path.Combine(Path.GetTempPath(), "carrier-next-jump-host-" + Guid.NewGuid().ToString("N"));
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
            new RouteNameImporter(new Resolver()),
            new Results(),
            FollowRouteKind.FleetCarrier
        );
        await route.UpdateContextAsync("F1", "Origin", 1, null);
        using var host = new HostedOverlayTestHarness(new OverlayWindowRegistry());
        using var coordinator = new FleetCarrierRouteOverlayCoordinator(route, host.Session);
        try
        {
            await route.ApplyJournalEventsAsync([
                Parse(
                    $$"""{"event":"CarrierJumpRequest","CarrierID":1,"SystemName":"Destination","DepartureTime":"{{DateTimeOffset.UtcNow.AddMinutes(15):O}}"}"""
                ),
            ]);
            FleetCarrierRouteOverlayWindow window = Assert.Single(
                host.PreparedWindows.OfType<FleetCarrierRouteOverlayWindow>()
            );
            Assert.True(window.IsVisible);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            TextBlock[] visible = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();
            Assert.Contains(visible, text => text.Text == "Destination");
            Assert.DoesNotContain(visible, text => text.Text is "TRITIUM" or "—" or "In market" or "Fuel left");
            route.ShowCarrierNextJumpInformation = false;
            Assert.DoesNotContain(
                host.PreparedWindows.OfType<FleetCarrierRouteOverlayWindow>(),
                candidate => candidate.IsVisible
            );
            route.ShowCarrierNextJumpInformation = true;
            Assert.Contains(
                host.PreparedWindows.OfType<FleetCarrierRouteOverlayWindow>(),
                candidate => candidate.IsVisible
            );
        }
        finally
        {
            route.CancelCarrierDestinationLookup();
            await route.FlushWorkspaceAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(100, -50)]
    public void RestockWarningIconMatchesTheTextHeightAndRowCenter(int titleScale, int iconScale)
    {
        var preview = new OverlayPositionPreviewWindow(OverlayLayoutCatalog.GetRequired("PlotFleetCarrierRoute"));
        try
        {
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureTypography(OverlayTypographyScale.Default with { Title = titleScale, Icons = iconScale });
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Control icon =
                (Control?)preview.RuntimePresentation!.FindControl<Avalonia.Controls.Shapes.Path>("CarrierWarningIcon")
                ?? preview.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "⚠");
            TextBlock label = preview
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "RESTOCK REQUIRED");
            Point iconOrigin = icon.TranslatePoint(default, preview)!.Value;
            Point labelOrigin = label.TranslatePoint(default, preview)!.Value;
            Point iconEnd = icon.TranslatePoint(new Point(icon.Bounds.Width, icon.Bounds.Height), preview)!.Value;
            double height = iconEnd.Y - iconOrigin.Y;
            if (icon is TextBlock textIcon)
            {
                height *= textIcon.TextLayout.Height / icon.Bounds.Height;
            }
            Assert.True(height >= label.FontSize - 0.1, $"Icon height {height}, label font {label.FontSize}");
            Assert.InRange(Math.Abs(iconOrigin.Y + height / 2 - labelOrigin.Y - label.Bounds.Height / 2), 0, 1);
        }
        finally
        {
            preview.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1100, 760)]
    [InlineData(880, 560)]
    public async Task CarrierSelectorAndShortfallDialogFitAndRefreshContinuesAtTheCurrentWaypoint(int width, int height)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-carrier-fuel-ui-{Guid.NewGuid():N}");
        var at = DateTimeOffset.Parse("2026-10-15T12:00:00Z", CultureInfo.InvariantCulture);
        FrontierCarrierSnapshot carrier = FrontierCapiSnapshotParser.ParseCarrier(
            """
            {"id":1,"name":{"callsign":"OWN-001"},"currentStarSystem":"A","fuel":5,
             "capacity":{"cargoNotForSale":1000,"freeSpace":24000},"cargo":[]}
            """,
            at
        );
        var account = new FrontierAccountSnapshot(
            "CMDR",
            0,
            0,
            false,
            true,
            "Elsewhere",
            "",
            null,
            [],
            [],
            [],
            carrier,
            at,
            SquadronCarrier: carrier with
            {
                Callsign = "SQU-002",
                FuelData = carrier.FuelData! with
                {
                    Kind = FollowRouteCarrierKind.Squadron,
                    Callsign = "SQU-002",
                    CarrierId = 2,
                    TankTonnes = 900,
                    UsedCapacityTonnes = 40000,
                },
            }
        );
        var model = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
            new RouteNameImporter(new Resolver()),
            new Results(),
            new RouteWorkspaceOptions(FollowRouteKind.FleetCarrier, CarrierSnapshotSource: () => account)
        );
        await model.UpdateContextAsync("F1", "Elsewhere", 99, null);
        var window = new RouteWindow(model) { Width = width, Height = height };
        try
        {
            window.Show();
            Render(window, width, "selector");
            ComboBox selector = window.FindControl<ComboBox>("RouteCarrierSelector")!;
            Assert.Equal(2, selector.ItemCount);
            Assert.True(selector.IsEffectivelyVisible);
            Assert.True(selector.Bounds.Width >= 200);
            selector.SelectedIndex = 1;
            Assert.Equal(FollowRouteCarrierKind.Squadron, model.SelectedCarrierKind);
            Assert.Contains("SQU-002", window.FindControl<TextBlock>("CarrierFuelSummaryReadout")!.Text);
            selector.SelectedIndex = 0;
            await model.ImportSpanshUrlAsync(
                "https://spansh.co.uk/fleet-carrier/results/74FA2952-2048-11F1-8302-B948FF6DF5C1"
            );
            Render(window, width, "shortfall");
            Assert.True(model.IsFuelWarningVisible);
            TextBlock warning = window.FindControl<TextBlock>("CarrierFuelWarningText")!;
            Assert.True(warning.IsEffectivelyVisible);
            Assert.Contains("Mine or buy", warning.Text);
            Assert.Contains(
                model.AdditionalTritiumNeeded!.Value.ToString("N0", CultureInfo.CurrentCulture),
                warning.Text
            );
            Assert.InRange(warning.Bounds.Width, 100, width - 50);
            model.CancelDialogCommand.Execute(null);
            Assert.False(model.IsFuelWarningVisible);
            await model.ApplyJournalEventsAsync([
                Parse(
                    """{"timestamp":"2026-10-15T12:00:01Z","event":"CarrierJump","MarketID":1,"StarSystem":"B","StarPos":[100,0,0]}"""
                ),
                Parse(
                    """{"timestamp":"2026-10-15T12:00:02Z","event":"CarrierDepositFuel","CarrierID":1,"Total":300}"""
                ),
            ]);
            await model.RefreshAsync();
            Render(window, width, "resupplied");
            Assert.False(model.IsFuelWarningVisible);
            Assert.Equal(2, model.ReachedCount);
            Assert.Equal("C", model.NextHopName);
            Assert.Equal(0, model.AdditionalTritiumNeeded);
            Assert.Equal(0, model.Hops[1].Hop.Carrier!.FuelUsedTonnes);
            Assert.Equal(150, model.Hops[2].Hop.Carrier!.DistanceLy);
        }
        finally
        {
            window.Close();
            await model.FlushWorkspaceAsync();
            Directory.Delete(directory, true);
        }
    }

    private static void Render(Window window, int width, string stage)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_ROUTE_RENDER_OUTPUT");
        if (output is not null)
        {
            Directory.CreateDirectory(output);
            using FileStream stream = File.Create(Path.Combine(output, $"carrier-fuel-{width}-{stage}.png"));
            frame.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
        return entry!;
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class Results : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IReadOnlyList<FollowRouteHop>>([
                new("A", 1, new(0, 0, 0), null, false, false),
                new("B", 2, new(100, 0, 0), null, false, false),
                new("C", 3, new(250, 0, 0), null, false, false),
            ]);
    }
}
