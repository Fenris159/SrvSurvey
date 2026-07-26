using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationSystemEditorViewModelTests
{
    [Fact]
    public async Task LoadIsReadOnlyAndSecuredSystemRejectsEditing()
    {
        var client = new StubClient
        {
            Current = System(architect: "Other Cmdr", isOpen: false),
        };
        var editor = Create(client);
        editor.UpdateContext(Context());

        await editor.LoadAsync();

        Assert.True(editor.IsLoaded);
        Assert.False(editor.CanEdit);
        Assert.Equal(1, client.SystemReadCount);
        Assert.Equal(0, client.BodyImportCount);
        Assert.Equal(0, client.UpdateCount);
    }

    [Fact]
    public async Task MissingBodiesRequireExplicitConfirmationBeforeImport()
    {
        var client = new StubClient
        {
            Current = System() with { Bodies = null },
        };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();

        Assert.True(editor.NeedsBodyImport);
        Assert.Equal(0, client.BodyImportCount);

        editor.RequestBodyImport();
        Assert.True(editor.IsBodyImportConfirmationPending);
        Assert.Equal(0, client.BodyImportCount);

        await editor.ConfirmBodyImportAsync();

        Assert.Equal(1, client.BodyImportCount);
        Assert.False(editor.IsBodyImportConfirmationPending);
        Assert.False(editor.NeedsBodyImport);
        Assert.Equal(0, client.UpdateCount);
    }

    [Fact]
    public async Task ReviewIsReadOnlyAndConfirmationPublishesExactPlan()
    {
        var client = new StubClient { Current = System() };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BodyNumber = 2;

        await editor.ReviewAsync();

        Assert.True(editor.IsPublishConfirmationPending);
        Assert.Equal(2, client.SystemReadCount);
        Assert.Equal(0, client.UpdateCount);

        await editor.ConfirmPublishAsync();

        Assert.Equal(1, client.UpdateCount);
        Assert.Equal("secret", client.LastApiKey);
        var update = Assert.Single(client.LastUpdate!.UpdatedSites);
        Assert.Equal("site-1", update.Id);
        Assert.Equal(2, update.BodyNumber);
        Assert.Empty(client.LastUpdate.DeletedSiteIds);
        Assert.False(editor.HasLocalChanges);
    }

    [Fact]
    public async Task ConcurrentSameFieldChangeBlocksPublish()
    {
        var original = System();
        var client = new StubClient { Current = original };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BodyNumber = 2;
        client.Current = original with
        {
            Sites =
            [
                original.Sites[0] with { BodyNumber = 3 },
            ],
        };

        await editor.ReviewAsync();
        await editor.ConfirmPublishAsync();

        Assert.True(editor.HasConflicts);
        Assert.False(editor.IsPublishConfirmationPending);
        Assert.Equal(0, client.UpdateCount);
        Assert.Equal("bodyNum", Assert.Single(editor.Conflicts).Field);
    }

    [Fact]
    public async Task MissingApiKeyCannotPublishReviewedChanges()
    {
        var client = new StubClient { Current = System() };
        var editor = Create(client);
        editor.UpdateContext(Context() with { RavenApiKey = null });
        await editor.LoadAsync();
        editor.Sites[0].BodyNumber = 2;

        await editor.ReviewAsync();
        await editor.ConfirmPublishAsync();

        Assert.True(editor.IsPublishConfirmationPending);
        Assert.False(editor.CanConfirmPublish);
        Assert.Equal(0, client.UpdateCount);
    }

    [Fact]
    public async Task RemoteOnlySiteAndExtensionDataSurviveLocalPublish()
    {
        var original = System();
        var client = new StubClient { Current = original };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BuildType = "vesta";
        client.Current = original with
        {
            Sites =
            [
                original.Sites[0] with
                {
                    ExtensionData = new Dictionary<string, global::System.Text.Json.JsonElement>
                    {
                        ["future"] = global::System.Text.Json.JsonSerializer.SerializeToElement(7),
                    },
                },
                Site("remote", "Remote only", 2),
            ],
        };

        await editor.ReviewAsync();
        await editor.ConfirmPublishAsync();

        var published = Assert.Single(client.LastUpdate!.UpdatedSites);
        Assert.Equal(7, published.ExtensionData["future"].GetInt32());
        Assert.DoesNotContain(
            "remote",
            client.LastUpdate.DeletedSiteIds);
    }

    [Fact]
    public async Task StableLocalDeletionPublishesOnlyPersistedSiteId()
    {
        var client = new StubClient { Current = System() };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.SelectedSite = editor.Sites[0];
        editor.RemoveSelectedSite();

        await editor.ReviewAsync();

        Assert.True(editor.IsPublishConfirmationPending);
        Assert.Null(client.LastUpdate);

        await editor.ConfirmPublishAsync();

        Assert.Equal(["site-1"], client.LastUpdate!.DeletedSiteIds);
        Assert.Empty(client.LastUpdate.UpdatedSites);
    }

    [Fact]
    public async Task BodyImportCannotDiscardUnsavedLocalEdits()
    {
        var client = new StubClient
        {
            Current = System() with { Bodies = null },
        };
        var editor = Create(client);
        editor.UpdateContext(Context());
        await editor.LoadAsync();
        editor.Sites[0].BuildType = "vesta";

        editor.RequestBodyImport();
        await editor.ConfirmBodyImportAsync();

        Assert.False(editor.IsBodyImportConfirmationPending);
        Assert.Equal(0, client.BodyImportCount);
        Assert.Equal("vesta", editor.Sites[0].BuildType);
    }

    private static ColonizationSystemEditorViewModel Create(StubClient client)
    {
        return new ColonizationSystemEditorViewModel(
            client,
            ColonizationBuildCatalog.LoadEmbedded());
    }

    private static ColonizationSystemEditorContext Context()
    {
        return new ColonizationSystemEditorContext(
            true,
            "Test Cmdr",
            "Test System",
            42,
            "secret");
    }

    private static ColonizationSystemRecord System(
        string? architect = "Test Cmdr",
        bool isOpen = false)
    {
        return new ColonizationSystemRecord
        {
            SystemAddress = 42,
            Name = "Test System",
            Architect = architect,
            IsOpen = isOpen,
            Revision = 1,
            Sites = [Site("site-1", "Orbital One", 1)],
            Bodies =
            [
                new ColonizationSystemBody
                {
                    Number = 1,
                    Name = "Test System A 1",
                    Type = "Planet",
                },
                new ColonizationSystemBody
                {
                    Number = 2,
                    Name = "Test System A 2",
                    Type = "Planet",
                },
            ],
        };
    }

    private static ColonizationSystemSite Site(
        string id,
        string name,
        int body)
    {
        return new ColonizationSystemSite
        {
            Id = id,
            Name = name,
            BodyNumber = body,
            Status = ColonizationSystemSiteStatus.Complete,
        };
    }

    private sealed class StubClient : IRavenColonialClient
    {
        public ColonizationSystemRecord Current { get; set; } = System();

        public int SystemReadCount { get; private set; }

        public int BodyImportCount { get; private set; }

        public int UpdateCount { get; private set; }

        public ColonizationSystemSiteUpdate? LastUpdate { get; private set; }

        public string? LastApiKey { get; private set; }

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
        {
            SystemReadCount++;
            return Task.FromResult(Current);
        }

        public Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
        {
            BodyImportCount++;
            Current = Current with
            {
                Bodies = System().Bodies,
                Revision = Current.Revision + 1,
            };
            return Task.FromResult(Current);
        }

        public Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
            string systemNameOrAddress,
            ColonizationSystemSiteUpdate update,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            LastUpdate = update;
            LastApiKey = apiKey;
            var deleted = update.DeletedSiteIds.ToHashSet(
                StringComparer.Ordinal);
            var sites = Current.Sites
                .Where(site => !deleted.Contains(site.Id))
                .ToDictionary(site => site.Id, StringComparer.Ordinal);
            foreach (var site in update.UpdatedSites)
            {
                sites[site.Id] = site;
            }

            Current = Current with
            {
                Revision = Current.Revision + 1,
                Sites = sites.Values.ToList(),
            };
            return Task.FromResult(Current);
        }

        public Task PatchSystemSiteAsync(
            string systemNameOrAddress,
            string siteId,
            ColonizationSystemSitePatch patch,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetCommanderByApiKeyAsync(
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
            string commanderName,
            IEnumerable<string> hiddenProjectIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject?> GetProjectAsync(
            string buildId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject?> GetProjectAsync(
            long systemAddress,
            long marketId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject> UpdateProjectAsync(
            ColonizationProjectUpdate update,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkProjectCompleteAsync(
            string buildId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ContributeToProjectAsync(
            string buildId,
            string commanderName,
            IReadOnlyDictionary<string, int> contributions,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetPrimaryProjectAsync(
            string commanderName,
            string? buildId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject?> CreateProjectAsync(
            ColonizationProjectCreate project,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
            ColonizationFleetCarrierRegistration carrier,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>>
            ReplaceFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargo,
                string apiKey,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>>
            AdjustFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargoChanges,
                string apiKey,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
