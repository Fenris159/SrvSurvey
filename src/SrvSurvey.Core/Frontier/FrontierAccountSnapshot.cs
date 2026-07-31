namespace SrvSurvey.Core.Frontier;

public sealed record FrontierAccountSnapshot(
    string CommanderName,
    long Credits,
    long Debt,
    bool IsDocked,
    bool IsAlive,
    string LastSystem,
    string LastStation,
    FrontierShipSnapshot? CurrentShip,
    IReadOnlyList<FrontierRankSnapshot> Ranks,
    IReadOnlyList<FrontierShipSnapshot> Ships,
    IReadOnlyList<string> Capabilities,
    FrontierCarrierSnapshot? Carrier,
    DateTimeOffset FetchedAt,
    long? CommanderId = null,
    FrontierLocationSnapshot? LastSystemDetails = null,
    FrontierLocationSnapshot? LastStationDetails = null,
    FrontierMarketSnapshot? Market = null,
    FrontierShipyardSnapshot? Shipyard = null,
    IReadOnlyList<FrontierCommunityGoalSnapshot>? CommunityGoals = null,
    IReadOnlyList<FrontierDataPointSnapshot>? ProfileData = null,
    IReadOnlyList<FrontierDataPointSnapshot>? CommunityGoalsData = null,
    DateTimeOffset? CarrierFetchedAt = null,
    DateTimeOffset? MarketFetchedAt = null,
    DateTimeOffset? ShipyardFetchedAt = null,
    DateTimeOffset? CommunityGoalsFetchedAt = null,
    string CarrierError = "",
    string MarketError = "",
    string ShipyardError = "",
    string CommunityGoalsError = "",
    IReadOnlyList<FrontierReputationSnapshot>? CommanderReputation = null,
    DateTimeOffset? CommanderReputationFetchedAt = null,
    IReadOnlyList<FrontierDataPointSnapshot>? CarrierEndpointData = null)
{
    public long FleetValue => Ships.Sum(ship => ship.Value);

    public long NetWorth => Credits - Debt + FleetValue
        + (Carrier?.BankBalance ?? 0);
}

public sealed record FrontierRankSnapshot(
    string Key,
    string Category,
    int Level,
    string Name);

public sealed record FrontierShipSnapshot(
    long? Id,
    string Type,
    string Name,
    string Identifier,
    string System,
    string Station,
    long Value,
    bool IsCurrent,
    double? HullHealth,
    double? ShieldHealth,
    long HullValue = 0,
    long ModulesValue = 0,
    long CargoValue = 0,
    long UnloanedValue = 0,
    bool IsFree = false,
    bool IsAlive = true,
    bool ShieldUp = false,
    double? Integrity = null,
    double? Paintwork = null,
    bool CockpitBreached = false,
    double? OxygenRemaining = null,
    long? SystemId = null,
    long? SystemAddress = null,
    long? StationId = null,
    IReadOnlyList<FrontierShipModuleSnapshot>? Modules = null,
    IReadOnlyList<FrontierLaunchBaySnapshot>? LaunchBays = null,
    IReadOnlyList<FrontierDataPointSnapshot>? DataPoints = null);

public sealed record FrontierCarrierSnapshot(
    string Callsign,
    string Name,
    string System,
    string State,
    string DockingAccess,
    long BankBalance,
    long ReservedBalance,
    long WeeklyMaintenance,
    long MarketCargoValue,
    long MarketProfit,
    long PurchaseOrderAllocation,
    int Tritium,
    int CapacityUsed,
    int CapacityFree,
    IReadOnlyList<FrontierCapacitySnapshot> Capacity,
    IReadOnlyList<FrontierInventorySnapshot> Cargo,
    IReadOnlyList<FrontierInventorySnapshot> Locker,
    IReadOnlyList<FrontierMarketOrderSnapshot> SellOrders,
    IReadOnlyList<FrontierMarketOrderSnapshot> BuyOrders,
    IReadOnlyList<string> Services,
    string Theme = "",
    bool NotoriousAccess = false,
    long Taxation = 0,
    long DebtThreshold = 0,
    long MaintenanceToDate = 0,
    long CoreCost = 0,
    long ServicesCost = 0,
    long ServicesCostToDate = 0,
    long JumpsCost = 0,
    int WeeklyJumps = 0,
    double TotalDistanceJumped = 0,
    string CurrentJump = "",
    FrontierCarrierFinanceSnapshot? BlackMarketFinances = null,
    FrontierCarrierFinanceSnapshot? BartenderFinances = null,
    IReadOnlyList<FrontierNamedValueSnapshot>? ServiceTaxation = null,
    IReadOnlyList<FrontierCarrierCrewSnapshot>? ServiceCrew = null,
    IReadOnlyList<FrontierCarrierJumpSnapshot>? Itinerary = null,
    IReadOnlyList<FrontierReputationSnapshot>? Reputation = null,
    FrontierMarketSnapshot? Market = null,
    FrontierShipyardSnapshot? Shipyard = null,
    IReadOnlyList<FrontierDataPointSnapshot>? DataPoints = null);

