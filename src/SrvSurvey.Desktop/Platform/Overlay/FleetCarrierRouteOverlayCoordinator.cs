using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class FleetCarrierRouteOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotFleetCarrierRoute";

    private readonly RouteWorkspaceViewModel route;
    private readonly FleetCarrierRouteOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private FleetCarrierRouteOverlayWindow? window;
    private bool isSuppressed;
    private bool isPolling;
    private bool disposed;

    public FleetCarrierRouteOverlayCoordinator(
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
        viewModel = new FleetCarrierRouteOverlayViewModel(
            route,
            platform.Capabilities);
        route.PropertyChanged += OnRoutePropertyChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        SynchronizePolling();
    }

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizePolling();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (isPolling)
        {
            timer.Stop();
            isPolling = false;
        }
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
            nameof(RouteWorkspaceViewModel.ShouldShowFleetCarrierRouteOverlay)
            or nameof(RouteWorkspaceViewModel.NextHop))
        {
            SynchronizePolling();
        }
    }

    private void SynchronizePolling()
    {
        var shouldPoll = !disposed
            && !isSuppressed
            && route.ShouldShowFleetCarrierRouteOverlay
            && platform.Capabilities.SupportsPassiveOverlay
            && platform.Capabilities.SupportsClickThrough
            && platform.Capabilities.SupportsGameWindowTracking;
        if (shouldPoll != isPolling)
        {
            isPolling = shouldPoll;
            if (shouldPoll)
            {
                timer.Start();
            }
            else
            {
                timer.Stop();
            }
        }

        SynchronizeWindow();
    }

    private void SynchronizeWindow()
    {
        if (disposed)
        {
            return;
        }

        if (isSuppressed
            || !route.ShouldShowFleetCarrierRouteOverlay
            || !platform.Capabilities.SupportsPassiveOverlay
            || !platform.Capabilities.SupportsClickThrough
            || !platform.Capabilities.SupportsGameWindowTracking)
        {
            CloseWindow();
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        if (!gameWindow.IsAvailable
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

        var overlay = new FleetCarrierRouteOverlayWindow(viewModel);
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            PlotterName);
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay, gameWindow.ClientBounds);
            var preparation = platform.PreparePassiveWindow(overlay);
            if (!preparation.IsClickThrough)
            {
                isSuppressed = true;
                SynchronizePolling();
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
            PlotterName);
        var screen = target.Screens.ScreenFromBounds(gameBounds)
            ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var width = (int)Math.Ceiling(target.Width * screen.Scaling);
        var logicalHeight = target.Bounds.Height > 0
            ? target.Bounds.Height
            : target.MinHeight;
        var height = (int)Math.Ceiling(logicalHeight * screen.Scaling);
        var size = new PixelSize(width, Math.Max(height, 1));
        var position = overlayLayout.GetPosition(
                PlotterName,
                gameBounds,
                size)
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
