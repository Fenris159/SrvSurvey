using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConsentCycleKeepsSelectedPlansAndShoppingListUntilAuthoritativeRefresh(bool edit)
    {
        var clock = new PlanningSaveClock();
        StubRavenColonialClient client = ReviewPlanningClient();
        using ColonizationViewModel model = Create(client, clock.DelayAsync);
        model.MarketSearchFactory = () =>
            new ColonizationShoppingViewModelTests.SearchStub([
                ColonizationShoppingViewModelTests.Quote(1, "steel", 100),
            ]);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 42);
        await model.SystemEditor.LoadAsync();
        if (edit)
        {
            model.SystemEditor.Sites[0].Name = "Unsaved local plan";
        }
        ColonizationProjectRowViewModel plan = Assert.Single(model.Projects, row => row.Project.IsPlanned);
        string id = plan.Project.BuildId;
        string name = plan.BuildName;
        plan.IsShoppingSelected = true;
        model.SelectedWorkspaceTabIndex = 1;
        await model.MarketSearchWorkspace!.SearchAsync();
        model.MarketSearchWorkspace.Results[0].AvailableSupplies[0].ToggleWishListCommand.Execute(null);
        long quantity = Assert.Single(model.MarketSearchWorkspace.CaptureState().WishList).Quantity;
        Task pending = model.PlannedSystemsSaveTask;
        model.IsEnabled = false;
        Assert.Empty(model.Projects);
        Assert.False(model.CanSearchMarkets);
        client.LoadPlanningSystem = (_, _) =>
            Task.FromException<ColonizationSystemRecord>(new HttpRequestException("Planning is offline"));
        model.IsEnabled = true;
        await model.RefreshAsync();
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Contains(id, store.LoadShopping("Test Cmdr")!.ProjectIds);
        ColonizationProjectRowViewModel restored = Assert.Single(model.Projects, row => row.Project.IsPlanned);
        Assert.Equal(name, restored.BuildName);
        Assert.True(restored.IsShoppingSelected);
        Assert.Equal(quantity, Assert.Single(model.MarketSearchWorkspace.CaptureState().WishList).Quantity);
        Assert.Equal(quantity, Assert.Single(store.LoadShopping("Test Cmdr")!.Search!.WishList).Quantity);
        client.LoadPlanningSystem = (_, _) =>
            Task.FromResult(new ColonizationSystemRecord { Name = "Sol", SystemAddress = 42 });
        await model.RefreshAsync();
        Assert.Empty(store.LoadShopping("Test Cmdr")!.ProjectIds);
        Assert.Null(model.MarketSearchWorkspace);
        Assert.DoesNotContain(model.Projects, row => row.Project.IsPlanned);
        clock.ReleaseAll();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(store.LoadPlannedSystems("Test Cmdr"));
    }
}
