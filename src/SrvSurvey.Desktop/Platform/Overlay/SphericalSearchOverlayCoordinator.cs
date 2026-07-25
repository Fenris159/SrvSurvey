using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SphericalSearchOverlayCoordinator : IDisposable
{
    private readonly SphereLimitViewModel sphere;
    private readonly BoxelSearchViewModel boxel;
    private readonly RouteWorkspaceViewModel route;
    private readonly SphericalSearchOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private SphericalSearchOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public SphericalSearchOverlayCoordinator(
        SphereLimitViewModel sphere,
        BoxelSearchViewModel boxel,
        RouteWorkspaceViewModel route,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker)
    {
        this.sphere = sphere ?? throw new ArgumentNullException(nameof(sphere));
        this.boxel = boxel ?? throw new ArgumentNullException(nameof(boxel));
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        viewModel = new SphericalSearchOverlayViewModel(
            sphere,
            boxel,
            route,
            platform.Capabilities);
        sphere.PropertyChanged += OnSearchPropertyChanged;
        boxel.PropertyChanged += OnSearchPropertyChanged;
        route.PropertyChanged += OnSearchPropertyChanged;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

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
        SynchronizeWindow();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        sphere.PropertyChanged -= OnSearchPropertyChanged;
        boxel.PropertyChanged -= OnSearchPropertyChanged;
        route.PropertyChanged -= OnSearchPropertyChanged;
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindow();
    }

    private void OnSearchPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(
                SphereLimitViewModel.ShouldShowGalaxyMapOverlay)
            or nameof(BoxelSearchViewModel.ShouldShowGalaxyMapOverlay)
            or nameof(RouteWorkspaceViewModel.ShouldShowGalaxyMapOverlay))
        {
            SynchronizeWindow();
        }
    }

    private void SynchronizeWindow()
    {
        if (disposed)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        if (isSuppressed
            || !ShouldShow
            || !platform.Capabilities.SupportsPassiveOverlay
            || !platform.Capabilities.SupportsClickThrough
            || !platform.Capabilities.SupportsGameWindowTracking
            || !gameWindow.IsAvailable
            || !gameWindow.IsVisible
            || !gameWindow.IsForeground)
        {
            CloseWindow();
            return;
        }

        if (window is not null)
        {
            PositionWindow(window, gameWindow.ClientBounds);
            return;
        }

        var overlay = new SphericalSearchOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay);
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay, gameWindow.ClientBounds);
            var preparation = platform.PreparePassiveWindow(overlay);
            viewModel.ApplyPreparation(preparation);
            if (!preparation.IsClickThrough)
            {
                isSuppressed = true;
                CloseWindow();
            }
        };
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, overlay))
            {
                window = null;
            }
        };
        window = overlay;
        overlay.Show();
    }

    private bool ShouldShow => sphere.ShouldShowGalaxyMapOverlay
        || boxel.ShouldShowGalaxyMapOverlay
        || route.ShouldShowGalaxyMapOverlay;

    private static void PositionWindow(Window window, PixelRect gameBounds)
    {
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var width = (int)Math.Ceiling(window.Width * screen.Scaling);
        var logicalHeight = window.Bounds.Height > 0
            ? window.Bounds.Height
            : window.MinHeight;
        var height = (int)Math.Ceiling(logicalHeight * screen.Scaling);
        var position = OverlayWindowPlacement.TopRight(
            gameBounds,
            new PixelSize(width, Math.Max(height, 1)),
            8);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseWindow()
    {
        var overlay = window;
        window = null;
        overlay?.Close();
    }
}
