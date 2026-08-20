using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class StationInfoOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotStationInfo";

    private readonly StationInfoViewModel stationInfo;
    private readonly HostedOverlayWindow hostedWindow;
    private StationInfoOverlayViewModel? overlayViewModel;
    private bool isSuppressed;
    private bool disposed;

    public StationInfoOverlayCoordinator(
        StationInfoViewModel stationInfo,
        OverlayPresentationSession presentationSession)
    {
        this.stationInfo = stationInfo
            ?? throw new ArgumentNullException(nameof(stationInfo));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                PlotterName,
                capabilities => new StationInfoOverlayWindow(
                    GetOrCreateOverlayViewModel(capabilities)),
                (gameBounds, windowSize) =>
                    OverlayWindowPlacement.MiddleLeft(
                        gameBounds,
                        windowSize,
                        margin: 8),
                preparation => overlayViewModel?.ApplyPreparation(
                    preparation)));
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        stationInfo.PropertyChanged += OnStationInfoPropertyChanged;
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
        stationInfo.PropertyChanged -= OnStationInfoPropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
    }

    private void OnStationInfoPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(StationInfoViewModel.ShouldShow))
        {
            SynchronizeIntent();
        }
    }

    private StationInfoOverlayViewModel GetOrCreateOverlayViewModel(
        OverlayPlatformCapabilities capabilities)
    {
        return overlayViewModel ??= new StationInfoOverlayViewModel(
            stationInfo,
            capabilities);
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        hostedWindow.Reconcile(!isSuppressed && stationInfo.ShouldShow);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
