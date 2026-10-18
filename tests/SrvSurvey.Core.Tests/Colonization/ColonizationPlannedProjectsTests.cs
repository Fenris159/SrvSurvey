using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationPlannedProjectsTests
{
    private static readonly ColonizationBuildCatalog catalog = ColonizationBuildCatalog.LoadEmbedded();

    private static readonly double[] position = [1, 2, 3];

    [Fact]
    public void AsteroidPlanUsesCatalogCargoAndCannotBecomeALiveProjectThroughSerialization()
    {
        var site = new ColonizationSystemSite
        {
            Id = "planned",
            Name = "Test Project",
            BuildType = "asteroid",
            BodyNumber = 36,
            BodyName = "Sol 36",
            Status = ColonizationSystemSiteStatus.Plan,
        };
        var system = new ColonizationSystemRecord
        {
            Name = "Sol",
            SystemAddress = 42,
            Architect = "Cmdr",
            Sites = [site],
        };
        ColonizationProject project = Assert.Single(
            ColonizationPlannedProjects.Create(system, catalog, [], "Cmdr", position)
        );
        Assert.Equal("plan:42:planned", project.BuildId);
        Assert.Equal("Test Project (Plan)", project.BuildName);
        Assert.True(project.IsPlanned);
        Assert.Equal(0, project.MarketId);
        Assert.Equal(36, project.BodyNumber);
        Assert.Equal("Sol 36", project.BodyName);
        Assert.Equal(position, project.StarPosition);
        Assert.Equal(
            catalog.FindByBuildType("asteroid")!.CommodityCosts.OrderBy(pair => pair.Key),
            project.Commodities.OrderBy(pair => pair.Key)
        );
        Assert.Equal(project.Commodities.Values.Sum(), project.RemainingRequired);
        Assert.Equal(project.RemainingRequired, project.MaximumRequired);
        Assert.False(JsonSerializer.Deserialize<ColonizationProject>(JsonSerializer.Serialize(project))!.IsPlanned);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlanningUsesTheExistingSiteVisibilityRulesAndSkipsUnknownOrCompletedBuilds(bool architect)
    {
        ColonizationBuildCost surface = catalog.Builds.First(build =>
            build.Location == ColonizationBuildLocation.Surface
        );
        var system = new ColonizationSystemRecord
        {
            Name = "Sol",
            SystemAddress = 42,
            Architect = "Cmdr",
            Sites =
            [
                new()
                {
                    Id = "surface",
                    Name = "Surface",
                    BuildType = surface.BuildType,
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new()
                {
                    Id = "orbital",
                    Name = "Orbital",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new()
                {
                    Id = "unknown",
                    Name = "Unknown",
                    BuildType = "not-a-layout",
                    Status = ColonizationSystemSiteStatus.Plan,
                },
                new()
                {
                    Id = "complete",
                    Name = "Complete",
                    BuildType = "asteroid",
                    Status = ColonizationSystemSiteStatus.Complete,
                },
            ],
        };
        IReadOnlyList<ColonizationProject> projects = ColonizationPlannedProjects.Create(
            system,
            catalog,
            [],
            architect ? "Cmdr" : "Other",
            []
        );
        Assert.Equal(architect ? 2 : 1, projects.Count);
        Assert.Contains(projects, project => project.BuildName == "Orbital (Plan)");
    }

    [Theory]
    [InlineData("build")]
    [InlineData("market")]
    [InlineData("name")]
    [InlineData("other-system")]
    public void LiveBuildsReplaceTheirMatchingPlansWithoutDoubleCountingNeeds(string match)
    {
        var site = new ColonizationSystemSite
        {
            Id = "site",
            Name = "Planned",
            BuildType = "asteroid",
            Status = ColonizationSystemSiteStatus.Plan,
            BuildId = match == "build" ? "active" : null,
            MarketId = match == "market" ? 100 : null,
        };
        var system = new ColonizationSystemRecord
        {
            Name = "Sol",
            SystemAddress = 42,
            Sites = [site],
        };
        var active = new ColonizationProject
        {
            BuildId = "active",
            MarketId = 100,
            BuildName = match is "name" or "other-system" ? "Planned" : "Active",
            SystemName = match == "other-system" ? "Elsewhere" : "Sol",
        };
        IReadOnlyList<ColonizationProject> projects = ColonizationPlannedProjects.Create(
            system,
            catalog,
            [active],
            "Cmdr",
            []
        );
        Assert.Equal(match == "other-system" ? 1 : 0, projects.Count);
    }
}
