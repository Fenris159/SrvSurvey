using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationProjectEditorViewModelTests
{
    private readonly ColonizationBuildCatalog catalog =
        ColonizationBuildCatalog.LoadEmbedded();

    [Fact]
    public async Task DoesNotReadOrWriteWithoutCompleteConsentedContext()
    {
        var client = new StubRavenColonialClient();
        var editor = Create(client);

        await editor.PrepareAsync();

        Assert.False(editor.CanPrepare);
        Assert.False(editor.IsPrepared);
        Assert.Equal(0, client.SiteReadCount);
        Assert.Equal(0, client.ArchitectReadCount);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Enable", editor.StatusMessage);
    }

    [Fact]
    public async Task PrepareReadsContextAndMapsAPlannedSiteWithoutPublishing()
    {
        var layout = catalog.FindByBuildType("no_truss")!.Layouts[1];
        var client = new StubRavenColonialClient
        {
            Architect = "Project Architect",
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-1",
                    Name = "Hope",
                    BodyNumber = 7,
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new ColonizationSystemSite
                {
                    Id = "complete",
                    Name = "Already Built",
                    BuildType = layout,
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        var editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();
        editor.SelectedSystemSite = editor.SystemSites[1];

        Assert.True(editor.IsPrepared);
        Assert.Equal(1, client.SiteReadCount);
        Assert.Equal(1, client.ArchitectReadCount);
        Assert.Equal(0, client.CreateCount);
        Assert.Equal(2, editor.SystemSites.Count);
        Assert.Equal("Project Architect", editor.ArchitectName);
        Assert.Equal(layout, editor.SelectedLayout);
        Assert.Equal("7", editor.BodyNumberText);
        Assert.True(editor.IsPlannedSiteSelected);
        Assert.False(editor.IsBuildSelectionEnabled);
    }

    [Fact]
    public async Task ReviewIsLocalAndConfirmationPublishesExactlyOnce()
    {
        var createdCount = 0;
        var client = new StubRavenColonialClient();
        var editor = Create(client, _ =>
        {
            createdCount++;
            return Task.CompletedTask;
        });
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option =>
            option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";

        await editor.ReviewAsync();

        Assert.True(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);

        await editor.ConfirmCreateAsync();

        Assert.Equal(1, client.CreateCount);
        Assert.Equal(1, createdCount);
        Assert.False(editor.IsConfirmationPending);
        Assert.True(editor.HasCreatedProject);
        Assert.Equal("Test Cmdr", Assert.Single(
            client.LastCreated!.Commanders.Keys));
        Assert.Equal("no_truss", client.LastCreated.BuildType);
        Assert.Equal(42, client.LastCreated.MarketId);
    }

    [Fact]
    public async Task InvalidBodyNumberCannotReachPublishConfirmation()
    {
        var client = new StubRavenColonialClient();
        var editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        editor.BodyNumberText = "invalid";

        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.False(editor.IsConfirmationPending);
        Assert.Equal(0, client.CreateCount);
        Assert.Contains("Body number", editor.StatusMessage);
    }

    [Fact]
    public async Task UnknownPlannedLayoutFallsBackToEditableManualSelection()
    {
        var client = new StubRavenColonialClient
        {
            Sites =
            [
                new ColonizationSystemSite
                {
                    Id = "site-unknown",
                    Name = "Future Site",
                    BodyNumber = 4,
                    BuildType = "not-in-the-local-catalog",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        var editor = Create(client);
        editor.UpdateContext(ReadyContext());

        await editor.PrepareAsync();

        Assert.True(editor.IsPrepared);
        Assert.False(editor.IsPlannedSiteSelected);
        Assert.True(editor.IsBuildSelectionEnabled);
        Assert.Equal("-1", editor.BodyNumberText);
        Assert.Contains("could not be matched", editor.StatusMessage);
        Assert.Equal(0, client.CreateCount);
    }

    [Fact]
    public async Task ContextChangeDiscardsStaleConfirmation()
    {
        var client = new StubRavenColonialClient();
        var editor = Create(client);
        editor.UpdateContext(ReadyContext());
        await editor.PrepareAsync();
        await editor.ReviewAsync();
        Assert.True(editor.IsConfirmationPending);

        editor.UpdateContext(ReadyContext() with { SystemName = "Other" });

        Assert.False(editor.IsPrepared);
        Assert.False(editor.IsConfirmationPending);
        await editor.ConfirmCreateAsync();
        Assert.Equal(0, client.CreateCount);
    }

    private ColonizationProjectEditorViewModel Create(
        StubRavenColonialClient client,
        Func<ColonizationProject, Task>? onCreated = null)
    {
        return new ColonizationProjectEditorViewModel(
            client,
            catalog,
            onCreated ?? (_ => Task.CompletedTask));
    }

    private static ColonizationProjectEditorContext ReadyContext()
    {
        return new ColonizationProjectEditorContext(
            true,
            "Test Cmdr",
            "Test System",
            [1, 2, 3],
            new ColonizationDockingSnapshot(
                42,
                99,
                "Test System",
                "Orbital Construction Site: Hope",
                "Test Faction",
                ["colonisationcontribution"]),
            new ColonizationConstructionDepotSnapshot(
                DateTimeOffset.Parse("2026-07-24T12:00:00Z"),
                42,
                0.25,
                IsComplete: false,
                IsFailed: false,
                [
                    new ColonizationResourceRequirement(
                        "steel", "Steel", 100, 25, 1),
                ]));
    }

    private sealed class StubRavenColonialClient : IRavenColonialClient
    {
        public IReadOnlyList<ColonizationSystemSite> Sites { get; set; } = [];

        public string? Architect { get; set; }

        public int SiteReadCount { get; private set; }

        public int ArchitectReadCount { get; private set; }

        public int CreateCount { get; private set; }

        public ColonizationProjectCreate? LastCreated { get; private set; }

        public Task<ColonizationCommanderProjects> GetCommanderProjectsAsync(
            string commanderName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ColonizationCommanderProjects(
                [],
                [],
                null,
                []));
        }

        public Task<string?> GetCommanderByApiKeyAsync(
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> SaveHiddenProjectIdsAsync(
            string commanderName,
            IEnumerable<string> hiddenProjectIds,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                hiddenProjectIds.ToArray());
        }

        public Task<ColonizationProject?> GetProjectAsync(
            string buildId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ColonizationProject?>(null);
        }

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
            CancellationToken cancellationToken = default)
        {
            SiteReadCount++;
            return Task.FromResult(Sites);
        }

        public Task<string?> GetSystemArchitectAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default)
        {
            ArchitectReadCount++;
            return Task.FromResult(Architect);
        }

        public Task<ColonizationSystemRecord> GetSystemAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationSystemRecord> ImportSystemBodiesAsync(
            string systemNameOrAddress,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationSystemRecord> UpdateSystemSitesAsync(
            string systemNameOrAddress,
            ColonizationSystemSiteUpdate update,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ColonizationProject?> CreateProjectAsync(
            ColonizationProjectCreate project,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            LastCreated = project;
            return Task.FromResult<ColonizationProject?>(
                new ColonizationProject
                {
                    BuildId = "created-1",
                    BuildType = project.BuildType,
                    BuildName = project.BuildName,
                    MarketId = project.MarketId,
                    SystemAddress = project.SystemAddress,
                    SystemName = project.SystemName,
                    StarPosition = project.StarPosition,
                    MaximumRequired = project.MaximumRequired,
                    RemainingRequired = project.Commodities.Values.Sum(),
                    Commodities = project.Commodities,
                });
        }

        public Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ColonizationFleetCarrier?>(null);
        }

        public Task<ColonizationFleetCarrier> PublishFleetCarrierAsync(
            ColonizationFleetCarrierRegistration carrier,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyDictionary<string, int>>
            ReplaceFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargo,
                string apiKey,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(cargo);
        }

        public Task<IReadOnlyDictionary<string, int>>
            AdjustFleetCarrierCargoAsync(
                long marketId,
                IReadOnlyDictionary<string, int> cargoChanges,
                string apiKey,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(cargoChanges);
        }

        public Task PublishCurrentShipAsync(
            ColonizationCurrentShip ship,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
