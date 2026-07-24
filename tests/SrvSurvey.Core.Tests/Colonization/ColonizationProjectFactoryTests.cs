using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationProjectFactoryTests
{
    private readonly ColonizationProjectFactory factory = new(
        ColonizationBuildCatalog.LoadEmbedded());

    [Fact]
    public void CreatesLegacyCompatiblePayloadFromLiveDepot()
    {
        var result = factory.Create(
            Draft(),
            Dock(),
            Depot());

        Assert.True(result.IsValid);
        var project = Assert.IsType<ColonizationProjectCreate>(result.Project);
        Assert.Equal("no_truss", project.BuildType);
        Assert.Equal("Primary port", project.BuildName);
        Assert.Equal(42, project.MarketId);
        Assert.Equal(99, project.SystemAddress);
        Assert.Equal([1d, 2d, 3d], project.StarPosition);
        Assert.Equal(140, project.MaximumRequired);
        Assert.Equal(75, project.Commodities["steel"]);
        Assert.Equal(30, project.Commodities["water"]);
        Assert.Contains("Test Cmdr", project.Commanders.Keys);
        Assert.Equal("site-1", project.SystemSiteId);
        Assert.Equal(2, project.ConstructionDepot?.ResourcesRequired.Count);
    }

    [Fact]
    public void CombinesDuplicateCommodityRowsWithoutLosingRemainingCargo()
    {
        var depot = Depot() with
        {
            Resources =
            [
                new ColonizationResourceRequirement(
                    "steel", "Steel", 100, 25, 1),
                new ColonizationResourceRequirement(
                    "STEEL", "Steel", 50, 10, 1),
            ],
        };

        var result = factory.Create(Draft(), Dock(), depot);

        Assert.True(result.IsValid);
        Assert.Equal(115, result.Project?.Commodities["steel"]);
        Assert.Equal(150, result.Project?.MaximumRequired);
    }

    [Fact]
    public void RejectsStaleDepotUnknownLayoutAndInvalidPosition()
    {
        var draft = Draft() with
        {
            BuildType = "unknown-layout",
            StarPosition = [double.NaN, 2, 3],
        };
        var depot = Depot() with { MarketId = 999 };

        var result = factory.Create(draft, Dock(), depot);

        Assert.False(result.IsValid);
        Assert.Null(result.Project);
        Assert.Contains(result.Errors, error => error.Contains("different market"));
        Assert.Contains(result.Errors, error => error.Contains("known"));
        Assert.Contains(result.Errors, error => error.Contains("finite"));
    }

    [Fact]
    public void RejectsMissingDockDepotAndCommander()
    {
        var result = factory.Create(
            Draft() with { CommanderName = string.Empty },
            dock: null,
            depot: null);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void RejectsCompletedOrFailedDepot()
    {
        var result = factory.Create(
            Draft(),
            Dock(),
            Depot() with { IsComplete = true, IsFailed = true });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("complete"));
        Assert.Contains(result.Errors, error => error.Contains("failed"));
    }

    private static ColonizationProjectDraft Draft()
    {
        return new ColonizationProjectDraft(
            "Test Cmdr",
            "Test System",
            [1, 2, 3],
            "No_Truss",
            " Primary port ",
            " Architect ",
            " Notes ",
            BodyNumber: 3,
            BodyName: "Test System 3",
            SystemSiteId: " site-1 ");
    }

    private static ColonizationDockingSnapshot Dock()
    {
        return new ColonizationDockingSnapshot(
            42,
            99,
            "Test System",
            "$EXT_PANEL_ColonisationShip; Primary",
            "Test Faction",
            ["colonisationcontribution"]);
    }

    private static ColonizationConstructionDepotSnapshot Depot()
    {
        return new ColonizationConstructionDepotSnapshot(
            DateTimeOffset.UtcNow,
            42,
            0.25,
            IsComplete: false,
            IsFailed: false,
            [
                new ColonizationResourceRequirement(
                    "steel", "Steel", 100, 25, 1),
                new ColonizationResourceRequirement(
                    "water", "Water", 40, 10, 1),
            ]);
    }
}
