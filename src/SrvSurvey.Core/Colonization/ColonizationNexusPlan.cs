namespace SrvSurvey.Core.Colonization;

public sealed record ColonizationNexusNeed(string Commodity, long Required, long Ready)
{
    public long Missing => Math.Max(0, Required - Ready);
}

public sealed record ColonizationNexusCargo(long Total, long Delivered, long Remaining, long Ready, bool IsKnown)
{
    public double DeliveredPercent => Total > 0 ? Math.Clamp(Delivered * 100d / Total, 0, 100) : 0;
    public double CoveredPercent => Total > 0 ? Math.Clamp((Delivered + (double)Ready) * 100 / Total, 0, 100) : 0;
    public bool IsComplete => IsKnown && Total > 0 && Delivered >= Total;
}

public sealed record ColonizationNexusSystemPlan(
    ColonizationNexusSystem System,
    ColonizationNexusCargo Cargo,
    IReadOnlyList<ColonizationNexusNeed> Needs,
    bool IsHub
);

/// <summary>Estimates each system independently against shared Nexus cargo and counts stock once in route totals.</summary>
public sealed record ColonizationNexusPlan(
    ColonizationNexus Nexus,
    IReadOnlyList<ColonizationNexusSystemPlan> Systems,
    ColonizationNexusCargo Cargo,
    IReadOnlyList<ColonizationNexusNeed> Needs,
    bool HasIncompleteRequirements
)
{
    public int CompletedSystems => Systems.Count(system => system.Cargo.IsComplete);

    public static ColonizationNexusPlan Create(ColonizationNexus nexus)
    {
        ArgumentNullException.ThrowIfNull(nexus);
        var systems = new List<ColonizationNexusSystemPlan>();
        foreach (ColonizationNexusSystem system in nexus.Systems)
        {
            ColonizationNexusNeed[] needs = CalculateReadiness(
                NormalizeNeeds(system.Needs).ToArray(),
                nexus.FleetCarriers
            );
            long remaining = needs.Sum(need => need.Required);
            long inferredTotal = system.Needs is null ? 0 : checked(remaining + Math.Max(0, system.Progress ?? 0));
            long total = Math.Max(0, system.Total ?? inferredTotal);
            long delivered = Math.Clamp(system.Progress ?? 0, 0, total);
            var cargo = new ColonizationNexusCargo(
                total,
                delivered,
                system.Needs is null ? total - delivered : remaining,
                needs.Sum(need => need.Ready),
                total > 0 && (system.Total.HasValue || system.Needs is not null)
            );
            systems.Add(new(system, cargo, needs, nexus.Hubs.Contains(system.SystemAddress)));
        }

        ColonizationShoppingNeed[] combined = systems
            .SelectMany(system => system.Needs)
            .GroupBy(need => need.Commodity, StringComparer.Ordinal)
            .Select(group => new ColonizationShoppingNeed(
                group.Key,
                ColonizationShopping.DisplayName(group.Key),
                group.Sum(need => need.Required)
            ))
            .OrderBy(need => need.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        ColonizationNexusNeed[] aggregate = CalculateReadiness(combined, nexus.FleetCarriers);
        var totals = new ColonizationNexusCargo(
            systems.Sum(system => system.Cargo.Total),
            systems.Sum(system => system.Cargo.Delivered),
            systems.Sum(system => system.Cargo.Remaining),
            aggregate.Sum(need => need.Ready),
            systems.Count > 0 && systems.All(system => system.Cargo.IsKnown)
        );
        return new(
            nexus,
            systems,
            totals,
            aggregate,
            systems.Any(system => !system.Cargo.IsKnown || system.Cargo.Remaining > 0 && system.System.Needs is null)
        );
    }

    private static IEnumerable<ColonizationShoppingNeed> NormalizeNeeds(Dictionary<string, long>? needs) =>
        (needs ?? [])
            .GroupBy(pair => ColonizationMarketCommodities.Key(pair.Key), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0)
            .Select(group => new ColonizationShoppingNeed(
                group.Key,
                ColonizationShopping.DisplayName(group.Key),
                Math.Max(0, group.Max(pair => pair.Value))
            ))
            .Where(need => need.Required > 0)
            .OrderBy(need => need.DisplayName, StringComparer.CurrentCultureIgnoreCase);

    private static ColonizationNexusNeed[] CalculateReadiness(
        IReadOnlyList<ColonizationShoppingNeed> needs,
        IReadOnlyList<ColonizationFleetCarrier> carriers
    )
    {
        var missing = ColonizationShopping
            .SubtractCarrierCargo(needs, carriers)
            .ToDictionary(need => need.Commodity, need => need.Required, StringComparer.Ordinal);
        return needs
            .Select(need => new ColonizationNexusNeed(
                need.Commodity,
                need.Required,
                need.Required - missing.GetValueOrDefault(need.Commodity)
            ))
            .ToArray();
    }
}
