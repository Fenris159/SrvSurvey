using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MineMapOverlayCoordinator : IDisposable
{
    private readonly MineMapViewModel mineMap;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly HostedOverlayWindow referenceWindow;
    private readonly IOverlayPlatformService zoomPlatform;
    private readonly IOverlayPlatformService alignmentPlatform;
    private readonly IGameWindowTracker alignmentGameWindowTracker;
    private readonly OverlayPresentationSession presentationSession;
    private readonly OverlayDispatcherTimer timer;
    private MineMapZoomOverlayWindow? zoomWindow;
    private SurfaceMiningAlignmentOverlayWindow? alignmentWindow;
    private bool zoomUnavailable;
    private bool alignmentUnavailable;
    private bool suppressed;
    private bool disposed;

    public MineMapOverlayCoordinator(MineMapViewModel mineMap, OverlayPresentationSession presentationSession)
    {
        this.mineMap = mineMap ?? throw new ArgumentNullException(nameof(mineMap));
        ArgumentNullException.ThrowIfNull(presentationSession);
        this.presentationSession = presentationSession;
        zoomPlatform = presentationSession.CreatePlatformService();
        alignmentPlatform = presentationSession.CreatePlatformService();
        alignmentGameWindowTracker = presentationSession.CreateGameWindowTracker();
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMineMap",
                _ => new MineMapOverlayWindow(mineMap),
                (gameBounds, windowSize) => OverlayWindowPlacement.MiddleLeft(gameBounds, windowSize)
            )
        );
        referenceWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMiningReference",
                _ => new MiningReferenceOverlayWindow(mineMap),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopRight(gameBounds, windowSize)
            )
        );
        hostedWindow.VisibilityChanged += OnHostedWindowVisibilityChanged;
        mineMap.PropertyChanged += OnMineMapPropertyChanged;
        timer = new OverlayDispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += OnTimerTick;
        timer.Start();
        Synchronize();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public void SetSuppressed(bool value)
    {
        if (disposed || suppressed == value)
        {
            return;
        }

        suppressed = value;
        Synchronize();
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
        mineMap.PropertyChanged -= OnMineMapPropertyChanged;
        hostedWindow.VisibilityChanged -= OnHostedWindowVisibilityChanged;
        CloseZoomWindow();
        CloseAlignmentWindow();
        hostedWindow.Dispose();
        referenceWindow.Dispose();
        zoomPlatform.Dispose();
        alignmentGameWindowTracker.Dispose();
        alignmentPlatform.Dispose();
    }

    private void OnMineMapPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is null
                or nameof(MineMapViewModel.ShouldShowOverlay)
                or nameof(MineMapViewModel.ShouldShowMiningReference)
                or nameof(MineMapViewModel.ShouldShowAlignmentHelper)
        )
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        if (disposed)
        {
            return;
        }

        hostedWindow.Reconcile(!suppressed && mineMap.ShouldShowOverlay);
        referenceWindow.Reconcile(!suppressed && mineMap.ShouldShowMiningReference);
        SynchronizeZoomWindow();
        SynchronizeAlignmentWindow();
    }

    private void OnHostedWindowVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeZoomWindow();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeZoomWindow();
        SynchronizeAlignmentWindow();
    }

    private void SynchronizeAlignmentWindow()
    {
        if (disposed || suppressed || !mineMap.ShouldShowAlignmentHelper || alignmentUnavailable)
        {
            CloseAlignmentWindow();
            return;
        }

        GameWindowSnapshot gameWindow = alignmentGameWindowTracker.GetSnapshot();
        if (
            !alignmentPlatform.Capabilities.SupportsPassiveOverlay
            || !alignmentPlatform.Capabilities.SupportsClickThrough
            || !alignmentPlatform.Capabilities.SupportsGameWindowTracking
            || !gameWindow.IsAvailable
            || !gameWindow.IsVisible
            || !gameWindow.IsForeground
        )
        {
            CloseAlignmentWindow();
            return;
        }

        if (alignmentWindow is not null)
        {
            PositionAlignmentWindow(alignmentWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new SurfaceMiningAlignmentOverlayWindow();
        presentationSession.ConfigureAuxiliaryWindow(overlay, "PlotMineMap", applyOpacity: false);
        overlay.Opened += OnAlignmentWindowOpened;
        overlay.Closed += OnAlignmentWindowClosed;
        alignmentWindow = overlay;
        PositionAlignmentWindow(overlay, gameWindow.ClientBounds);
        overlay.Show();
    }

    private void OnAlignmentWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not SurfaceMiningAlignmentOverlayWindow opened || !ReferenceEquals(alignmentWindow, opened))
        {
            return;
        }

        GameWindowSnapshot gameWindow = alignmentGameWindowTracker.GetSnapshot();
        if (gameWindow.IsAvailable)
        {
            PositionAlignmentWindow(opened, gameWindow.ClientBounds);
        }

        OverlayPreparationResult preparation = alignmentPlatform.PreparePassiveWindow(opened);
        if (!preparation.IsClickThrough)
        {
            alignmentUnavailable = true;
            CloseAlignmentWindow();
        }
    }

    private void OnAlignmentWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is SurfaceMiningAlignmentOverlayWindow closed && ReferenceEquals(alignmentWindow, closed))
        {
            alignmentWindow = null;
        }
    }

    private static void PositionAlignmentWindow(Window overlay, PixelRect gameBounds)
    {
        Screen? screen = overlay.Screens.ScreenFromBounds(gameBounds) ?? overlay.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        PixelRect bounds = GetAlignmentHelperBounds(gameBounds);
        overlay.Width = bounds.Width / screen.Scaling;
        overlay.Height = bounds.Height / screen.Scaling;
        if (overlay.Position != bounds.Position)
        {
            overlay.Position = bounds.Position;
        }
    }

    internal static PixelRect GetAlignmentHelperBounds(PixelRect gameBounds)
    {
        const int lineWidth = 4;
        const double heightRatio = 0.3;
        int lineHeight = Math.Max(1, (int)Math.Round(gameBounds.Height * heightRatio));
        return new PixelRect(
            gameBounds.X + (gameBounds.Width - lineWidth) / 2,
            gameBounds.Y + (gameBounds.Height - lineHeight) / 2,
            lineWidth,
            lineHeight
        );
    }

    private void SynchronizeZoomWindow()
    {
        if (disposed || !hostedWindow.IsVisible || hostedWindow.CurrentWindow is not { } mapWindow || zoomUnavailable)
        {
            CloseZoomWindow();
            return;
        }

        if (zoomWindow is not null)
        {
            PositionZoomWindow(zoomWindow, mapWindow);
            return;
        }

        var overlay = new MineMapZoomOverlayWindow(mineMap);
        OverlayThemeResources.Apply(overlay);
        presentationSession.ConfigureAuxiliaryWindow(overlay, "PlotMineMap");
        overlay.Opened += OnZoomWindowOpened;
        overlay.Closed += OnZoomWindowClosed;
        zoomWindow = overlay;
        overlay.Show(mapWindow);
    }

    private void OnZoomWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (
            sender is not MineMapZoomOverlayWindow opened
            || !ReferenceEquals(zoomWindow, opened)
            || hostedWindow.CurrentWindow is not { } mapWindow
        )
        {
            return;
        }

        PositionZoomWindow(opened, mapWindow);
        var preparation = zoomPlatform.PreparePassiveWindow(opened);
        var interaction = zoomPlatform.SetInteractive(opened, interactive: true);
        if (!preparation.IsClickThrough || !interaction.IsPrepared || !interaction.IsInteractive)
        {
            zoomUnavailable = true;
            CloseZoomWindow();
        }
    }

    private void OnZoomWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is MineMapZoomOverlayWindow closed && ReferenceEquals(zoomWindow, closed))
        {
            zoomWindow = null;
        }
    }

    private static void PositionZoomWindow(Window controls, Window mapWindow)
    {
        var screen = mapWindow.Screens.ScreenFromWindow(mapWindow) ?? mapWindow.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        const int inset = 12;
        var scale = screen.Scaling;
        var mapWidth = Math.Max(1, (int)Math.Ceiling(mapWindow.Bounds.Width * scale));
        var mapHeight = Math.Max(1, (int)Math.Ceiling(mapWindow.Bounds.Height * scale));
        var width = Math.Max(1, (int)Math.Ceiling(controls.Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(controls.Bounds.Height * scale));
        var position = new PixelPoint(
            mapWindow.Position.X + mapWidth - width - (int)Math.Ceiling(inset * scale),
            mapWindow.Position.Y + mapHeight - height - (int)Math.Ceiling(inset * scale)
        );
        if (controls.Position != position)
        {
            controls.Position = position;
        }
    }

    private void CloseZoomWindow()
    {
        var closing = zoomWindow;
        if (closing is null)
        {
            return;
        }

        zoomWindow = null;
        closing.Opened -= OnZoomWindowOpened;
        closing.Closed -= OnZoomWindowClosed;
        closing.Close();
    }

    private void CloseAlignmentWindow()
    {
        SurfaceMiningAlignmentOverlayWindow? closing = alignmentWindow;
        if (closing is null)
        {
            return;
        }

        alignmentWindow = null;
        closing.Opened -= OnAlignmentWindowOpened;
        closing.Closed -= OnAlignmentWindowClosed;
        closing.Close();
    }
}
