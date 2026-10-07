using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class RouteBioOverlayCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel route;
    private readonly RouteBioOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public RouteBioOverlayCoordinator(RouteWorkspaceViewModel route, OverlayPresentationSession presentationSession)
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotRouteBio",
                _ => CreateWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, margin: 8),
                ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
                Placement = PlaceWithinVisibleBounds,
            }
        );
        viewModel = new RouteBioOverlayViewModel(route, hostedWindow.Capabilities);
        route.PropertyChanged += OnRoutePropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

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

    private void OnRoutePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(RouteWorkspaceViewModel.ShouldShowRouteBioOverlay)
                or nameof(RouteWorkspaceViewModel.CurrentBioTargets)
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

        hostedWindow.Reconcile(!isSuppressed && route.ShouldShowRouteBioOverlay);
    }

    private RouteBioOverlayWindow CreateWindow() => new(viewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
    }

    private static PixelPoint PlaceWithinVisibleBounds(HostedOverlayPlacement placement)
    {
        Window target = placement.Window;
        Screen screen = placement.Screen;
        PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
            new OverlayScreenGeometry(screen.Bounds, screen.WorkingArea),
            target
                .Screens.All.Select(current => new OverlayScreenGeometry(current.Bounds, current.WorkingArea))
                .ToArray()
        );
        PixelRect visibleBounds = OverlayWindowPlacement.GetUsableBounds(placement.GameBounds, workingArea);
        target.MaxHeight = Math.Max(target.MinHeight, Math.Min(680, (visibleBounds.Height - 16) / screen.Scaling));
        PixelSize size = placement.PrepareSize();
        PixelPoint position = placement.GetPosition(size);
        return new PixelPoint(
            position.X,
            Math.Clamp(position.Y, visibleBounds.Y, Math.Max(visibleBounds.Y, visibleBounds.Bottom - size.Height - 8))
        );
    }
}
