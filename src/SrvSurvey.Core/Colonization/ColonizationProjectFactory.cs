namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationProjectFactory
{
    private readonly ColonizationBuildCatalog buildCatalog;

    public ColonizationProjectFactory(ColonizationBuildCatalog buildCatalog)
    {
        this.buildCatalog = buildCatalog
            ?? throw new ArgumentNullException(nameof(buildCatalog));
    }

    public ColonizationProjectCreateResult Create(
        ColonizationProjectDraft draft,
        ColonizationDockingSnapshot? dock,
        ColonizationConstructionDepotSnapshot? depot)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = Validate(draft, dock, depot);
        if (errors.Count > 0 || dock is null || depot is null)
        {
            return new ColonizationProjectCreateResult(null, errors);
        }

        var remaining = depot.Resources
            .GroupBy(
                resource => resource.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(resource => resource.RemainingAmount),
                StringComparer.OrdinalIgnoreCase);
        return new ColonizationProjectCreateResult(
            new ColonizationProjectCreate
            {
                BuildType = draft.BuildType.Trim().ToLowerInvariant(),
                BuildName = draft.BuildName.Trim(),
                ArchitectName = NormalizeOptional(draft.ArchitectName),
                FactionName = NormalizeOptional(dock.FactionName),
                Notes = NormalizeOptional(draft.Notes),
                IsPrimaryPort = dock.IsPrimaryPortShip,
                MarketId = dock.MarketId,
                SystemAddress = dock.SystemAddress,
                SystemName = dock.SystemName,
                StarPosition = [.. draft.StarPosition],
                BodyNumber = draft.BodyNumber,
                BodyName = draft.BodyNumber is >= 0
                    ? NormalizeOptional(draft.BodyName)
                    : null,
                Commanders = new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [draft.CommanderName.Trim()] = [],
                },
                Commodities = remaining,
                MaximumRequired = checked((int)depot.TotalRequired),
                SystemSiteId = NormalizeOptional(draft.SystemSiteId),
                ConstructionDepot =
                    ColonizationConstructionDepotPayload.FromSnapshot(depot),
            },
            []);
    }

    private IReadOnlyList<string> Validate(
        ColonizationProjectDraft draft,
        ColonizationDockingSnapshot? dock,
        ColonizationConstructionDepotSnapshot? depot)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(draft.CommanderName))
        {
            errors.Add("An active commander is required.");
        }

        if (dock is null)
        {
            errors.Add("Dock at a colonisation construction site first.");
        }
        else if (!dock.IsConstructionSite)
        {
            errors.Add("The current station is not a colonisation construction site.");
        }

        if (depot is null)
        {
            errors.Add("Open Construction Services to load required commodities.");
        }
        else
        {
            if (dock is not null && depot.MarketId != dock.MarketId)
            {
                errors.Add("The construction requirements belong to a different market.");
            }

            if (depot.IsComplete)
            {
                errors.Add("The current construction project is already complete.");
            }

            if (depot.IsFailed)
            {
                errors.Add("The current construction project has failed.");
            }

            if (depot.Resources.Count == 0)
            {
                errors.Add("The construction depot reported no required commodities.");
            }

            if (depot.TotalRequired > int.MaxValue)
            {
                errors.Add("The construction requirement exceeds the supported size.");
            }
        }

        if (string.IsNullOrWhiteSpace(draft.BuildName))
        {
            errors.Add("Enter a project name.");
        }

        if (string.IsNullOrWhiteSpace(draft.BuildType)
            || (buildCatalog.FindByLayout(draft.BuildType).Count == 0
                && buildCatalog.FindByBuildType(draft.BuildType) is null))
        {
            errors.Add("Select a known colonisation build layout.");
        }

        if (string.IsNullOrWhiteSpace(draft.SystemName)
            || dock is not null
                && !string.Equals(
                    draft.SystemName.Trim(),
                    dock.SystemName,
                    StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The project system does not match the current dock.");
        }

        if (draft.StarPosition.Count != 3
            || draft.StarPosition.Any(coordinate => !double.IsFinite(coordinate)))
        {
            errors.Add("A finite three-axis galactic position is required.");
        }

        if (draft.BodyNumber is < -1)
        {
            errors.Add("The body number is not valid.");
        }

        return errors;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record ColonizationProjectDraft(
    string CommanderName,
    string SystemName,
    IReadOnlyList<double> StarPosition,
    string BuildType,
    string BuildName,
    string? ArchitectName,
    string? Notes,
    int? BodyNumber,
    string? BodyName,
    string? SystemSiteId);

public sealed record ColonizationProjectCreateResult(
    ColonizationProjectCreate? Project,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Project is not null && Errors.Count == 0;
}
