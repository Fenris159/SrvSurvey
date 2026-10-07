using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class MultiGameCommanderOverlayCoordinator : IDisposable
{
    private static readonly TimeSpan InventoryInterval = TimeSpan.FromSeconds(5);
    private readonly CommanderInstancesViewModel commanderInstances;
    private readonly OverlayBehaviorViewModel overlayBehavior;
    private readonly Func<bool> isApplicationActive;
    private readonly TimeProvider timeProvider;
    private readonly HostedOverlayWindow hostedWindow;
    private DateTimeOffset nextInventoryRefresh;
    private bool isSuppressed;
    private bool disposed;

    public MultiGameCommanderOverlayCoordinator(
        CommanderInstancesViewModel commanderInstances,
        OverlayBehaviorViewModel overlayBehavior,
        OverlayPresentationSession presentationSession,
        Func<bool> isApplicationActive,
        TimeProvider? timeProvider = null
    )
    {
        this.commanderInstances = commanderInstances ?? throw new ArgumentNullException(nameof(commanderInstances));
        this.overlayBehavior = overlayBehavior ?? throw new ArgumentNullException(nameof(overlayBehavior));
        ArgumentNullException.ThrowIfNull(presentationSession);
        this.isApplicationActive = isApplicationActive ?? throw new ArgumentNullException(nameof(isApplicationActive));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        hostedWindow = presentationSession.HostPassiveWindow(
            new PassiveOverlayWindowDefinition(
                "PlotMultiGameCommander",
                _ => new MultiGameCommanderOverlayWindow(commanderInstances),
                (gameBounds, windowSize) => OverlayWindowPlacement.TopCenter(gameBounds, windowSize)
            )
            {
                Tick = OnTick,
                ConfigureWindow = window => window.Opacity = 0.82,
                Placement = PlaceAboveGame,
                IsGameWindowEligible = snapshot =>
                    snapshot.IsAvailable && snapshot.IsVisible && (snapshot.IsForeground || this.isApplicationActive()),
            }
        );
        commanderInstances.PropertyChanged += OnStateChanged;
        overlayBehavior.PropertyChanged += OnStateChanged;
        RefreshInventory();
        SynchronizeIntent();
    }

    public bool IsVisible => hostedWindow.IsVisible;

    public void SetSuppressed(bool value)
    {
        if (disposed || value == isSuppressed)
        {
            return;
        }

        isSuppressed = value;
        SynchronizeIntent();
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
        commanderInstances.PropertyChanged -= OnStateChanged;
        overlayBehavior.PropertyChanged -= OnStateChanged;
        hostedWindow.Dispose();
    }

    private void OnTick()
    {
        if (timeProvider.GetUtcNow() >= nextInventoryRefresh)
        {
            RefreshInventory();
        }

        SynchronizeIntent();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName
            is nameof(CommanderInstancesViewModel.HasMultipleGameWindows)
                or nameof(CommanderInstancesViewModel.MultiGameOverlayLabel)
                or nameof(OverlayBehaviorViewModel.HideMultiGameCommanderOverlay)
        )
        {
            SynchronizeIntent();
        }
    }

    private void RefreshInventory()
    {
        commanderInstances.RefreshGameWindowCount();
        nextInventoryRefresh = timeProvider.GetUtcNow() + InventoryInterval;
    }

    private void SynchronizeIntent()
    {
        if (disposed)
        {
            return;
        }

        OverlayPlatformCapabilities capabilities = hostedWindow.Capabilities;
        hostedWindow.Reconcile(
            ShouldShow(
                new MultiGameOverlayVisibilityContext
                {
                    HasMultipleGameWindows = commanderInstances.HasMultipleGameWindows,
                    HideByPreference = overlayBehavior.HideMultiGameCommanderOverlay,
                    IsSuppressed = isSuppressed,
                    SupportsPassiveOverlay = capabilities.SupportsPassiveOverlay,
                    SupportsClickThrough = capabilities.SupportsClickThrough,
                    SupportsGameWindowTracking = capabilities.SupportsGameWindowTracking,
                    GameWindow = hostedWindow.GameWindow,
                    IsApplicationActive = isApplicationActive(),
                }
            )
        );
    }

    private static PixelPoint PlaceAboveGame(HostedOverlayPlacement placement)
    {
        PixelSize size = Measure(placement.Window, placement.Screen);
        PixelPoint? position = placement.TryGetLayoutPosition(size);
        if (position is not null)
        {
            return position.Value;
        }

        PixelRect gameBounds = placement.GameBounds;
        int x = gameBounds.X + ((gameBounds.Width - size.Width) / 2);
        int aboveClient = gameBounds.Y - size.Height - 2;
        int y = aboveClient >= placement.Screen.WorkingArea.Y ? aboveClient : gameBounds.Y;
        return new PixelPoint(x, y);
    }

    private static PixelSize Measure(Window overlay, Screen screen)
    {
        double logicalWidth = overlay.Bounds.Width > 0 ? overlay.Bounds.Width : overlay.MinWidth;
        double logicalHeight = overlay.Bounds.Height > 0 ? overlay.Bounds.Height : 32;
        int width = Math.Max(1, (int)Math.Ceiling(logicalWidth * screen.Scaling));
        int height = Math.Max(1, (int)Math.Ceiling(logicalHeight * screen.Scaling));
        return new PixelSize(width, height);
    }
}

public sealed class MultiGameOverlayVisibilityContext
{
    public bool HasMultipleGameWindows { get; init; }

    public bool HideByPreference { get; init; }

    public bool IsSuppressed { get; init; }

    public bool SupportsPassiveOverlay { get; init; }

    public bool SupportsClickThrough { get; init; }

    public bool SupportsGameWindowTracking { get; init; }

    public required GameWindowSnapshot GameWindow { get; init; }

    public bool IsApplicationActive { get; init; }
}
