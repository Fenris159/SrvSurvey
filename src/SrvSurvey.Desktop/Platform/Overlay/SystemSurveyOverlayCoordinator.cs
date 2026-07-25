using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class SystemSurveyOverlayCoordinator : IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly SystemSurveyOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private FssInfoOverlayWindow? fssWindow;
    private SystemStatusOverlayWindow? statusWindow;
    private bool isSuppressed;
    private bool isFssObscured;
    private bool disposed;

    public SystemSurveyOverlayCoordinator(
        SystemSurveyViewModel survey,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        viewModel = new SystemSurveyOverlayViewModel(
            survey,
            platform.Capabilities);
        survey.PropertyChanged += OnSurveyPropertyChanged;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindows();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => fssWindow is not null || statusWindow is not null;

    public bool IsFssVisible => fssWindow is not null;

    public bool IsSuppressed => isSuppressed;

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeWindows();
    }

    public void SetFssObscured(bool value)
    {
        if (disposed || value == isFssObscured)
        {
            return;
        }

        isFssObscured = value;
        SynchronizeWindows();
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
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        CloseFssWindow();
        CloseStatusWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindows();
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.ShouldShowFssInfo)
            or nameof(SystemSurveyViewModel.ShouldShowSystemStatus)
            or nameof(SystemSurveyViewModel.IsFssInfoForced))
        {
            SynchronizeWindows();
        }
    }

    private void SynchronizeWindows()
    {
        if (disposed)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        var platformReady = !isSuppressed
            && platform.Capabilities.SupportsPassiveOverlay
            && platform.Capabilities.SupportsClickThrough
            && platform.Capabilities.SupportsGameWindowTracking
            && gameWindow.IsAvailable
            && gameWindow.IsVisible
            && gameWindow.IsForeground;
        var showFss = platformReady
            && survey.ShouldShowFssInfo
            && (!isFssObscured || survey.IsFssInfoForced);
        var showStatus = platformReady && survey.ShouldShowSystemStatus;

        SynchronizeFssWindow(showFss);
        SynchronizeStatusWindow(showStatus);
    }

    private void SynchronizeFssWindow(bool show)
    {
        if (!show)
        {
            CloseFssWindow();
            return;
        }

        if (fssWindow is not null)
        {
            PositionTopLeft(fssWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new FssInfoOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopLeft,
            CloseFssWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(fssWindow, overlay))
            {
                fssWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        fssWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeStatusWindow(bool show)
    {
        if (!show)
        {
            CloseStatusWindow();
            return;
        }

        if (statusWindow is not null)
        {
            PositionBottomLeft(statusWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new SystemStatusOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBottomLeft,
            CloseStatusWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(statusWindow, overlay))
            {
                statusWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        statusWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareWindow(
        Window window,
        Action<Window, PixelRect> position,
        Action close)
    {
        position(window, gameWindow.ClientBounds);
        var preparation = platform.PreparePassiveWindow(window);
        viewModel.ApplyPreparation(preparation);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            close();
        }
    }

    private static void PositionTopLeft(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, OverlayWindowPlacement.TopLeft);
    }

    private static void PositionBottomLeft(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, OverlayWindowPlacement.BottomLeft);
    }

    private static void PositionWindow(
        Window window,
        PixelRect gameBounds,
        Func<PixelRect, PixelSize, int, PixelPoint> calculate)
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
        var position = calculate(
            gameBounds,
            new PixelSize(width, Math.Max(height, 1)),
            20);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseFssWindow()
    {
        var overlay = fssWindow;
        if (overlay is null)
        {
            return;
        }

        fssWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseStatusWindow()
    {
        var overlay = statusWindow;
        if (overlay is null)
        {
            return;
        }

        statusWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
