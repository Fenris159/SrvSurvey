using System.Text.Json.Serialization;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public static class ColonizationCommodityPlanner
{
    private static readonly IReadOnlyDictionary<string, string> Categories =
        CreateCategories();

    private static readonly IReadOnlyDictionary<string, string> DisplayNames =
        CreateDisplayNames();

    public static ColonizationCommodityPlan Create(
        IEnumerable<ColonizationProject> projects,
        IEnumerable<string>? hiddenBuildIds,
        string? primaryBuildId,
        string? commanderName,
        IReadOnlyList<ColonizationFleetCarrier>? fleetCarriers,
        CargoSnapshot? shipCargo,
        ColonizationConstructionSnapshot construction,
        MarketSnapshot? market = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(construction);
        var allProjects = projects.Where(project => !project.IsComplete)
            .ToArray();
        var hidden = hiddenBuildIds?.ToHashSet(
            StringComparer.OrdinalIgnoreCase) ?? [];
        var carriers = fleetCarriers ?? [];
        var dock = construction.CurrentDock;
        var depot = construction.CurrentDepot;
        var atConstructionSite = dock is { IsConstructionSite: true };
        var hasCurrentDepot = atConstructionSite
            && depot is not null
            && dock!.MarketId == depot.MarketId;
        var localProject = atConstructionSite
            ? allProjects.FirstOrDefault(project =>
                project.SystemAddress == dock!.SystemAddress
                && project.MarketId == dock.MarketId)
            : null;
        var relevantProjects = SelectProjects(
            allProjects,
            hidden,
            primaryBuildId,
            atConstructionSite,
            localProject);
        var relevantCarriers = SelectCarriers(
            relevantProjects,
            carriers,
            atConstructionSite && localProject is null);
        var requirements = hasCurrentDepot
            ? CreateDepotRequirements(depot!)
            : CreateProjectRequirements(relevantProjects);
        var cargoGroups = shipCargo?.Inventory
            .GroupBy(item => Normalize(item.Name))
            .ToArray() ?? [];
        var cargoNames = cargoGroups.ToDictionary(
            group => group.Key,
            group => group.Select(item => item.LocalizedName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        var shipCounts = cargoGroups.ToDictionary(
            group => group.Key,
            group => group.Sum(item => Math.Max(0, item.Count)),
            StringComparer.OrdinalIgnoreCase);
        var carrierCounts = SumCarrierCargo(relevantCarriers);
        var localMarket = GetLocalMarketContext(market, dock);
        var dockedAtLinkedCarrier = dock is not null
            && relevantCarriers.Any(carrier =>
                carrier.MarketId == dock.MarketId);
        var rows = requirements
            .Where(requirement => requirement.Value.Remaining > 0)
            .Select(requirement => CreateRow(
                requirement.Key,
                requirement.Value,
                relevantProjects,
                commanderName,
                cargoNames,
                shipCounts,
                carrierCounts,
                localMarket,
                capacity: Math.Max(0, construction.ShipCargoCapacity),
                dockedAtLinkedCarrier))
            .OrderBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalRemaining = rows.Sum(row => (long)row.Needed);
        var carrierCovered = rows.Sum(row =>
            Math.Min((long)row.Needed, row.OnFleetCarriers));
        var carrierDeficit = Math.Max(0, totalRemaining - carrierCovered);
        var capacity = Math.Max(0, construction.ShipCargoCapacity);
        return new ColonizationCommodityPlan(
            CreateTitle(
                relevantProjects,
                dock,
                localProject,
                atConstructionSite),
            relevantProjects.Select(project =>
                    $"{project.BuildName} ({project.BuildType})")
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            rows,
            relevantCarriers,
            totalRemaining,
            capacity > 0
                ? (long?)Math.Ceiling(totalRemaining / (double)capacity)
                : null,
            carrierDeficit,
            capacity > 0
                ? (long?)Math.Ceiling(carrierDeficit / (double)capacity)
                : null,
            atConstructionSite,
            atConstructionSite && localProject is null,
            hasCurrentDepot && depot?.IsComplete == true,
            hasCurrentDepot && depot?.IsFailed == true);
    }

    private static ColonizationProject[] SelectProjects(
        IReadOnlyList<ColonizationProject> projects,
        IReadOnlySet<string> hidden,
        string? primaryBuildId,
        bool atConstructionSite,
        ColonizationProject? localProject)
    {
        if (atConstructionSite)
        {
            return localProject is null ? [] : [localProject];
        }

        var primary = projects.FirstOrDefault(project => string.Equals(
            project.BuildId,
            primaryBuildId,
            StringComparison.OrdinalIgnoreCase));
        return primary is not null
            ? [primary]
            : projects.Where(project => !hidden.Contains(project.BuildId))
                .ToArray();
    }

    private static ColonizationFleetCarrier[] SelectCarriers(
        IReadOnlyList<ColonizationProject> projects,
        IReadOnlyList<ColonizationFleetCarrier> carriers,
        bool includeAll)
    {
        if (includeAll)
        {
            return carriers.ToArray();
        }

        var marketIds = projects
            .SelectMany(project => project.LinkedFleetCarriers)
            .Select(carrier => carrier.MarketId)
            .ToHashSet();
        return carriers.Where(carrier => marketIds.Contains(carrier.MarketId))
            .ToArray();
    }

    private static Dictionary<string, Requirement> CreateDepotRequirements(
        ColonizationConstructionDepotSnapshot depot)
    {
        return depot.Resources
            .GroupBy(resource => Normalize(resource.Name))
            .ToDictionary(
                group => group.Key,
                group => new Requirement(
                    group.Sum(resource => resource.RemainingAmount),
                    group.Select(resource => resource.LocalizedName)
                        .FirstOrDefault(name =>
                            !string.IsNullOrWhiteSpace(name))),
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Requirement> CreateProjectRequirements(
        IReadOnlyList<ColonizationProject> projects)
    {
        return projects
            .SelectMany(project => project.Commodities)
            .GroupBy(pair => Normalize(pair.Key))
            .ToDictionary(
                group => group.Key,
                group => new Requirement(
                    group.Sum(pair => Math.Max(0, pair.Value)),
                    null),
                StringComparer.OrdinalIgnoreCase);
    }

    private static ColonizationCommodityPlanRow CreateRow(
        string commodity,
        Requirement requirement,
        IReadOnlyList<ColonizationProject> projects,
        string? commanderName,
        IReadOnlyDictionary<string, string?> cargoNames,
        IReadOnlyDictionary<string, int> shipCounts,
        IReadOnlyDictionary<string, int> carrierCounts,
        LocalMarketContext localMarket,
        int capacity,
        bool dockedAtLinkedCarrier)
    {
        var assigners = projects
            .SelectMany(project => project.Commanders)
            .Where(pair => pair.Value.Any(assigned => string.Equals(
                Normalize(assigned),
                commodity,
                StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var assignedToCommander = !string.IsNullOrWhiteSpace(commanderName)
            && assigners.Contains(
                commanderName,
                StringComparer.OrdinalIgnoreCase);
        var localizedName = requirement.LocalizedName
            ?? cargoNames.GetValueOrDefault(commodity)
            ?? localMarket.LocalizedNames.GetValueOrDefault(commodity);
        var inShip = Math.Max(0, shipCounts.GetValueOrDefault(commodity));
        var onCarriers = Math.Max(
            0,
            carrierCounts.GetValueOrDefault(commodity));
        var isAvailable = localMarket.AvailableCommodities.Contains(commodity);
        var carrierDeficit = Math.Max(0, requirement.Remaining - onCarriers);
        return new ColonizationCommodityPlanRow(
            commodity,
            string.IsNullOrWhiteSpace(localizedName)
                ? DisplayNames.GetValueOrDefault(
                    commodity,
                    commodity)
                : localizedName,
            Categories.GetValueOrDefault(commodity, "Other"),
            requirement.Remaining,
            inShip,
            onCarriers,
            assignedToCommander,
            !assignedToCommander && assigners.Length > 0,
            isAvailable,
            localMarket.HasAvailableCommodities && !isAvailable,
            isAvailable
                && !dockedAtLinkedCarrier
                && carrierDeficit > 0
                && capacity > carrierDeficit);
    }

    private static LocalMarketContext GetLocalMarketContext(
        MarketSnapshot? market,
        ColonizationDockingSnapshot? dock)
    {
        if (market is null
            || dock?.Timestamp is null
            || market.MarketId != dock.MarketId
            || market.Timestamp <= dock.Timestamp)
        {
            return LocalMarketContext.Empty;
        }

        var availableItems = market.Items
            .Where(item => item.Stock > 0)
            .ToArray();
        return new LocalMarketContext(
            availableItems.Length > 0,
            availableItems.Select(item => item.Commodity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            market.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.LocalizedName))
                .GroupBy(
                    item => item.Commodity,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().LocalizedName!,
                    StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, int> SumCarrierCargo(
        IReadOnlyList<ColonizationFleetCarrier> carriers)
    {
        var result = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var carrier in carriers)
        {
            foreach (var pair in carrier.Cargo)
            {
                var commodity = Normalize(pair.Key);
                result[commodity] = checked(
                    result.GetValueOrDefault(commodity)
                    + Math.Max(0, pair.Value));
            }
        }

        return result;
    }

    private static string CreateTitle(
        IReadOnlyList<ColonizationProject> projects,
        ColonizationDockingSnapshot? dock,
        ColonizationProject? localProject,
        bool atConstructionSite)
    {
        if (atConstructionSite)
        {
            return localProject is null
                ? dock?.DefaultProjectName ?? "Construction site"
                : $"{localProject.BuildName} ({localProject.BuildType})";
        }

        return projects.Count == 1
            ? $"{projects[0].BuildName} ({projects[0].BuildType})"
            : $"{projects.Count:N0} projects";
    }

    private static string Normalize(string value)
    {
        return ColonizationConstructionState.NormalizeCommodityName(value);
    }

    private static IReadOnlyDictionary<string, string> CreateCategories()
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        AddCategory(result, "Chemicals",
            "liquidoxygen", "pesticides", "surfacestabilisers", "water");
        AddCategory(result, "Consumer items",
            "evacuationshelter", "survivalequipment");
        AddCategory(result, "Foods",
            "animalmeat", "coffee", "fish", "foodcartridges",
            "fruitandvegetables", "grain", "tea");
        AddCategory(result, "Industrial materials",
            "ceramiccomposites", "cmmcomposite", "insulatingmembrane",
            "polymers", "semiconductors", "superconductors");
        AddCategory(result, "Legal drugs", "beer", "liquor", "wine");
        AddCategory(result, "Machinery",
            "buildingfabricators", "cropharvesters", "emergencypowercells",
            "geologicalequipment", "microbialfurnaces",
            "heliostaticfurnaces", "mineralextractors", "powergenerators",
            "thermalcoolingunits", "waterpurifiers");
        AddCategory(result, "Medicines",
            "agriculturalmedicines", "basicmedicines",
            "combatstabilisers", "combatstabilizers");
        AddCategory(result, "Metals",
            "aluminium", "copper", "steel", "titanium");
        AddCategory(result, "Technology",
            "advancedcatalysers", "autofabricators", "bioreducinglichen",
            "computercomponents", "hazardousenvironmentsuits",
            "landenrichmentsystems", "terrainenrichmentsystems",
            "medicaldiagnosticequipment", "microcontrollers", "muonimager",
            "mutomimager", "resonatingseparators", "robotics",
            "structuralregulators");
        AddCategory(result, "Textiles", "militarygradefabrics");
        AddCategory(result, "Waste", "biowaste");
        AddCategory(result, "Weapons",
            "battleweapons", "nonlethalweapons", "reactivearmour");
        return result;
    }

    private static void AddCategory(
        IDictionary<string, string> destination,
        string category,
        params string[] commodities)
    {
        foreach (var commodity in commodities)
        {
            destination[commodity] = category;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateDisplayNames()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["liquidoxygen"] = "Liquid oxygen",
            ["pesticides"] = "Pesticides",
            ["surfacestabilisers"] = "Surface stabilisers",
            ["water"] = "Water",
            ["evacuationshelter"] = "Evacuation shelter",
            ["survivalequipment"] = "Survival equipment",
            ["animalmeat"] = "Animal meat",
            ["coffee"] = "Coffee",
            ["fish"] = "Fish",
            ["foodcartridges"] = "Food cartridges",
            ["fruitandvegetables"] = "Fruit and vegetables",
            ["grain"] = "Grain",
            ["tea"] = "Tea",
            ["ceramiccomposites"] = "Ceramic composites",
            ["cmmcomposite"] = "CMM composite",
            ["insulatingmembrane"] = "Insulating membrane",
            ["polymers"] = "Polymers",
            ["semiconductors"] = "Semiconductors",
            ["superconductors"] = "Superconductors",
            ["beer"] = "Beer",
            ["liquor"] = "Liquor",
            ["wine"] = "Wine",
            ["buildingfabricators"] = "Building fabricators",
            ["cropharvesters"] = "Crop harvesters",
            ["emergencypowercells"] = "Emergency power cells",
            ["geologicalequipment"] = "Geological equipment",
            ["microbialfurnaces"] = "Microbial furnaces",
            ["heliostaticfurnaces"] = "Heliostatic furnaces",
            ["mineralextractors"] = "Mineral extractors",
            ["powergenerators"] = "Power generators",
            ["thermalcoolingunits"] = "Thermal cooling units",
            ["waterpurifiers"] = "Water purifiers",
            ["agriculturalmedicines"] = "Agri-medicines",
            ["basicmedicines"] = "Basic medicines",
            ["combatstabilisers"] = "Combat stabilisers",
            ["combatstabilizers"] = "Combat stabilisers",
            ["aluminium"] = "Aluminium",
            ["copper"] = "Copper",
            ["steel"] = "Steel",
            ["titanium"] = "Titanium",
            ["advancedcatalysers"] = "Advanced catalysers",
            ["autofabricators"] = "Auto-fabricators",
            ["bioreducinglichen"] = "Bioreducing lichen",
            ["computercomponents"] = "Computer components",
            ["hazardousenvironmentsuits"] = "H.E. suits",
            ["landenrichmentsystems"] = "Land enrichment systems",
            ["terrainenrichmentsystems"] = "Terrain enrichment systems",
            ["medicaldiagnosticequipment"] =
                "Medical diagnostic equipment",
            ["microcontrollers"] = "Micro controllers",
            ["muonimager"] = "Muon imager",
            ["mutomimager"] = "Mutom imager",
            ["resonatingseparators"] = "Resonating separators",
            ["robotics"] = "Robotics",
            ["structuralregulators"] = "Structural regulators",
            ["militarygradefabrics"] = "Military grade fabrics",
            ["biowaste"] = "Bio-waste",
            ["battleweapons"] = "Battle weapons",
            ["nonlethalweapons"] = "Non-lethal weapons",
            ["reactivearmour"] = "Reactive armour",
        };
    }

    private sealed record Requirement(int Remaining, string? LocalizedName);

    private sealed record LocalMarketContext(
        bool HasAvailableCommodities,
        IReadOnlySet<string> AvailableCommodities,
        IReadOnlyDictionary<string, string> LocalizedNames)
    {
        public static LocalMarketContext Empty { get; } = new(
            false,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}

public sealed record ColonizationCommodityPlan(
    string Title,
    IReadOnlyList<string> ProjectNames,
    IReadOnlyList<ColonizationCommodityPlanRow> Rows,
    IReadOnlyList<ColonizationFleetCarrier> FleetCarriers,
    long TotalRemaining,
    long? TripsInCurrentShip,
    long FleetCarrierDeficit,
    long? FleetCarrierDeficitTrips,
    bool IsAtConstructionSite,
    bool IsLocalProjectUntracked,
    bool IsConstructionComplete,
    bool IsConstructionFailed)
{
    public bool HasContent => Rows.Count > 0
        || IsAtConstructionSite
        || IsConstructionComplete
        || IsConstructionFailed;
}

public sealed record ColonizationCommodityPlanRow(
    string Commodity,
    string DisplayName,
    string Category,
    int Needed,
    int InShip,
    int OnFleetCarriers,
    bool IsAssignedToCommander,
    bool IsAssignedToOther,
    bool IsAvailableAtCurrentMarket = false,
    bool IsUnavailableAtCurrentMarket = false,
    bool CanCompleteFleetCarrierLoad = false)
{
    public bool ShipHasEnough => InShip >= Needed;

    public bool FleetCarriersHaveEnough => OnFleetCarriers >= Needed;

    public bool HasSurplusInShip => InShip > Needed;
}

public sealed record ColonizationFleetCarrier
{
    [JsonPropertyName("marketId")]
    public long MarketId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("cargo")]
    public Dictionary<string, int> Cargo { get; init; } = new(
        StringComparer.OrdinalIgnoreCase);
}
