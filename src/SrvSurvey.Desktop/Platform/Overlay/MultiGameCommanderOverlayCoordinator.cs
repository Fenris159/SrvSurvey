using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MultiGameCommanderOverlayCoordinator : IDisposable
{
    private static readonly TimeSpan InventoryInterval = TimeSpan.FromSeconds(5);
    private readonly CommanderInstancesViewModel commanderInstances;
    private readonly OverlayBehaviorViewModel overlayBehavior;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly Func<bool> isApplicationActive;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly TimeProvider timeProvider;
    private readonly OverlayDispatcherTimer timer;
    private DateTimeOffset nextInventoryRefresh;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private MultiGameCommanderOverlayWindow? window;
    private bool isSuppressed;
    private bool disposed;

    public MultiGameCommanderOverlayCoordinator(
        CommanderInstancesViewModel commanderInstances,
        OverlayBehaviorViewModel overlayBehavior,
        IOverlayPlatformService platform,
        IGameWindowTracker gameWindowTracker,
        Func<bool> isApplicationActive,
        LegacyOverlayLayout overlayLayout,
        TimeProvider? timeProvider = null)
    {
        this.commanderInstances = commanderInstances
            ?? throw new ArgumentNullException(nameof(commanderInstances));
        this.overlayBehavior = overlayBehavior
            ?? throw new ArgumentNullException(nameof(overlayBehavior));
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.isApplicationActive = isApplicationActive
            ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.overlayLayout = overlayLayout
            ?? throw new ArgumentNullException(nameof(overlayLayout));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        commanderInstances.PropertyChanged += OnStateChanged;
        overlayBehavior.PropertyChanged += OnStateChanged;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
        RefreshInventory();
        SynchronizeWindow();
    }

    public bool IsVisible => window is not null;

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeWindow();
    }

    public static bool ShouldShow(MultiGameOverlayVisibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.GameWindow);
        return context.HasMultipleGameWindows
            && !context.HideByPreference
            && !context.IsSuppressed
            && context.SupportsPassiveOverlay
            && context.SupportsClickThrough
            && context.SupportsGameWindowTracking
            && context.GameWindow.IsAvailable
            && context.GameWindow.IsVisible
            && (context.GameWindow.IsForeground || context.IsApplicationActive);
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
        commanderInstances.PropertyChanged -= OnStateChanged;
        overlayBehavior.PropertyChanged -= OnStateChanged;
        CloseWindow();
        gameWindowTracker.Dispose();
        platform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        if (timeProvider.GetUtcNow() >= nextInventoryRefresh)
        {
            RefreshInventory();
        }

        SynchronizeWindow();
    }

    private void OnStateChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is
            nameof(CommanderInstancesViewModel.HasMultipleGameWindows)
            or nameof(CommanderInstancesViewModel.MultiGameOverlayLabel)
            or nameof(OverlayBehaviorViewModel.HideMultiGameCommanderOverlay))
        {
            SynchronizeWindow();
        }
    }

    private void RefreshInventory()
    {
        commanderInstances.RefreshGameWindowCount();
        nextInventoryRefresh = timeProvider.GetUtcNow() + InventoryInterval;
    }

    private void SynchronizeWindow()
    {
        if (disposed)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        var capabilities = platform.Capabilities;
        var shouldShow = ShouldShow(
            new MultiGameOverlayVisibilityContext(
                commanderInstances.HasMultipleGameWindows,
                overlayBehavior.HideMultiGameCommanderOverlay,
                isSuppressed,
                capabilities.SupportsPassiveOverlay,
                capabilities.SupportsClickThrough,
                capabilities.SupportsGameWindowTracking,
                gameWindow,
                isApplicationActive()));
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

        var overlay = new MultiGameCommanderOverlayWindow(commanderInstances)
        {
            Opacity = 0.82,
        };
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            "PlotMultiGameCommander");
        overlay.Opened += (_, _) => PrepareWindow(overlay);
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

    private void PrepareWindow(MultiGameCommanderOverlayWindow overlay)
    {
        PositionWindow(overlay);
        var preparation = platform.PreparePassiveWindow(overlay);
        if (!preparation.IsClickThrough)
        {
            isSuppressed = true;
            CloseWindow();
        }
    }

    private void PositionWindow(Window overlay)
    {
        OverlayThemeResources.ApplyOpacity(
            overlay,
            overlayLayout,
            "PlotMultiGameCommander");
        var screen = overlay.Screens.ScreenFromBounds(gameWindow.ClientBounds)
            ?? overlay.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var logicalWidth = overlay.Bounds.Width > 0
            ? overlay.Bounds.Width
            : overlay.MinWidth;
        var logicalHeight = overlay.Bounds.Height > 0
            ? overlay.Bounds.Height
            : 32;
        var width = Math.Max(
            1,
            (int)Math.Ceiling(logicalWidth * screen.Scaling));
        var height = Math.Max(
            1,
            (int)Math.Ceiling(logicalHeight * screen.Scaling));
        var size = new PixelSize(width, height);
        var position = overlayLayout.GetPosition(
            "PlotMultiGameCommander",
            gameWindow.ClientBounds,
            size);
        if (position is null)
        {
            var x = gameWindow.ClientBounds.X
                + ((gameWindow.ClientBounds.Width - width) / 2);
            var aboveClient = gameWindow.ClientBounds.Y - height - 2;
            var y = aboveClient >= screen.WorkingArea.Y
                ? aboveClient
                : gameWindow.ClientBounds.Y;
            position = new PixelPoint(x, y);
        }

        if (overlay.Position != position.Value)
        {
            overlay.Position = position.Value;
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
    }
}

public sealed record MultiGameOverlayVisibilityContext(
    bool HasMultipleGameWindows,
    bool HideByPreference,
    bool IsSuppressed,
    bool SupportsPassiveOverlay,
    bool SupportsClickThrough,
    bool SupportsGameWindowTracking,
    GameWindowSnapshot GameWindow,
    bool IsApplicationActive);
