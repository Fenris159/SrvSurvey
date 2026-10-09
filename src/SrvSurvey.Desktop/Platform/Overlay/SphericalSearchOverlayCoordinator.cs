using System.ComponentModel;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SphericalSearchOverlayCoordinator : IDisposable
{
    private readonly SphereLimitViewModel sphere;
    private readonly BoxelSearchViewModel boxel;
    private readonly RouteWorkspaceViewModel route;
    private readonly SphericalSearchOverlayViewModel viewModel;
    private readonly HostedOverlayWindow hostedWindow;
    private bool isSuppressed;
    private bool disposed;

    public SphericalSearchOverlayCoordinator(
        SphereLimitViewModel sphere,
        BoxelSearchViewModel boxel,
        RouteWorkspaceViewModel route,
        OverlayPresentationSession presentationSession,
        SphericalSearchOverlayCoordinatorOptions? options = null
    )
    {
        options ??= new SphericalSearchOverlayCoordinatorOptions();
        this.sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
        this.boxel = boxel ?? throw new ArgumentNullException(nameof(boxel));
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        ArgumentNullException.ThrowIfNull(presentationSession);
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotSphericalSearch",
                _ => CreateWindow(),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize, 8),
                ApplyPreparation
            )
            {
                Tick = SynchronizeIntent,
            }
        );
        viewModel = new SphericalSearchOverlayViewModel(
            sphere,
            boxel,
            route,
            hostedWindow.Capabilities,
            options.SystemNicknames,
            options.InputSettings
        );
        sphere.PropertyChanged += OnSearchPropertyChanged;
        boxel.PropertyChanged += OnSearchPropertyChanged;
        route.PropertyChanged += OnSearchPropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public bool IsSuppressed => isSuppressed;

    public void ToggleVisibility()
    {
        SetSuppressed(!isSuppressed);
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
        sphere.PropertyChanged -= OnSearchPropertyChanged;
        boxel.PropertyChanged -= OnSearchPropertyChanged;
        route.PropertyChanged -= OnSearchPropertyChanged;
        hostedWindow.Dispose();
        viewModel.Dispose();
    }

    private bool ShouldShow =>
        sphere.ShouldShowGalaxyMapOverlay || boxel.ShouldShowGalaxyMapOverlay || route.ShouldShowGalaxyMapOverlay;

    private SphericalSearchOverlayWindow CreateWindow() => new(viewModel);

    private void ApplyPreparation(OverlayPreparationResult preparation)
    {
        viewModel.ApplyPreparation(preparation);
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(SphereLimitViewModel.ShouldShowGalaxyMapOverlay)
                or nameof(BoxelSearchViewModel.ShouldShowGalaxyMapOverlay)
                or nameof(RouteWorkspaceViewModel.ShouldShowGalaxyMapOverlay)
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

        hostedWindow.Reconcile(!isSuppressed && ShouldShow);
    }
}

public sealed class SphericalSearchOverlayCoordinatorOptions
{
    public SystemNicknameViewModel? SystemNicknames { get; init; }

    public GlobalInputSettingsViewModel? InputSettings { get; init; }
}
