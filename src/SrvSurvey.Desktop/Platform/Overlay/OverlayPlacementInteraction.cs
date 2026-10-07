using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns one live overlay placement edit: panel drags, the working placements they produce, and their save or restore.</summary>
internal sealed class OverlayPlacementInteraction
{
    private readonly IOverlayPlatformService platform;
    private readonly LegacyOverlayLayoutStore layoutStore;
    private readonly LegacyOverlayLayout activeLayout;
    private readonly OverlayWindowRegistry registry;
    private readonly PixelRect hostBounds;
    private readonly Func<OverlayBehaviorViewModel?> behavior;
    private readonly Dictionary<Window, AttachedPanel> panels = [];
    private OverlayPositionEditSession session;

    /// <summary>Starts from the active layout; every move is measured against the game bounds captured for this edit.</summary>
    internal OverlayPlacementInteraction(
        IOverlayPlatformService platform,
        LegacyOverlayLayoutStore layoutStore,
        LegacyOverlayLayout activeLayout,
        OverlayWindowRegistry registry,
        PixelRect hostBounds,
        Func<OverlayBehaviorViewModel?> behavior
    )
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.layoutStore = layoutStore ?? throw new ArgumentNullException(nameof(layoutStore));
        this.activeLayout = activeLayout ?? throw new ArgumentNullException(nameof(activeLayout));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        this.hostBounds = hostBounds;
        session = new OverlayPositionEditSession(activeLayout);
    }

    /// <summary>Raised after a move of an attached panel changed its working placement and the active layout.</summary>
    internal event EventHandler<OverlayPlacementMovedEventArgs>? PlacementMoved;

    /// <summary>Raised after an attached panel closed and was detached; a drag move still pending at close is discarded.</summary>
    internal event EventHandler<OverlayPanelClosedEventArgs>? PanelClosed;

    internal IReadOnlyCollection<Window> Panels => panels.Keys;

    internal IReadOnlyDictionary<string, LegacyOverlayPlacement> Changes => session.Changes;

    /// <summary>Lets a placement-owning panel be dragged; related windows that do not own the placement are ignored.</summary>
    internal bool Attach(RegisteredOverlayWindow registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        Window window = registered.Window;
        if (!registered.ParticipatesInPlacement || panels.ContainsKey(window))
        {
            return false;
        }

        EventHandler<PointerPressedEventArgs> pointerPressed = (_, eventArgs) => OnPointerPressed(window, eventArgs);
        EventHandler<PixelPointEventArgs> positionChanged = (_, eventArgs) =>
            OnPositionChanged(registered, eventArgs.Point);
        EventHandler closed = (_, _) => OnClosed(window);
        panels.Add(window, new AttachedPanel(pointerPressed, positionChanged, closed));
        OverlayDragPolicy.SetOptionsFactory(
            window,
            () =>
                OverlayDragPolicy.CreateOptions(
                    window,
                    behavior(),
                    hostBounds,
                    OverlayWindowMetrics.GetPixelSize(registered)
                )
        );
        window.AddHandler(InputElement.PointerPressedEvent, pointerPressed, RoutingStrategies.Bubble, true);
        window.PositionChanged += positionChanged;
        window.Closed += closed;
        return true;
    }

    /// <summary>Stops dragging and tracking a panel, applying its last accepted drag move first.</summary>
    internal void Detach(Window window)
    {
        if (!panels.Remove(window, out AttachedPanel? panel))
        {
            return;
        }

        window.RemoveHandler(InputElement.PointerPressedEvent, panel.PointerPressed);
        ManagedOverlayWindowDragSession.Cancel(window);
        OverlayDragPolicy.SetOptionsFactory(window, null);
        window.PositionChanged -= panel.PositionChanged;
        window.Closed -= panel.Closed;
    }

    /// <summary>Ends in-progress managed drags so their last accepted move is part of <see cref="Changes"/>.</summary>
    internal void CompleteDrags()
    {
        foreach (Window window in panels.Keys.ToArray())
        {
            ManagedOverlayWindowDragSession.Cancel(window);
        }
    }

    /// <summary>Adopts a placement edited elsewhere and moves the live panel that owns it.</summary>
    internal void SetPlacement(string plotterName, LegacyOverlayPlacement placement)
    {
        session.SetPlacement(plotterName, placement);
        activeLayout.SetPlacement(plotterName, placement);
        RegisteredOverlayWindow? registered = registry
            .Snapshot()
            .FirstOrDefault(candidate =>
                candidate.ParticipatesInPlacement
                && string.Equals(candidate.PlotterName, plotterName, StringComparison.Ordinal)
            );
        if (registered is null)
        {
            return;
        }

        PixelPoint? runtimePosition = activeLayout.GetPosition(
            plotterName,
            hostBounds,
            OverlayWindowMetrics.GetPixelSize(registered)
        );
        if (runtimePosition is { } position)
        {
            registered.Window.Position = position;
        }
    }

    /// <summary>Returns moved panels to their original placements in the active layout and reports what was restored.</summary>
    internal IReadOnlyDictionary<string, LegacyOverlayPlacement> Cancel()
    {
        var restored = session.Changes.Keys.ToDictionary(
            plotterName => plotterName,
            session.GetOriginalPlacement,
            StringComparer.Ordinal
        );
        foreach ((string plotterName, LegacyOverlayPlacement original) in restored)
        {
            activeLayout.SetPlacement(plotterName, original);
        }

        session = new OverlayPositionEditSession(activeLayout);
        return restored;
    }

    /// <summary>Persists the working placements, adopts the reloaded layout, and continues the edit from it.</summary>
    internal LegacyOverlayLayoutSaveResult Save()
    {
        LegacyOverlayLayoutSaveResult result = layoutStore.Save(session.Changes);
        LegacyOverlayLayout updated = layoutStore.Load();
        if (updated.Error is not null)
        {
            throw new InvalidDataException(updated.Error);
        }

        activeLayout.ReplaceWith(updated);
        session = new OverlayPositionEditSession(activeLayout);
        return result;
    }

    private void OnPointerPressed(Window window, PointerPressedEventArgs eventArgs)
    {
        if (!ManagedOverlayWindowDragSession.CanBeginFrom(window, eventArgs))
        {
            return;
        }

        if (platform.Capabilities.UsesManagedDragForMonitorLock && behavior()?.LockToMonitor == true)
        {
            ManagedOverlayWindowDragSession.Begin(window, eventArgs);
        }
        else
        {
            platform.BeginMoveDrag(window, eventArgs);
        }
        eventArgs.Handled = true;
    }

    private void OnPositionChanged(RegisteredOverlayWindow registered, PixelPoint position)
    {
        string plotterName = registered.PlotterName;
        if (!session.Move(plotterName, position, OverlayWindowMetrics.GetPixelSize(registered), hostBounds))
        {
            return;
        }

        LegacyOverlayPlacement placement = session.GetPlacement(plotterName);
        activeLayout.SetPlacement(plotterName, placement);
        PlacementMoved?.Invoke(this, new OverlayPlacementMovedEventArgs(plotterName, placement));
    }

    private void OnClosed(Window window)
    {
        ManagedOverlayWindowDragSession.Cancel(window, applyPendingMove: false);
        Detach(window);
        PanelClosed?.Invoke(this, new OverlayPanelClosedEventArgs(window));
    }

    private sealed record AttachedPanel(
        EventHandler<PointerPressedEventArgs> PointerPressed,
        EventHandler<PixelPointEventArgs> PositionChanged,
        EventHandler Closed
    );
}

internal sealed class OverlayPlacementMovedEventArgs(string plotterName, LegacyOverlayPlacement placement) : EventArgs
{
    internal string PlotterName { get; } = plotterName;

    internal LegacyOverlayPlacement Placement { get; } = placement;
}

internal sealed class OverlayPanelClosedEventArgs(Window window) : EventArgs
{
    internal Window Window { get; } = window;
}
