using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class GuardianOverlayCoordinator : IDisposable
{
    private const string GuardianPlotterName = "PlotGuardians";

    private readonly GuardianViewModel guardian;
    private readonly GuardianOverlayViewModel viewModel;
    private readonly OverlayPresentationSession presentationSession;
    private readonly IOverlayPlatformService zoomPlatform;
    private readonly HostedOverlayWindow liveSiteWindow;
    private readonly HostedOverlayWindow guardianStatusWindow;
    private readonly HostedOverlayWindow systemSummaryWindow;
    private readonly HostedOverlayWindow ramTahWindow;
    private GuardianZoomOverlayWindow? zoomWindow;
    private bool zoomOverlayUnavailable;
    private bool isSuppressed;
    private bool disposed;

    public GuardianOverlayCoordinator(GuardianViewModel guardian, OverlayPresentationSession presentationSession)
    {
        this.guardian = guardian ?? throw new ArgumentNullException(nameof(guardian));
        this.presentationSession = presentationSession ?? throw new ArgumentNullException(nameof(presentationSession));
        zoomPlatform = presentationSession.CreatePlatformService();
        liveSiteWindow = HostWindow(
            GuardianPlotterName,
            model => new GuardianOverlayWindow(model),
            (gameBounds, windowSize) => OverlayWindowPlacement.BottomRight(gameBounds, windowSize, 20),
            OnTick
        );
        guardianStatusWindow = HostWindow(
            "PlotGuardianStatus",
            model => new GuardianStatusOverlayWindow(model),
            (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize, 8)
        );
        systemSummaryWindow = HostWindow(
            "PlotGuardianSystem",
            model => new GuardianSystemOverlayWindow(model),
            (gameBounds, _) => new PixelPoint(gameBounds.X + 10, gameBounds.Y + 8)
        );
        ramTahWindow = HostWindow(
            "PlotRamTah",
            model => new RamTahOverlayWindow(model),
            (gameBounds, windowSize) => OverlayWindowPlacement.MiddleRight(gameBounds, windowSize, 8)
        );
        viewModel = new GuardianOverlayViewModel(guardian, liveSiteWindow.Capabilities);
        liveSiteWindow.VisibilityChanged += OnLiveSiteVisibilityChanged;
        guardianStatusWindow.VisibilityChanged += OnHostedVisibilityChanged;
        systemSummaryWindow.VisibilityChanged += OnHostedVisibilityChanged;
        ramTahWindow.VisibilityChanged += OnHostedVisibilityChanged;
        this.guardian.PropertyChanged += OnGuardianPropertyChanged;
        SynchronizeIntent();
    }

    public bool IsVisible => IsLiveSiteVisible || IsGuardianStatusVisible || IsSystemSummaryVisible || IsRamTahVisible;

    public bool IsLiveSiteVisible => liveSiteWindow.IsVisible;

    public bool IsGuardianStatusVisible => guardianStatusWindow.IsVisible;

    public bool IsSystemSummaryVisible => systemSummaryWindow.IsVisible;

    public bool IsRamTahVisible => ramTahWindow.IsVisible;

    public event EventHandler? VisibilityChanged;

    public string PlatformStatus => liveSiteWindow.Capabilities.StatusText;

    public bool IsSuppressed => isSuppressed;

    public void ToggleVisibility()
    {
        if (disposed)
        {
            return;
        }

        isSuppressed = !isSuppressed;
        SynchronizeIntent();
    }

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeIntent();
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
        guardian.PropertyChanged -= OnGuardianPropertyChanged;
        liveSiteWindow.VisibilityChanged -= OnLiveSiteVisibilityChanged;
        guardianStatusWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        systemSummaryWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        ramTahWindow.VisibilityChanged -= OnHostedVisibilityChanged;
        CloseZoomWindow();
        liveSiteWindow.Dispose();
        guardianStatusWindow.Dispose();
        systemSummaryWindow.Dispose();
        ramTahWindow.Dispose();
        zoomPlatform.Dispose();
    }

    private HostedOverlayWindow HostWindow(
        string plotterName,
        Func<GuardianOverlayViewModel, Window> createWindow,
        Func<PixelRect, PixelSize, PixelPoint> fallbackPlacement,
        Action? tick = null
    )
    {
        return presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                plotterName,
                _ => createWindow(viewModel),
                fallbackPlacement,
                preparation => viewModel.ApplyPreparation(preparation)
            )
            {
                Tick = tick,
            }
        );
    }

    private void OnTick()
    {
        guardian.UpdateOverlayAnimation(DateTimeOffset.UtcNow);
        SynchronizeIntent();
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
            SynchronizeIntent();
        }
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        OverlayWindowRegistry registry = presentationSession.WindowRegistry;
        liveSiteWindow.Reconcile(!isSuppressed && guardian.ShouldShowLiveSiteOverlay);
        SynchronizeZoomWindow();
        guardianStatusWindow.Reconcile(
            !isSuppressed && guardian.ShouldShowGuardianStatusOverlay && registry.ShouldHost("PlotGuardianStatus")
        );
        systemSummaryWindow.Reconcile(
            !isSuppressed && guardian.ShouldShowGuardianSystemSummary && registry.ShouldHost("PlotGuardianSystem")
        );
        ramTahWindow.Reconcile(!isSuppressed && guardian.ShouldShowRamTahOverlay);
    }

    private void OnLiveSiteVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeZoomWindow();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHostedVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeZoomWindow()
    {
        if (disposed || !liveSiteWindow.IsVisible || zoomOverlayUnavailable)
        {
            CloseZoomWindow();
            return;
        }

        if (zoomWindow is not null)
        {
            PositionZoomWindow(zoomWindow);
            return;
        }

        var overlay = new GuardianZoomOverlayWindow(new GuardianZoomOverlayViewModel(zoomIn => AdjustZoom(zoomIn)));
        OverlayThemeResources.Apply(overlay);
        presentationSession.ConfigureAuxiliaryWindow(overlay, GuardianPlotterName);
        overlay.Opened += OnZoomWindowOpened;
        overlay.Closed += OnZoomWindowClosed;
        zoomWindow = overlay;
        overlay.Show();
    }

    private void OnZoomWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not GuardianZoomOverlayWindow opened || !ReferenceEquals(zoomWindow, opened))
        {
            return;
        }

        PositionZoomWindow(opened);
        OverlayPreparationResult preparation = zoomPlatform.PreparePassiveWindow(opened);
        if (!preparation.IsClickThrough)
        {
            zoomOverlayUnavailable = true;
            CloseZoomWindow();
            return;
        }

        OverlayInteractionResult interaction = zoomPlatform.SetInteractive(opened, interactive: true);
        if (!interaction.IsPrepared || !interaction.IsInteractive)
        {
            zoomOverlayUnavailable = true;
            CloseZoomWindow();
        }
    }

    private void OnZoomWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is GuardianZoomOverlayWindow closed && ReferenceEquals(zoomWindow, closed))
        {
            zoomWindow = null;
        }
    }

    private void PositionZoomWindow(Window window)
    {
        PixelRect gameBounds = liveSiteWindow.GameWindow.ClientBounds;
        Window? siteWindow = liveSiteWindow.CurrentWindow;
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
            presentationSession.OverlayLayout,
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

    private void CloseZoomWindow()
    {
        GuardianZoomOverlayWindow? closing = zoomWindow;
        if (closing is null)
        {
            return;
        }

        zoomWindow = null;
        closing.Opened -= OnZoomWindowOpened;
        closing.Closed -= OnZoomWindowClosed;
        closing.Close();
    }
}
