using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task ShoppingSelectionIsIndependentAndSurvivesRefreshUntilCommanderChanges()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects(
                [Project("one", "Port", remaining: 100), Project("two", "Planned", remaining: 200)],
                ["one"],
                "two",
                []
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.OpenShopping();
        Assert.Null(model.MarketSearchWorkspace);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        Assert.False(model.CanSearchMarkets);
        ColonizationProjectRowViewModel row = model.Projects.Single(row => row.Project.BuildId == "one");
        row.IsShoppingSelected = true;
        row.IsShoppingSelected = true;
        Assert.True(model.SearchMarketsCommand.CanExecute(null));
        model.SearchMarketsCommand.Execute(null);
        Assert.Equal(row.Project.BuildId, Assert.Single(model.MarketSearchWorkspace!.ProjectIds));
        Assert.False(row.IsShown);
        Assert.False(model.HasUnsavedProjectVisibility);
        Assert.Equal(0, client.SaveCount);
        await model.RefreshAsync();
        Assert.True(model.Projects.Single(row => row.Project.BuildId == "one").IsShoppingSelected);
        row.IsShoppingSelected = false;
        Assert.True(model.CanSearchMarkets);
        model.Projects.Single(row => row.Project.BuildId == "one").IsShoppingSelected = false;
        Assert.False(model.CanSearchMarkets);
        model.Projects[1].IsShoppingSelected = true;
        model.IsEnabled = false;
        Assert.False(model.CanSearchMarkets);
        await model.SetCommanderAsync(null);
        Assert.Empty(model.Projects);
    }

    [Fact]
    public async Task ShoppingAutofillsLatestJournalLocationWithoutApplyingPrimarySelection()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects([Project("one", "Port", remaining: 100)], [], "one", []),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Sol", null, 10477373803);
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            model.Projects.Select(row => row.Project).ToArray(),
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("Sol", shopping.ReferenceSystem);
        model.UpdateSystemContext("Achenar", null, 1);
        shopping.UseCurrentLocation();
        Assert.Equal("Achenar", shopping.ReferenceSystem);
    }
}
