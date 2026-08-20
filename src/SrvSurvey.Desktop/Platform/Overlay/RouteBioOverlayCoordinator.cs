using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class RouteBioOverlayCoordinator : IDisposable
{
    private readonly RouteWorkspaceViewModel route;
    private readonly RouteBioOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private RouteBioOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public RouteBioOverlayCoordinator(
        RouteWorkspaceViewModel route,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null)
    {
        this.route = route ?? throw new ArgumentNullException(nameof(route));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        viewModel = new RouteBioOverlayViewModel(route, platform.Capabilities);
        route.PropertyChanged += OnRoutePropertyChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

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
        route.PropertyChanged -= OnRoutePropertyChanged;
        viewModel.Dispose();
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindow();
    }

    private void OnRoutePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is
            nameof(RouteWorkspaceViewModel.ShouldShowRouteBioOverlay)
            or nameof(RouteWorkspaceViewModel.CurrentBioTargets))
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
            || !route.ShouldShowRouteBioOverlay
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

        var overlay = new RouteBioOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotRouteBio");
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

    private void PositionWindow(Window target, PixelRect gameBounds)
    {
        OverlayThemeResources.ApplyOpacity(
            target,
            overlayLayout,
            "PlotRouteBio");
        var screen = target.Screens.ScreenFromBounds(gameBounds)
            ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            target,
            overlayLayout,
            "PlotRouteBio",
            screen.Scaling);
        var position = overlayLayout.GetPosition("PlotRouteBio", gameBounds, size)
            ?? OverlayWindowPlacement.TopRight(gameBounds, size, margin: 8);
        if (target.Position != position)
        {
            target.Position = position;
        }
    }

    private void CloseWindow()
    {
        var overlay = window;
        if (overlay is null)
        {
            return;
        }

        window = null;
        overlay.Close();
    }
}
