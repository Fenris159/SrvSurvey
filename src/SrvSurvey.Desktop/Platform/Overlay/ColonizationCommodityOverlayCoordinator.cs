using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class ColonizationCommodityOverlayCoordinator : IDisposable
{
    private readonly ColonizationCommodityOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool manualShow;
    private bool isSuppressed;
    private bool disposed;

    public ColonizationCommodityOverlayCoordinator(
        ColonizationCommodityOverlayViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotBuildCommodities",
                _ => new ColonizationCommodityOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize),
                viewModel.ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    public void ToggleVisibility()
    {
        if (disposed)
        {
            return;
        }

        if (IsVisible)
        {
            manualShow = false;
            isSuppressed = true;
        }
        else
        {
            manualShow = true;
            isSuppressed = false;
        }

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
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        hostedWindow.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(ColonizationCommodityOverlayViewModel.ShouldAutoShow))
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

        bool wantsWindow = manualShow && viewModel.CanShowManually || viewModel.ShouldAutoShow;
        hostedWindow.Reconcile(!isSuppressed && wantsWindow);
    }
}
