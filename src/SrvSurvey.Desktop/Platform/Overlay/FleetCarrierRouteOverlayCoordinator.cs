using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class FleetCarrierRouteOverlayCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel route;
    private readonly FleetCarrierRouteOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool wantsWindow;
    private bool disposed;

    public FleetCarrierRouteOverlayCoordinator(
        RouteWorkspaceViewModel route,
        OverlayPresentationSession presentationSession
    )
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotFleetCarrierRoute",
                _ => CreateWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, margin: 8)
            )
            {
                Tick = OnTick,
            }
        );
        viewModel = new FleetCarrierRouteOverlayViewModel(route, hostedWindow.Capabilities);
        route.PropertyChanged += OnRoutePropertyChanged;
        SynchronizeIntent();
    }

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeIntent();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        route.PropertyChanged -= OnRoutePropertyChanged;
        hostedWindow.Dispose();
        viewModel.Dispose();
    }

    private FleetCarrierRouteOverlayWindow CreateWindow() => new(viewModel);

    private void OnTick()
    {
        if (wantsWindow)
        {
            viewModel.AdvanceTimedTransitions();
        }

        SynchronizeIntent();
    }

    private void OnRoutePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(RouteWorkspaceViewModel.ShouldShowFleetCarrierRouteOverlay)
                or nameof(RouteWorkspaceViewModel.NextHop)
        )
        {
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        wantsWindow = !isSuppressed && viewModel.ShouldShow;
        hostedWindow.Reconcile(wantsWindow);
    }
}
