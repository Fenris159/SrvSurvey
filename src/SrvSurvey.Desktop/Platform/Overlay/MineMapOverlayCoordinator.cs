using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MineMapOverlayCoordinator : IDisposable
{
    private readonly MineMapViewModel mineMap;
    private readonly HostedOverlayWindow hostedWindow;
    private bool suppressed;
    private bool disposed;

    public MineMapOverlayCoordinator(
        MineMapViewModel mineMap,
        OverlayPresentationSession presentationSession)
    {
        this.mineMap = mineMap ?? throw new ArgumentNullException(nameof(mineMap));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMineMap",
                _ => new MineMapOverlayWindow(mineMap),
                (gameBounds, windowSize) => OverlayWindowPlacement.MiddleLeft(gameBounds, windowSize)));
        mineMap.PropertyChanged += OnMineMapPropertyChanged;
        Synchronize();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public void SetSuppressed(bool value)
    {
        if (disposed || suppressed == value) return;
        suppressed = value;
        Synchronize();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mineMap.PropertyChanged -= OnMineMapPropertyChanged;
        hostedWindow.Dispose();
    }

    private void OnMineMapPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is null or nameof(MineMapViewModel.ShouldShowOverlay))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (!disposed) hostedWindow.Reconcile(!suppressed && mineMap.ShouldShowOverlay);
    }
}
