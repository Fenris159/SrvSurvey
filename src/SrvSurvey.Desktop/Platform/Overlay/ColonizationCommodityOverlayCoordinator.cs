using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class ColonizationCommodityOverlayCoordinator : IDisposable
{
    private readonly ColonizationCommodityOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private ColonizationCommodityOverlayWindow? window;
    private bool manualShow;
    private bool isSuppressed;
    private bool disposed;

    public ColonizationCommodityOverlayCoordinator(
        ColonizationCommodityOverlayViewModel viewModel,
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
        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
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
        if (disposed)
        {
            return;
        }

        if (IsVisible)
        {
            manualShow = false;
            isSuppressed = true;
        }
        else
        {
            manualShow = true;
            isSuppressed = false;
        }

        SynchronizeWindow();
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
        if (eventArgs.PropertyName
            == nameof(ColonizationCommodityOverlayViewModel.ShouldAutoShow))
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
        var wantsWindow = manualShow || viewModel.ShouldAutoShow;
        if (isSuppressed
            || !wantsWindow
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

        var overlay = new ColonizationCommodityOverlayWindow(viewModel);
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            "PlotBuildCommodities");
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

    private void PositionWindow(Window window, PixelRect gameBounds)
    {
        OverlayThemeResources.ApplyOpacity(
            window,
            overlayLayout,
            "PlotBuildCommodities");
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var width = (int)Math.Ceiling(window.Width * screen.Scaling);
        var height = (int)Math.Ceiling(window.Height * screen.Scaling);
        var size = new PixelSize(width, height);
        var position = overlayLayout.GetPosition(
                "PlotBuildCommodities",
                gameBounds,
                size)
            ?? OverlayWindowPlacement.TopRight(gameBounds, size);
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
