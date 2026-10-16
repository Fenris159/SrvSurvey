using System.Text.Json.Nodes;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class FollowRouteRepeatTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-repeat-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(FollowRouteKind.Standard)]
    [InlineData(FollowRouteKind.FleetCarrier)]
    public async Task RepeatFromStartKeepsCyclingWithoutCompletingTheRoute(FollowRouteKind kind)
    {
        (FollowRouteStore store, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            kind,
            FollowRouteRepeatMode.FromStart
        );
        for (int pass = 0; pass < 3; pass++)
        {
            route = (await service.ApplyArrivalAsync(route, "B", 2)).Route;
            bool carrier = kind == FollowRouteKind.FleetCarrier;
            FollowRouteArrivalResult end = await service.ApplyArrivalAsync(route, carrier ? "A" : "C", carrier ? 1 : 3);
            route = end.Route;

            Assert.True(end.Restarted);
            Assert.False(end.Completed);
            Assert.True(route.IsActive);
            Assert.Equal(carrier ? 0 : -1, route.LastReachedIndex);
            Assert.Equal(carrier ? "B" : "A", route.NextHop!.Name);
            string[] expectedNames = carrier ? ["A", "B", "A"] : ["A", "B", "C"];
            Assert.Equal(expectedNames, route.GetNavigationHops().Select(hop => hop.Name));
            Assert.False((await service.ApplyArrivalAsync(route, carrier ? "A" : "C", carrier ? 1 : 3)).Changed);
            if (!carrier)
            {
                route = (await service.ApplyArrivalAsync(route, "A", 1)).Route;
            }
        }
        FollowRouteDocument saved = (await store.LoadAsync("F123")).Route!;
        Assert.Equal(FollowRouteRepeatMode.FromStart, saved.RepeatMode);
        Assert.True(saved.IsActive);
        Assert.Equal("B", saved.NextHop!.Name);
    }

    [Theory]
    [InlineData(FollowRouteKind.Standard)]
    [InlineData(FollowRouteKind.FleetCarrier)]
    public async Task ReverseModeAlternatesFullItinerariesAndPersistsEachTurnaround(FollowRouteKind kind)
    {
        (FollowRouteStore store, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            kind,
            FollowRouteRepeatMode.Reverse
        );
        for (int pass = 0; pass < 4; pass++)
        {
            route = (await service.ApplyArrivalAsync(route, "B", 2)).Route;
            string endpoint = pass % 2 == 0 ? "C" : "A";
            long address = pass % 2 == 0 ? 3 : 1;
            FollowRouteArrivalResult end = await service.ApplyArrivalAsync(route, endpoint, address);
            route = end.Route;

            Assert.True(end.Restarted);
            Assert.False(end.Completed);
            Assert.Equal(0, route.LastReachedIndex);
            Assert.Equal("B", route.NextHop!.Name);
            Assert.Equal(pass % 2 == 0, route.IsReversed);
            string[] expectedNames = pass % 2 == 0 ? ["C", "B", "A"] : ["A", "B", "C"];
            Assert.Equal(expectedNames, route.GetNavigationHops().Select(hop => hop.Name));
            Assert.Equal(["A", "B", "C"], route.Hops.Select(hop => hop.Name));
            Assert.True(Assert.Single(route.Hops[0].BioTargets).IsCompleted);
            Assert.False((await service.ApplyArrivalAsync(route, endpoint, address)).Changed);
            FollowRouteDocument saved = (await store.LoadAsync("F123")).Route!;
            Assert.Equal(route.IsReversed, saved.IsReversed);
            Assert.Equal("B", saved.NextHop!.Name);
            Assert.Equal("Keep C notes", saved.Hops[2].Notes);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OpenCarrierRoutesStopEvenWhenLoadedWithRepeatFromStart(bool matchingNames)
    {
        (_, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.FleetCarrier,
            FollowRouteRepeatMode.Reverse
        );
        route = route with { RepeatMode = FollowRouteRepeatMode.FromStart };
        if (matchingNames)
        {
            route = route with { Hops = [route.Hops[0], route.Hops[1], route.Hops[2] with { Name = "A" }] };
        }
        Assert.False(FollowRouteProgression.CanRepeatFromStart(route.Kind, route.Hops));
        route = (await service.ApplyArrivalAsync(route, "B", 2, persist: false)).Route;
        FollowRouteArrivalResult result = await service.ApplyArrivalAsync(
            route,
            matchingNames ? "A" : "C",
            3,
            persist: false
        );
        Assert.True(result.Completed);
        Assert.False(result.Route.IsActive);
        Assert.False(result.Restarted);
    }

    [Theory]
    [InlineData(FollowRouteRepeatMode.FromStart)]
    [InlineData(FollowRouteRepeatMode.Reverse)]
    public async Task ActivatingACompletedRepeatingRouteBeginsTheNextPass(FollowRouteRepeatMode mode)
    {
        (FollowRouteStore store, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            mode
        );
        FollowRouteDocument activated = await service.SetActiveAsync(
            route with
            {
                IsActive = false,
                LastReachedIndex = 2,
            },
            true
        );

        Assert.True(activated.IsActive);
        Assert.Equal(mode == FollowRouteRepeatMode.Reverse, activated.IsReversed);
        Assert.Equal(mode == FollowRouteRepeatMode.Reverse ? "B" : "A", activated.NextHop!.Name);
        Assert.Equal(activated.LastReachedIndex, (await store.ReloadAsync(route)).Route!.LastReachedIndex);
    }

    [Fact]
    public async Task DisablingRepeatFinishesTheCurrentReversePassNormally()
    {
        (_, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            FollowRouteRepeatMode.Reverse
        );
        route = (await service.ApplyArrivalAsync(route, "C", 3)).Route;
        route = (await service.ApplyArrivalAsync(route with { RepeatMode = FollowRouteRepeatMode.None }, "B", 2)).Route;
        FollowRouteArrivalResult end = await service.ApplyArrivalAsync(route, "A", 1);

        Assert.True(end.Completed);
        Assert.False(end.Restarted);
        Assert.False(end.Route.IsActive);
        Assert.Null(end.Route.NextHop);
        Assert.True(end.Route.IsReversed);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, false)]
    public async Task EmptySingleWaypointAndPausedRoutesDoNotRestart(int count, bool active)
    {
        (_, _, FollowRouteDocument route) = await CreateAsync(FollowRouteKind.Standard, FollowRouteRepeatMode.Reverse);
        route = route with { Hops = route.Hops.Take(count).ToArray(), IsActive = active };

        FollowRouteDocument result = FollowRouteProgression.SetProgress(route, int.MaxValue);

        Assert.False(result.IsActive);
        Assert.Null(result.NextHop);
        Assert.False(result.IsReversed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedLoopStartsWithItsAlreadyReachedFirstSystem(bool useAddresses)
    {
        (_, _, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            FollowRouteRepeatMode.FromStart
        );
        FollowRouteHop first = route.Hops[0] with { SystemAddress = useAddresses ? 1 : null };
        route = route with { Hops = [first, route.Hops[1], first] };

        FollowRouteDocument next = FollowRouteProgression.SetProgress(route, 2);

        Assert.Equal(0, next.LastReachedIndex);
        Assert.Equal("B", next.NextHop!.Name);
        Assert.True(next.IsActive);
    }

    [Fact]
    public async Task ManualProgressAndReverseBodyProgressUseNavigationIndexes()
    {
        (FollowRouteStore store, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            FollowRouteRepeatMode.Reverse
        );
        route = await service.SetProgressAsync(route, 2);
        Assert.True(route.IsReversed);
        Assert.Equal("C", route.GetNavigationHop(0).Name);

        route = await service.SetBioTargetCompletedAsync(route, 2, 0, false);

        Assert.False(Assert.Single(route.Hops[0].BioTargets).IsCompleted);
        Assert.Empty(route.Hops[2].BioTargets);
        FollowRouteDocument saved = (await store.LoadAsync("F123")).Route!;
        Assert.True(saved.IsReversed);
        Assert.False(Assert.Single(saved.GetNavigationHop(2).BioTargets).IsCompleted);
    }

    [Fact]
    public async Task DraftArrivalsDoNotChangeTheSavedRoute()
    {
        (FollowRouteStore store, FollowRouteService service, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            FollowRouteRepeatMode.Reverse
        );
        byte[] before = await File.ReadAllBytesAsync(route.FilePath);

        FollowRouteArrivalResult result = await service.ApplyArrivalAsync(route, "C", 3, persist: false);

        Assert.True(result.Restarted);
        Assert.True(result.Route.IsReversed);
        Assert.Equal(before, await File.ReadAllBytesAsync(route.FilePath));
        Assert.False((await store.LoadAsync("F123")).Route!.IsReversed);
    }

    [Theory]
    [InlineData(FollowRouteKind.Standard, FollowRouteRepeatMode.FromStart)]
    [InlineData(FollowRouteKind.Standard, FollowRouteRepeatMode.Reverse)]
    [InlineData(FollowRouteKind.FleetCarrier, FollowRouteRepeatMode.FromStart)]
    [InlineData(FollowRouteKind.FleetCarrier, FollowRouteRepeatMode.Reverse)]
    public async Task RepeatSettingsRoundTripInWorkingAndSavedRoutes(FollowRouteKind kind, FollowRouteRepeatMode mode)
    {
        (FollowRouteStore store, _, FollowRouteDocument route) = await CreateAsync(kind, mode);
        await store.SaveProgressAsync(route with { IsReversed = true });
        FollowRouteDocument saved = (await store.LoadAsync("F123")).Route!;
        Assert.Equal(mode, saved.RepeatMode);
        Assert.True(saved.IsReversed);
        Assert.Equal("B", saved.NextHop!.Name);

        await store.SaveWorkspaceAsync(saved with { RepeatMode = FollowRouteRepeatMode.None }, true);
        FollowRouteDocument draft = (await store.LoadAsync("F123")).WorkspaceDraft!;
        Assert.Equal(FollowRouteRepeatMode.None, draft.RepeatMode);
        Assert.True(draft.IsReversed);

        await store.SaveProgressAsync(saved with { RepeatMode = FollowRouteRepeatMode.None, IsReversed = false });
        FollowRouteDocument cleared = (await store.ReloadAsync(saved)).Route!;
        Assert.Equal(FollowRouteRepeatMode.None, cleared.RepeatMode);
        Assert.False(cleared.IsReversed);
        JsonObject json = JsonNode.Parse(await File.ReadAllTextAsync(saved.FilePath))!.AsObject();
        Assert.False(json.ContainsKey("repeatMode"));
        Assert.False(json.ContainsKey("reversed"));
    }

    [Theory]
    [InlineData("both")]
    [InlineData("999")]
    [InlineData("1")]
    public async Task InvalidRepeatModesAreRejected(string mode)
    {
        (FollowRouteStore store, _, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.Standard,
            FollowRouteRepeatMode.None
        );
        JsonObject json = JsonNode.Parse(await File.ReadAllTextAsync(route.FilePath))!.AsObject();
        json["repeatMode"] = mode;
        await File.WriteAllTextAsync(route.FilePath, json.ToJsonString());

        FollowRouteLoadResult loaded = await store.ReloadAsync(route);

        Assert.False(loaded.IsSuccess);
        Assert.Contains("repeatMode", loaded.Error);
    }

    [Theory]
    [InlineData("Squadron", true)]
    [InlineData("personal", true)]
    [InlineData("Other", false)]
    [InlineData("1", false)]
    public async Task CarrierSelectionIsValidatedAndSurvivesProgressAndWorkspaceSaves(string value, bool valid)
    {
        (FollowRouteStore store, _, FollowRouteDocument route) = await CreateAsync(
            FollowRouteKind.FleetCarrier,
            FollowRouteRepeatMode.Reverse
        );
        JsonObject json = JsonNode.Parse(await File.ReadAllTextAsync(route.FilePath))!.AsObject();
        json["carrierKind"] = value;
        await File.WriteAllTextAsync(route.FilePath, json.ToJsonString());
        FollowRouteLoadResult loaded = await store.ReloadAsync(route);
        Assert.Equal(valid, loaded.IsSuccess);
        if (!valid)
        {
            Assert.Contains("carrierKind", loaded.Error);
            return;
        }
        FollowRouteCarrierKind expected =
            value == "Squadron" ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal;
        route = loaded.Route!;
        Assert.Equal(expected, route.CarrierKind);
        await store.SaveProgressAsync(route with { LastReachedIndex = 1 });
        Assert.Equal(expected, (await store.ReloadAsync(route)).Route!.CarrierKind);
        await store.SaveWorkspaceAsync(route, true);
        Assert.Equal(expected, (await store.LoadAsync("F123")).WorkspaceDraft!.CarrierKind);
    }

    private async Task<(FollowRouteStore Store, FollowRouteService Service, FollowRouteDocument Route)> CreateAsync(
        FollowRouteKind kind,
        FollowRouteRepeatMode mode
    )
    {
        var store = new FollowRouteStore(directory, kind);
        FollowRouteDocument route = await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                IsActive = true,
                LastReachedIndex = 0,
                RepeatMode = mode,
                Hops =
                [
                    new("A", 1, null, "Keep A notes", false, false, [new("A 1", 10, ["Bacteria"], IsCompleted: true)]),
                    new("B", 2, null, null, false, false),
                    kind == FollowRouteKind.FleetCarrier && mode == FollowRouteRepeatMode.FromStart
                        ? new("A", 1, null, "Keep C notes", false, false)
                        : new("C", 3, null, "Keep C notes", false, false),
                ],
            },
            $"Repeat {Guid.NewGuid():N}"
        );
        return (store, new FollowRouteService(store), route);
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
    }
}
