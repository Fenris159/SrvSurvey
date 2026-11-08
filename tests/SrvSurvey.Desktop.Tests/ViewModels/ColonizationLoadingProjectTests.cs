using System.Globalization;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    private static readonly string[] LoadingCarrierProjectIds =
    [
        "loading-a",
        "loading-b",
        "loading-hidden",
        "loading-other",
        "loading-complete",
        "loading-planned",
        "plan:42:loading",
    ];

    /// <summary>Known loading totals produce percentages and numeric sort values; an unknown total stays unavailable.</summary>
    [Theory]
    [InlineData(10_080, 10_080, "0% of 10,080", 0d)]
    [InlineData(10_080, 5_040, "50% of 10,080", 0.5d)]
    [InlineData(10_080, 0, "100% of 10,080", 1d)]
    [InlineData(0, 0, "Progress unavailable", 0d)]
    public void LoadingProjectsDisplayAndSortKnownProgress(int total, int remaining, string text, double progress)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var row = new ColonizationProjectRowViewModel(
                LoadingProject("loading", "Sol") with
                {
                    MaximumRequired = total,
                    RemainingRequired = remaining,
                },
                "Fleet Carrier loading",
                false,
                true,
                static (_, _) => { },
                static _ => Task.CompletedTask
            );
            Assert.Equal(text, row.ProgressText);
            Assert.Equal(progress, row.ProgressSortValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Loading has its own active-project category, combines with system filters and retains cargo selections.</summary>
    [Fact]
    public async Task LoadingProjectsHaveTheirOwnFilterAndRetainSelectionsAcrossRefresh()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [
                    Project("build", "Construction", 500),
                    LoadingProject("loading-a", "Sol"),
                    LoadingProject("loading-b", "Achenar"),
                    LoadingProject("loading-complete", "Sol") with
                    {
                        IsComplete = true,
                    },
                    LoadingProject("planned", "Sol") with
                    {
                        IsPlanned = true,
                    },
                ],
                ["loading-b"],
                null,
                []
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects.Single(row => row.Project.BuildId == "loading-a").IsShoppingSelected = true;
        long needed = model.CommodityOverlay.Plan.TotalRemaining;
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option => option.Label == "Building");
        Assert.Equal(["build"], model.VisibleProjects.Select(row => row.Project.BuildId));

        ColonizationProjectBuildOption loading = Assert.Single(
            model.ProjectBuildOptions,
            option => option.Label == "Loading"
        );
        model.ProjectBuildFilter = loading;
        Assert.Equal(["loading-a", "loading-b"], model.VisibleProjects.Select(row => row.Project.BuildId).Order());
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == "Sol");
        Assert.Equal("loading-a", Assert.Single(model.VisibleProjects).Project.BuildId);
        await model.RefreshAsync();
        Assert.Equal("Loading", model.ProjectBuildFilter.Label);
        Assert.Equal("loading-a", Assert.Single(model.VisibleProjects).Project.BuildId);
        Assert.Equal(needed, model.CommodityOverlay.Plan.TotalRemaining);
        Assert.False(model.Projects.Single(row => row.Project.BuildId == "loading-b").IsShown);
        Assert.True(model.Projects.Single(row => row.Project.BuildId == "loading-a").IsShoppingSelected);

        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option => option.Label == "Planning");
        Assert.Equal("planned", Assert.Single(model.VisibleProjects).Project.BuildId);
        model.ProjectSystemFilter = model.ProjectSystemOptions[0];
        model.ProjectBuildFilter = model.ProjectBuildOptions.Single(option => option.Label == "All Projects");
        Assert.Equal(
            ["build", "loading-a", "loading-b", "loading-complete", "planned"],
            model.VisibleProjects.Select(row => row.Project.BuildId).Order()
        );
    }

    /// <summary>Both bulk actions apply checked carriers to shown Loading results, retaining plan and synthetic-ID guards.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoadingProjectsSupportBulkCarrierActionsWithoutTouchingOtherCategories(bool link)
    {
        StubRavenColonialClient client = ProjectCarrierClient();
        client.Workspace = client.Workspace with
        {
            Projects =
            [
                .. client.Workspace.Projects,
                .. LoadingCarrierProjectIds.Select(id =>
                    LoadingProject(id, id == "loading-other" ? "Achenar" : "Sol") with
                    {
                        IsComplete = id == "loading-complete",
                        IsPlanned = id == "loading-planned",
                        LinkedFleetCarriers = link ? [] : [new() { MarketId = 42 }, new() { MarketId = 43 }],
                    }
                ),
            ],
            HiddenProjectIds = ["unchecked", "loading-hidden"],
        };
        using ColonizationViewModel model = await CreateProjectCarrierModelAsync(client);
        model.ProjectBuildFilter = Assert.Single(model.ProjectBuildOptions, option => option.Label == "Loading");
        model.CarrierSelection.SelectAllCommand.Execute(null);
        Assert.True(model.ShowProjectCarrierActions);
        Assert.True(model.LinkCarrierToSelectedProjectsCommand.CanExecute(null));
        Assert.True(model.UnlinkCarrierFromSelectedProjectsCommand.CanExecute(null));

        await ChangeProjectCarriersAsync(model, link);

        Assert.Equal(
            new[]
            {
                ("loading-a", 42L, link),
                ("loading-a", 43L, link),
                ("loading-b", 42L, link),
                ("loading-b", 43L, link),
            },
            client.ProjectCarrierRequests
        );
        foreach (string id in new[] { "loading-a", "loading-b" })
        {
            Assert.Equal(
                link ? new long[] { 42, 43 } : [],
                model
                    .Projects.Single(row => row.Project.BuildId == id)
                    .Project.LinkedFleetCarriers.Select(carrier => carrier.MarketId)
                    .Order()
            );
        }
        Assert.Equal(
            link ? "Carrier links added: 4. Failed: 0." : "Carrier links removed: 4. Failed: 0.",
            model.StatusMessage
        );
    }

    /// <summary>Creates a loading target with known remaining requirements and an ordinary, API-addressable build ID.</summary>
    private static ColonizationProject LoadingProject(string id, string system) =>
        Project(id, id, 10_080) with
        {
            BuildType = ColonizationProject.FleetCarrierLoadingBuildType,
            SystemName = system,
            SystemAddress = system == "Sol" ? 42 : 43,
            MaximumRequired = 10_080,
        };
}
