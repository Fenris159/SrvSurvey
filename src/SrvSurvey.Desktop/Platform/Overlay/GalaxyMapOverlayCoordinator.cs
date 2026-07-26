using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GalaxyMapOverlayCoordinator : IDisposable
{
    private readonly GalaxyMapOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private GalaxyMapOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public GalaxyMapOverlayCoordinator(
        GalaxyMapOverlayViewModel viewModel,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null)
    {
        this.viewModel = viewModel
            ?? throw new ArgumentNullException(nameof(viewModel));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindow();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(GalaxyMapOverlayViewModel.ShouldShow)
            or nameof(GalaxyMapOverlayViewModel.PrimarySystem)
            or nameof(GalaxyMapOverlayViewModel.SecondarySystem)
            or nameof(GalaxyMapOverlayViewModel.Factions))
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
            || !viewModel.ShouldShow
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

        var overlay = new GalaxyMapOverlayWindow(viewModel);
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            "PlotGalMap");
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay, gameWindow.ClientBounds);
            var preparation = platform.PreparePassiveWindow(overlay);
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

    private void PositionWindow(Window window, PixelRect gameBounds)
    {
        OverlayThemeResources.ApplyOpacity(
            window,
            overlayLayout,
            "PlotGalMap");
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
        var size = new PixelSize(
            width,
            Math.Max((int)Math.Ceiling(logicalHeight * screen.Scaling), 1));
        var position = overlayLayout.GetPosition(
                "PlotGalMap",
                gameBounds,
                size)
            ?? OverlayWindowPlacement.TopLeft(gameBounds, size, 8);
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
