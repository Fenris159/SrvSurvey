using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class QuestIndicatorOverlayCoordinator : IDisposable
{
    private readonly QuestIndicatorViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public QuestIndicatorOverlayCoordinator(
        QuestIndicatorViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotQuestMini",
                _ => new QuestIndicatorOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, margin: 8)
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

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
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(QuestIndicatorViewModel.ShouldShow))
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

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
