using System.ComponentModel;
using Avalonia;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SurfaceMiningOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotSurfaceMining";

    private readonly SurfaceMiningViewModel surfaceMining;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly HostedOverlayWindow warningWindow;
    private SurfaceMiningOverlayViewModel? overlayViewModel;
    private bool isSuppressed;
    private bool disposed;

    public SurfaceMiningOverlayCoordinator(
        SurfaceMiningViewModel surfaceMining,
        OverlayPresentationSession presentationSession)
    {
        this.surfaceMining = surfaceMining
            ?? throw new ArgumentNullException(nameof(surfaceMining));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                PlotterName,
                capabilities => new SurfaceMiningOverlayWindow(
                    GetOrCreateOverlayViewModel(capabilities)),
                (gameBounds, windowSize) =>
                    OverlayWindowPlacement.BottomCenter(
                        gameBounds,
                        windowSize),
                preparation => overlayViewModel?.ApplyPreparation(
                    preparation)));
        warningWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningWarning",
                capabilities => new MiningWarningOverlayWindow(GetOrCreateOverlayViewModel(capabilities)),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize),
                preparation => overlayViewModel?.ApplyPreparation(preparation)));
        warningWindow.VisibilityChanged += OnHostedVisibilityChanged;
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        surfaceMining.PropertyChanged += OnSurfaceMiningPropertyChanged;
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsWarningVisible => warningWindow.IsVisible;

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
        surfaceMining.PropertyChanged -= OnSurfaceMiningPropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
        warningWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        warningWindow.Dispose();
    }

    private void OnSurfaceMiningPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is null or nameof(SurfaceMiningViewModel.ShouldShow)
            or nameof(SurfaceMiningViewModel.ShouldShowRigWarning))
        {
            SynchronizeIntent();
        }
    }

    private SurfaceMiningOverlayViewModel GetOrCreateOverlayViewModel(
        OverlayPlatformCapabilities capabilities)
    {
        return overlayViewModel ??= new SurfaceMiningOverlayViewModel(
            surfaceMining,
            capabilities);
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        hostedWindow.Reconcile(!isSuppressed && surfaceMining.ShouldShow);
        warningWindow.Reconcile(!isSuppressed && surfaceMining.ShouldShowRigWarning);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
