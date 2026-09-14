using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GuardianOverlayCoordinator : IDisposable
{
    private const string GuardianPlotterName = "PlotGuardians";

    private readonly GuardianViewModel guardian;
    private readonly GuardianOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayWindowRegistry windowRegistry;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private GuardianOverlayWindow? liveSiteWindow;
    private GuardianZoomOverlayWindow? zoomWindow;
    private bool zoomOverlayUnavailable;
    private GuardianStatusOverlayWindow? guardianStatusWindow;
    private GuardianSystemOverlayWindow? systemSummaryWindow;
    private RamTahOverlayWindow? ramTahWindow;
    private bool isSuppressed;
    private bool disposed;

    public GuardianOverlayCoordinator(
        GuardianViewModel guardian,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null,
        OverlayWindowRegistry? windowRegistry = null
    )
    {
        this.guardian = guardian ?? throw new ArgumentNullException(nameof(guardian));
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        this.windowRegistry = windowRegistry ?? OverlayWindowRegistry.Shared;
        viewModel = new GuardianOverlayViewModel(guardian, platform.Capabilities);
        this.guardian.PropertyChanged += OnGuardianPropertyChanged;
        timer = new OverlayDispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindows();
    }

    public bool IsVisible => IsLiveSiteVisible || IsGuardianStatusVisible || IsSystemSummaryVisible || IsRamTahVisible;

    public bool IsLiveSiteVisible => liveSiteWindow is not null;

    public bool IsGuardianStatusVisible => guardianStatusWindow is not null;

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

    public bool AdjustZoom(bool zoomIn)
    {
        return !disposed && IsLiveSiteVisible && guardian.AdjustMapZoom(zoomIn);
    }

    public bool ResetZoom()
    {
        if (disposed || !IsLiveSiteVisible)
        {
            return false;
        }

        guardian.EnableAutomaticMapZoom();
        return true;
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
        CloseGuardianStatusWindow();
        CloseSystemSummaryWindow();
        CloseRamTahWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        guardian.UpdateOverlayAnimation(DateTimeOffset.UtcNow);
        SynchronizeWindows();
    }

    private void OnGuardianPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(GuardianViewModel.HasActiveSite)
                or nameof(GuardianViewModel.EnableGuardianSites)
                or nameof(GuardianViewModel.ShouldShowLiveSiteOverlay)
                or nameof(GuardianViewModel.ShouldShowGuardianStatusOverlay)
                or nameof(GuardianViewModel.ShouldShowGuardianSystemSummary)
                or nameof(GuardianViewModel.ShouldShowRamTahOverlay)
        )
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
        bool platformReady =
            !isSuppressed
            && platform.Capabilities.SupportsPassiveOverlay
            && platform.Capabilities.SupportsClickThrough
            && platform.Capabilities.SupportsGameWindowTracking
            && gameWindow.IsAvailable
            && gameWindow.IsVisible
            && gameWindow.IsForeground;

        SynchronizeLiveSiteWindow(platformReady && guardian.ShouldShowLiveSiteOverlay);
        SynchronizeGuardianStatusWindow(
            platformReady && guardian.ShouldShowGuardianStatusOverlay && windowRegistry.ShouldHost("PlotGuardianStatus")
        );
        SynchronizeSystemSummaryWindow(
            platformReady && guardian.ShouldShowGuardianSystemSummary && windowRegistry.ShouldHost("PlotGuardianSystem")
        );
        SynchronizeRamTahWindow(platformReady && guardian.ShouldShowRamTahOverlay);
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
            SynchronizeZoomWindow(shouldShow: true);
            return;
        }

        var overlay = new GuardianOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, GuardianPlotterName, windowRegistry);
        overlay.Opened += (_, _) =>
        {
            PrepareWindow(overlay, PositionLiveSite);
            SynchronizeZoomWindow(shouldShow: true);
        };
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

    private void SynchronizeZoomWindow(bool shouldShow)
    {
        if (!shouldShow || liveSiteWindow is null || zoomOverlayUnavailable)
        {
            CloseZoomWindow();
            return;
        }

        if (zoomWindow is not null)
        {
            PositionZoomWindow(zoomWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new GuardianZoomOverlayWindow(new GuardianZoomOverlayViewModel(zoomIn => AdjustZoom(zoomIn)));
        OverlayThemeResources.Apply(overlay);
        OverlayThemeResources.ApplyOpacity(overlay, overlayLayout, GuardianPlotterName);
        windowRegistry.Register(overlay, GuardianPlotterName, participatesInPlacement: false);
        overlay.Opened += (_, _) => PrepareZoomWindow(overlay);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(zoomWindow, overlay))
            {
                zoomWindow = null;
            }
        };
        zoomWindow = overlay;
        overlay.Show();
    }

    private void PrepareZoomWindow(GuardianZoomOverlayWindow overlay)
    {
        PositionZoomWindow(overlay, gameWindow.ClientBounds);
        OverlayPreparationResult preparation = platform.PreparePassiveWindow(overlay);
        if (!preparation.IsClickThrough)
        {
            zoomOverlayUnavailable = true;
            CloseZoomWindow();
            return;
        }

        OverlayInteractionResult interaction = platform.SetInteractive(overlay, interactive: true);
        if (!interaction.IsPrepared || !interaction.IsInteractive)
        {
            zoomOverlayUnavailable = true;
            CloseZoomWindow();
        }
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
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotGuardianSystem", windowRegistry);
        overlay.Opened += (_, _) => PrepareWindow(overlay, PositionSystemSummary);
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

    private void SynchronizeGuardianStatusWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseGuardianStatusWindow();
            return;
        }

        if (guardianStatusWindow is not null)
        {
            PositionGuardianStatus(guardianStatusWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new GuardianStatusOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotGuardianStatus", windowRegistry);
        overlay.Opened += (_, _) => PrepareWindow(overlay, PositionGuardianStatus);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(guardianStatusWindow, overlay))
            {
                guardianStatusWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        guardianStatusWindow = overlay;
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
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotRamTah", windowRegistry);
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

    private void PrepareWindow(Window window, Action<Window, PixelRect> position)
    {
        position(window, gameWindow.ClientBounds);
        OverlayPreparationResult preparation = platform.PreparePassiveWindow(window);
        viewModel.ApplyPreparation(preparation);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            CloseLiveSiteWindow();
            CloseGuardianStatusWindow();
            CloseSystemSummaryWindow();
            CloseRamTahWindow();
        }
    }

    private void PositionLiveSite(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, GuardianPlotterName, OverlayWindowPlacement.BottomRight, margin: 20);
    }

    private void PositionZoomWindow(Window window, PixelRect gameBounds)
    {
        GuardianOverlayWindow? siteWindow = liveSiteWindow;
        Screen? screen =
            siteWindow?.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (siteWindow is null || screen is null)
        {
            return;
        }

        const int inset = 8;
        double scale = screen.Scaling;
        PixelSize siteSize = OverlayWindowMetrics.PrepareForPlacement(
            siteWindow,
            overlayLayout,
            GuardianPlotterName,
            scale
        );
        int width = Math.Max(1, (int)Math.Ceiling(window.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(window.Height * scale));
        var position = new PixelPoint(
            siteWindow.Position.X + siteSize.Width - width - (int)Math.Ceiling(inset * scale),
            siteWindow.Position.Y + siteSize.Height - height - (int)Math.Ceiling(inset * scale)
        );
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void PositionSystemSummary(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, "PlotGuardianSystem", PlaceGuardianSystem, margin: 0);
    }

    private void PositionGuardianStatus(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, "PlotGuardianStatus", OverlayWindowPlacement.TopCenter, margin: 8);
    }

    private void PositionRamTah(Window window, PixelRect gameBounds)
    {
        PositionWindow(window, gameBounds, "PlotRamTah", OverlayWindowPlacement.MiddleRight, margin: 8);
    }

    private static PixelPoint PlaceGuardianSystem(PixelRect gameBounds, PixelSize overlaySize, int margin)
    {
        return new PixelPoint(gameBounds.X + 10, gameBounds.Y + 8);
    }

    private void PositionWindow(
        Window window,
        PixelRect gameBounds,
        string plotterName,
        Func<PixelRect, PixelSize, int, PixelPoint> placement,
        int margin
    )
    {
        OverlayThemeResources.ApplyOpacity(window, overlayLayout, plotterName);
        Screen? screen = window.Screens.ScreenFromBounds(gameBounds) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        PixelSize size = OverlayWindowMetrics.PrepareForPlacement(window, overlayLayout, plotterName, screen.Scaling);
        PixelPoint position =
            overlayLayout.GetPosition(plotterName, gameBounds, size) ?? placement(gameBounds, size, margin);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseLiveSiteWindow()
    {
        CloseZoomWindow();
        GuardianOverlayWindow? overlay = liveSiteWindow;
        if (overlay is null)
        {
            return;
        }

        liveSiteWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseZoomWindow()
    {
        GuardianZoomOverlayWindow? overlay = zoomWindow;
        if (overlay is null)
        {
            return;
        }

        zoomWindow = null;
        overlay.Close();
    }

    private void CloseSystemSummaryWindow()
    {
        GuardianSystemOverlayWindow? overlay = systemSummaryWindow;
        if (overlay is null)
        {
            return;
        }

        systemSummaryWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseGuardianStatusWindow()
    {
        GuardianStatusOverlayWindow? overlay = guardianStatusWindow;
        if (overlay is null)
        {
            return;
        }

        guardianStatusWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseRamTahWindow()
    {
        RamTahOverlayWindow? overlay = ramTahWindow;
        if (overlay is null)
        {
            return;
        }

        ramTahWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
