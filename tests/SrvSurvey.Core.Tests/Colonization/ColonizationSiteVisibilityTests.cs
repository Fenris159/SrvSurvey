using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationSiteVisibilityTests
{
    private readonly ColonizationBuildCatalog catalog = ColonizationBuildCatalog.LoadEmbedded();

    [Fact]
    public void ArchitectMatchIsCaseInsensitiveAndRequiresBothNames()
    {
        Assert.True(ColonizationSiteVisibility.CommanderIsArchitect("Test Cmdr", "test cmdr"));
        Assert.True(ColonizationSiteVisibility.CommanderIsArchitect(" Test Cmdr ", "Test Cmdr"));
        Assert.False(ColonizationSiteVisibility.CommanderIsArchitect(null, "Test Cmdr"));
        Assert.False(ColonizationSiteVisibility.CommanderIsArchitect("Test Cmdr", null));
        Assert.False(ColonizationSiteVisibility.CommanderIsArchitect("Other Cmdr", "Test Cmdr"));
        Assert.False(ColonizationSiteVisibility.CommanderIsArchitect(string.Empty, "Test Cmdr"));
        Assert.True(ColonizationSiteVisibility.CanLoadSystem(null, "Test Cmdr"));
        Assert.True(ColonizationSiteVisibility.CanLoadSystem(" ", "Test Cmdr"));
        Assert.True(ColonizationSiteVisibility.CanLoadSystem("Test Cmdr", "test cmdr"));
        Assert.False(ColonizationSiteVisibility.CanLoadSystem("Other Cmdr", "Test Cmdr"));
        Assert.False(ColonizationSiteVisibility.CanLoadSystem("Other Cmdr", null));
    }

    [Fact]
    public void NonArchitectsSeeOnlyOrbitalPlannedSites()
    {
        var complete = new ColonizationSystemSite
        {
            Id = "complete",
            Status = ColonizationSystemSiteStatus.Complete,
            BuildType = "hestia",
        };
        var orbitalPlan = new ColonizationSystemSite
        {
            Id = "orbital",
            Status = ColonizationSystemSiteStatus.Plan,
            BuildType = "vesta",
        };
        var surfacePlan = new ColonizationSystemSite
        {
            Id = "surface",
            Status = ColonizationSystemSiteStatus.Plan,
            BuildType = "hestia",
        };

        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(complete, false, catalog));
        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(orbitalPlan, false, catalog));
        Assert.False(ColonizationSiteVisibility.CanViewerSeeSite(surfacePlan, false, catalog));
        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(surfacePlan, true, catalog));
    }

    [Fact]
    public void NonArchitectsSeeOnlyCatalogResolvableOrbitalPlans()
    {
        var vestaPrimary = new ColonizationSystemSite
        {
            Id = "vesta-primary",
            Status = ColonizationSystemSiteStatus.Plan,
            BuildType = "Vesta (primary)",
        };
        var journalGuess = new ColonizationSystemSite
        {
            Id = "outpost-guess",
            Status = ColonizationSystemSiteStatus.Plan,
            BuildType = "outpost?",
        };
        var inProgressSurface = new ColonizationSystemSite
        {
            Id = "surface-build",
            Status = ColonizationSystemSiteStatus.Build,
            BuildType = "hestia",
        };

        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(vestaPrimary, false, catalog));
        Assert.False(ColonizationSiteVisibility.CanViewerSeeSite(journalGuess, false, catalog));
        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(journalGuess, true, catalog));
        Assert.True(ColonizationSiteVisibility.CanViewerSeeSite(inProgressSurface, false, catalog));
    }
}
