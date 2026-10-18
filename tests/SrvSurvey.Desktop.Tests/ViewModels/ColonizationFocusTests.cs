using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task PlannedFocusTracksCatalogNeedsAndCarrierCargoWithoutPublishingSyntheticBuild()
    {
        StubRavenColonialClient client = FocusClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.RefreshAsync();
        ColonizationProjectRowViewModel plan = model.Projects.Single(row => row.Project.IsPlanned);
        plan.IsShown = false;

        Assert.Equal("Make Focus", plan.FocusActionLabel);
        Assert.True(plan.ToggleFocusCommand.CanExecute(null));
        await model.ToggleFocusProjectAsync(plan);

        ColonizationProjectRowViewModel focused = model.Projects.Single(row => row.Project.IsPlanned);
        Assert.True(focused.IsFocused);
        Assert.Equal("FOCUS", focused.FocusLabel);
        Assert.Equal("Clear Focus", focused.FocusActionLabel);
        Assert.False(model.Projects.Single(row => !row.Project.IsPlanned).IsFocused);
        Assert.Contains("Test Project (Plan)", model.CommodityOverlay.Plan.Title);
        ColonizationCommodityPlanRow steel = model.CommodityOverlay.Plan.Rows.Single(row => row.Commodity == "steel");
        Assert.Equal(plan.Project.Commodities["steel"], steel.Needed);
        Assert.Equal(85, steel.OnFleetCarriers);
        model.SelectedPlanningCarrierMarketId = 99;
        Assert.Equal(60, model.CommodityOverlay.Plan.Rows.Single(row => row.Commodity == "steel").OnFleetCarriers);
        model.SelectedPlanningCarrierMarketId = 100;
        Assert.Equal(25, model.CommodityOverlay.Plan.Rows.Single(row => row.Commodity == "steel").OnFleetCarriers);
        Assert.Empty(client.PrimaryProjectRequests);
    }

    [Fact]
    public async Task PlannedFocusSurvivesRefreshTravelRestartAndCommanderChanges()
    {
        StubRavenColonialClient client = FocusClient();
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
            await model.RefreshAsync();
            Assert.True(model.Projects.Single(row => row.Project.IsPlanned).IsFocused);
            model.UpdateSystemContext("Achenar", null, 43);
            await model.RefreshAsync();
            Assert.True(model.Projects.Single(row => row.Project.IsPlanned).IsFocused);
        }
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.True(reopened.Projects.Single(row => row.Project.IsPlanned).IsFocused);
        await reopened.SetCommanderAsync("Other Cmdr");
        Assert.False(reopened.Projects.Single(row => row.Project.IsPlanned).IsFocused);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.True(reopened.Projects.Single(row => row.Project.IsPlanned).IsFocused);
        Assert.Empty(client.PrimaryProjectRequests);
    }

    [Fact]
    public async Task ClearingPlannedFocusRestoresShowAggregateAndCanSwitchBackToLiveFocus()
    {
        StubRavenColonialClient client = FocusClient();
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
            await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
            Assert.DoesNotContain(model.Projects, row => row.IsFocused);
            Assert.Equal(
                model.Projects.Sum(row => row.Project.RemainingRequired),
                model.CommodityOverlay.Plan.TotalRemaining
            );
        }
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.DoesNotContain(reopened.Projects, row => row.IsFocused);
        await reopened.ToggleFocusProjectAsync(reopened.Projects.Single(row => row.Project.IsPlanned));
        await reopened.ToggleFocusProjectAsync(reopened.Projects.Single(row => !row.Project.IsPlanned));
        Assert.Equal("active", Assert.Single(client.PrimaryProjectRequests));
        Assert.Equal("active", Assert.Single(reopened.Projects, row => row.IsFocused).Project.BuildId);
        Assert.Equal(100, reopened.CommodityOverlay.Plan.TotalRemaining);
        await reopened.ToggleFocusProjectAsync(reopened.Projects.Single(row => row.IsFocused));
        Assert.Null(client.PrimaryProjectRequests[1]);
        Assert.DoesNotContain(reopened.Projects, row => row.IsFocused);
    }

    [Fact]
    public async Task FailedLocalFocusSaveKeepsThePreviousFocus()
    {
        StubRavenColonialClient client = FocusClient();
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        string path = Path.Combine(directory, "ui.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        await model.ToggleFocusProjectAsync(model.Projects.Single(row => row.Project.IsPlanned));
        Assert.Equal("active", Assert.Single(model.Projects, row => row.IsFocused).Project.BuildId);
        Assert.Contains("could not be saved", model.StatusMessage);
        Assert.False(model.IsBusy);
        Assert.Empty(client.PrimaryProjectRequests);
    }

    private static StubRavenColonialClient FocusClient() =>
        new()
        {
            Workspace = new(
                [Project("active", "Active", 100) with { SystemName = "Sol" }],
                [],
                "active",
                [
                    new()
                    {
                        MarketId = 99,
                        Name = "AAA-111",
                        Cargo = new() { ["steel"] = 60 },
                    },
                    new()
                    {
                        MarketId = 100,
                        Name = "BBB-222",
                        Cargo = new() { ["steel"] = 25 },
                    },
                ]
            ),
            PlanningSystems = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Sol"] = new()
                {
                    Name = "Sol",
                    SystemAddress = 42,
                    Architect = "Test Cmdr",
                    Sites =
                    [
                        new()
                        {
                            Id = "planned",
                            Name = "Test Project",
                            BuildType = "asteroid",
                            Status = ColonizationSystemSiteStatus.Plan,
                        },
                    ],
                },
                ["Achenar"] = new() { Name = "Achenar", SystemAddress = 43 },
            },
        };
}
