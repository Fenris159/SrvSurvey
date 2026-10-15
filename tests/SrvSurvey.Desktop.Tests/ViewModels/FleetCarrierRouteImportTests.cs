using System.Net;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierRouteImportTests
{
    private const string RouteUrl = "https://spansh.co.uk/fleet-carrier/results/41B4D994-C830-11F1-B393-A7063E1886B9";

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
        }
        finally
        {
            route.SpanshSearch!.Dispose();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class ResultHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }

    private sealed class Planner : ISpanshPlannerClient
    {
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
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }
}
