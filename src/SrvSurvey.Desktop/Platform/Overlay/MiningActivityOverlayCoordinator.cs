using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MiningActivityOverlayCoordinator : IDisposable
{
    private readonly MiningWorkspaceViewModel workspace;
    private readonly HostedOverlayWindow notifications,
        cargo,
        firegroups,
        sessionOverview;
    private readonly MiningActivityOverlayViewModel notificationModel,
        firegroupModel;
    private readonly MiningCargoOverlayViewModel cargoModel;
    private readonly FiregroupsWorkspaceViewModel firegroupsWorkspace;
    private bool suppressed;
    private readonly MiningSessionOverlayViewModel sessionModel;

    public MiningActivityOverlayCoordinator(
        MiningWorkspaceViewModel workspace,
        FiregroupsWorkspaceViewModel firegroupsWorkspace,
        OverlayPresentationSession session
    )
    {
        this.workspace = workspace;
        this.firegroupsWorkspace = firegroupsWorkspace;
        sessionModel = new(workspace);
        sessionOverview = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningSession",
                _ => new MiningSessionOverlayWindow(sessionModel),
                (bounds, size) => OverlayWindowPlacement.TopRight(bounds, size)
            )
        );
        notificationModel = new(workspace, false);
        cargoModel = new(workspace);
        firegroupModel = new(null, true, firegroupsWorkspace);
        notifications = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningNotifications",
                _ => new MiningActivityOverlayWindow(notificationModel),
                (bounds, size) => OverlayWindowPlacement.TopCenter(bounds, size)
            )
        );
        cargo = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningCargo",
                _ => new MiningCargoOverlayWindow(cargoModel),
                (bounds, size) => OverlayWindowPlacement.TopRight(bounds, size)
            )
        );
        firegroups = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningFiregroups",
                _ => new MiningActivityOverlayWindow(firegroupModel),
                (bounds, size) => OverlayWindowPlacement.BottomRight(bounds, size)
            )
        );
        workspace.PropertyChanged += OnChanged;
        firegroupsWorkspace.PropertyChanged += OnChanged;
        Synchronize();
    }

    public void SetSuppressed(bool value)
    {
        suppressed = value;
        Synchronize();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(MiningWorkspaceViewModel.ShouldShowNotifications)
                or nameof(MiningWorkspaceViewModel.ShouldShowCargo)
                or nameof(MiningWorkspaceViewModel.ShouldShowSession)
                or nameof(FiregroupsWorkspaceViewModel.ShouldShow)
        )
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        notifications.Reconcile(!suppressed && workspace.ShouldShowNotifications);
        sessionOverview.Reconcile(!suppressed && workspace.ShouldShowSession);
        cargo.Reconcile(!suppressed && workspace.ShouldShowCargo);
        firegroups.Reconcile(!suppressed && firegroupsWorkspace.ShouldShow);
    }

    public void Dispose()
    {
        workspace.PropertyChanged -= OnChanged;
        firegroupsWorkspace.PropertyChanged -= OnChanged;
        notifications.Dispose();
        sessionOverview.Dispose();
        sessionModel.Dispose();
        cargo.Dispose();
        firegroups.Dispose();
        notificationModel.Dispose();
        cargoModel.Dispose();
        firegroupModel.Dispose();
    }
}
