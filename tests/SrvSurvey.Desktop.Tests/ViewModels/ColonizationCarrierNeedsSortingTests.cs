using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    /// <summary>Carrier detection sorts the whole list and docking transitions restore normal market categories.</summary>
    [Theory]
    [InlineData("FleetCarrier", "[]", true)]
    [InlineData("fleetcarrier", "[]", true)]
    [InlineData("FleetCarrier", "[\"squadronBank\"]", true)]
    [InlineData(null, "[\"SquadronBank\"]", true)]
    [InlineData("Coriolis", "[]", false)]
    [InlineData("Outpost", "[]", false)]
    [InlineData(null, "[]", false)]
    public async Task CarrierNeedsSortingOnlyAppliesWhileDockedAtCarriers(
        string? stationType,
        string services,
        bool isCarrier
    )
    {
        ColonizationProject project = Project("needs-sort", "Port", 505, marketId: 44, systemAddress: 99) with
        {
            Commodities = new Dictionary<string, int>
            {
                ["aluminium"] = 400,
                ["copper"] = 50,
                ["steel"] = 50,
                ["water"] = 5,
            },
        };
        var client = new StubRavenColonialClient
        {
            Workspace = new ColonizationCommanderProjects([project], [], null, []),
        };
        using ColonizationViewModel viewModel = Create(client);
        viewModel.IsEnabled = true;
        await viewModel.SetCommanderAsync("Test Cmdr");
        string[] normalOrder = CommodityOrder(viewModel);
        string[] normalCategories = viewModel.CommodityOverlay.Groups.Select(group => group.Name).ToArray();
        viewModel.SortNeedsAscendingAtCarriers = true;
        Assert.Equal(normalOrder, CommodityOrder(viewModel));

        string dockProperties = $$"""
            "MarketID":42,"SystemAddress":99,"StarSystem":"Test System","StationName":"Test dock",
            "StationType":{{JsonSerializer.Serialize(stationType)}},"StationServices":{{services}}
            """;
        viewModel.ApplyJournalEvents([Event("Docked", dockProperties)]);
        string[] expectedOrder = isCarrier ? ["water", "copper", "steel", "aluminium"] : normalOrder;
        Assert.Equal(expectedOrder, CommodityOrder(viewModel));
        Assert.Equal(
            isCarrier ? ["Commodities"] : normalCategories,
            viewModel.CommodityOverlay.Groups.Select(group => group.Name)
        );

        viewModel.SortNeedsAscendingAtCarriers = false;
        Assert.Equal(normalOrder, CommodityOrder(viewModel));
        viewModel.SortNeedsAscendingAtCarriers = true;
        Assert.Equal(expectedOrder, CommodityOrder(viewModel));

        viewModel.ApplyJournalEvents([Event("Undocked", "")]);
        Assert.Equal(normalOrder, CommodityOrder(viewModel));
        viewModel.ApplyJournalEvents([Event("Location", dockProperties + ",\"Docked\":true")]);
        Assert.Equal(expectedOrder, CommodityOrder(viewModel));
        viewModel.ApplyJournalEvents([Event("Location", "\"Docked\":false")]);
        Assert.Equal(normalOrder, CommodityOrder(viewModel));
        viewModel.ApplyJournalEvents([Event("Docked", dockProperties)]);
        viewModel.ApplyJournalEvents([
            Event(
                "Docked",
                """
                "MarketID":43,"SystemAddress":99,"StarSystem":"Test System","StationName":"Normal station",
                "StationType":"Coriolis","StationServices":["commodities"]
                """
            ),
        ]);
        Assert.Equal(normalOrder, CommodityOrder(viewModel));
        Assert.Equal(normalCategories, viewModel.CommodityOverlay.Groups.Select(group => group.Name));
        viewModel.ApplyJournalEvents([
            Event(
                "Docked",
                """
                "MarketID":44,"SystemAddress":99,"StarSystem":"Test System",
                "StationName":"Orbital Construction Site: Test","StationType":"SpaceConstructionDepot",
                "StationServices":["colonisationcontribution"]
                """
            ),
        ]);
        Assert.True(viewModel.CommodityOverlay.Plan.IsAtConstructionSite);
        Assert.Equal(normalOrder, CommodityOrder(viewModel));
        Assert.Equal(normalCategories, viewModel.CommodityOverlay.Groups.Select(group => group.Name));
    }

    private static string[] CommodityOrder(ColonizationViewModel viewModel) =>
        viewModel.CommodityOverlay.Groups.SelectMany(group => group.Rows).Select(row => row.Commodity).ToArray();
}

public sealed class ColonizationCarrierNeedsSortingOverlayTests
{
    /// <summary>The single ascending list remains readable when linked carriers cover every category.</summary>
    [Fact]
    public void SortedCarrierListStaysExpandedAndUsesNeedRatherThanCargoDeficit()
    {
        var viewModel = new ColonizationCommodityOverlayViewModel();
        var plan = new ColonizationCommodityPlan
        {
            Title = "Test build",
            ProjectNames = ["Test build"],
            Rows =
            [
                new()
                {
                    Commodity = "steel",
                    DisplayName = "Steel",
                    Category = "Metals",
                    Needed = 100,
                    OnFleetCarriers = 300,
                },
                new()
                {
                    Commodity = "water",
                    DisplayName = "Water",
                    Category = "Chemicals",
                    Needed = 10,
                    OnFleetCarriers = 10,
                },
            ],
            FleetCarriers = [new() { MarketId = 1 }],
        };
        viewModel.ApplyPreferences(
            ColonizationOverlayPreferences.Default with
            {
                SortNeedsAscendingAtCarriers = true,
                HideCollapsedGroups = true,
            }
        );
        viewModel.Apply(plan, null, updatedIsDockedAtCarrier: true);

        ColonizationCommodityGroupViewModel group = Assert.Single(viewModel.Groups);
        Assert.False(group.IsCollapsed);
        Assert.Equal(["water", "steel"], group.Rows.Select(row => row.Commodity));
        Assert.True(group.Rows[0].IsAlternateRow);
        Assert.False(group.Rows[1].IsAlternateRow);
        viewModel.ToggleSatisfiedGroups();
        Assert.Equal(group.Rows, Assert.Single(viewModel.Groups).Rows);
        viewModel.ApplyPendingFleetCarrierCargo(["water"]);
        Assert.Equal(["water", "steel"], Assert.Single(viewModel.Groups).Rows.Select(row => row.Commodity));
    }
}
