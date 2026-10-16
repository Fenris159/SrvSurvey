using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class CarrierRouteFuelPresentationTests
{
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
