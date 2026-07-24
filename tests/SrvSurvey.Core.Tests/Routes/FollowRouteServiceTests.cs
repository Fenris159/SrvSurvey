using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FollowRouteServiceTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-service-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ActivationSkipsCurrentFirstHopBySystemAddress()
    {
        var (service, route) = CreateServiceAndRoute(isActive: false);

        var updated = await service.SetActiveAsync(route, true, 1);

        Assert.True(updated.IsActive);
        Assert.Equal(0, updated.LastReachedIndex);
        Assert.Equal("Second", updated.NextHop!.Name);
        Assert.True(File.Exists(updated.FilePath));
    }

    [Fact]
    public async Task EmptyAndCompletedRoutesCannotBeActivated()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        var service = new FollowRouteService(store);
        var empty = new FollowRouteDocument(
            "F123",
            store.GetPath("F123"),
            false,
            true,
            -1,
            []);
        var (_, complete) = CreateServiceAndRoute(
            isActive: false,
            lastReachedIndex: 2);

        var activatedEmpty = await service.SetActiveAsync(empty, true);
        var activatedComplete = await service.SetActiveAsync(complete, true);

        Assert.False(activatedEmpty.IsActive);
        Assert.False(activatedComplete.IsActive);
        Assert.Null(activatedEmpty.NextHop);
        Assert.Null(activatedComplete.NextHop);
    }

    [Fact]
    public async Task ExpectedArrivalAdvancesAndFinalArrivalCompletesRoute()
    {
        var (service, route) = CreateServiceAndRoute();

        var second = await service.ApplyArrivalAsync(route, "second", 2);
        var final = await service.ApplyArrivalAsync(
            second.Route,
            "Different casing is irrelevant when address matches",
            3);

        Assert.True(second.Changed);
        Assert.Equal(1, second.ReachedIndex);
        Assert.Equal("Third", second.Route.NextHop!.Name);
        Assert.True(final.Changed);
        Assert.True(final.Completed);
        Assert.Equal(2, final.Route.LastReachedIndex);
        Assert.False(final.Route.IsActive);
        Assert.Null(final.Route.NextHop);
    }

    [Fact]
    public async Task KnownOutOfOrderArrivalDoesNotAdvanceUnlessItIsFinalHop()
    {
        var (service, route) = CreateServiceAndRoute(lastReachedIndex: -1);

        var outOfOrder = await service.ApplyArrivalAsync(route, "Second", 2);
        var final = await service.ApplyArrivalAsync(route, "Third", 3);

        Assert.False(outOfOrder.Changed);
        Assert.Same(route, outOfOrder.Route);
        Assert.True(final.Changed);
        Assert.True(final.Completed);
    }

    [Fact]
    public async Task ArrivalMatchesNamesWithoutCaseAndIgnoresUnknownSystems()
    {
        var (service, route) = CreateServiceAndRoute(lastReachedIndex: -1);

        var unknown = await service.ApplyArrivalAsync(route, "Unknown", 99);
        var first = await service.ApplyArrivalAsync(route, "sOl", null);

        Assert.False(unknown.Changed);
        Assert.True(first.Changed);
        Assert.Equal(0, first.Route.LastReachedIndex);
        Assert.Equal("Second", first.Route.NextHop!.Name);
    }

    [Fact]
    public async Task ManualFinalProgressDisablesRouteAndUncheckingCanResume()
    {
        var (service, route) = CreateServiceAndRoute();

        var complete = await service.SetProgressAsync(route, 99);
        var resumed = await service.ReplaceAsync(
            complete,
            complete.Hops,
            0,
            true,
            complete.AutoCopy);

        Assert.Equal(2, complete.LastReachedIndex);
        Assert.True(complete.IsComplete);
        Assert.False(complete.IsActive);
        Assert.True(resumed.IsActive);
        Assert.Equal("Second", resumed.NextHop!.Name);
    }

    [Fact]
    public void DistanceRequiresCoordinatesOnBothHops()
    {
        var sol = Hop("Sol", 1, new GalacticCoordinate(0, 0, 0));
        var other = Hop("Other", 2, new GalacticCoordinate(3, 4, 0));
        var unresolved = Hop("Unknown", null, null);

        Assert.Equal(5, sol.DistanceTo(other));
        Assert.Null(sol.DistanceTo(unresolved));
    }

    private (FollowRouteService Service, FollowRouteDocument Route)
        CreateServiceAndRoute(
            bool isActive = true,
            int lastReachedIndex = 0)
    {
        var store = new FollowRouteStore(temporaryDirectory);
        return (
            new FollowRouteService(store),
            new FollowRouteDocument(
                "F123",
                store.GetPath("F123"),
                isActive,
                true,
                lastReachedIndex,
                [
                    Hop("Sol", 1, new GalacticCoordinate(0, 0, 0)),
                    Hop("Second", 2, new GalacticCoordinate(3, 4, 0)),
                    Hop("Third", 3, new GalacticCoordinate(3, 4, 12)),
                ]));
    }

    private static FollowRouteHop Hop(
        string name,
        long? address,
        GalacticCoordinate? position)
    {
        return new FollowRouteHop(
            name,
            address,
            position,
            null,
            false,
            false);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
