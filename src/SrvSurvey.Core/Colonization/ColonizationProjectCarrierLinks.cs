namespace SrvSurvey.Core.Colonization;

/// <summary>Keeps carrier associations on real builds without changing other carriers or their assignments.</summary>
public static class ColonizationProjectCarrierLinks
{
    public static bool CanLink(ColonizationProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return !project.IsPlanned
            && !project.IsComplete
            && !string.IsNullOrWhiteSpace(project.BuildId)
            && !project.BuildId.StartsWith(
                ColonizationPlannedProjects.BuildIdPrefix,
                StringComparison.OrdinalIgnoreCase
            );
    }

    public static ColonizationProject Apply(ColonizationProject project, ColonizationFleetCarrier carrier, bool link)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(carrier);
        var carriers = project.LinkedFleetCarriers.Where(item => item.MarketId != carrier.MarketId).ToList();
        if (link)
        {
            carriers.Add(
                project.LinkedFleetCarriers.FirstOrDefault(item => item.MarketId == carrier.MarketId)
                    ?? new ColonizationProjectFleetCarrier
                    {
                        MarketId = carrier.MarketId,
                        Name = carrier.Name,
                        DisplayName = carrier.DisplayName,
                    }
            );
        }
        return project with { LinkedFleetCarriers = carriers };
    }
}
