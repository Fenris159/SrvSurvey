using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GalaxyMapOverlayCoordinator : IDisposable
{
    private readonly GalaxyMapOverlayViewModel viewModel;
    private readonly OverlayWindowRegistry registry;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public GalaxyMapOverlayCoordinator(
        GalaxyMapOverlayViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        registry = OverlayWindowRegistry.Shared;
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotGalMap",
                _ => new GalaxyMapOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopLeft(gameBounds, windowSize, 8)
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        registry.SetGalaxyMapContextActive(viewModel.IsGalaxyMapOpen);
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    public void SetSuppressed(bool value)
    {
        if (disposed || isSuppressed == value)
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
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        registry.SetGalaxyMapContextActive(false);
        hostedWindow.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(GalaxyMapOverlayViewModel.IsGalaxyMapOpen))
        {
            registry.SetGalaxyMapContextActive(viewModel.IsGalaxyMapOpen);
        }

        if (
            eventArgs.PropertyName
            is nameof(GalaxyMapOverlayViewModel.ShouldShow)
                or nameof(GalaxyMapOverlayViewModel.IsGalaxyMapOpen)
                or nameof(GalaxyMapOverlayViewModel.PrimarySystem)
                or nameof(GalaxyMapOverlayViewModel.SecondarySystem)
                or nameof(GalaxyMapOverlayViewModel.Factions)
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

        hostedWindow.Reconcile(!isSuppressed && viewModel.ShouldShow);
    }
}
