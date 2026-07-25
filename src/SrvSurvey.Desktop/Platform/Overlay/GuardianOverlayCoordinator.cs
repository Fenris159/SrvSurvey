using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GuardianOverlayCoordinator : IDisposable
{
    private readonly GuardianViewModel guardian;
    private readonly GuardianOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly DispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private GuardianOverlayWindow? liveSiteWindow;
    private GuardianSystemOverlayWindow? systemSummaryWindow;
    private RamTahOverlayWindow? ramTahWindow;
    private bool isSuppressed;
    private bool isObscured;
    private bool disposed;

    public GuardianOverlayCoordinator(
        GuardianViewModel guardian,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null)
    {
        this.guardian = guardian
            ?? throw new ArgumentNullException(nameof(guardian));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        viewModel = new GuardianOverlayViewModel(
            guardian,
            platform.Capabilities);
        this.guardian.PropertyChanged += OnGuardianPropertyChanged;
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindows();
    }

    public bool IsVisible => IsLiveSiteVisible
        || IsSystemSummaryVisible
        || IsRamTahVisible;

    public bool IsLiveSiteVisible => liveSiteWindow is not null;

    public bool IsSystemSummaryVisible => systemSummaryWindow is not null;

    public bool IsRamTahVisible => ramTahWindow is not null;

    public event EventHandler? VisibilityChanged;

    public string PlatformStatus => platform.Capabilities.StatusText;

    public bool IsSuppressed => isSuppressed;

    public void ToggleVisibility()
    {
        if (disposed)
        {
            return;
        }

        isSuppressed = !isSuppressed;
        SynchronizeWindows();
    }

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeWindows();
    }

    public void SetObscured(bool value)
    {
        if (disposed || value == isObscured)
        {
            return;
        }

        isObscured = value;
        SynchronizeWindows();
    }

    public void SetSystemSummaryObscured(bool value)
    {
        if (!disposed)
        {
            guardian.SetSystemSummaryObscured(value);
        }
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
        guardian.PropertyChanged -= OnGuardianPropertyChanged;
        CloseLiveSiteWindow();
        CloseSystemSummaryWindow();
        CloseRamTahWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindows();
    }

    private void OnGuardianPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(GuardianViewModel.HasActiveSite)
            or nameof(GuardianViewModel.EnableGuardianSites)
            or nameof(GuardianViewModel.ShouldShowGuardianSystemSummary)
            or nameof(GuardianViewModel.ShouldShowRamTahOverlay))
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

        SynchronizeLiveSiteWindow(
            platformReady
            && guardian.EnableGuardianSites
            && guardian.HasActiveSite
            && !isObscured);
        SynchronizeSystemSummaryWindow(
            platformReady && guardian.ShouldShowGuardianSystemSummary);
        SynchronizeRamTahWindow(
            platformReady && guardian.ShouldShowRamTahOverlay);
    }

    private void SynchronizeLiveSiteWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseLiveSiteWindow();
            return;
        }

        if (liveSiteWindow is not null)
        {
            PositionLiveSite(liveSiteWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new GuardianOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotGuardians");
        overlay.Opened += (_, _) => PrepareWindow(overlay, PositionLiveSite);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(liveSiteWindow, overlay))
            {
                liveSiteWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        liveSiteWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeSystemSummaryWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseSystemSummaryWindow();
            return;
        }

        if (systemSummaryWindow is not null)
        {
            PositionSystemSummary(systemSummaryWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new GuardianSystemOverlayWindow(viewModel);
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            "PlotGuardianSystem");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionSystemSummary);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(systemSummaryWindow, overlay))
            {
                systemSummaryWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        systemSummaryWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeRamTahWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseRamTahWindow();
            return;
        }

        if (ramTahWindow is not null)
        {
            PositionRamTah(ramTahWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new RamTahOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotRamTah");
        overlay.Opened += (_, _) => PrepareWindow(overlay, PositionRamTah);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(ramTahWindow, overlay))
            {
                ramTahWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        ramTahWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareWindow(
        Window window,
        Action<Window, PixelRect> position)
    {
        position(window, gameWindow.ClientBounds);
        var preparation = platform.PreparePassiveWindow(window);
        viewModel.ApplyPreparation(preparation);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            CloseLiveSiteWindow();
            CloseSystemSummaryWindow();
            CloseRamTahWindow();
        }
    }

    private void PositionLiveSite(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotGuardians",
            OverlayWindowPlacement.BottomRight,
            margin: 20);
    }

    private void PositionSystemSummary(
        Window window,
        PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotGuardianSystem",
            PlaceGuardianSystem,
            margin: 0);
    }

    private void PositionRamTah(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotRamTah",
            OverlayWindowPlacement.MiddleRight,
            margin: 8);
    }

    private static PixelPoint PlaceGuardianSystem(
        PixelRect gameBounds,
        PixelSize overlaySize,
        int margin)
    {
        return new PixelPoint(gameBounds.X + 10, gameBounds.Y + 8);
    }

    private void PositionWindow(
        Window window,
        PixelRect gameBounds,
        string plotterName,
        Func<PixelRect, PixelSize, int, PixelPoint> placement,
        int margin)
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
            : window.MinHeight > 0
                ? window.MinHeight
                : window.Height;
        var height = (int)Math.Ceiling(logicalHeight * screen.Scaling);
        var size = new PixelSize(Math.Max(width, 1), Math.Max(height, 1));
        var position = overlayLayout.GetPosition(plotterName, gameBounds, size)
            ?? placement(gameBounds, size, margin);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseLiveSiteWindow()
    {
        var overlay = liveSiteWindow;
        if (overlay is null)
        {
            return;
        }

        liveSiteWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseSystemSummaryWindow()
    {
        var overlay = systemSummaryWindow;
        if (overlay is null)
        {
            return;
        }

        systemSummaryWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseRamTahWindow()
    {
        var overlay = ramTahWindow;
        if (overlay is null)
        {
            return;
        }

        ramTahWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
