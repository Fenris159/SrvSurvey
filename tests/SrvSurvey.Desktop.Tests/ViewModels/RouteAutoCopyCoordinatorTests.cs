using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteAutoCopyCoordinatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-autocopy-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ClaimingClipboardDisablesAndPersistsTheCompetingRoute()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier);

        Assert.True(standard.ShouldAutoCopyNextHop);
        Assert.True(carrier.ShouldAutoCopyNextHop);

        await coordinator.ClaimAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);

        await carrier.SetAutoCopyAsync(true);
        await coordinator.ClaimAsync(carrier);

        Assert.True(carrier.AutoCopy);
        Assert.False(standard.AutoCopy);

        var standardSaved = await new FollowRouteStore(temporaryDirectory)
            .LoadAsync("F123");
        var carrierSaved = await new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier).LoadAsync("F123");
        Assert.False(standardSaved.Route!.AutoCopy);
        Assert.True(carrierSaved.Route!.AutoCopy);
    }

    [Fact]
    public async Task InactiveRouteDoesNotTakeOwnershipFromActiveRoute()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(
            FollowRouteKind.FleetCarrier,
            isActive: false);
        using var coordinator = new RouteAutoCopyCoordinator(standard, carrier);

        await coordinator.ClaimAsync(carrier);

        Assert.True(standard.ShouldAutoCopyNextHop);
        Assert.False(carrier.ShouldAutoCopyNextHop);
        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    [Fact]
    public async Task LatePropertyChangeClaimIsIgnoredAfterDisposal()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var coordinator = new RouteAutoCopyCoordinator(standard, carrier);
        coordinator.Dispose();

        await coordinator.ClaimAfterPropertyChangeAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    private async Task<RouteWorkspaceViewModel> CreateWorkspaceAsync(
        FollowRouteKind kind,
        bool isActive = true)
    {
        var store = new FollowRouteStore(temporaryDirectory, kind);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = isActive,
                AutoCopy = true,
                LastReachedIndex = 0,
                Hops =
                [
                    new FollowRouteHop("Sol", 1, null, null, false, false),
                    new FollowRouteHop("Achenar", 2, null, null, false, false),
                ],
            },
            kind == FollowRouteKind.FleetCarrier
                ? "Carrier Route"
                : "Standard Route");
        var workspace = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind);
        await workspace.UpdateContextAsync("F123", "Sol", 1, null);
        return workspace;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
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
