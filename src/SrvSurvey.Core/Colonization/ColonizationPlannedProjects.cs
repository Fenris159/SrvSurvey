using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>Projects visible planned sites into catalog-based cargo estimates without creating live Raven builds.</summary>
public static class ColonizationPlannedProjects
{
    public const string BuildIdPrefix = "plan:";

    public static IReadOnlyList<ColonizationProject> Create(
        ColonizationSystemRecord system,
        ColonizationBuildCatalog catalog,
        IReadOnlyList<ColonizationProject> activeProjects,
        string? commander,
        IReadOnlyList<double> starPosition
    )
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(activeProjects);
        ArgumentNullException.ThrowIfNull(starPosition);
        bool isArchitect = ColonizationSiteVisibility.CommanderIsArchitect(system.Architect, commander);
        var projects = new List<ColonizationProject>();
        foreach (ColonizationSystemSite site in system.Sites)
        {
            if (
                !site.HasExplicitStatus
                || site.Status != ColonizationSystemSiteStatus.Plan
                || !ColonizationSiteVisibility.CanViewerSeeSite(site, isArchitect, catalog)
                || !catalog.TryResolveSiteBuildType(site.BuildType, out ColonizationBuildCost? build)
                || build is null
                || activeProjects.Any(project => RepresentsSite(project, site, system))
            )
            {
                continue;
            }
            int total = checked((int)build.TotalCargo);
            projects.Add(
                new ColonizationProject
                {
                    BuildId = BuildIdPrefix + system.SystemAddress + ":" + SiteIdentity(site),
                    BuildName = site.Name + " (Plan)",
                    BuildType = ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(site.BuildType),
                    SystemAddress = system.SystemAddress,
                    SystemName = system.Name,
                    StarPosition = starPosition.ToArray(),
                    BodyNumber = site.BodyNumber,
                    BodyName = site.BodyName,
                    ArchitectName = system.Architect,
                    MaximumRequired = total,
                    RemainingRequired = total,
                    Commodities = build.CommodityCosts.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase
                    ),
                    IsPlanned = true,
                }
            );
        }
        return projects;
    }

    private static string SiteIdentity(ColonizationSystemSite site) =>
        string.IsNullOrWhiteSpace(site.Id)
            ? "name:"
                + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(site.Name.Trim().ToUpperInvariant())))
            : site.Id;

    private static bool RepresentsSite(
        ColonizationProject project,
        ColonizationSystemSite site,
        ColonizationSystemRecord system
    ) =>
        (
            !string.IsNullOrWhiteSpace(site.BuildId)
            && string.Equals(project.BuildId, site.BuildId, StringComparison.OrdinalIgnoreCase)
        )
        || (site.MarketId is > 0 && project.MarketId == site.MarketId)
        || (
            string.Equals(project.SystemName, system.Name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(project.BuildName, site.Name, StringComparison.OrdinalIgnoreCase)
        );
}
