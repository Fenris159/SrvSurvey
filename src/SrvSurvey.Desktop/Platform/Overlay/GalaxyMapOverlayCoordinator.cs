using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GalaxyMapOverlayCoordinator : IDisposable
{
    private const string PlotterName = "PlotGalMap";

    private readonly GalaxyMapOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayWindowRegistry registry;
    private readonly OverlayDispatcherTimer timer;
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
        registry = OverlayWindowRegistry.Shared;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        registry.SetGalaxyMapContextActive(viewModel.IsGalaxyMapOpen);
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

    public bool IsSuppressed => isSuppressed;

    public void SetSuppressed(bool value)
    {
        if (disposed || isSuppressed == value)
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
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        registry.SetGalaxyMapContextActive(false);
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
        if (eventArgs.PropertyName is nameof(
                GalaxyMapOverlayViewModel.IsGalaxyMapOpen))
        {
            registry.SetGalaxyMapContextActive(viewModel.IsGalaxyMapOpen);
        }

        if (eventArgs.PropertyName is nameof(GalaxyMapOverlayViewModel.ShouldShow)
            or nameof(GalaxyMapOverlayViewModel.IsGalaxyMapOpen)
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
            PlotterName);
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
            PlotterName);
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            window, overlayLayout, PlotterName, screen.Scaling);
        var position = overlayLayout.GetPosition(
                PlotterName,
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
