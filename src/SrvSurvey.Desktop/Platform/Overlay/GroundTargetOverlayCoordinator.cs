using System.ComponentModel;
using Avalonia;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GroundTargetOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotTrackTarget";

    private readonly GroundTargetViewModel groundTarget;
    private readonly HostedOverlayWindow hostedWindow;
    private GroundTargetOverlayViewModel? overlayViewModel;
    private bool isSuppressed;
    private bool disposed;

    public GroundTargetOverlayCoordinator(
        GroundTargetViewModel groundTarget,
        OverlayPresentationSession presentationSession)
    {
        this.groundTarget = groundTarget
            ?? throw new ArgumentNullException(nameof(groundTarget));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                PlotterName,
                capabilities => new GroundTargetOverlayWindow(
                    GetOrCreateOverlayViewModel(capabilities)),
                (gameBounds, windowSize) =>
                    OverlayWindowPlacement.BottomCenter(
                        gameBounds,
                        windowSize),
                preparation => overlayViewModel?.ApplyPreparation(
                    preparation)));
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        groundTarget.PropertyChanged += OnGroundTargetPropertyChanged;
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
        groundTarget.PropertyChanged -= OnGroundTargetPropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
    }

    private void OnGroundTargetPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GroundTargetViewModel.ShouldShow))
        {
            SynchronizeIntent();
        }
    }

    private GroundTargetOverlayViewModel GetOrCreateOverlayViewModel(
        OverlayPlatformCapabilities capabilities)
    {
        return overlayViewModel ??= new GroundTargetOverlayViewModel(
            groundTarget,
            capabilities);
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        hostedWindow.Reconcile(!isSuppressed && groundTarget.ShouldShow);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
