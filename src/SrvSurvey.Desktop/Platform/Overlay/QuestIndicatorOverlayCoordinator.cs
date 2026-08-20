using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class QuestIndicatorOverlayCoordinator : IDisposable
{
    private readonly QuestIndicatorViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private QuestIndicatorOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public QuestIndicatorOverlayCoordinator(
        QuestIndicatorViewModel viewModel,
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
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindow();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => window is not null;

    public bool IsSuppressed => isSuppressed;

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
        if (eventArgs.PropertyName == nameof(QuestIndicatorViewModel.ShouldShow))
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
        var shouldShow = !isSuppressed
            && viewModel.ShouldShow
            && platform.Capabilities.SupportsPassiveOverlay
            && platform.Capabilities.SupportsClickThrough
            && platform.Capabilities.SupportsGameWindowTracking
            && gameWindow.IsAvailable
            && gameWindow.IsVisible
            && gameWindow.IsForeground;
        if (!shouldShow)
        {
            CloseWindow();
            return;
        }

        if (window is not null)
        {
            PositionWindow(window);
            return;
        }

        var overlay = new QuestIndicatorOverlayWindow(viewModel);
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            "PlotQuestMini");
        overlay.Opened += (_, _) => PrepareWindow(overlay);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(window, overlay))
            {
                window = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        window = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareWindow(QuestIndicatorOverlayWindow overlay)
    {
        PositionWindow(overlay);
        var preparation = platform.PreparePassiveWindow(overlay);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            CloseWindow();
        }
    }

    private void PositionWindow(QuestIndicatorOverlayWindow overlay)
    {
        OverlayThemeResources.ApplyOpacity(
            overlay,
            overlayLayout,
            "PlotQuestMini");
        var screen = overlay.Screens.ScreenFromBounds(gameWindow.ClientBounds)
            ?? overlay.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            overlay,
            overlayLayout,
            "PlotQuestMini",
            screen.Scaling);
        var position = overlayLayout.GetPosition(
                "PlotQuestMini",
                gameWindow.ClientBounds,
                size)
            ?? OverlayWindowPlacement.TopRight(
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
        if (overlay is null)
        {
            return;
        }

        window = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
