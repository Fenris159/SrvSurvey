using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationProjectEditorViewModelTests
{
    /// <summary>Returning credentials to their earlier value must not revive a revoked publication or its displayed result.</summary>
    [Theory]
    [InlineData("consent")]
    [InlineData("api-key")]
    public async Task CarrierReviewPublicationCannotResumeAfterOwnerCredentialsAreRevokedAndRestored(string change)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient
        {
            LinkCreatedCarrier = (_, _) =>
            {
                started.TrySetResult();
                return response.Task;
            },
        };
        ColonizationProject? installed = null;
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            false,
            project =>
            {
                installed = project;
                return Task.CompletedTask;
            }
        );
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        Task publish = editor.ConfirmCreateAsync();
        await started.Task;
        ColonizationProjectEditorContext context = ReadyContext() with { RavenApiKey = "test-key" };
        editor.UpdateContext(
            change == "consent"
                ? context with
                {
                    IsExternalDataEnabled = false,
                }
                : context with
                {
                    RavenApiKey = "replacement-key",
                }
        );
        editor.UpdateContext(context);
        response.SetResult();
        await publish;
        Assert.Equal(("created-1", 42L), Assert.Single(client.CreatedCarrierLinks));
        Assert.Null(installed);
        Assert.False(editor.HasCreatedProject);
    }

    /// <summary>A roster expansion during a failed publish leaves no reusable confirmation for unreviewed carriers.</summary>
    [Fact]
    public async Task CarrierReviewMustBeRepeatedAfterAnInFlightRosterChangeAndFailedCreation()
    {
        var response = new TaskCompletionSource<ColonizationProject?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubRavenColonialClient { CreationResponse = response.Task, CreationStarted = started };
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(client, false);
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        Task publish = editor.ConfirmCreateAsync();
        await started.Task;
        editor.UpdateCarrierOptions([.. CreationCarriers(), new() { MarketId = 44, Name = "CCC-333" }]);
        response.SetException(new HttpRequestException("creation unavailable"));
        await publish;

        Assert.False(editor.IsConfirmationPending);
        Assert.False(editor.ConfirmCommand.CanExecute(null));
        Assert.Contains("creation unavailable", editor.StatusMessage);
        client.CreationResponse = null;
        await editor.ConfirmCreateAsync();
        Assert.Equal(1, client.CreateCount);
        Assert.Empty(client.CreatedCarrierLinks);
        await editor.ReviewAsync();
        Assert.Contains("Link 3 carriers", editor.ConfirmationSummary);
    }

    /// <summary>Carrier failure names and retry advice remain visible alongside an independent cleanup failure.</summary>
    [Fact]
    public async Task CarrierReviewReportsLinkFailuresTogetherWithCleanupFailure()
    {
        var client = new StubRavenColonialClient
        {
            LinkCreatedCarrier = (_, marketId) =>
                marketId == 42 ? Task.FromException(new HttpRequestException("carrier rejected")) : Task.CompletedTask,
        };
        ColonizationProjectEditorViewModel editor = await PrepareCarrierCreationAsync(
            client,
            false,
            _ => Task.FromException(new HttpRequestException("cleanup unavailable"))
        );
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        await editor.ConfirmCreateAsync();

        Assert.Equal("created-1", editor.CreatedProjectId);
        Assert.Contains("cleanup unavailable", editor.StatusMessage);
        Assert.Contains("AAA-111: carrier rejected", editor.StatusMessage);
        Assert.Contains("Carriers linked: 1 of 2.", editor.StatusMessage);
        Assert.Contains("Retry failed links", editor.StatusMessage);
    }
}

