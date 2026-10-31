using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class SharedCarrierWorkspaceNotesTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-shared-carrier-notes-{Guid.NewGuid():N}"
    );
    private readonly List<RouteWorkspaceViewModel> workspaces = [];

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PopoutNotesReachTheOtherCarrierWithoutReplacingItsUnsavedNotes(
        bool editSquadron,
        bool retainedDraft
    )
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteStore fcStore = library.ForCarrier(FollowRouteCarrierKind.Personal);
        FollowRouteStore scStore = library.ForCarrier(FollowRouteCarrierKind.Squadron);
        FollowRouteDocument saved = await fcStore.SaveAsAsync(
            (await fcStore.CreateNewAsync("F1")) with
            {
                Notes = "Original shared notes",
                Hops = [new("Sol", 1, null, null, false, false), new("Lave", 2, null, null, false, false)],
            },
            "Shared"
        );
        FollowRouteStore editorStore = editSquadron ? scStore : fcStore;
        FollowRouteStore peerStore = editSquadron ? fcStore : scStore;
        FollowRouteCarrierKind editorKind = editSquadron
            ? FollowRouteCarrierKind.Squadron
            : FollowRouteCarrierKind.Personal;
        FollowRouteCarrierKind peerKind = editSquadron
            ? FollowRouteCarrierKind.Personal
            : FollowRouteCarrierKind.Squadron;
        FollowRouteDocument peerRoute = (await peerStore.LoadNamedAsync("F1", "Shared.json", false)).Route!;
        if (retainedDraft)
        {
            await peerStore.SaveWorkspaceAsync(peerRoute with { Notes = "My unsaved working notes" }, true);
        }
        RouteWorkspaceViewModel editor = CreateWorkspace(editorStore, editorKind);
        RouteWorkspaceViewModel peer = CreateWorkspace(peerStore, peerKind);
        editor.ShareCarrierLibraryWith(peer);
        await editor.UpdateContextAsync("F1", "Elsewhere", 99, null);
        await peer.UpdateContextAsync("F1", "Elsewhere", 99, null);
        await editor.LoadSavedRouteAsync("Shared.json", false);
        if (!retainedDraft)
        {
            await peer.LoadSavedRouteAsync("Shared.json", false);
        }
        peer.NotesCommand.Execute(null);
        Assert.True(peer.IsNotesVisible);
        peer.NotesDraft = "My unfinished dialog notes";
        editor.NotesDraft = "New shared supply instructions";
        await editor.SaveNotesAsync();
        Assert.Equal("New shared supply instructions", editor.RouteNotesPreview);
        Assert.True(peer.IsNotesVisible);
        Assert.Equal("My unfinished dialog notes", peer.NotesDraft);
        if (retainedDraft)
        {
            Assert.Equal("My unsaved working notes", peer.RouteNotesPreview);
            Assert.True(peer.HasNotesChanges);
            peer.CancelDialogCommand.Execute(null);
            await peer.DiscardAsync();
        }
        Assert.Equal("New shared supply instructions", peer.RouteNotesPreview);
        Assert.False(peer.HasNotesChanges);
        FollowRouteDocument stored = (await library.LoadNamedAsync("F1", "Shared.json", false)).Route!;
        Assert.Equal("New shared supply instructions", stored.Notes);
        Assert.Equal(saved.Hops.Select(hop => hop.Name), stored.Hops.Select(hop => hop.Name));
    }

    private RouteWorkspaceViewModel CreateWorkspace(FollowRouteStore store, FollowRouteCarrierKind kind)
    {
        var workspace = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new Resolver()),
            new Routes(),
            new RouteWorkspaceOptions(FollowRouteKind.FleetCarrier, DedicatedCarrier: kind)
        );
        workspaces.Add(workspace);
        return workspace;
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (RouteWorkspaceViewModel workspace in workspaces)
        {
            await workspace.FlushWorkspaceAsync();
        }
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class Routes : ISpanshRouteClient
    {
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<FollowRouteHop>>([]);
    }
}
