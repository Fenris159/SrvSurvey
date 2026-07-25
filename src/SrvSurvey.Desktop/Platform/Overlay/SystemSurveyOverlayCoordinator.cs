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
    private BiologySurveyOverlayWindow? biologyWindow;
    private BiologyStatusOverlayWindow? biologyStatusWindow;
    private BodyInformationOverlayWindow? bodyInfoWindow;
    private FssInfoOverlayWindow? fssWindow;
    private LastFssBodyOverlayWindow? lastFssBodyWindow;
    private SystemStatusOverlayWindow? statusWindow;
    private bool isSuppressed;
    private bool isBiologyObscured;
    private bool isBiologyStatusObscured;
    private bool isBodyInfoObscured;
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

    public bool IsVisible => biologyWindow is not null
        || biologyStatusWindow is not null
        || bodyInfoWindow is not null
        || fssWindow is not null
        || lastFssBodyWindow is not null
        || statusWindow is not null;

    public bool IsFssVisible => fssWindow is not null;

    public bool IsLastFssBodyVisible => lastFssBodyWindow is not null;

    public bool IsBodyInfoVisible => bodyInfoWindow is not null;

    public bool IsBiologyVisible => biologyWindow is not null;

    public bool IsBiologyStatusVisible => biologyStatusWindow is not null;

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

    public void SetBodyInfoObscured(bool value)
    {
        if (disposed || value == isBodyInfoObscured)
        {
            return;
        }

        isBodyInfoObscured = value;
        SynchronizeWindows();
    }

    public void SetBiologyObscured(bool value)
    {
        if (disposed || value == isBiologyObscured)
        {
            return;
        }

        isBiologyObscured = value;
        SynchronizeWindows();
    }

    public void SetBiologyStatusObscured(bool value)
    {
        if (disposed || value == isBiologyStatusObscured)
        {
            return;
        }

        isBiologyStatusObscured = value;
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
        CloseBiologyWindow();
        CloseBiologyStatusWindow();
        CloseBodyInfoWindow();
        CloseFssWindow();
        CloseLastFssBodyWindow();
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
            or nameof(SystemSurveyViewModel.ShouldShowLastFssBody)
            or nameof(SystemSurveyViewModel.ShouldShowBodyInfo)
            or nameof(SystemSurveyViewModel.ShouldShowBioSystem)
            or nameof(SystemSurveyViewModel.ShouldShowBioStatus)
            or nameof(SystemSurveyViewModel.ShouldShowSystemStatus)
            or nameof(SystemSurveyViewModel.IsFssInfoForced)
            or nameof(SystemSurveyViewModel.IsBodyInfoForced))
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
        var showLastFssBody = platformReady
            && survey.ShouldShowLastFssBody;
        var showBodyInfo = platformReady
            && survey.ShouldShowBodyInfo
            && (!isBodyInfoObscured || survey.IsBodyInfoForced);
        var showStatus = platformReady && survey.ShouldShowSystemStatus;
        var showBiology = platformReady
            && survey.ShouldShowBioSystem
            && !isBiologyObscured;
        var showBiologyStatus = platformReady
            && survey.ShouldShowBioStatus
            && !isBiologyStatusObscured;

        SynchronizeBodyInfoWindow(showBodyInfo);
        SynchronizeFssWindow(showFss);
        SynchronizeLastFssBodyWindow(showLastFssBody);
        SynchronizeStatusWindow(showStatus);
        SynchronizeBiologyWindow(showBiology);
        SynchronizeBiologyStatusWindow(showBiologyStatus);
    }

    private void SynchronizeBiologyStatusWindow(bool show)
    {
        if (!show)
        {
            CloseBiologyStatusWindow();
            return;
        }

        if (biologyStatusWindow is not null)
        {
            PositionTopCenter(biologyStatusWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BiologyStatusOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopCenter,
            CloseBiologyStatusWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(biologyStatusWindow, overlay))
            {
                biologyStatusWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        biologyStatusWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeBiologyWindow(bool show)
    {
        if (!show)
        {
            CloseBiologyWindow();
            return;
        }

        if (biologyWindow is not null)
        {
            PositionBiologyWindow(biologyWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BiologySurveyOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionBiologyWindow,
            CloseBiologyWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(biologyWindow, overlay))
            {
                biologyWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        biologyWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeBodyInfoWindow(bool show)
    {
        if (!show)
        {
            CloseBodyInfoWindow();
            return;
        }

        if (bodyInfoWindow is not null)
        {
            PositionTopLeft(bodyInfoWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new BodyInformationOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopLeft,
            CloseBodyInfoWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(bodyInfoWindow, overlay))
            {
                bodyInfoWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        bodyInfoWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeLastFssBodyWindow(bool show)
    {
        if (!show)
        {
            CloseLastFssBodyWindow();
            return;
        }

        if (lastFssBodyWindow is not null)
        {
            PositionTopCenter(lastFssBodyWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new LastFssBodyOverlayWindow(viewModel);
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopCenter,
            CloseLastFssBodyWindow);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(lastFssBodyWindow, overlay))
            {
                lastFssBodyWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        lastFssBodyWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
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

    private static void PositionTopCenter(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, OverlayWindowPlacement.TopCenter);
    }

    private static void PositionBottomLeft(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, OverlayWindowPlacement.BottomLeft);
    }

    private void PositionBiologyWindow(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, (bounds, size, margin) =>
        {
            var statusOffset = statusWindow is null
                || statusWindow.Bounds.Height <= 0
                ? 0
                : Math.Max(0, bounds.Bottom - statusWindow.Position.Y) + 12;
            return new PixelPoint(
                bounds.X + margin,
                Math.Max(
                    bounds.Y + margin,
                    bounds.Bottom - size.Height - margin - statusOffset));
        });
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

    private void CloseBiologyWindow()
    {
        var overlay = biologyWindow;
        if (overlay is null)
        {
            return;
        }

        biologyWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseBiologyStatusWindow()
    {
        var overlay = biologyStatusWindow;
        if (overlay is null)
        {
            return;
        }

        biologyStatusWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseBodyInfoWindow()
    {
        var overlay = bodyInfoWindow;
        if (overlay is null)
        {
            return;
        }

        bodyInfoWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseLastFssBodyWindow()
    {
        var overlay = lastFssBodyWindow;
        if (overlay is null)
        {
            return;
        }

        lastFssBodyWindow = null;
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
