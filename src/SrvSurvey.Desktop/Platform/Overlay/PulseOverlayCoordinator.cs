using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class PulseOverlayCoordinator : IDisposable
{
    private readonly PulseOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private PulseOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public PulseOverlayCoordinator(
        PulseOverlayViewModel viewModel,
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
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

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
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        viewModel.Refresh();
        SynchronizeWindow();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(PulseOverlayViewModel.ShouldShow)
            or nameof(PulseOverlayViewModel.PulseHeight)
            or nameof(PulseOverlayViewModel.IsScoActive)
            or nameof(PulseOverlayViewModel.IsScoCoolingDown)
            or nameof(PulseOverlayViewModel.IsScoReady))
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
            PositionWindow(window);
            return;
        }

        var overlay = new PulseOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotPulse");
        overlay.Opened += (_, _) =>
        {
            PositionWindow(overlay);
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

    private void PositionWindow(Window overlay)
    {
        OverlayThemeResources.ApplyOpacity(overlay, overlayLayout, "PlotPulse");
        var screen = overlay.Screens.ScreenFromBounds(gameWindow.ClientBounds)
            ?? overlay.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            overlay,
            overlayLayout,
            "PlotPulse",
            screen.Scaling);
        var position = overlayLayout.GetPosition(
                "PlotPulse",
                gameWindow.ClientBounds,
                size)
            ?? OverlayWindowPlacement.BottomLeft(
                gameWindow.ClientBounds,
                size,
                margin: 8);
        if (overlay.Position != position)
        {
            overlay.Position = position;
        }
    }

    private void CloseWindow()
    {
        var overlay = window;
        window = null;
        overlay?.Close();
    }
}
