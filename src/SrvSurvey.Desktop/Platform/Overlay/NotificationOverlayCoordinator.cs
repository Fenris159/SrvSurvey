using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class NotificationOverlayCoordinator : IDisposable
{
    private readonly NotificationViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public NotificationOverlayCoordinator(
        NotificationViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotFloatie",
                _ => new NotificationOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.BottomCenter(gameBounds, windowSize, margin: 24)
            )
            {
                PollInterval = TimeSpan.FromMilliseconds(50),
                Tick = OnTick,
            }
        );
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

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
            eventArgs.PropertyName is nameof(NotificationViewModel.ShouldShow) or nameof(NotificationViewModel.Messages)
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
