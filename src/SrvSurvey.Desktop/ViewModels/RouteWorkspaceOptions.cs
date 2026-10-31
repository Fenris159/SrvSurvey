using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Optional planner, clock and carrier data dependencies for a route workspace.</summary>
public sealed record RouteWorkspaceOptions(
    FollowRouteKind Kind = FollowRouteKind.Standard,
    Func<DateTimeOffset>? UtcNow = null,
    ISpanshPlannerClient? PlannerClient = null,
    CurrentShipExportViewModel? CurrentShip = null,
    Func<FrontierAccountSnapshot?>? CarrierSnapshotSource = null,
    Func<Task>? RefreshCarrierSnapshot = null,
    CarrierNextJumpDependencies? CarrierNextJump = null,
    FollowRouteCarrierKind? DedicatedCarrier = null
);

public sealed record CarrierNextJumpDependencies(
    ISystemSummaryClient SummaryClient,
    CarrierNextJumpSettingsStore SettingsStore,
    IStarSystemResolver? Resolver = null
);
