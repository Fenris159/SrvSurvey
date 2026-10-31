using System.Text.Json;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Core.Tests.Routes;

public sealed class CarrierRouteWorkspaceStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-carrier-workspaces-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task SharedLibraryKeepsCarrierProgressAndAutoCopySeparateAcrossRestarts()
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteStore personal = library.ForCarrier(FollowRouteCarrierKind.Personal);
        FollowRouteStore squadron = library.ForCarrier(FollowRouteCarrierKind.Squadron);
        FollowRouteDocument fc = await personal.SaveAsAsync(Itinerary(personal.GetPath("F1")), "Shared itinerary");
        FollowRouteDocument sc = (await squadron.LoadNamedAsync("F1", "Shared itinerary.json", false)).Route!;
        Assert.Equal(FollowRouteCarrierKind.Squadron, sc.CarrierKind);
        Assert.Equal(-1, sc.LastReachedIndex);
        Assert.False(sc.AutoCopy);
        fc = fc with { LastReachedIndex = 0, AutoCopy = true };
        sc = sc with { LastReachedIndex = 1, AutoCopy = false };
        await personal.SaveProgressAsync(fc);
        await squadron.SaveProgressAsync(sc);
        await personal.SaveWorkspaceAsync(fc, true);
        await squadron.SaveWorkspaceAsync(sc, true);
        var restarted = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteLoadResult restoredFc = await restarted.ForCarrier(FollowRouteCarrierKind.Personal).LoadAsync("F1");
        FollowRouteLoadResult restoredSc = await restarted.ForCarrier(FollowRouteCarrierKind.Squadron).LoadAsync("F1");
        Assert.Equal(0, restoredFc.Route!.LastReachedIndex);
        Assert.True(restoredFc.Route.AutoCopy);
        Assert.Equal(1, restoredSc.Route!.LastReachedIndex);
        Assert.False(restoredSc.Route.AutoCopy);
        Assert.Equal(FollowRouteCarrierKind.Squadron, restoredSc.WorkspaceDraft!.CarrierKind);
        Assert.Single(await squadron.ListAsync("F1"));
        Assert.Null((await squadron.LoadAsync("F2")).WorkspaceDraft);
        await personal.SetFavoriteAsync("F1", "Shared itinerary.json", false, true);
        Assert.True(Assert.Single(await squadron.ListAsync("F1")).IsFavorite);
        Assert.Equal("Shared itinerary", Assert.Single(await personal.ListAsync("F1")).Name);
    }

    [Theory]
    [InlineData("arrival")]
    [InlineData("active")]
    [InlineData("autoCopy")]
    [InlineData("progress")]
    public async Task StaleCarrierTrackingCannotOverwriteSharedLibraryMetadataOrWaypoints(string operation)
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteStore personal = library.ForCarrier(FollowRouteCarrierKind.Personal);
        FollowRouteStore squadron = library.ForCarrier(FollowRouteCarrierKind.Squadron);
        FollowRouteDocument fc = await personal.SaveAsAsync(Itinerary(personal.GetPath("F1")), "Shared");
        FollowRouteDocument sc = (await squadron.LoadNamedAsync("F1", "Shared.json", false)).Route! with
        {
            LastReachedIndex = 0,
        };
        await squadron.SaveProgressAsync(sc);
        FollowRouteDocument changed = fc with
        {
            Notes = "New shared supply instructions",
            IsFavorite = true,
            Hops = [fc.Hops[0], new("Changed destination", 4, null, null, false, false), fc.Hops[2]],
        };
        await library.SaveAsync(changed);
        string savedLibrary = await File.ReadAllTextAsync(fc.FilePath);
        var service = new FollowRouteService(squadron);
        FollowRouteDocument updated = operation switch
        {
            "arrival" => (await service.ApplyArrivalAsync(sc, "Lave", 2)).Route,
            "active" => await service.SetActiveAsync(sc, false),
            "autoCopy" => await service.SetAutoCopyAsync(sc, true),
            _ => await service.SetProgressAsync(sc, 1),
        };
        Assert.Equal(savedLibrary, await File.ReadAllTextAsync(fc.FilePath));
        string progressPath = Path.Combine(
            directory,
            "Routes",
            "FleetCarrier",
            "F1",
            ".workspaces",
            "Squadron",
            "Progress",
            "Shared.json"
        );
        using var progress = JsonDocument.Parse(await File.ReadAllTextAsync(progressPath));
        Assert.Equal(updated.LastReachedIndex, progress.RootElement.GetProperty("last").GetInt32());
        Assert.Equal(updated.IsActive, progress.RootElement.GetProperty("active").GetBoolean());
        Assert.Equal(updated.AutoCopy, progress.RootElement.GetProperty("autoCopy").GetBoolean());
        FollowRouteDocument own = (await personal.LoadNamedAsync("F1", "Shared.json", false)).Route!;
        Assert.Equal(fc.LastReachedIndex, own.LastReachedIndex);
        Assert.Equal(changed.Notes, own.Notes);
        Assert.True(own.IsFavorite);
        Assert.Equal("Changed destination", own.Hops[1].Name);
    }

    [Theory]
    [InlineData(FollowRouteCarrierKind.Personal)]
    [InlineData(FollowRouteCarrierKind.Squadron)]
    public async Task LegacyWorkingCopyIsRecoveredOnlyByItsOwnCarrier(FollowRouteCarrierKind kind)
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument draft = Itinerary(library.GetPath("F1")) with { CarrierKind = kind, LastReachedIndex = 1 };
        await library.SaveWorkspaceAsync(draft, false);
        FollowRouteStore own = library.ForCarrier(kind);
        FollowRouteStore other = library.ForCarrier(
            kind == FollowRouteCarrierKind.Personal ? FollowRouteCarrierKind.Squadron : FollowRouteCarrierKind.Personal
        );
        FollowRouteLoadResult recovered = await own.LoadAsync("F1");
        Assert.Equal(1, recovered.WorkspaceDraft!.LastReachedIndex);
        Assert.Equal(kind, recovered.WorkspaceDraft.CarrierKind);
        Assert.Null((await other.LoadAsync("F1")).WorkspaceDraft);
        await own.SaveWorkspaceAsync(recovered.WorkspaceDraft, false);
        await other.SaveWorkspaceAsync((await other.CreateNewAsync("F1")), false);
        Assert.Equal(1, (await own.LoadAsync("F1")).WorkspaceDraft!.LastReachedIndex);
        Assert.Equal(1, (await library.LoadAsync("F1")).WorkspaceDraft!.LastReachedIndex);
    }

    [Fact]
    public async Task LegacyNamedSelectionKeepsItsProgressDuringMigration()
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteDocument saved = await library.SaveAsAsync(
            Itinerary(library.GetPath("F1")) with
            {
                CarrierKind = FollowRouteCarrierKind.Squadron,
                LastReachedIndex = 1,
            },
            "Legacy"
        );
        FollowRouteLoadResult result = await library.ForCarrier(FollowRouteCarrierKind.Squadron).LoadAsync("F1");
        Assert.Equal(saved.FilePath, result.Path);
        Assert.Equal(1, result.Route!.LastReachedIndex);
        Assert.Empty((await library.ForCarrier(FollowRouteCarrierKind.Personal).LoadAsync("F1")).Route!.Hops);
    }

    [Fact]
    public async Task ChangedLibraryHopsResetOnlyTheMatchingPrivateProgress()
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteStore fcStore = library.ForCarrier(FollowRouteCarrierKind.Personal);
        FollowRouteStore scStore = library.ForCarrier(FollowRouteCarrierKind.Squadron);
        FollowRouteDocument fc = await fcStore.SaveAsAsync(Itinerary(fcStore.GetPath("F1")), "Shared");
        FollowRouteDocument sc = (await scStore.LoadNamedAsync("F1", "Shared.json", false)).Route!;
        await scStore.SaveProgressAsync(sc with { LastReachedIndex = 1, AutoCopy = true });
        await fcStore.SaveAsync(fc with { Hops = [fc.Hops[0], fc.Hops[2]] });
        FollowRouteDocument reopened = (await scStore.ReloadAsync(sc)).Route!;
        Assert.Equal(-1, reopened.LastReachedIndex);
        Assert.True(reopened.AutoCopy);
        Assert.Equal(FollowRouteCarrierKind.Squadron, reopened.CarrierKind);
        await File.WriteAllTextAsync(
            Path.Combine(
                directory,
                "Routes",
                "FleetCarrier",
                "F1",
                ".workspaces",
                "Squadron",
                "Progress",
                "Shared.json"
            ),
            "invalid json"
        );
        FollowRouteLoadResult corrupt = await scStore.ReloadAsync(sc);
        Assert.Null(corrupt.Route);
        Assert.NotNull(corrupt.Error);
    }

    [Fact]
    public async Task SharedRenameKeepsBothSnapshotsAndDeletionClearsOldProgressBeforeANameIsReused()
    {
        var library = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier);
        FollowRouteStore personal = library.ForCarrier(FollowRouteCarrierKind.Personal);
        FollowRouteStore squadron = library.ForCarrier(FollowRouteCarrierKind.Squadron);
        FollowRouteDocument fc = await personal.SaveAsAsync(Itinerary(personal.GetPath("F1")), "Shared");
        FollowRouteDocument sc = (await squadron.LoadNamedAsync("F1", "Shared.json", false)).Route!;
        await personal.SaveWorkspaceAsync(fc with { LastReachedIndex = 0, AutoCopy = true }, true);
        await squadron.SaveWorkspaceAsync(sc with { LastReachedIndex = 1, AutoCopy = false }, true);
        await personal.RenameAsync("F1", "Shared.json", false, "Renamed");
        FollowRouteLoadResult renamedFc = await personal.LoadAsync("F1");
        FollowRouteLoadResult renamedSc = await squadron.LoadAsync("F1");
        Assert.Equal("Renamed", renamedSc.Route!.Name);
        Assert.Equal(0, renamedFc.Route!.LastReachedIndex);
        Assert.Equal(1, renamedSc.Route.LastReachedIndex);
        Assert.Equal(1, renamedSc.WorkspaceDraft!.LastReachedIndex);
        Assert.False(renamedSc.WorkspaceDraft.AutoCopy);
        await personal.RenameAsync("F1", "Renamed.json", false, "Renamed");
        await personal.DeleteNamedAsync("F1", "Renamed.json", false);
        Assert.Empty(await squadron.ListAsync("F1"));
        Assert.Null((await personal.LoadAsync("F1")).WorkspaceDraft);
        Assert.Null((await squadron.LoadAsync("F1")).WorkspaceDraft);
        await personal.SaveAsAsync(fc, "Renamed");
        FollowRouteDocument reopened = (await squadron.LoadNamedAsync("F1", "Renamed.json", false)).Route!;
        Assert.Equal(-1, reopened.LastReachedIndex);
        Assert.False(reopened.AutoCopy);
    }

    [Theory]
    [InlineData(".workspace")]
    [InlineData(".WORKSPACE")]
    public async Task SharedBookmarksCannotOverwriteLegacyWorkspaceMetadata(string name)
    {
        FollowRouteStore store = new FollowRouteStore(directory, FollowRouteKind.FleetCarrier).ForCarrier(
            FollowRouteCarrierKind.Personal
        );
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsAsync(Itinerary(store.GetPath("F1")), name));
        Assert.Empty(await store.ListAsync("F1"));
    }

    [Fact]
    public void CarrierScopesRequireACarrierLibraryAndAKnownCarrier()
    {
        Assert.Throws<ArgumentException>(() =>
            new FollowRouteStore(directory).ForCarrier(FollowRouteCarrierKind.Personal)
        );
        Assert.Throws<ArgumentException>(() =>
            new FollowRouteStore(directory, FollowRouteKind.FleetCarrier).ForCarrier((FollowRouteCarrierKind)99)
        );
    }

    private static FollowRouteDocument Itinerary(string path) =>
        new(
            "F1",
            path,
            true,
            true,
            -1,
            [
                new("Sol", 1, null, null, false, false),
                new("Lave", 2, null, null, false, false),
                new("Achenar", 3, null, null, false, false),
            ],
            Kind: FollowRouteKind.FleetCarrier
        );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