public sealed partial class ColonizationViewModelTests
{
    /// <summary>Explicitly checked inventory contributes the same quantities to Loaded, shopping and overlay readouts.</summary>
    [Fact]
    public async Task CarrierReviewCheckedUnlinkedCarriersCountConsistentlyInEveryCargoReadout()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build-1", "Port", 1_000)], [], null, SevenCheckboxCarriers()),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        CheckThreeCarriers(model.CarrierSelection);
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            model.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("30.00%", Assert.Single(model.Projects).CarrierCoverageText);
        Assert.Equal(700, Assert.Single(shopping.Needs).Remaining);
        Assert.Equal(300, Assert.Single(model.CommodityOverlay.Plan.Rows).OnFleetCarriers);
        Assert.Equal(700, model.CommodityOverlay.Plan.FleetCarrierDeficit);
    }

    /// <summary>The newest durable shopping choice recovers a planning-file write failure after restart.</summary>
    [Fact]
    public async Task CarrierReviewShoppingSubsetSurvivesAFailedPlanningSaveAndRestart()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build-1", "Port", 1_000)], [], null, SevenCheckboxCarriers()),
        };
        string path = Path.Combine(directory, "ui.json");
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            using ColonizationShoppingViewModel shopping = model.CreateShopping(
                model.Projects.Select(row => row.Project).ToArray(),
                new ColonizationShoppingViewModelTests.SearchStub([])
            );
            File.Move(path, path + ".before-failure");
            Directory.CreateDirectory(path);
            try
            {
                CheckThreeCarriers(shopping.CarrierSelection);
                Assert.Equal(700, Assert.Single(shopping.Needs).Remaining);
                Assert.Contains("could not be saved", model.StatusMessage);
            }
            finally
            {
                Directory.Delete(path);
                File.Move(path + ".before-failure", path);
            }
            Assert.Equal(
                new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
                new ColonizationSettingsStore(path).LoadShopping("Test Cmdr")!.Search!.SelectedCarrierMarketIds!.Order()
            );
        }
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        using ColonizationShoppingViewModel restored = reopened.CreateShopping(
            reopened.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
            restored.CarrierSelection.CaptureSelectedIds().Order()
        );
        Assert.Equal("3 carriers selected", reopened.CarrierSelection.SelectionNote);
        Assert.Equal(700, Assert.Single(restored.Needs).Remaining);
    }

    /// <summary>A newer durable ALL choice outranks an older shopping subset when the shopping write fails.</summary>
    [Fact]
    public async Task CarrierReviewNewerPlanningAllWinsOverAnOlderShoppingSubsetAfterSaveFailure()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("build-1", "Port", 1_000)], [], null, SevenCheckboxCarriers()),
        };
        string path = Path.Combine(directory, "ui.json.shopping.json");
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            CheckThreeCarriers(model.CarrierSelection);
            using ColonizationShoppingViewModel shopping = model.CreateShopping(
                model.Projects.Select(row => row.Project).ToArray(),
                new ColonizationShoppingViewModelTests.SearchStub([])
            );
            File.Move(path, path + ".before-failure");
            Directory.CreateDirectory(path);
            try
            {
                model.CarrierSelection.SelectAllCommand.Execute(null);
                Assert.Equal("7 carriers selected", model.CarrierSelection.SelectionNote);
                // Close while storage is still unavailable so Dispose cannot heal the failed write.
                shopping.Dispose();
                model.Dispose();
            }
            finally
            {
                Directory.Delete(path);
                File.Move(path + ".before-failure", path);
            }
        }
        Assert.Equal(
            new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 },
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
                .LoadShopping("Test Cmdr")!
                .Search!.SelectedCarrierMarketIds!.Order()
        );
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        using ColonizationShoppingViewModel restored = reopened.CreateShopping(
            reopened.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("7 carriers selected", reopened.CarrierSelection.SelectionNote);
        Assert.Equal(7, restored.CarrierSelection.SelectedCount);
        Assert.Equal(300, Assert.Single(restored.Needs).Remaining);
    }

    /// <summary>Revoking and restoring Raven access never resumes the remaining writes from an older unlink batch.</summary>
    [Fact]
    public async Task CarrierReviewDisableAndReenableCannotResumeAnOldUnlinkOperation()
    {
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRavenColonialClient client = ProjectCarrierClient();
        client.ChangeProjectCarrier = (_, _, _) => response.Task;
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        Task update = model.UnlinkCarrierFromSelectedProjectsAsync();
        model.IsEnabled = false;
        model.IsEnabled = true;
        response.SetResult();
        await update;

        Assert.Equal(("first", 42L, false), Assert.Single(client.ProjectCarrierRequests));
        Assert.Empty(model.Projects);
        Assert.DoesNotContain("Carrier links removed", model.StatusMessage);
    }

    /// <summary>A read begun before carrier mutations cannot replace their acknowledged links with an older snapshot.</summary>
    [Fact]
    public async Task CarrierReviewStaleCurrentSystemReadCannotEraseAnAcknowledgedLink()
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        ColonizationProject stale = client.Workspace.Projects.Single(project => project.BuildId == "first") with
        {
            LinkedFleetCarriers = [],
        };
        client.Workspace = client.Workspace with
        {
            Projects = client
                .Workspace.Projects.Select(project => project.BuildId == "first" ? stale : project)
                .ToArray(),
        };
        client.PlanningSystems!["Sol"] = client.PlanningSystems["Sol"] with
        {
            Sites =
            [
                new()
                {
                    Id = "first-site",
                    BuildId = "first",
                    MarketId = 10,
                    Status = ColonizationSystemSiteStatus.Build,
                },
            ],
        };
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        var oldRead = new TaskCompletionSource<ColonizationProject?>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.LoadProject = (_, _) =>
        {
            reading.TrySetResult();
            return oldRead.Task;
        };
        model.ShowUnaffiliatedProjectsInCurrentSystem = true;
        model.UpdateSystemContext("Sol", null, 42);
        Task systemRead = model.CurrentSystemPlanningTask;
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondLink = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ChangeProjectCarrier = (id, _, _) => id == "second" ? secondLink.Task : Task.CompletedTask;
        Task update = model.LinkCarrierToSelectedProjectsAsync();
        Assert.Equal(
            42,
            Assert
                .Single(model.Projects.Single(row => row.Project.BuildId == "first").Project.LinkedFleetCarriers)
                .MarketId
        );
        oldRead.SetResult(stale);
        await systemRead;
        client.LoadProject = (_, _) =>
            Task.FromResult<ColonizationProject?>(stale with { LinkedFleetCarriers = [new() { MarketId = 42 }] });
        secondLink.SetResult();
        await update;

        Assert.Equal(
            42,
            Assert
                .Single(model.Projects.Single(row => row.Project.BuildId == "first").Project.LinkedFleetCarriers)
                .MarketId
        );
        Assert.Contains("Carrier links added: 2.", model.StatusMessage);
    }
}

