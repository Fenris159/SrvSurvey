using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Optional planner, clock and carrier data dependencies for a route workspace.</summary>
public sealed record RouteWorkspaceOptions(
    FollowRouteKind Kind = FollowRouteKind.Standard,
    Func<DateTimeOffset>? UtcNow = null,
    ISpanshPlannerClient? PlannerClient = null,
    CurrentShipExportViewModel? CurrentShip = null,
    Func<FrontierAccountSnapshot?>? CarrierSnapshotSource = null,
    Func<Task>? RefreshCarrierSnapshot = null
);
