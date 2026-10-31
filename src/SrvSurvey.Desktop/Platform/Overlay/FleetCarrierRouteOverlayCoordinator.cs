using System.ComponentModel;
using Avalonia;
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
        string plotterName = route.IsSquadronCarrierWorkspace ? "PlotSquadronCarrierRoute" : "PlotFleetCarrierRoute";
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(plotterName, _ => CreateWindow(), PlaceWindow) { Tick = OnTick }
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

    private PixelPoint PlaceWindow(PixelRect bounds, PixelSize size)
    {
        PixelPoint position = OverlayWindowPlacement.TopRight(bounds, size, margin: 8);
        // Reserve the personal panel's 260-pixel width and a 16-pixel gap.
        return route.IsSquadronCarrierWorkspace
            ? new PixelPoint(Math.Max(bounds.X + 8, position.X - 276), position.Y)
            : position;
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
                or nameof(RouteWorkspaceViewModel.ShouldShowCarrierNextJumpOverlay)
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
