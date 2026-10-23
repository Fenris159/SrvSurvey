using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Owns one live overlay placement edit: panel drags, the working placements they produce, and their save or restore.</summary>
internal sealed class OverlayPlacementInteraction
{
    private readonly LegacyOverlayLayoutStore layoutStore;
    private readonly LegacyOverlayLayout activeLayout;
    private readonly OverlayWindowRegistry registry;
    private readonly PixelRect hostBounds;
    private readonly Func<PixelRect> currentHostBounds;
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
        Func<OverlayBehaviorViewModel?> behavior,
        Func<PixelRect>? currentHostBounds = null
    )
    {
        ArgumentNullException.ThrowIfNull(platform);
        this.layoutStore = layoutStore ?? throw new ArgumentNullException(nameof(layoutStore));
        this.activeLayout = activeLayout ?? throw new ArgumentNullException(nameof(activeLayout));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
        this.hostBounds = hostBounds;
        this.currentHostBounds = currentHostBounds ?? (() => hostBounds);
        session = new OverlayPositionEditSession(activeLayout);
    }

    /// <summary>Raised after a move of an attached panel changed its working placement and the active layout.</summary>
    internal event EventHandler<OverlayPlacementMovedEventArgs>? PlacementMoved;

    /// <summary>Raised after an attached panel closed and was detached; a drag move still pending at close is discarded.</summary>
    internal event EventHandler<OverlayPanelClosedEventArgs>? PanelClosed;

    internal IReadOnlyCollection<Window> Panels => panels.Keys;

    internal IReadOnlyDictionary<string, LegacyOverlayPlacement> Changes => session.Changes;

    /// <summary>Retains a failed save's immutable baseline and working values until the caller retries or discards them.</summary>
    internal OverlayPositionEditSession EditSession => session;

    /// <summary>Lets a placement-owning panel be dragged; related windows that do not own the placement are ignored.</summary>
    internal bool Attach(RegisteredOverlayWindow registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        Window window = registered.Window;
        if (!registered.ParticipatesInPlacement || panels.ContainsKey(window))
        {
            return false;
        }

        EventHandler<PointerPressedEventArgs> pointerPressed = (_, eventArgs) =>
            OnPointerPressed(registered, eventArgs);
        EventHandler closed = (_, _) => OnClosed(window);
        panels.Add(window, new AttachedPanel(registered, pointerPressed, closed));
        if (panels.Count == 1)
        {
            registry.UserPositionChanged += OnUserPositionChanged;
        }
        OverlayDragPolicy.SetOptionsFactory(
            window,
            () =>
                OverlayDragPolicy.CreateOptions(
                    window,
                    behavior(),
                    GetHostBounds(),
                    OverlayWindowMetrics.GetPixelSize(registered)
                )
        );
        window.AddHandler(InputElement.PointerPressedEvent, pointerPressed, RoutingStrategies.Bubble, true);
        window.Closed += closed;
        return true;
    }

    /// <summary>Stops dragging and tracking a panel, applying its last accepted drag move first.</summary>
    internal void Detach(Window window)
    {
        if (!panels.TryGetValue(window, out AttachedPanel? panel))
        {
            return;
        }

        window.RemoveHandler(InputElement.PointerPressedEvent, panel.PointerPressed);
        ManagedOverlayWindowDragSession.Cancel(window);
        panels.Remove(window);
        if (panels.Count == 0)
        {
            registry.UserPositionChanged -= OnUserPositionChanged;
        }
        OverlayDragPolicy.SetOptionsFactory(window, null);
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

    /// <summary>Ends pending drags, returns moved panels to their original placements, and reports what was restored.</summary>
    internal IReadOnlyDictionary<string, LegacyOverlayPlacement> Cancel()
    {
        CompleteDrags();
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

    /// <summary>Ends pending drags, persists the working placements, and continues the edit from the reloaded layout.</summary>
    internal LegacyOverlayLayoutSaveResult Save()
    {
        CompleteDrags();
        LegacyOverlayLayoutSaveResult result = layoutStore.SaveChanges(session);
        LegacyOverlayLayout updated = result.CommittedLayout ?? layoutStore.Load();
        if (updated.Error is not null)
        {
            throw new InvalidDataException(updated.Error);
        }

        activeLayout.ReplaceWith(updated);
        session = new OverlayPositionEditSession(activeLayout);
        return result;
    }

    /// <summary>Records requested user drag positions, never asynchronous acknowledgements of host placement.</summary>
    private void OnPointerPressed(RegisteredOverlayWindow registered, PointerPressedEventArgs eventArgs)
    {
        Window window = registered.Window;
        if (!ManagedOverlayWindowDragSession.CanBeginFrom(window, eventArgs))
        {
            return;
        }

        ManagedOverlayWindowDragSession.Begin(
            window,
            eventArgs,
            position => registry.ReportUserPosition(window, position)
        );
        eventArgs.Handled = true;
    }

    /// <summary>Records accepted gestures for this session's placement-owning panels.</summary>
    private void OnUserPositionChanged(Window window, PixelPoint position)
    {
        if (!panels.TryGetValue(window, out AttachedPanel? panel))
        {
            return;
        }
        RegisteredOverlayWindow registered = panel.Registration;
        string name = registered.PlotterName;
        PixelSize size = OverlayWindowMetrics.GetPixelSize(registered);
        PixelRect bounds = GetHostBounds();
        PixelRect monitor = OverlayContentPlacement.GetMonitorBounds(window, position, size, bounds);
        if (session.Move(name, position, size, bounds, monitor))
        {
            LegacyOverlayPlacement placement = session.GetPlacement(name);
            activeLayout.SetPlacement(name, placement);
            PlacementMoved?.Invoke(this, new OverlayPlacementMovedEventArgs(name, placement));
        }
    }

    /// <summary>Uses the current tracked frame when available, retaining the opening frame during temporary game loss.</summary>
    private PixelRect GetHostBounds() =>
        currentHostBounds() is { Width: > 0, Height: > 0 } bounds ? bounds : hostBounds;

    private void OnClosed(Window window)
    {
        ManagedOverlayWindowDragSession.Cancel(window, applyPendingMove: false);
        Detach(window);
        PanelClosed?.Invoke(this, new OverlayPanelClosedEventArgs(window));
    }

    private sealed record AttachedPanel(
        RegisteredOverlayWindow Registration,
        EventHandler<PointerPressedEventArgs> PointerPressed,
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
