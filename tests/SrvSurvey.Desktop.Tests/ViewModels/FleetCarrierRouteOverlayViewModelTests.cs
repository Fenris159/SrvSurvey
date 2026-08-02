using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FleetCarrierRouteOverlayViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-fc-overlay-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task FormatsNextCarrierHopAsCompactLogisticsReadout()
    {
        var store = new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier);
        var document = (await store.CreateNewAsync("F123")) with
        {
            Name = "Carrier Test",
            IsActive = true,
            LastReachedIndex = 0,
            Kind = FollowRouteKind.FleetCarrier,
            Hops =
            [
                Hop("Sol", null),
                Hop("Col 359 Sector EE-X b16-1", new FollowRouteCarrierHop(
                    499.76,
                    21502.09,
                    1000,
                    2799,
                    93,
                    true,
                    true,
                    true,
                    3892)),
                Hop("Colonia", null),
            ],
        };
        await store.SaveAsAsync(document, "Carrier Test");
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            FollowRouteKind.FleetCarrier);
        await route.UpdateContextAsync("F123", "Sol", null, null);
        using var viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            OverlayPlatformCapabilities.ForHost(OverlayHostKind.Other));

        Assert.Equal("HOP 2 / 3", viewModel.HopProgress);
        Assert.Equal("Col 359 Sector EE-X b16-1", viewModel.SystemName);
        Assert.Equal(
            $"{499.76:N2} LY JUMP  •  {21502.09:N2} LY REMAINING",
            viewModel.JumpSummary);
        Assert.Equal("1 JUMP LEFT", viewModel.JumpsLeft);
        Assert.Equal($"{1000:N0} t", viewModel.FuelLeft);
        Assert.Equal($"{2799:N0} t", viewModel.TritiumInMarket);
        Assert.Equal($"{93:N0} t", viewModel.JumpFuel);
        Assert.True(viewModel.HasIcyRing);
        Assert.Equal("PRISTINE ICY RING", viewModel.IcyRingLabel);
        Assert.True(viewModel.HasRestockWarning);
        Assert.Equal($"{3892:N0} t", viewModel.RestockAmount);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static FollowRouteHop Hop(
        string name,
        FollowRouteCarrierHop? carrier)
    {
        return new FollowRouteHop(
            name,
            null,
            null,
            null,
            false,
            false,
            Carrier: carrier);
    }

    private sealed class EmptyResolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
        }
    }

    private sealed class EmptySpanshClient : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
        }
    }
}
