using System.ComponentModel;
using Avalonia;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class FleetCarrierRouteOverlayCoordinator : IDisposable
{
    private const string PersonalPlotterName = "PlotFleetCarrierRoute";
    private const string SquadronPlotterName = "PlotSquadronCarrierRoute";
    private readonly RouteWorkspaceViewModel route;
    private readonly FleetCarrierRouteOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly OverlayWindowRegistry windowRegistry;
    private readonly LegacyOverlayLayout layout;
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
        windowRegistry = presentationSession.WindowRegistry;
        layout = presentationSession.OverlayLayout;
        string plotterName = route.IsSquadronCarrierWorkspace ? SquadronPlotterName : PersonalPlotterName;
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(plotterName, _ => CreateWindow(), PlaceFallback)
            {
                Tick = OnTick,
                Placement = PlaceWindow,
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

    private PixelPoint PlaceWindow(HostedOverlayPlacement placement)
    {
        PixelSize size = placement.PrepareSize();
        return route.IsSquadronCarrierWorkspace && UsesDefaultSquadronPosition()
            ? PlaceSquadron(placement.GameBounds, size, placement.Screen.Scaling)
            : placement.GetPosition(size);
    }

    private bool UsesDefaultSquadronPosition()
    {
        if (!layout.Placements.TryGetValue(SquadronPlotterName, out LegacyOverlayPlacement? saved))
        {
            return true;
        }
        LegacyOverlayPlacement defaults = OverlayLayoutCatalog.GetRequired(SquadronPlotterName).DefaultPlacement;
        return saved.PositionReference is null
            && (saved.Horizontal, saved.HorizontalOffset, saved.Vertical, saved.VerticalOffset)
                == (defaults.Horizontal, defaults.HorizontalOffset, defaults.Vertical, defaults.VerticalOffset);
    }

    private PixelPoint PlaceFallback(PixelRect bounds, PixelSize size) =>
        route.IsSquadronCarrierWorkspace
            ? PlaceSquadron(bounds, size, 1)
            : OverlayWindowPlacement.TopRight(bounds, size, margin: 8);

    private PixelPoint PlaceSquadron(PixelRect bounds, PixelSize size, double scaling)
    {
        PixelPoint position = OverlayWindowPlacement.TopRight(bounds, size, margin: 8);
        RegisteredOverlayWindow? personal = windowRegistry
            .Snapshot()
            .FirstOrDefault(window => window.PlotterName == PersonalPlotterName);
        double width = personal is not null
            ? OverlayWindowMetrics.GetPixelSize(personal).Width
            : (
                layout.GetSizeOverride(PersonalPlotterName)?.Width
                ?? OverlayLayoutCatalog.GetRequired(PersonalPlotterName).PreviewSize.Width
            )
                * scaling
                * OverlayScaleCatalog.GetRelativeScale(layout.GetScaleIndex(PersonalPlotterName), scaling);
        return new(Math.Max(bounds.X + 8, position.X - Math.Max(1, (int)Math.Ceiling(width)) - 16), position.Y);
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