public sealed partial class ColonizationWorkflowSimulationTests
{
    /// <summary>Closing the owning workspace stops further creation links and prevents late installation.</summary>
    [Fact]
    public async Task CarrierReviewDisposedWorkspaceStopsCreationLinksAndInstallation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<long>();
        var client = new WorkflowClient
        {
            Architect = "Test Cmdr",
            LinkCarrier = (_, id) =>
            {
                requests.Add(id);
                started.TrySetResult();
                return response.Task;
            },
        };
        using ColonizationViewModel model = await ReadyCarrierReviewBuildAsync(client);
        Task publish = model.ProjectEditor.ConfirmCreateAsync();
        await started.Task;
        model.Dispose();
        response.SetResult();
        await publish;
        Assert.Equal(new long[] { 42 }, requests);
        Assert.Empty(model.Projects);
        Assert.False(model.ProjectEditor.HasCreatedProject);
    }

    /// <summary>Leaving the depot during carrier requests retains the created build and its acknowledged associations.</summary>
    [Fact]
    public async Task CarrierReviewCreatedBuildSurvivesLeavingTheDepotDuringLinking()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new WorkflowClient
        {
            Architect = "Test Cmdr",
            LinkCarrier = (_, _) =>
            {
                started.TrySetResult();
                return response.Task;
            },
        };
        using ColonizationViewModel model = await ReadyCarrierReviewBuildAsync(client);
        Task publish = model.ProjectEditor.ConfirmCreateAsync();
        await started.Task;
        model.ApplyJournalEvents([Event("Undocked", "\"MarketID\":10")]);
        response.SetResult();
        await publish;

        ColonizationProject created = Assert.Single(model.Projects).Project;
        Assert.Equal("created-1", created.BuildId);
        Assert.Equal(new long[] { 42, 43 }, created.LinkedFleetCarriers.Select(carrier => carrier.MarketId));
        Assert.Equal("created-1", model.ProjectEditor.CreatedProjectId);
        Assert.Contains("Carriers linked: 2 of 2.", model.ProjectEditor.StatusMessage);
    }

    /// <summary>Carrier results merge with newer project requirements, names and unrelated carrier associations.</summary>
    [Fact]
    public async Task CarrierReviewLinkResultsPreserveARefreshedBuildsNewerRequirementsAndMetadata()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new WorkflowClient
        {
            Architect = "Test Cmdr",
            LinkCarrier = (_, _) =>
            {
                started.TrySetResult();
                return response.Task;
            },
        };
        using ColonizationViewModel model = await ReadyCarrierReviewBuildAsync(client);
        Task publish = model.ProjectEditor.ConfirmCreateAsync();
        await started.Task;
        client.RefreshedProjects =
        [
            Project("created-1", "Test Cmdr") with
            {
                BuildName = "Renamed during linking",
                RemainingRequired = 13,
                Commodities = new Dictionary<string, int> { ["steel"] = 13 },
                LinkedFleetCarriers = [new() { MarketId = 99 }],
            },
        ];
        await model.RefreshAsync();
        response.SetResult();
        await publish;

        ColonizationProject created = Assert.Single(model.Projects).Project;
        Assert.Equal("Renamed during linking", created.BuildName);
        Assert.Equal(13, created.Commodities["steel"]);
        Assert.Equal(
            new long[] { 42, 43, 99 },
            created.LinkedFleetCarriers.Select(carrier => carrier.MarketId).Order()
        );
    }

    private async Task<ColonizationViewModel> ReadyCarrierReviewBuildAsync(WorkflowClient client)
    {
        ColonizationViewModel model = await ReadyAsync(client);
        ColonizationProjectEditorViewModel editor = model.ProjectEditor;
        editor.UpdateCarrierOptions([
            new() { MarketId = 42, Name = "AAA-111" },
            new() { MarketId = 43, Name = "BBB-222" },
        ]);
        await editor.PrepareAsync();
        editor.SelectedBuild = editor.BuildOptions.Single(option => option.Build.BuildType == "no_truss");
        editor.SelectedLayout = "no_truss";
        editor.BodyName = "Test System A 1";
        editor.IncludeCarrierLinks = true;
        await editor.ReviewAsync();
        return model;
    }

    private sealed partial class WorkflowClient : IRavenColonialProjectCarrierClient
    {
        public IReadOnlyList<ColonizationProject> RefreshedProjects { get; set; } = [];
        public Func<string, long, Task>? LinkCarrier { get; init; }

        public Task LinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        ) => LinkCarrier?.Invoke(buildId, marketId) ?? Task.CompletedTask;

        public Task UnlinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
