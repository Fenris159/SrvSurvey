using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MineMapOverlayCoordinator : IDisposable
{
    private readonly MineMapViewModel mineMap;
    private readonly HostedOverlayWindow hostedWindow;
    private readonly HostedOverlayWindow referenceWindow;
    private readonly IOverlayPlatformService zoomPlatform;
    private readonly OverlayPresentationSession presentationSession;
    private readonly OverlayDispatcherTimer timer;
    private MineMapZoomOverlayWindow? zoomWindow;
    private bool zoomUnavailable;
    private bool suppressed;
    private bool disposed;

    public MineMapOverlayCoordinator(MineMapViewModel mineMap, OverlayPresentationSession presentationSession)
    {
        this.mineMap = mineMap ?? throw new ArgumentNullException(nameof(mineMap));
        ArgumentNullException.ThrowIfNull(presentationSession);
        this.presentationSession = presentationSession;
        zoomPlatform = presentationSession.CreatePlatformService();
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
        hostedWindow.Dispose();
        referenceWindow.Dispose();
        zoomPlatform.Dispose();
    }

    private void OnMineMapPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is null
                or nameof(MineMapViewModel.ShouldShowOverlay)
                or nameof(MineMapViewModel.ShouldShowMiningReference)
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
    }

    private void OnHostedWindowVisibilityChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeZoomWindow();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeZoomWindow();
    }

    private void SynchronizeZoomWindow()
    {
        if (disposed || !hostedWindow.IsVisible || hostedWindow.CurrentWindow is null || zoomUnavailable)
        {
            CloseZoomWindow();
            return;
        }

        if (zoomWindow is not null)
        {
            PositionZoomWindow(zoomWindow, hostedWindow.CurrentWindow);
            return;
        }

        var overlay = new MineMapZoomOverlayWindow(mineMap);
        OverlayThemeResources.Apply(overlay);
        presentationSession.ConfigureAuxiliaryWindow(overlay, "PlotMineMap");
        overlay.Opened += OnZoomWindowOpened;
        overlay.Closed += OnZoomWindowClosed;
        zoomWindow = overlay;
        overlay.Show();
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
}
