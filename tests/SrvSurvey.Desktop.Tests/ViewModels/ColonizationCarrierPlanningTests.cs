using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task PlanningCarrierDrivesCoverageAndOpenOrClosedSearchAndPersistsPerCommander()
    {
        var carrier = new ColonizationFleetCarrier
        {
            MarketId = 42,
            Name = "AAA-111",
            DisplayName = "Personal",
            Cargo = new() { ["steel"] = 60 },
        };
        var teammate = new ColonizationFleetCarrier
        {
            MarketId = 43,
            Name = "BBB-222",
            DisplayName = "Team",
            Owner = "Someone else",
            Cargo = new() { ["steel"] = 20 },
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new(
                [Project("one", "Port", 100), Project("two", "Planned", 200)],
                [],
                null,
                [carrier, teammate]
            ),
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        await model.SetCommanderAsync("Test Cmdr");
        Assert.Equal("ALL", model.SelectedPlanningCarrier.Label);
        Assert.Equal(3, model.PlanningCarrierOptions.Count);
        Assert.Equal("80%", model.Projects.Single(row => row.Project.BuildId == "one").CarrierCoverageText);
        Assert.Equal("40%", model.Projects.Single(row => row.Project.BuildId == "two").CarrierCoverageText);
        model.Projects.Single(row => row.Project.BuildId == "one").IsShoppingSelected = true;
        using ColonizationShoppingViewModel shopping = model.CreateShopping(
            [model.Projects.Single(row => row.Project.BuildId == "one").Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal("Steel: 20", shopping.NeedsText);
        model.SelectedPlanningCarrier = model.PlanningCarrierOptions.Single(choice => choice.MarketId == 43);
        Assert.Equal("20%", model.Projects.Single(row => row.Project.BuildId == "one").CarrierCoverageText);
        Assert.Equal(43, shopping.SelectedCarrier.MarketId);
        Assert.Equal("Steel: 80", shopping.NeedsText);
        shopping.Dispose();
        model.SelectedPlanningCarrier = model.PlanningCarrierOptions.Single(choice => choice.MarketId == 42);
        Assert.Equal(
            42,
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json"))
                .LoadShopping("Test Cmdr")!
                .Search!.SelectedCarrierMarketId
        );
        using ColonizationViewModel reopened = Create(client);
        await reopened.SetCommanderAsync("test cmdr");
        Assert.Equal(42, reopened.SelectedPlanningCarrier.MarketId);
        using ColonizationShoppingViewModel search = reopened.CreateShopping(
            [reopened.Projects.Single(row => row.Project.BuildId == "one").Project],
            new ColonizationShoppingViewModelTests.SearchStub([])
        );
        Assert.Equal(42, search.SelectedCarrier.MarketId);
        Assert.Equal("Steel: 40", search.NeedsText);
        client.Workspace = client.Workspace with
        {
            FleetCarriers = [carrier with { Cargo = new() { ["steel"] = 90 } }, teammate],
        };
        await reopened.RefreshAsync();
        Assert.Equal("90%", reopened.Projects.Single(row => row.Project.BuildId == "one").CarrierCoverageText);
        Assert.Equal("Steel: 10", search.NeedsText);
        await reopened.SetCommanderAsync("Other Cmdr");
        Assert.Equal("ALL", reopened.SelectedPlanningCarrier.Label);
        await reopened.SetCommanderAsync("Test Cmdr");
        Assert.Equal(42, reopened.SelectedPlanningCarrier.MarketId);
        client.Workspace = client.Workspace with { FleetCarriers = [teammate] };
        await reopened.RefreshAsync();
        Assert.Equal("ALL", reopened.SelectedPlanningCarrier.Label);
        Assert.Equal("20%", reopened.Projects.Single(row => row.Project.BuildId == "one").CarrierCoverageText);
    }

    [Fact]
    public void PlanningCarrierStoreHandlesMalformedAndCommanderSpecificPreferences()
    {
        string path = Path.Combine(directory, "ui.json");
        var store = new ColonizationSettingsStore(path);
        Assert.Equal(0, store.LoadPlanningCarrier("Cmdr"));
        store.SavePlanningCarrier("Cmdr", 42);
        Assert.Equal(42, store.LoadPlanningCarrier("CMDR"));
        Assert.Equal(0, store.LoadPlanningCarrier("Other Cmdr"));
        File.WriteAllText(path, """{"Colonization":{"PlanningCarriers":{"CMDR":-1}}}""");
        Assert.Equal(0, store.LoadPlanningCarrier("Cmdr"));
        File.WriteAllText(path, """{"Colonization":{"PlanningCarriers":0}}""");
        Assert.Equal(0, store.LoadPlanningCarrier("Cmdr"));
        store.SavePlanningCarrier("Cmdr", 43);
        Assert.Equal(43, store.LoadPlanningCarrier("Cmdr"));
    }
}
