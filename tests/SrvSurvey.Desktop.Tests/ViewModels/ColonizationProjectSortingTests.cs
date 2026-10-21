using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData("BuildName", "a,b,c,d", "d,c,b,a")]
    [InlineData("CarrierCoveragePercent", "c,a,d,b", "b,d,a,c")]
    [InlineData("ProgressSortValue", "a,c,d,b", "b,d,a,c")]
    [InlineData("TypeDescription", "b,c,d,a", "a,d,b,c")]
    [InlineData("SystemName", "b,d,a,c", "a,c,b,d")]
    public async Task ProjectHeadersToggleNumericAndGroupedAlphabeticalSorts(
        string column,
        string ascending,
        string descending
    )
    {
        using ColonizationViewModel model = Create(ProjectSortingClient());
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        Assert.Equal(["b", "d", "a", "c"], model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.Equal("↓", model.ProjectSortIndicators["ProgressSortValue"]);
        ColonizationProjectRowViewModel first = model.Projects[0];
        first.IsShown = false;
        first.IsShoppingSelected = true;

        model.SortProjectsCommand.Execute(column);

        Assert.Equal(ascending.Split(','), model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.Equal("↑", model.ProjectSortIndicators[column]);
        model.SortProjectsCommand.Execute(column);
        Assert.Equal(descending.Split(','), model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.Equal("↓", model.ProjectSortIndicators[column]);
        Assert.Equal(string.Empty, model.ProjectSortIndicators["Other"]);
        Assert.Same(first, model.Projects[0]);
        Assert.False(first.IsShown);
        Assert.True(first.IsShoppingSelected);
    }

    [Fact]
    public async Task ProjectSortingSurvivesFiltersAndProjectRefreshes()
    {
        StubRavenColonialClient client = ProjectSortingClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.SortProjectsCommand.Execute("BuildName");
        model.SortProjectsCommand.Execute("BuildName");
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == "Sol");
        Assert.Equal(["c", "a"], model.VisibleProjects.Select(row => row.Project.BuildId));

        await model.RefreshAsync();

        Assert.Equal(["c", "a"], model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.Equal("↓", model.ProjectSortIndicators["BuildName"]);
        Assert.Equal(string.Empty, model.ProjectSortIndicators["ProgressSortValue"]);
        model.ProjectSystemFilter = model.ProjectSystemOptions[0];
        Assert.Equal(["d", "c", "b", "a"], model.VisibleProjects.Select(row => row.Project.BuildId));
    }

    [Theory]
    [InlineData(true, "no_truss", 1_000)]
    [InlineData(false, ColonizationProject.FleetCarrierLoadingBuildType, 1_000)]
    [InlineData(false, "no_truss", 0)]
    public async Task PlansAndUnavailableProgressSortBelowWorkedOnProjects(bool planned, string type, int total)
    {
        StubRavenColonialClient client = ProjectSortingClient();
        client.Workspace = client.Workspace with
        {
            Projects =
            [
                Project("unknown", "Unknown", 0) with
                {
                    IsPlanned = planned,
                    BuildType = type,
                    MaximumRequired = total,
                },
                Project("active", "Active", 900),
            ],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");

        Assert.Equal(["active", "unknown"], model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.Equal(0, model.VisibleProjects[1].ProgressSortValue);
    }

    [Fact]
    public async Task LoadedSortUpdatesForCarrierChangesEvenWhenDisplayedPercentagesAreEqual()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [
                    Project("a", "Alpha", 1_000_000),
                    Project("b", "Bravo", 1_000_000) with
                    {
                        Commodities = new() { ["water"] = 1_000_000 },
                    },
                ],
                [],
                null,
                [
                    new()
                    {
                        MarketId = 42,
                        Cargo = new() { ["steel"] = 1, ["water"] = 2 },
                    },
                    new()
                    {
                        MarketId = 43,
                        Cargo = new() { ["steel"] = 2, ["water"] = 1 },
                    },
                ]
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.SelectedPlanningCarrierMarketId = 42;
        model.SortProjectsCommand.Execute("CarrierCoveragePercent");
        Assert.Equal(["a", "b"], model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.All(model.Projects, row => Assert.Equal("0.00%", row.CarrierCoverageText));

        model.SelectedPlanningCarrierMarketId = 43;

        Assert.Equal(["b", "a"], model.VisibleProjects.Select(row => row.Project.BuildId));
        Assert.All(model.Projects, row => Assert.Equal("0.00%", row.CarrierCoverageText));
        IReadOnlyList<ColonizationProjectRowViewModel> rows = model.VisibleProjects;
        model.UpdateStatus(new EliteStatus());
        Assert.Same(rows, model.VisibleProjects);
    }

    private static StubRavenColonialClient ProjectSortingClient() =>
        new()
        {
            Workspace = new(
                [
                    Project("a", "Alpha", 900) with
                    {
                        BuildType = "zeta-type",
                        SystemName = "Sol",
                    },
                    Project("b", "bravo", 100) with
                    {
                        BuildType = "alpha-type",
                        SystemName = "Achenar",
                    },
                    Project("c", "Charlie", 9_000) with
                    {
                        MaximumRequired = 10_000,
                        BuildType = "alpha-type",
                        SystemName = "Sol",
                    },
                    Project("d", "delta", 500) with
                    {
                        BuildType = "beta-type",
                        SystemName = "Achenar",
                    },
                ],
                [],
                null,
                [
                    new()
                    {
                        MarketId = 42,
                        Cargo = new() { ["steel"] = 100 },
                    },
                ]
            ),
        };
}
