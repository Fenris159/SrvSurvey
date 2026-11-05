using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    private static readonly string[] ProjectCarrierBuildIds =
    [
        "first",
        "second",
        "unchecked",
        "other-system",
        "complete",
        "plan:42:cached",
    ];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkCarrierAllAppliesEveryDistinctCarrierToEveryCheckedBuildingProject(bool link)
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        client.Workspace = client.Workspace with
        {
            FleetCarriers =
            [
                .. client.Workspace.FleetCarriers,
                new() { MarketId = 42, Name = "Duplicate" },
                new() { MarketId = 0 },
            ],
        };
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        model.SelectedPlanningCarrierMarketId = 0;
        await ChangeProjectCarriersAsync(model, link);
        Assert.Equal(
            new[] { ("first", 42L, link), ("first", 43L, link), ("second", 42L, link), ("second", 43L, link) },
            client.ProjectCarrierRequests
        );
        Assert.All(
            model.Projects.Where(row => row.Project.BuildId is "first" or "second"),
            row =>
                Assert.Equal(
                    link ? new long[] { 42, 43 } : [],
                    row.Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId).Order()
                )
        );
        Assert.Equal(
            link ? "Carrier links added: 4. Failed: 0." : "Carrier links removed: 4. Failed: 0.",
            model.StatusMessage
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkCarrierLinksUseVisibleShowSelectionAndPreserveOtherCarriers(bool link)
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        model.Projects.Single(row => row.Project.BuildId == "unchecked").IsShoppingSelected = true;
        Assert.Contains(model.Projects, row => row.Project.IsPlanned);

        await ChangeProjectCarriersAsync(model, link);

        Assert.Equal(new[] { ("first", 42L, link), ("second", 42L, link) }, client.ProjectCarrierRequests);
        foreach (
            ColonizationProjectRowViewModel row in model.Projects.Where(row =>
                row.Project.BuildId is "first" or "second"
            )
        )
        {
            Assert.Equal(
                link ? new long[] { 42, 43 } : [43],
                row.Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId).Order()
            );
        }
        if (link)
        {
            Assert.Equal(
                "steel",
                Assert.Single(
                    model
                        .Projects.Single(row => row.Project.BuildId == "first")
                        .Project.LinkedFleetCarriers.Single(carrier => carrier.MarketId == 42)
                        .AssignedCommodities
                )
            );
        }
        ColonizationProject untouched = model.Projects.Single(row => row.Project.BuildId == "unchecked").Project;
        Assert.Equal(new long[] { 42, 43 }, untouched.LinkedFleetCarriers.Select(carrier => carrier.MarketId));
        Assert.False(model.HasUnsavedProjectVisibility);
        Assert.Equal(0, client.SaveCount);
        Assert.Equal(
            link ? "Carrier links added: 2. Failed: 0." : "Carrier links removed: 2. Failed: 0.",
            model.StatusMessage
        );
    }

    [Fact]
    public async Task BulkCarrierCommandsRequireBuildingFilterAvailableCarriersAndCheckedProject()
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        Assert.True(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
        foreach (
            ColonizationProjectBuildOption filter in model.ProjectBuildOptions.Where(option =>
                option.Value != ColonizationProjectBuildFilter.Building
            )
        )
        {
            model.ProjectBuildFilter = filter;
            Assert.False(model.ShowProjectCarrierActions);
            Assert.False(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
            await model.LinkCarrierToSelectedProjectsAsync();
            await model.UnlinkCarrierFromSelectedProjectsAsync();
        }
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option =>
            option.Value == ColonizationProjectBuildFilter.Building
        );
        model.SelectedPlanningCarrierMarketId = 0;
        Assert.True(model.ShowProjectCarrierActions);
        Assert.True(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
        model.SelectedPlanningCarrierMarketId = 999;
        await model.UnlinkCarrierFromSelectedProjectsAsync();
        model.SelectedPlanningCarrierMarketId = 42;
        model.UncheckProjectSelection();
        Assert.False(model.UnlinkCarrierFromSelectedProjectsCommand.CanExecute(null));
        await model.UnlinkCarrierFromSelectedProjectsAsync();
        Assert.Empty(client.ProjectCarrierRequests);
        model.Projects.Single(row => row.Project.BuildId == "first").IsShown = true;
        Assert.True(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BulkCarrierFailureLeavesRejectedProjectUnchangedAndContinuesWithRemainingProjects(bool link)
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        client.ChangeProjectCarrier = (buildId, _, _) =>
            buildId == "first" ? Task.FromException(new HttpRequestException("Rejected by Raven")) : Task.CompletedTask;
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);

        await ChangeProjectCarriersAsync(model, link);

        Assert.Equal(new[] { ("first", 42L, link), ("second", 42L, link) }, client.ProjectCarrierRequests);
        Assert.Equal(
            new long[] { 42, 43 },
            model
                .Projects.Single(row => row.Project.BuildId == "first")
                .Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId)
        );
        Assert.Equal(
            link ? new long[] { 42, 43 } : [43],
            model
                .Projects.Single(row => row.Project.BuildId == "second")
                .Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId)
                .Order()
        );
        Assert.Contains(
            link ? "Carrier links added: 1. Failed: 1." : "Carrier links removed: 1. Failed: 1.",
            model.StatusMessage
        );
        Assert.Contains("First / AAA-111: Rejected by Raven", model.StatusMessage);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task BulkCarrierOperationCapturesCarrierAndSelectionAndBlocksOverlappingWrites()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRavenColonialClient client = ProjectCarrierClient();
        client.ChangeProjectCarrier = (_, _, _) => pending.Task;
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);

        Task change = model.UnlinkCarrierFromSelectedProjectsAsync();
        Assert.True(model.IsBusy);
        Assert.False(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
        Assert.False(model.UnlinkCarrierFromSelectedProjectsCommand.CanExecute(null));
        await model.LinkCarrierToSelectedProjectsAsync();
        model.SelectedPlanningCarrierMarketId = 43;
        model.Projects.Single(row => row.Project.BuildId == "second").IsShown = false;
        pending.SetResult();
        await change;

        Assert.Equal(new[] { ("first", 42L, false), ("second", 42L, false) }, client.ProjectCarrierRequests);
        Assert.False(model.Projects.Single(row => row.Project.BuildId == "second").IsShown);
        Assert.True(model.HasUnsavedProjectVisibility);
        Assert.Equal(43, model.SelectedPlanningCarrierMarketId);
        Assert.False(model.IsBusy);
    }

    [Theory]
    [InlineData("commander")]
    [InlineData("disabled")]
    [InlineData("disposed")]
    public async Task BulkCarrierOperationStopsWhenItsCommanderContextIsLost(string change)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StubRavenColonialClient client = ProjectCarrierClient();
        client.ChangeProjectCarrier = (_, _, _) => pending.Task;
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        Task update = model.UnlinkCarrierFromSelectedProjectsAsync();
        switch (change)
        {
            case "commander":
                await model.SetCommanderAsync("Other Cmdr");
                break;
            case "disabled":
                model.IsEnabled = false;
                break;
            default:
                model.Dispose();
                break;
        }
        pending.SetResult();
        await update;

        Assert.Equal(("first", 42L, false), Assert.Single(client.ProjectCarrierRequests));
        Assert.DoesNotContain("Carrier links removed", model.StatusMessage);
        if (change == "disabled")
        {
            Assert.Empty(model.Projects);
        }
        else
        {
            Assert.Equal(
                new long[] { 42, 43 },
                model
                    .Projects.Single(row => row.Project.BuildId == "first")
                    .Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId)
            );
        }
    }

    private async Task<ColonizationViewModel> CreateProjectCarrierModelAsync(StubRavenColonialClient client)
    {
        ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == "Sol");
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option =>
            option.Value == ColonizationProjectBuildFilter.Building
        );
        model.SelectedPlanningCarrierMarketId = 42;
        return model;
    }

    private static Task ChangeProjectCarriersAsync(ColonizationViewModel model, bool link) =>
        link ? model.LinkCarrierToSelectedProjectsAsync() : model.UnlinkCarrierFromSelectedProjectsAsync();

    private static StubRavenColonialClient ProjectCarrierClient() =>
        new()
        {
            Workspace = new(
                ProjectCarrierBuildIds
                    .Select(id =>
                        Project(id, id == "first" ? "First" : id, 100) with
                        {
                            SystemName = id == "other-system" ? "Achenar" : "Sol",
                            SystemAddress = id == "other-system" ? 43 : 42,
                            IsComplete = id == "complete",
                            LinkedFleetCarriers =
                                id == "second"
                                    ? [new() { MarketId = 43 }]
                                    :
                                    [
                                        new() { MarketId = 42, AssignedCommodities = ["steel"] },
                                        new() { MarketId = 43 },
                                    ],
                        }
                    )
                    .ToArray(),
                ["unchecked"],
                null,
                [new() { MarketId = 42, Name = "AAA-111" }, new() { MarketId = 43, Name = "BBB-222" }]
            ),
            PlanningSystems = new() { ["Sol"] = FilterPlanningSystem("Sol", 42) },
        };

    private sealed partial class StubRavenColonialClient : IRavenColonialProjectCarrierClient
    {
        public List<(string BuildId, long MarketId, bool Link)> ProjectCarrierRequests { get; } = [];
        public Func<string, long, bool, Task>? ChangeProjectCarrier { get; set; }

        public Task LinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        ) => RecordProjectCarrierAsync(buildId, marketId, true);

        public Task UnlinkProjectCarrierAsync(
            string buildId,
            long marketId,
            CancellationToken cancellationToken = default
        ) => RecordProjectCarrierAsync(buildId, marketId, false);

        private Task RecordProjectCarrierAsync(string buildId, long marketId, bool link)
        {
            ProjectCarrierRequests.Add((buildId, marketId, link));
            return ChangeProjectCarrier?.Invoke(buildId, marketId, link) ?? Task.CompletedTask;
        }
    }
}
