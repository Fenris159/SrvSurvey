using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
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
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        store.SaveShopping("Test Cmdr", new ColonizationShoppingSession([], null));
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

    [Theory]
    [InlineData(null, "one,two")]
    [InlineData("one", "one")]
    [InlineData("two", "two")]
    [InlineData("hidden", "hidden")]
    [InlineData("complete", "one,two")]
    [InlineData("missing", "one,two")]
    public async Task FirstMarketVisitSelectsFocusedOrShownIncompleteProjects(string? focus, string expected)
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [
                    Project("one", "Building", 100),
                    Project("two", "Planned", 200) with
                    {
                        IsPlanned = true,
                        SystemName = "Other System",
                    },
                    Project("hidden", "Hidden", 300),
                    Project("complete", "Complete", 0) with
                    {
                        IsComplete = true,
                    },
                ],
                ["hidden"],
                focus,
                []
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        model.ProjectSystemFilter = model.ProjectSystemOptions.Single(option => option.SystemName == "Test System");
        model.ProjectBuildFilter = model.ProjectBuildOptions[1];
        model.OpenShopping();
        string[] selected = expected.Split(',');
        Assert.Equal(
            selected,
            model
                .Projects.Where(row => row.IsShoppingSelected)
                .Select(row => row.Project.BuildId)
                .Order(StringComparer.Ordinal)
        );
        Assert.Equal(selected, model.MarketSearchWorkspace!.ProjectIds.Order(StringComparer.Ordinal));
        Assert.True(model.CanSearchMarkets);
        Assert.False(model.HasUnsavedProjectVisibility);
        Assert.False(model.Projects.Single(row => row.Project.BuildId == "hidden").IsShown);
        Assert.Equal(0, client.SaveCount);
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Equal(selected, store.LoadShopping("Test Cmdr")!.ProjectIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FirstMarketVisitUsesLatestShowChoicesAndKeepsAnExplicitlyClearedSelection()
    {
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("one", "First", 100), Project("two", "Second", 200)], [], null, []),
        };
        using (ColonizationViewModel model = Create(client))
        {
            model.IsEnabled = true;
            await model.SetCommanderAsync("Test Cmdr");
            model.Projects[0].IsShown = false;
            model.OpenShopping();
            Assert.Equal("two", Assert.Single(model.MarketSearchWorkspace!.ProjectIds));
            model.Projects[1].IsShoppingSelected = false;
            model.SelectedWorkspaceTabIndex = 0;
            model.OpenShopping();
            await model.RefreshAsync();
            Assert.Null(model.MarketSearchWorkspace);
            Assert.All(model.Projects, row => Assert.False(row.IsShoppingSelected));
        }
        using ColonizationViewModel restarted = Create(client);
        await restarted.SetCommanderAsync("Test Cmdr");
        restarted.OpenShopping();
        Assert.Null(restarted.MarketSearchWorkspace);
        Assert.All(restarted.Projects, row => Assert.False(row.IsShoppingSelected));
        await restarted.SetCommanderAsync("Other Cmdr");
        Assert.Equal(2, restarted.MarketSearchWorkspace!.ProjectIds.Count);
        await restarted.SetCommanderAsync("Test Cmdr");
        Assert.Null(restarted.MarketSearchWorkspace);
    }

    [Fact]
    public async Task MarketDefaultsWaitForCurrentProjectsAndForAtLeastOneTrackedProject()
    {
        var response = new TaskCompletionSource<ColonizationCommanderProjects>();
        var client = new StubRavenColonialClient { LoadWorkspace = _ => response.Task };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        Task load = model.SetCommanderAsync("Test Cmdr");
        model.OpenShopping();
        Assert.Null(model.MarketSearchWorkspace);
        var store = new ColonizationSettingsStore(Path.Combine(directory, "ui.json"));
        Assert.Null(store.LoadShopping("Test Cmdr"));
        response.SetResult(new([Project("one", "Hidden", 100)], ["one"], null, []));
        await load;
        Assert.Null(model.MarketSearchWorkspace);
        Assert.Null(store.LoadShopping("Test Cmdr"));
        model.Projects[0].IsShown = true;
        model.OpenShopping();
        Assert.Equal("one", Assert.Single(model.MarketSearchWorkspace!.ProjectIds));
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
