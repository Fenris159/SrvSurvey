using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class ColonizationViewModelTests
{
    [Fact]
    public async Task CommanderPlanningDiscoveryIsIndependentOfPlayerLocation()
    {
        ColonizationSystemRecord remote = FilterPlanningSystem("Remote Colony", 47);
        var client = new StubRavenColonialClient
        {
            Workspace = new([Project("live", "Active", 100) with { SystemName = "Sol" }], [], null, []),
            DiscoveredPlanningSystems = [remote],
            PlanningSystems = new() { ["Remote Colony"] = remote },
        };
        using ColonizationViewModel model = Create(client);
        model.IsEnabled = true;
        model.SetCommanderProfile("F123", true, "test-key");
        await model.SetCommanderAsync("Test Cmdr");
        model.UpdateSystemContext("Achenar", null, 43);
        await model.RefreshAsync();
        ColonizationProjectRowViewModel plan = Assert.Single(model.Projects, row => row.Project.IsPlanned);
        Assert.Equal("Remote Colony", plan.SystemName);
        Assert.Contains("Test Cmdr", client.PlanningDiscoveryCommanders);
    }

    private static ColonizationSystemRecord FilterPlanningSystem(string name, long address) =>
        new()
        {
            Name = name,
            SystemAddress = address,
            Architect = "Test Cmdr",
            Sites =
            [
                new()
                {
                    Id = "plan",
                    Name = "Planned Port",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
            ],
        };
}
