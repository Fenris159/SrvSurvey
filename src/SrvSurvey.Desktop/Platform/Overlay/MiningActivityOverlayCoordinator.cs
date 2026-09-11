using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MiningActivityOverlayCoordinator : IDisposable
{
    private readonly MiningWorkspaceViewModel workspace;
    private readonly HostedOverlayWindow notifications,
        firegroups;
    private readonly MiningActivityOverlayViewModel notificationModel,
        firegroupModel;
    private readonly FiregroupsWorkspaceViewModel firegroupsWorkspace;
    private bool suppressed;

    public MiningActivityOverlayCoordinator(
        MiningWorkspaceViewModel workspace,
        FiregroupsWorkspaceViewModel firegroupsWorkspace,
        OverlayPresentationSession session
    )
    {
        this.workspace = workspace;
        this.firegroupsWorkspace = firegroupsWorkspace;
        notificationModel = new(workspace, false);
        firegroupModel = new(null, true, firegroupsWorkspace);
        notifications = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningNotifications",
                _ => new MiningActivityOverlayWindow(notificationModel),
                (bounds, size) => OverlayWindowPlacement.TopCenter(bounds, size)
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
                or nameof(FiregroupsWorkspaceViewModel.ShouldShow)
        )
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        notifications.Reconcile(!suppressed && workspace.ShouldShowNotifications);
        firegroups.Reconcile(!suppressed && firegroupsWorkspace.ShouldShow);
    }

    public void Dispose()
    {
        workspace.PropertyChanged -= OnChanged;
        firegroupsWorkspace.PropertyChanged -= OnChanged;
        notifications.Dispose();
        firegroups.Dispose();
        notificationModel.Dispose();
        firegroupModel.Dispose();
    }
}
