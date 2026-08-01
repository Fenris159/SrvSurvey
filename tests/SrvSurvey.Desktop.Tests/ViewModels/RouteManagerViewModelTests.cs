using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class RouteManagerViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-route-manager-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task SameCommanderAndCatalogRefreshPreserveCollectionAndRows()
    {
        var (store, _, manager) = await CreateViewModelsAsync();
        var routes = manager.Routes;
        var alpha = manager.Routes.Single(route => route.Name == "Alpha");
        alpha.IsSelected = true;
        var notifications = new List<string?>();
        manager.PropertyChanged += (_, eventArgs) =>
            notifications.Add(eventArgs.PropertyName);

        await manager.UpdateContextAsync("F123");

        Assert.Empty(notifications);
        Assert.Same(routes, manager.Routes);
        Assert.Same(alpha, manager.Routes.Single(route => route.Name == "Alpha"));

        await store.SaveNotesAsync(
            "F123",
            alpha.FileName,
            alpha.IsLegacy,
            "Updated outside the manager");
        await manager.RefreshAsync();

        var refreshedAlpha = manager.Routes.Single(route => route.Name == "Alpha");
        Assert.Same(routes, manager.Routes);
        Assert.Same(alpha, refreshedAlpha);
        Assert.True(refreshedAlpha.IsSelected);
        Assert.Equal("Updated outside the manager", refreshedAlpha.Notes);
    }

    [Fact]
    public async Task FavoritePersistsAndCanBeSortedAheadOfName()
    {
        var (store, _, manager) = await CreateViewModelsAsync();
        var alpha = manager.Routes.Single(route => route.Name == "Alpha");
        var beta = manager.Routes.Single(route => route.Name == "Beta");

        Assert.Same(alpha, manager.Routes[0]);
        await manager.ToggleFavoriteAsync(beta);

        Assert.True(beta.IsFavorite);
        Assert.Equal("★", beta.FavoriteGlyph);
        Assert.Same(beta, manager.Routes[0]);
        manager.FavoritesFirst = false;
        Assert.Same(alpha, manager.Routes[0]);

        var catalogBeta = (await store.ListAsync("F123"))
            .Single(route => route.Name == "Beta");
        Assert.True(catalogBeta.IsFavorite);
    }

    [Fact]
    public async Task NotesAndBulkDeleteUpdateFilesAndWorkspaceState()
    {
        var (_, workspace, manager) = await CreateViewModelsAsync();
        var beta = manager.Routes.Single(route => route.Name == "Beta");
        var betaPath = beta.FilePath;
        beta.EditNotesCommand.Execute(null);
        manager.NotesDraft = "Watch the neutron jump near waypoint five.";

        await manager.SaveNotesAsync();

        Assert.False(manager.IsDialogVisible);
        Assert.Equal(
            "Watch the neutron jump near waypoint five.",
            beta.Notes);

        beta.IsSelected = true;
        manager.RequestDeleteCommand.Execute(null);
        Assert.True(manager.IsDeleteConfirmationVisible);
        await manager.ConfirmDeleteAsync();

        Assert.False(File.Exists(betaPath));
        Assert.DoesNotContain(manager.Routes, route => route.Name == "Beta");
        Assert.False(workspace.HasSavedRoute);
        Assert.False(manager.IsDialogVisible);
    }

    [Fact]
    public async Task RouteCanBeActivatedAndDeactivatedWithoutOpeningWorkspace()
    {
        var (store, workspace, manager) = await CreateViewModelsAsync();
        var alpha = manager.Routes.Single(route => route.Name == "Alpha");

        await manager.ActivateAsync(alpha);

        Assert.True(workspace.HasSavedRoute);
        Assert.True(workspace.IsActive);
        Assert.Equal("Alpha", workspace.RouteName);
        Assert.Equal("Achenar", workspace.NextHopName);
        Assert.True(manager.CanDeactivate);
        Assert.Contains("Activated Alpha", manager.StatusMessage);

        await manager.DeactivateAsync();

        Assert.False(workspace.HasSavedRoute);
        Assert.False(workspace.IsActive);
        Assert.Equal("No active route", workspace.RouteName);
        Assert.False(manager.CanDeactivate);
        Assert.Contains("deactivated", manager.StatusMessage);

        var paused = await store.LoadNamedAsync(
            "F123",
            alpha.FileName,
            alpha.IsLegacy);
        Assert.False(paused.Route!.IsActive);
        Assert.Equal(0, paused.Route.LastReachedIndex);
    }

    [Fact]
    public async Task AutoCopyCanBeChangedAndPersistedFromRouteManager()
    {
        var (store, workspace, manager) = await CreateViewModelsAsync();
        var alpha = manager.Routes.Single(route => route.Name == "Alpha");
        await manager.ActivateAsync(alpha);

        Assert.True(manager.AutoCopy);
        Assert.True(manager.CanToggleAutoCopy);

        await manager.ToggleAutoCopyAsync();

        Assert.False(manager.AutoCopy);
        Assert.False(workspace.AutoCopy);
        Assert.Contains("disabled", manager.StatusMessage);
        var saved = await store.LoadNamedAsync(
            "F123",
            alpha.FileName,
            alpha.IsLegacy);
        Assert.False(saved.Route!.AutoCopy);

        await manager.DeactivateAsync();
        Assert.False(manager.CanToggleAutoCopy);
    }

    private async Task<(
        FollowRouteStore Store,
        RouteWorkspaceViewModel Workspace,
        RouteManagerViewModel Manager)> CreateViewModelsAsync()
    {
        var store = new FollowRouteStore(temporaryDirectory);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop("Sol", 1, null, null, false, false),
                    new FollowRouteHop("Achenar", 2, null, null, false, false),
                ],
            },
            "Alpha");
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F123")) with
            {
                Hops =
                [
                    new FollowRouteHop("Sol", 1, null, null, false, false),
                    new FollowRouteHop("Colonia", 3, null, null, false, false),
                ],
            },
            "Beta");
        var service = new FollowRouteService(store);
        var workspace = new RouteWorkspaceViewModel(
            service,
            new RouteNameImporter(new EmptyResolver()),
            new EmptySpanshClient());
        await workspace.UpdateContextAsync("F123", "Sol", 1, null);
        var manager = new RouteManagerViewModel(service, workspace);
        await manager.UpdateContextAsync("F123");
        return (store, workspace, manager);
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
