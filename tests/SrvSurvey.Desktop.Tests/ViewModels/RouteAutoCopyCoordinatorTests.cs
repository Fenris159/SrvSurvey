using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class RouteAutoCopyCoordinatorTests : IAsyncLifetime
{
    private readonly List<BoxelSearchSession> sessions = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-autocopy-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ClaimingClipboardDisablesAndPersistsTheCompetingRoute()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var boxel = CreateInactiveBoxel();
        using var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            boxel.Session);

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
    public async Task InactiveRouteSelectionStillOwnsTheAutoCopySetting()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(
            FollowRouteKind.FleetCarrier,
            isActive: false);
        using var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            CreateInactiveBoxel().Session);

        await coordinator.ClaimAsync(carrier);

        Assert.False(standard.ShouldAutoCopyNextHop);
        Assert.False(carrier.ShouldAutoCopyNextHop);
        Assert.False(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    [Fact]
    public async Task LatePropertyChangeClaimIsIgnoredAfterDisposal()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            CreateInactiveBoxel().Session);
        coordinator.Dispose();

        await coordinator.ClaimAfterPropertyChangeAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);
    }

    [Fact]
    public async Task BoxelAndBothRouteTypesShareOneAutoCopyOwner()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var boxel = await CreateConfiguredBoxelAsync(autoCopy: true);
        using var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            boxel.Session);

        await coordinator.ReconcileAsync();

        Assert.True(boxel.AutoCopy);
        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);

        await standard.SetAutoCopyAsync(true);
        await WaitUntilAsync(() => !boxel.AutoCopy);
        // The selection event starts an asynchronous ownership claim. Entering
        // the coordinator once more drains that work before the test removes
        // its profile directory.
        await coordinator.ClaimAsync(standard);

        Assert.True(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        Assert.False(boxel.AutoCopy);
        var savedProfile = await new CommanderProfileStore(temporaryDirectory)
            .LoadAsync("F123", true);
        Assert.False(savedProfile.Data!.BoxelSearch.AutoCopy);
    }

    [AvaloniaFact]
    public async Task SelectingBoxelAutoCopyAutomaticallyClearsBothRouteSelections()
    {
        var standard = await CreateWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateWorkspaceAsync(FollowRouteKind.FleetCarrier);
        var boxel = await CreateConfiguredBoxelAsync(autoCopy: false);
        using var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            boxel.Session);
        await coordinator.ClaimAsync(standard);

        await Task.Run(() => boxel.AutoCopy = true);
        await WaitUntilAsync(() => !standard.AutoCopy && !carrier.AutoCopy);
        await coordinator.ClaimAsync(boxel.Session);
        var profileStore = new CommanderProfileStore(temporaryDirectory);
        await WaitUntilAsync(async () =>
            (await profileStore.LoadAsync("F123", true))
                .Data?.BoxelSearch.AutoCopy == true);

        Assert.True(boxel.AutoCopy);
        var standardSaved = await new FollowRouteStore(temporaryDirectory)
            .LoadAsync("F123");
        var carrierSaved = await new FollowRouteStore(
            temporaryDirectory,
            FollowRouteKind.FleetCarrier).LoadAsync("F123");
        Assert.False(standardSaved.Route!.AutoCopy);
        Assert.False(carrierSaved.Route!.AutoCopy);
    }

    [Fact]
    public async Task ReconcileClearsImplicitSelectionsWithoutSavedRoutes()
    {
        var standard = await CreateUnsavedWorkspaceAsync(FollowRouteKind.Standard);
        var carrier = await CreateUnsavedWorkspaceAsync(FollowRouteKind.FleetCarrier);
        using var coordinator = new RouteAutoCopyCoordinator(
            standard,
            carrier,
            CreateInactiveBoxel().Session);

        Assert.True(standard.AutoCopy);
        Assert.True(carrier.AutoCopy);

        await coordinator.ReconcileAsync();

        Assert.False(standard.AutoCopy);
        Assert.False(carrier.AutoCopy);
        Assert.False(standard.IsDirty);
        Assert.False(carrier.IsDirty);
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

    private async Task<RouteWorkspaceViewModel> CreateUnsavedWorkspaceAsync(
        FollowRouteKind kind)
    {
        var store = new FollowRouteStore(temporaryDirectory, kind);
        var workspace = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient(),
            kind);
        await workspace.UpdateContextAsync("F123", "Sol", 1, null);
        return workspace;
    }

    private BoxelSearchViewModel CreateInactiveBoxel()
    {
        var viewModel = BoxelSearchViewModelTestFactory.Create(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyBoxelResolver(),
            out var session);
        sessions.Add(session);
        return viewModel;
    }

    private async Task<BoxelSearchViewModel> CreateConfiguredBoxelAsync(
        bool autoCopy,
        bool active = false)
    {
        var boxel = CreateInactiveBoxel();
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        await boxel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            new BoxelSearchSnapshot
            {
                Active = active,
                TopBoxel = top,
                Current = top,
                StartedOn = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                CurrentCount = 1,
                LowMassCode = 'c',
                AutoCopy = autoCopy,
            });
        return boxel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition() && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var satisfied = await condition();
        while (!satisfied && !timeout.IsCancellationRequested)
        {
            await Task.Delay(10);
            satisfied = await condition();
        }

        Assert.True(satisfied);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.AsEnumerable().Reverse())
        {
            await session.DisposeAsync();
        }

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

    private sealed class EmptyBoxelResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }
}
