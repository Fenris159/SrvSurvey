namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationProjectPreview
{
    private readonly bool? nexusRequirementsKnown;

    /// <summary>Uses Raven's system totals, which include planned facilities without individual build IDs.</summary>
    public static ColonizationProjectPreview CreateNexusReport(
        ColonizationNexus nexus,
        string? systemName,
        DateTimeOffset fetchedAt
    )
    {
        ArgumentNullException.ThrowIfNull(nexus);
        var plan = ColonizationNexusPlan.Create(nexus);
        ColonizationNexusSystemPlan[] systems = plan
            .Systems.Where(system =>
                systemName is null || string.Equals(system.System.Name, systemName, StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();
        return new ColonizationProjectPreview(nexus, systems, fetchedAt);
    }

    private ColonizationProjectPreview(
        ColonizationNexus nexus,
        IReadOnlyList<ColonizationNexusSystemPlan> systems,
        DateTimeOffset fetchedAt
    )
    {
        IsCombined = true;
        nexusRequirementsKnown = systems.Count > 0 && systems.All(system => system.Cargo.IsKnown);
        FetchedAt = fetchedAt;
        Projects = systems.SelectMany(system => system.System.Builds).DistinctBy(build => build.BuildId).ToArray();
        Project = Projects.Count > 0 ? Projects[0] : new ColonizationProject { BuildName = nexus.Name };
        MaximumRequired = systems.Sum(system => system.Cargo.Total);
        Delivered = systems.Sum(system => system.Cargo.Delivered);
        Carriers = (systems.Count > 0 ? nexus.FleetCarriers : [])
            .DistinctBy(carrier => carrier.MarketId)
            .Select(carrier => new ColonizationPreviewCarrier(
                carrier.MarketId,
                carrier.Name,
                carrier.DisplayName,
                Normalize(carrier.Cargo)
            ))
            .ToArray();
        var needs = systems
            .SelectMany(system => system.Needs)
            .GroupBy(need => need.Commodity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(need => need.Required),
                StringComparer.OrdinalIgnoreCase
            );
        Rows = CreateRows(needs, Carriers);
        Remaining = Rows.Sum(row => row.Need);
        CarrierDeficit = CalculateDeficit(Rows, Carriers);
    }
}
