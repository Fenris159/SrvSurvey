using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class ExplorationPoiOverlayCoordinator : IDisposable
{
    private readonly ExplorationPoiViewModel viewModel;
    private readonly HostedOverlayWindow window;
    private bool suppressed;

    /// <summary>Connects POI visibility evidence to the existing hosted overlay lifecycle.</summary>
    public ExplorationPoiOverlayCoordinator(ExplorationPoiViewModel viewModel, OverlayPresentationSession session)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(session);
        window = session.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotExplorationPois",
                _ => new ExplorationPoiOverlayWindow(viewModel),
                (bounds, size) => OverlayWindowPlacement.TopRight(bounds, size, 8)
            )
        );
        viewModel.PropertyChanged += OnChanged;
        Synchronize();
    }

    public bool IsVisible => window.IsVisible;

    /// <summary>Applies global overlay suppression to the current POI visibility intent.</summary>
    public void SetSuppressed(bool value)
    {
        suppressed = value;
        Synchronize();
    }

    /// <summary>Reconciles the hosted panel after its presentation model changes.</summary>
    private void OnChanged(object? sender, PropertyChangedEventArgs args) => Synchronize();

    /// <summary>Shows or hides the hosted POI panel from current settings and system evidence.</summary>
    private void Synchronize() => window.Reconcile(!suppressed && viewModel.ShouldShow);

    /// <summary>Detaches presentation notifications and releases the hosted panel.</summary>
    public void Dispose()
    {
        viewModel.PropertyChanged -= OnChanged;
        window.Dispose();
    }
}
