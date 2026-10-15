using System.Net;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierRouteImportTests
{
    private const string RouteUrl = "https://spansh.co.uk/fleet-carrier/results/41B4D994-C830-11F1-B393-A7063E1886B9";

    /// <summary>Legacy routes without fuel costs retain their actual rows and explain unavailable calculations.</summary>
    [Fact]
    public async Task MissingCarrierFuelDataKeepsTheOriginalTableAvailable()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey-CarrierMissingFuel-" + Guid.NewGuid().ToString("N")
        );
        var store = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F1")) with
            {
                Hops = [new("Sol", 1, null, "Original note", false, false)],
            },
            "Legacy route"
        );
        using var http = new HttpClient(new ResultHandler("{}"));
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new Resolver()),
            new SpanshRouteClient(http),
            FollowRouteKind.FleetCarrier
        );
        try
        {
            await route.UpdateContextAsync("F1", null, null, null);
            FollowRouteHop original = route.Hops[0].Hop;
            route.RestockOnlyWhenLow = true;
            Assert.True(route.HasCarrierFuelPlanMessage);
            Assert.Contains("require starting tank", route.CarrierFuelPlanMessage);
            Assert.Same(original, route.Hops[0].Hop);
            route.RestockOnlyWhenLow = false;
            Assert.False(route.HasCarrierFuelPlanMessage);
            await route.ConfirmNewAsync();
            Assert.False(route.RestockOnlyWhenLow);
        }
        finally
        {
            await route.FlushWorkspaceAsync();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Checks that saved carrier rows leave unavailable restock amounts blank.
    /// </summary>
    [Theory]
    [InlineData(false, 0d)]
    [InlineData(true, 0d)]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 900d)]
    public void SavedCarrierRowsLeaveUnavailableRestockAmountsBlank(bool restock, double? amount)
    {
        var hop = new FollowRouteHop(
            "Sol",
            null,
            null,
            null,
            false,
            false,
            Carrier: new FollowRouteCarrierHop(null, null, 0, 0, 0, false, false, restock, amount)
        );
        var row = new RouteHopItemViewModel(0, 1, hop, new("", "", 0, true, false, false, true));

        Assert.Equal(restock ? "YES" : "", row.CarrierRestock);
        Assert.Empty(row.CarrierRestockAmount);
        Assert.Equal($"{0:N0}", row.CarrierFuelRemaining);
    }

    /// <summary>
    /// Checks that reported carrier logistics reach the workspace from clipboard and search.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportedCarrierLogisticsReachTheWorkspaceFromClipboardAndSearch(bool search)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-CarrierImport-" + Guid.NewGuid().ToString("N"));
        string payload = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spansh", "fleet-carrier-41b4.json")
        );
        using var http = new HttpClient(new ResultHandler(payload));
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
            new RouteNameImporter(new Resolver()),
            new SpanshRouteClient(http, new Uri("https://example.test/api/")),
            FollowRouteKind.FleetCarrier,
            plannerClient: new Planner()
        );
        try
        {
            await route.UpdateContextAsync("F1", "Sol", 10477373803, new(0, 0, 0));
            if (search)
            {
                SpanshSystemListViewModel destinations = route
                    .SpanshSearch!.Fields.Single(entry => entry.Definition.Key == "destinations")
                    .SystemList!;
                destinations.Draft = "Colonia";
                destinations.CommitCommand.Execute(null);
                Assert.True(await route.SpanshSearch.SearchAsync(), route.SpanshSearch.Status);
            }
            else
            {
                await route.ImportSpanshUrlAsync(RouteUrl);
            }

            Assert.Equal(46, route.Hops.Count);
            Assert.Equal($"{1000:N0}", route.Hops[0].CarrierFuelRemaining);
            Assert.Equal($"{150:N0}", route.Hops[17].CarrierFuelRemaining);
            Assert.Equal($"{0:N0}", route.Hops[45].CarrierFuelRemaining);
            Assert.All(route.Hops, entry => Assert.NotEqual("—", entry.CarrierFuelRemaining));
            RouteHopItemViewModel[] restocks = route.Hops.Where(entry => entry.Hop.Carrier!.MustRestock).ToArray();
            Assert.Equal(["Flyiedgiae ZQ-S b8-1", "Clooku PO-N c8-115"], restocks.Select(entry => entry.Name));
            Assert.All(restocks, entry => Assert.Equal("YES", entry.CarrierRestock));
            Assert.Equal($"{953:N0}", restocks[0].CarrierRestockAmount);
            Assert.Equal($"{937:N0}", restocks[1].CarrierRestockAmount);
            Assert.All(
                route.Hops.Except(restocks),
                entry =>
                {
                    Assert.Empty(entry.CarrierRestock);
                    Assert.Empty(entry.CarrierRestockAmount);
                    Assert.Null(entry.Hop.Carrier!.RestockAmountTonnes);
                }
            );
            using var overlay = new FleetCarrierRouteOverlayViewModel(
                route,
                OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other)
            );
            Assert.Equal($"{1000:N0} t", overlay.FuelLeft);

            FollowRouteHop[] originals = route.CreateSnapshot()!.Hops.ToArray();
            RouteHopItemViewModel[] rows = route.Hops.ToArray();
            route.RestockOnlyWhenLow = true;

            Assert.Equal($"{893:N0}", route.Hops[1].CarrierFuelRemaining);
            Assert.Equal($"{941:N0}", route.Hops[1].CarrierTritiumInMarket);
            Assert.Equal($"{984:N0}", route.Hops[9].CarrierFuelRemaining);
            Assert.Equal("YES", route.Hops[9].CarrierRestock);
            Assert.Equal($"{941:N0}", route.Hops[9].CarrierRestockAmount);
            Assert.Equal($"{0:N0}", route.Hops[9].CarrierTritiumInMarket);
            Assert.Equal($"{47:N0}", route.Hops[18].CarrierFuelRemaining);
            Assert.Empty(route.Hops[18].CarrierRestock);
            Assert.DoesNotContain("Carrier tritium restock required", route.Hops[18].Notes);
            Assert.Equal("—", route.Hops[19].CarrierFuelRemaining);
            Assert.Contains(route.Hops[19].Name, route.CarrierFuelPlanMessage);
            Assert.True(route.HasCarrierFuelPlanMessage);
            Assert.Equal($"{893:N0} t", overlay.FuelLeft);
            Assert.Equal(originals, route.CreateSnapshot()!.Hops);
            Assert.True(route.CreateSnapshot()!.RestockOnlyWhenLow);
            Assert.All(rows, row => Assert.Same(row, route.Hops[row.Index]));

            route.SetProgressThrough(8, true);
            Assert.Equal($"{984:N0} t", overlay.FuelLeft);
            Assert.Equal($"{941:N0} t", overlay.RestockAmount);
            Assert.True(overlay.HasRestockWarning);
            Assert.Equal($"{893:N0}", route.Hops[1].CarrierFuelRemaining);
            await route.FlushWorkspaceAsync();

            var reopened = new RouteWorkspaceViewModel(
                new FollowRouteService(new FollowRouteStore(directory, FollowRouteKind.FleetCarrier)),
                new RouteNameImporter(new Resolver()),
                new SpanshRouteClient(http, new Uri("https://example.test/api/")),
                FollowRouteKind.FleetCarrier
            );
            await reopened.UpdateContextAsync("F1", "Sol", 10477373803, new(0, 0, 0));
            Assert.True(reopened.RestockOnlyWhenLow);
            Assert.Equal(9, reopened.ReachedCount);
            Assert.Equal($"{941:N0}", reopened.Hops[9].CarrierRestockAmount);

            reopened.RestockOnlyWhenLow = false;
            Assert.False(reopened.HasCarrierFuelPlanMessage);
            Assert.Equal(originals.Select(hop => hop.Carrier), reopened.Hops.Select(row => row.Hop.Carrier));
            Assert.Equal(originals.Select(hop => hop.Notes), reopened.Hops.Select(row => row.Hop.Notes));
            await reopened.FlushWorkspaceAsync();

            route.RestockOnlyWhenLow = false;
            Assert.Equal(originals, route.Hops.Select(row => row.Hop));
            Assert.False(route.HasCarrierFuelPlanMessage);
            route.RestockOnlyWhenLow = true;
            await route.ImportSpanshUrlAsync(RouteUrl);
            Assert.False(route.RestockOnlyWhenLow);
            Assert.Equal(originals.Select(hop => hop.Carrier), route.Hops.Select(row => row.Hop.Carrier));
        }
        finally
        {
            route.SpanshSearch!.Dispose();
            await route.FlushWorkspaceAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class ResultHandler(string payload) : HttpMessageHandler
    {
        /// <summary>
        /// Serves configured HTTP fixtures and captures requests without network access.
        /// </summary>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }

    private sealed class Planner : ISpanshPlannerClient
    {
        /// <summary>
        /// Returns a synthetic planner job while exposing the test's submission controls.
        /// </summary>
        public Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new SpanshRouteReference(Guid.Parse("41B4D994-C830-11F1-B393-A7063E1886B9"), request.Planner.Kind)
            );
    }

    private sealed class Resolver : IStarSystemResolver
    {
        /// <summary>
        /// Returns deterministic system matches without contacting an external provider.
        /// </summary>
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
