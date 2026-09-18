namespace SrvSurvey.Core.Colonization;

public static class ColonizationSiteVisibility
{
    public static bool CommanderIsArchitect(string? systemArchitect, string? commanderName)
    {
        return !string.IsNullOrWhiteSpace(systemArchitect)
            && !string.IsNullOrWhiteSpace(commanderName)
            && string.Equals(systemArchitect.Trim(), commanderName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanLoadSystem(string? systemArchitect, string? commanderName)
    {
        return string.IsNullOrWhiteSpace(systemArchitect) || CommanderIsArchitect(systemArchitect, commanderName);
    }

    public static bool CanViewerSeeSite(ColonizationSystemSite site, bool isArchitect, ColonizationBuildCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(catalog);
        return site.Status != ColonizationSystemSiteStatus.Plan
            || isArchitect
            || (
                catalog.IsOrbitalSiteBuildType(site.BuildType) && catalog.TryResolveSiteBuildType(site.BuildType, out _)
            );
    }
}
