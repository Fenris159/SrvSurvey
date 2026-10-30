using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData(null, ColonizationProjectBuildFilter.All)]
    [InlineData(null, ColonizationProjectBuildFilter.Building)]
    [InlineData(null, ColonizationProjectBuildFilter.Planning)]
    [InlineData("Sol", ColonizationProjectBuildFilter.All)]
    [InlineData("Sol", ColonizationProjectBuildFilter.Building)]
    [InlineData("Sol", ColonizationProjectBuildFilter.Planning)]
    [InlineData("Remote Colony", ColonizationProjectBuildFilter.All)]
    [InlineData("Remote Colony", ColonizationProjectBuildFilter.Building)]
    [InlineData("Remote Colony", ColonizationProjectBuildFilter.Planning)]
    public async Task UncheckSelectionUsesBothFiltersAndPreservesShoppingSelection(
        string? system,
        ColonizationProjectBuildFilter stage
    )
    {
        StubRavenColonialClient client = ProjectFilterClient();
        client.Workspace = client.Workspace with
        {
            Projects =
            [
                .. client.Workspace.Projects,
                Project("complete", "Completed Build", 0) with
                {
                    IsComplete = true,
                    SystemName = "Sol",
                },
            ],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == system);
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option => option.Value == stage);
        foreach (ColonizationProjectRowViewModel row in model.Projects)
        {
            row.IsShoppingSelected = true;
        }

        model.UncheckProjectSelection();
        model.UncheckProjectSelection();

        Assert.Equal(4, model.Projects.Count);
        Assert.All(
            model.Projects,
            row =>
            {
                bool shouldRemainShown = stage switch
                {
                    ColonizationProjectBuildFilter.Planning => !row.Project.IsPlanned,
                    ColonizationProjectBuildFilter.Building => row.Project.IsPlanned || row.Project.IsComplete,
                    _ => false,
                };
                Assert.Equal(shouldRemainShown || (system is not null && row.SystemName != system), row.IsShown);
                Assert.True(row.IsShoppingSelected);
            }
        );
        Assert.Equal(
            model.Projects.Any(row => !row.Project.IsPlanned && !row.IsShown),
            model.HasUnsavedProjectVisibility
        );
        Assert.Equal(0, client.SaveCount);
        await model.SaveProjectVisibilityAsync();
        Assert.Equal(
            model
                .Projects.Where(row => !row.Project.IsPlanned && !row.IsShown)
                .Select(row => row.Project.BuildId)
                .Order(),
            client.LastSavedHiddenIds.Order()
        );
    }

    [Theory]
    [InlineData(null, ColonizationProjectBuildFilter.All, 3)]
    [InlineData("Sol", ColonizationProjectBuildFilter.All, 1)]
    [InlineData("Sol", ColonizationProjectBuildFilter.Planning, 0)]
    [InlineData("Remote Colony", ColonizationProjectBuildFilter.Building, 1)]
    [InlineData("Remote Colony", ColonizationProjectBuildFilter.Planning, 1)]
    [InlineData(null, ColonizationProjectBuildFilter.Building, 2)]
    [InlineData(null, ColonizationProjectBuildFilter.Planning, 1)]
    public async Task ProjectFiltersCombineSystemAndStageWithoutChangingCargoSelection(
        string? system,
        ColonizationProjectBuildFilter stage,
        int expectedCount
    )
    {
        StubRavenColonialClient client = ProjectFilterClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        Assert.Equal("All Systems", model.ProjectSystemFilter!.Label);
        Assert.Equal("All Projects", model.ProjectBuildFilter!.Label);
        model.Projects[0].IsShoppingSelected = true;
        long needed = model.CommodityOverlay.Plan.TotalRemaining;
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == system);
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option => option.Value == stage);
        Assert.Equal(expectedCount, model.VisibleProjects.Count);
        Assert.Equal(expectedCount == 0, model.HasNoMatchingProjects);
        Assert.Equal(3, model.Projects.Count);
        Assert.Equal(3, model.ProjectSystemOptions.Count);
        Assert.Equal(needed, model.CommodityOverlay.Plan.TotalRemaining);
        Assert.True(model.Projects[0].IsShoppingSelected);
        model.ProjectSystemFilter = null;
        model.ProjectBuildFilter = null;
        Assert.Equal(system, model.ProjectSystemFilter!.SystemName);
        Assert.Equal(stage, model.ProjectBuildFilter!.Value);
    }

    [Fact]
    public async Task ProjectFiltersSurviveRefreshIncludingMissingSystemsAndResetForCommanderChanges()
    {
        StubRavenColonialClient client = ProjectFilterClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == "Remote Colony");
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option =>
            option.Value == ColonizationProjectBuildFilter.Planning
        );
        await model.RefreshAsync();
        Assert.True(Assert.Single(model.VisibleProjects).Project.IsPlanned);
        client.Workspace = client.Workspace with { Projects = [client.Workspace.Projects[0]] };
        client.PlanningSystems!["Remote Colony"] = FilterPlanningSystem("Remote Colony", 47) with { Sites = [] };
        await model.RefreshAsync();
        Assert.Equal("Remote Colony", model.ProjectSystemFilter.SystemName);
        Assert.Contains(model.ProjectSystemOptions, option => option.SystemName == "Remote Colony");
        Assert.Equal(ColonizationProjectBuildFilter.Planning, model.ProjectBuildFilter.Value);
        Assert.True(model.HasNoMatchingProjects);
        await model.SetCommanderAsync("Other Cmdr");
        Assert.Equal(ColonizationProjectBuildFilter.All, model.ProjectBuildFilter.Value);
        Assert.Null(model.ProjectSystemFilter.SystemName);
        Assert.Single(model.VisibleProjects);
        Assert.Empty(client.PlanningDiscoveryCommanders);
    }

    [Fact]
    public async Task FailedPlanningDiscoveryKeepsCachedPlansAndNewSnapshotsCanSupplyFallbackNeeds()
    {
        ColonizationSystemRecord remote = FilterPlanningSystem("Remote Colony", 47);
        var client = new StubRavenColonialClient
        {
            DiscoveredPlanningSystems = [remote],
            PlanningSystemFailure = new HttpRequestException("System read offline"),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        Assert.True(Assert.Single(model.Projects).Project.IsPlanned);
        client.PlanningDiscoveryFailure = new HttpRequestException("Discovery offline");
        await model.RefreshAsync();
        Assert.True(Assert.Single(model.Projects).Project.IsPlanned);
        Assert.Contains("could not be refreshed", model.StatusMessage);
    }

    [Fact]
    public async Task DisabledColonizationDiscardsDelayedCommanderPlanningDiscovery()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ColonizationSystemRecord>>();
        var client = new StubRavenColonialClient { PlanningDiscoveryTask = pending.Task };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        Task refresh = model.SetCommanderAsync("Test Cmdr");
        Assert.Single(client.PlanningDiscoveryCommanders);
        model.IsEnabled = false;
        pending.SetResult([FilterPlanningSystem("Remote Colony", 47)]);
        await refresh;
        Assert.Empty(model.Projects);
    }

    private static StubRavenColonialClient ProjectFilterClient() =>
        new()
        {
            Workspace = new(
                [
                    Project("live-sol", "Sol Build", 100) with
                    {
                        SystemName = "Sol",
                        SystemAddress = 42,
                    },
                    Project("live-remote", "Remote Build", 200) with
                    {
                        SystemName = "Remote Colony",
                        SystemAddress = 47,
                    },
                ],
                [],
                null,
                []
            ),
            PlanningSystems = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Remote Colony"] = FilterPlanningSystem("Remote Colony", 47),
            },
        };
}
