using System.ComponentModel;
using Avalonia;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class HumanSiteOverlayCoordinator : IDisposable
{
    private readonly HumanSiteViewModel humanSite;
    private readonly HumanSiteOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public HumanSiteOverlayCoordinator(HumanSiteViewModel humanSite, OverlayPresentationSession presentationSession)
    {
        this.humanSite = humanSite ?? throw new ArgumentNullException(nameof(humanSite));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotHumanSite",
                _ => CreateWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.MiddleLeft(gameBounds, windowSize, margin: 8),
                ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
                Placement = SizeAndPlace,
            }
        );
        viewModel = new HumanSiteOverlayViewModel(humanSite, hostedWindow.Capabilities);
        hostedWindow.VisibilityChanged += OnHostedVisibilityChanged;
        humanSite.PropertyChanged += OnHumanSitePropertyChanged;
        SynchronizeIntent();
    }

    public event EventHandler? VisibilityChanged;

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

    public bool AdjustZoom(bool zoomIn)
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.AdjustZoom(zoomIn);
        return true;
    }

    public bool ResetZoom()
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.EnableAutomaticZoom();
        return true;
    }

    public bool ToggleHuge()
    {
        if (disposed || !IsVisible)
        {
            return false;
        }

        humanSite.ToggleHuge();
        SynchronizeIntent();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        humanSite.PropertyChanged -= OnHumanSitePropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        hostedWindow.Dispose();
    }

    private void OnHumanSitePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(HumanSiteViewModel.ShouldShow)
                or nameof(HumanSiteViewModel.IsHuge)
                or nameof(HumanSiteViewModel.PreferredWidth)
                or nameof(HumanSiteViewModel.PreferredHeight)
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

        hostedWindow.Reconcile(!isSuppressed && humanSite.ShouldShow);
    }

    private HumanSiteOverlayWindow CreateWindow() => new(viewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
    }

    private PixelPoint SizeAndPlace(HostedOverlayPlacement placement)
    {
        PixelRect gameBounds = placement.GameBounds;
        double scaling = placement.Screen.Scaling;
        placement.SetBaseSize(
            humanSite.IsHuge ? gameBounds.Width * 0.4 / scaling : humanSite.PreferredWidth,
            humanSite.IsHuge ? gameBounds.Height * 0.9 / scaling : humanSite.PreferredHeight
        );
        return placement.GetPosition(placement.PrepareSize());
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
