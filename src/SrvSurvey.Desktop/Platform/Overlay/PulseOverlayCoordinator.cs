using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class PulseOverlayCoordinator : IDisposable
{
    private readonly PulseOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public PulseOverlayCoordinator(PulseOverlayViewModel viewModel, OverlayPresentationSession presentationSession)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotPulse",
                _ => new PulseOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.BottomLeft(gameBounds, windowSize, margin: 8)
            )
            {
                PollInterval = TimeSpan.FromMilliseconds(500),
                Tick = OnTick,
            }
        );
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

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
        hostedWindow.Dispose();
    }

    private void OnTick()
    {
        viewModel.Refresh();
        SynchronizeIntent();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(PulseOverlayViewModel.ShouldShow)
                or nameof(PulseOverlayViewModel.PulseHeight)
                or nameof(PulseOverlayViewModel.IsScoActive)
                or nameof(PulseOverlayViewModel.IsScoCoolingDown)
                or nameof(PulseOverlayViewModel.IsScoReady)
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
