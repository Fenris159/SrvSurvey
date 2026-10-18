using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class ColonizationCommodityOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotBuildCommodities";
    private readonly ColonizationCommodityOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly OverlayWindowRegistry windowRegistry;
    private bool userEnabled;
    private bool? manualVisibility;
    private bool isSuppressed;
    private bool disposed;

    /// <summary>Hosts shopping cargo and distinguishes automatic eligibility from explicit session activation.</summary>
    public ColonizationCommodityOverlayCoordinator(
        ColonizationCommodityOverlayViewModel viewModel,
        OverlayPresentationSession presentationSession
    )
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(presentationSession);
        windowRegistry = presentationSession.WindowRegistry;
        userEnabled = windowRegistry.IsUserVisible(PlotterName);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                PlotterName,
                _ => new ColonizationCommodityOverlayWindow(viewModel),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize),
                viewModel.ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
        windowRegistry.Changed += OnRegistryChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    /// <summary>Toggles actual visibility, pins manual display, and keeps the availability checkbox in parity.</summary>
    public void ToggleVisibility(OverlayPanelVisibilityViewModel availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        if (disposed)
        {
            return;
        }

        bool show = !IsVisible;
        availability.Panels.Single(panel => panel.PlotterName == PlotterName).IsEnabled = show;
        manualVisibility = show;

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

    /// <summary>Releases visibility subscriptions and the hosted panel once.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        windowRegistry.Changed -= OnRegistryChanged;
        hostedWindow.Dispose();
    }

    /// <summary>Reconciles game-state eligibility after the commodity plan or display settings change.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(ColonizationCommodityOverlayViewModel.ShouldAutoShow)
                or nameof(ColonizationCommodityOverlayViewModel.CanShowManually)
        )
        {
            SynchronizeIntent();
        }
    }

    /// <summary>Pins the panel when availability is explicitly enabled and clears that request when disabled.</summary>
    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        bool enabled = windowRegistry.IsUserVisible(PlotterName);
        if (enabled == userEnabled)
        {
            return;
        }

        userEnabled = enabled;
        manualVisibility = enabled ? true : null;
        SynchronizeIntent();
    }

    /// <summary>Combines persistent manual display with automatic context and global overlay suppression.</summary>
    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        bool wantsWindow = manualVisibility switch
        {
            true => viewModel.CanShowManually,
            false => false,
            null => viewModel.ShouldAutoShow,
        };
        hostedWindow.Reconcile(!isSuppressed && wantsWindow);
    }
}
