using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarketCarrierSelectionMirrorsUseCarrierAndPersists(bool useDifference)
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [Project("one", "Port", 100)],
                [],
                null,
                [
                    new()
                    {
                        MarketId = 42,
                        Name = "AAA-111",
                        Cargo = new() { ["steel"] = 60 },
                    },
                ]
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.Projects[0].IsShoppingSelected = true;
        using ColonizationShoppingViewModel search = model.CreateShopping(
            [model.Projects[0].Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        search.UseCarrierDifference = useDifference;
        search.SelectedCarrier = search.CarrierOptions.Single(carrier => carrier.MarketId == 42);
        Assert.Equal(42, model.SelectedPlanningCarrierMarketId);
        Assert.Equal(
            42,
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")).LoadPlanningCarrier("Test Cmdr")
        );
        model.SelectedPlanningCarrierMarketId = 0;
        Assert.Equal(0, search.SelectedCarrier.MarketId);
        Assert.Equal(useDifference, search.UseCarrierDifference);
    }

    [Fact]
    public async Task CurrentSystemPlannedSitesAppearAsProjectsAndSupplyMarketNeeds()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("one", "Active", 100) with { SystemName = "Sol" }], [], null, []),
            SystemSitesResponse =
            [
                new()
                {
                    Id = "planned",
                    Name = "Test Project",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.RefreshAsync();
        ColonizationProjectRowViewModel plan = Assert.Single(
            model.Projects,
            row => row.BuildName == "Test Project (Plan)"
        );
        Assert.True(plan.Project.RemainingRequired > 0);
        Assert.StartsWith("~", plan.ProgressText);
        Assert.Contains("catalog", plan.ProjectPreviewHint);
        Assert.NotEmpty(plan.Project.Commodities);
        plan.IsShoppingSelected = true;
        model.SelectedWorkspaceTabIndex = 1;
        Assert.NotEmpty(model.MarketSearchWorkspace!.VisibleNeeds);
        Assert.Contains(plan.Project.BuildId, model.MarketSearchWorkspace.ProjectIds);
    }

    [Fact]
    public async Task PlannedShoppingListAndOverlayChoiceSurviveTravelRestartAndCommanderChanges()
    {
        var home = new ColonizationSystemRecord
        {
            Name = "Sol",
            SystemAddress = 42,
            Architect = "Test Cmdr",
            Bodies = [],
            Sites =
            [
                new()
                {
                    Id = "p",
                    Name = "Test Project",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new([], [], null, []),
            LoadPlanningDiscovery = (commander, _) =>
                Task.FromResult<IReadOnlyList<ColonizationSystemRecord>>(commander == "Test Cmdr" ? [home] : []),
            PlanningSystems = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Sol"] = home,
                ["Achenar"] = new() { Name = "Achenar", SystemAddress = 43 },
            },
        };
        long quantity;
        using (ColonizationViewModel model = Create(client))
        {
            model.MarketSearchFactory = () =>
                new ColonizationShoppingViewModelTests.SearchStub([
                    ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
                ]);
            model.IsEnabled = true;
            model.SetCommanderProfile("F123", true, "test-key");
            await model.SetCommanderAsync("Test Cmdr");
            model.UpdateSystemContext("Sol", null, 42);
            ColonizationProjectRowViewModel plan = Assert.Single(model.Projects);
            plan.IsShoppingSelected = true;
            plan.IsShown = false;
            Assert.False(model.HasUnsavedProjectVisibility);
            model.SelectedWorkspaceTabIndex = 1;
            ColonizationShoppingViewModel shopping = model.MarketSearchWorkspace!;
            await shopping.SearchAsync();
            shopping.Results[0].AvailableSupplies[0].ToggleWishListCommand.Execute(null);
            quantity = Assert.Single(shopping.CaptureState().WishList).Quantity;
            model.UpdateSystemContext("Achenar", null, 43);
            await model.RefreshAsync();
            Assert.Equal("Test Project (Plan)", Assert.Single(model.Projects).BuildName);
            Assert.Equal(quantity, Assert.Single(shopping.CaptureState().WishList).Quantity);
        }
        using ColonizationViewModel reopened = Create(client);
        reopened.IsEnabled = true;
        reopened.SetCommanderProfile("F123", true, "test-key");
        await reopened.SetCommanderAsync("Test Cmdr");
        ColonizationProjectRowViewModel restored = Assert.Single(reopened.Projects);
        Assert.True(restored.IsShoppingSelected);
        Assert.False(restored.IsShown);
        reopened.SelectedWorkspaceTabIndex = 1;
        Assert.Equal(quantity, Assert.Single(reopened.MarketSearchWorkspace!.CaptureState().WishList).Quantity);
        await reopened.SetCommanderAsync("Other Cmdr");
        Assert.Empty(reopened.Projects);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Equal("Test Project (Plan)", Assert.Single(reopened.Projects).BuildName);
        Assert.Equal(0, client.SaveCount);
    }

    [Fact]
    public async Task SystemEditorPlanChangesUpdateNeedsWithoutPublishingAndReadFailuresKeepThePlan()
    {
        var home = new ColonizationSystemRecord
        {
            Name = "Sol",
            SystemAddress = 42,
            Architect = "Test Cmdr",
            Bodies = [],
            Sites =
            [
                new()
                {
                    Id = "p",
                    Name = "Test Project",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new([], [], null, []),
            PlanningSystems = new() { ["Sol"] = home, ["42"] = home },
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        model.SystemEditor.Sites[0].Name = "Edited plan";
        Assert.Equal("Edited plan (Plan)", Assert.Single(model.Projects).BuildName);
        model.SystemEditor.Sites[0].Status = ColonizationSystemSiteStatus.Complete;
        Assert.Empty(model.Projects);
        model.SystemEditor.Sites[0].Status = ColonizationSystemSiteStatus.Plan;
        client.PlanningSystemFailure = new HttpRequestException("Offline");
        await model.RefreshAsync();
        Assert.Equal("Edited plan (Plan)", Assert.Single(model.Projects).BuildName);
        model.SystemEditor.Sites.Clear();
        Assert.Empty(model.Projects);
        Assert.Empty(client.SystemUpdates);
    }

    [Fact]
    public async Task DisablingColonizationDiscardsAnObsoletePlannedSiteRead()
    {
        var pending = new TaskCompletionSource<ColonizationSystemRecord>();
        var client = new StubRavenColonialClient
        {
            Workspace = new([], [], null, []),
            PlanningSystemTask = pending.Task,
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        Task refresh = model.RefreshAsync();
        model.IsEnabled = false;
        pending.SetResult(
            new()
            {
                Name = "Sol",
                SystemAddress = 42,
                Sites =
                [
                    new()
                    {
                        Id = "p",
                        Name = "Late plan",
                        BuildType = "asteroid",
                        Status = ColonizationSystemSiteStatus.Plan,
                    },
                ],
            }
        );
        await refresh;
        Assert.Empty(model.Projects);
    }

    [Fact]
    public void PlannedProjectPreferencesRejectMalformedCacheAndPreserveCommanderScopes()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "plans-ui.json");
        var store = new ColonizationSettingsStore(path);
        Assert.Empty(store.LoadPlannedSystems("Test Cmdr"));
        store.SaveHiddenPlannedProjects("First Cmdr", ["plan:42:first"]);
        File.WriteAllText(path + ".plans.json", "{\"TEST CMDR\":\"invalid\"}");
        Assert.Empty(store.LoadPlannedSystems("Test Cmdr"));
        store.SavePlannedSystems("Test Cmdr", [new() { Name = "Sol", SystemAddress = 42 }]);
        Assert.Equal("Sol", Assert.Single(store.LoadPlannedSystems("test cmdr")).Name);
        Assert.Empty(store.LoadPlannedSystems("Other Cmdr"));
        File.WriteAllText(
            path,
            "{\"Colonization\":{\"HiddenPlannedProjects\":{\"TEST CMDR\":[null,4,\"\",\"plan:42:p\"]}}}"
        );
        Assert.Equal("plan:42:p", Assert.Single(store.LoadHiddenPlannedProjects("test cmdr")));
        store.SaveHiddenPlannedProjects("Other Cmdr", ["plan:42:q"]);
        Assert.Equal("plan:42:q", Assert.Single(store.LoadHiddenPlannedProjects("Other Cmdr")));
        Assert.Equal("plan:42:p", Assert.Single(store.LoadHiddenPlannedProjects("Test Cmdr")));
    }
}
