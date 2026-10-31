using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private (RouteWorkspaceViewModel Workspace, RouteManagerViewModel Manager) CreateCarrierRouteWorkspace(
        FollowRouteStore store,
        FollowRouteCarrierKind carrier,
        RouteNameImporter importer,
        ISpanshRouteClient routeClient,
        ISpanshPlannerClient planner,
        ISystemSummaryClient summaries,
        IStarSystemResolver resolver
    )
    {
        var service = new FollowRouteService(store);
        var workspace = new RouteWorkspaceViewModel(
            service,
            importer,
            routeClient,
            new RouteWorkspaceOptions(
                FollowRouteKind.FleetCarrier,
                PlannerClient: planner,
                CurrentShip: CurrentShipExport,
                CarrierSnapshotSource: GetRouteCarrierSnapshot,
                RefreshCarrierSnapshot: RefreshRouteCarrierSnapshotAsync,
                CarrierNextJump: new(
                    summaries,
                    new CarrierNextJumpSettingsStore(
                        AppDataPaths.UiSettingsPath,
                        carrier == FollowRouteCarrierKind.Squadron
                    ),
                    resolver
                ),
                DedicatedCarrier: carrier
            )
        );
        return (workspace, new RouteManagerViewModel(service, workspace));
    }
}
