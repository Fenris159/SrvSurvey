using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class CombatOverlayCoordinator : IDisposable
{
    private readonly CombatViewModel combat;
    private readonly CombatOverlayViewModel viewModel;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly OverlayDispatcherTimer timer;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private FootCombatOverlayWindow? footCombatWindow;
    private MassacreMissionsOverlayWindow? massacreWindow;
    private bool isSuppressed;
    private bool disposed;

    public CombatOverlayCoordinator(
        CombatViewModel combat,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        LegacyOverlayLayout? overlayLayout = null)
    {
        this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.overlayLayout = overlayLayout ?? LegacyOverlayLayout.Empty;
        viewModel = new CombatOverlayViewModel(combat, platform.Capabilities);
        combat.PropertyChanged += OnCombatPropertyChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        SynchronizeWindows();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => footCombatWindow is not null
        || massacreWindow is not null;

    public bool IsFootCombatVisible => footCombatWindow is not null;

    public bool IsMassacreVisible => massacreWindow is not null;

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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
        combat.PropertyChanged -= OnCombatPropertyChanged;
        CloseFootCombatWindow();
        CloseMassacreWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        SynchronizeWindows();
    }

    private void OnCombatPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(CombatViewModel.ShouldShowFootCombat)
            or nameof(CombatViewModel.ShouldShowMassacreMissions))
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

        SynchronizeFootCombatWindow(
            platformReady && combat.ShouldShowFootCombat);
        SynchronizeMassacreWindow(
            platformReady && combat.ShouldShowMassacreMissions);
    }

    private void SynchronizeFootCombatWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseFootCombatWindow();
            return;
        }

        if (footCombatWindow is not null)
        {
            PositionTopLeft(footCombatWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new FootCombatOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotFootCombat");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopLeft);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(footCombatWindow, overlay))
            {
                footCombatWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        footCombatWindow = overlay;
        overlay.Show();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeMassacreWindow(bool shouldShow)
    {
        if (!shouldShow)
        {
            CloseMassacreWindow();
            return;
        }

        if (massacreWindow is not null)
        {
            PositionTopRight(massacreWindow, gameWindow.ClientBounds);
            return;
        }

        var overlay = new MassacreMissionsOverlayWindow(viewModel);
        OverlayThemeResources.Apply(overlay, overlayLayout, "PlotMassacre");
        overlay.Opened += (_, _) => PrepareWindow(
            overlay,
            PositionTopRight);
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(massacreWindow, overlay))
            {
                massacreWindow = null;
                VisibilityChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        massacreWindow = overlay;
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
            CloseFootCombatWindow();
            CloseMassacreWindow();
        }
    }

    private void PositionTopLeft(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotFootCombat",
            OverlayWindowPlacement.TopLeft);
    }

    private void PositionTopRight(Window window, PixelRect gameBounds)
    {
        PositionWindow(
            window,
            gameBounds,
            "PlotMassacre",
            OverlayWindowPlacement.TopRight);
    }

    private void PositionWindow(
        Window window,
        PixelRect gameBounds,
        string plotterName,
        Func<PixelRect, PixelSize, int, PixelPoint> placement)
    {
        OverlayThemeResources.ApplyOpacity(window, overlayLayout, plotterName);
        var screen = window.Screens.ScreenFromBounds(gameBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            window,
            overlayLayout,
            plotterName,
            screen.Scaling);
        var position = overlayLayout.GetPosition(plotterName, gameBounds, size)
            ?? placement(gameBounds, size, 8);
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    private void CloseFootCombatWindow()
    {
        var overlay = footCombatWindow;
        if (overlay is null)
        {
            return;
        }

        footCombatWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseMassacreWindow()
    {
        var overlay = massacreWindow;
        if (overlay is null)
        {
            return;
        }

        massacreWindow = null;
        overlay.Close();
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