public sealed record FrontierCarrierEndpointSnapshot(
    FrontierCarrierSnapshot? Carrier,
    IReadOnlyList<FrontierReputationSnapshot> CommanderReputation,
    IReadOnlyList<FrontierDataPointSnapshot> DataPoints);

public sealed record FrontierCapacitySnapshot(
    string Category,
    int Used);

public sealed record FrontierInventorySnapshot(
    string Category,
    string Name,
    int Quantity,
    long Value);

public sealed record FrontierMarketOrderSnapshot(
    string Category,
    string Name,
    int Quantity,
    int? Remaining,
    long Price,
    bool IsBlackMarket);

public sealed record FrontierLocationSnapshot(
    long? Id,
    long? SystemAddress,
    string Name,
    string Allegiance,
    string MinorFaction,
    IReadOnlyList<string> Services);

public sealed record FrontierShipModuleSnapshot(
    string Slot,
    long? Id,
    string Name,
    string Description,
    long Value,
    bool IsFree,
    double? Health,
    bool IsPowered,
    int? Priority,
    string Engineer,
    string Blueprint,
    int? BlueprintLevel,
    IReadOnlyList<string> ExperimentalEffects,
    string InternalName = "");

public sealed record FrontierLaunchBaySnapshot(
    string Slot,
    string Vehicle,
    string Loadout,
    int Rebuilds);

public sealed record FrontierNamedValueSnapshot(
    string Name,
    string Value);

public sealed record FrontierCarrierFinanceSnapshot(
    long CargoValue,
    long AllTimeProfit,
    int ItemsForSale,
    int PurchaseOrders,
    long PurchaseOrderAllocation);

public sealed record FrontierCarrierCrewSnapshot(
    string Service,
    string Name,
    string Gender,
    bool Enabled,
    string Faction,
    long Salary,
    string Status,
    DateTimeOffset? LastChanged);

public sealed record FrontierCarrierJumpSnapshot(
    string System,
    string State,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? DepartedAt,
    long VisitDurationSeconds);

public sealed record FrontierReputationSnapshot(
    string Faction,
    double Score);

public sealed record FrontierMarketSnapshot(
    long? Id,
    string Name,
    string OutpostType,
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Exported,
    IReadOnlyList<string> Prohibited,
    IReadOnlyList<FrontierNamedValueSnapshot> Services,
    IReadOnlyList<FrontierEconomySnapshot> Economies,
    IReadOnlyList<FrontierCommoditySnapshot> Commodities,
    DateTimeOffset FetchedAt,
    IReadOnlyList<FrontierDataPointSnapshot>? DataPoints = null);

public sealed record FrontierEconomySnapshot(
    long? Id,
    string Name,
    double Proportion);

public sealed record FrontierCommoditySnapshot(
    long? Id,
    string Category,
    string Name,
    string Legality,
    long BuyPrice,
    long SellPrice,
    long MeanPrice,
    int DemandBracket,
    int StockBracket,
    long Stock,
    long Demand,
    IReadOnlyList<string> StatusFlags);

public sealed record FrontierShipyardSnapshot(
    long? Id,
    string Name,
    string OutpostType,
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Exported,
    IReadOnlyList<string> Prohibited,
    IReadOnlyList<FrontierNamedValueSnapshot> Services,
    IReadOnlyList<FrontierEconomySnapshot> Economies,
    IReadOnlyList<FrontierOutfittingModuleSnapshot> Modules,
    IReadOnlyList<FrontierShipForSaleSnapshot> Ships,
    DateTimeOffset FetchedAt,
    IReadOnlyList<FrontierDataPointSnapshot>? DataPoints = null);

public sealed record FrontierOutfittingModuleSnapshot(
    long? Id,
    string Category,
    string Name,
    long Cost,
    string Sku,
    int Stock);

public sealed record FrontierShipForSaleSnapshot(
    long? Id,
    string Name,
    long BaseValue,
    string Sku,
    int Stock);

public sealed record FrontierCommunityGoalSnapshot(
    long? Id,
    string Title,
    string Description,
    string Objective,
    string Reward,
    string System,
    string Market,
    DateTimeOffset? ExpiresAt,
    bool IsComplete,
    long CurrentTotal,
    long? TargetTotal,
    long PlayerContribution,
    int Contributors,
    string TierReached,
    int? PlayerPercentile,
    long Bonus,
    int? TopRankSize,
    bool PlayerInTopRank,
    IReadOnlyList<FrontierDataPointSnapshot>? DataPoints = null,
    string ActivityType = "",
    bool HasPlayerContributionData = false,
    bool HasContributorData = false);

public sealed record FrontierDataPointSnapshot(
    string Path,
    string Value);
