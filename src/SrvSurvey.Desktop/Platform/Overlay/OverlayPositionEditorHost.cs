using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOverlayPositionEditorHost : IDisposable
{
    event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

    event EventHandler? Closed;

    bool Open(
        OverlayInteractionViewModel viewModel,
        OverlayPositionEditSession session,
        OverlayLayoutCategory category,
        PixelRect? preferredHostBounds);

    void ShowCategory(
        OverlayPositionEditSession session,
        OverlayLayoutCategory category);

    void Close(bool restoreRuntimeWindows = true);
}

public sealed record OverlayPreviewMovedEventArgs(
    string PlotterName,
    PixelPoint Position,
    PixelSize PreviewSize,
    PixelRect HostBounds);

public sealed class AvaloniaOverlayPositionEditorHost : IOverlayPositionEditorHost
{
    private readonly OverlayWindowRegistry registry;
    private readonly List<OverlayPositionPreviewWindow> previews = [];
    private readonly Dictionary<Window, RuntimeWindowState> runtimeWindows = [];
    private OverlayPositionEditorWindow? editor;
    private PixelRect hostBounds;
    private bool closing;
    private bool disposed;

    public AvaloniaOverlayPositionEditorHost(
        OverlayWindowRegistry? registry = null)
    {
        this.registry = registry ?? OverlayWindowRegistry.Shared;
    }

    public event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

    public event EventHandler? Closed;

    public bool Open(
        OverlayInteractionViewModel viewModel,
        OverlayPositionEditSession session,
        OverlayLayoutCategory category,
        PixelRect? preferredHostBounds)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(session);
        if (disposed || editor is not null)
        {
            return false;
        }

        var toolbar = new OverlayPositionEditorWindow(viewModel);
        toolbar.Closed += OnEditorClosed;
        editor = toolbar;
        toolbar.Show();

        var preferred = preferredHostBounds is { Width: > 0, Height: > 0 }
            ? preferredHostBounds.Value
            : (PixelRect?)null;
        var screen = preferred is { } gameBounds
            ? toolbar.Screens.ScreenFromBounds(gameBounds)
                ?? toolbar.Screens.Primary
            : toolbar.Screens.Primary;
        if (screen is null)
        {
            Close(restoreRuntimeWindows: true);
            return false;
        }

        hostBounds = preferred ?? screen.Bounds;
        var toolbarSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(toolbar.Width * screen.Scaling)),
            Math.Max(1, (int)Math.Ceiling(toolbar.Height * screen.Scaling)));
        toolbar.Position = OverlayWindowPlacement.TopCenter(
            hostBounds,
            toolbarSize,
            margin: 12);

        registry.Changed += OnRegistryChanged;
        SynchronizeRuntimeWindows();
        ShowCategory(session, category);
        toolbar.Activate();
        return true;
    }

    public void ShowCategory(
        OverlayPositionEditSession session,
        OverlayLayoutCategory category)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (disposed || editor is null || hostBounds.Width <= 0)
        {
            return;
        }

        ClosePreviews();
        foreach (var definition in OverlayLayoutCatalog.ForCategory(category))
        {
            var preview = new OverlayPositionPreviewWindow(definition);
            OverlayThemeResources.Apply(preview);
            var position = session.GetPosition(
                definition.Name,
                hostBounds,
                definition.PreviewSize);
            preview.Position = ClampToHost(
                position,
                definition.PreviewSize,
                hostBounds);
            preview.PointerPressed += OnPreviewPointerPressed;
            preview.PositionChanged += OnPreviewPositionChanged;
            previews.Add(preview);
            preview.Show();
        }

        editor.Activate();
    }

    public void Close(bool restoreRuntimeWindows = true)
    {
        if (closing)
        {
            return;
        }

        closing = true;
        registry.Changed -= OnRegistryChanged;
        ClosePreviews();
        var toolbar = editor;
        editor = null;
        if (toolbar is not null)
        {
            toolbar.Closed -= OnEditorClosed;
            toolbar.Close();
        }

        RestoreRuntimeWindows(restoreRuntimeWindows);
        closing = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close(restoreRuntimeWindows: false);
    }

    private static PixelPoint ClampToHost(
        PixelPoint position,
        PixelSize size,
        PixelRect bounds)
    {
        var maximumX = Math.Max(bounds.X, bounds.Right - size.Width);
        var maximumY = Math.Max(bounds.Y, bounds.Bottom - size.Height);
        return new PixelPoint(
            Math.Clamp(position.X, bounds.X, maximumX),
            Math.Clamp(position.Y, bounds.Y, maximumY));
    }

    private void OnPreviewPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (sender is not OverlayPositionPreviewWindow preview
            || !eventArgs.GetCurrentPoint(preview).Properties.IsLeftButtonPressed)
        {
            return;
        }

        preview.BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnPreviewPositionChanged(
        object? sender,
        PixelPointEventArgs eventArgs)
    {
        if (sender is not OverlayPositionPreviewWindow preview)
        {
            return;
        }

        PreviewMoved?.Invoke(
            this,
            new OverlayPreviewMovedEventArgs(
                preview.Definition.Name,
                eventArgs.Point,
                preview.Definition.PreviewSize,
                hostBounds));
    }

    private void ClosePreviews()
    {
        foreach (var preview in previews)
        {
            preview.PointerPressed -= OnPreviewPointerPressed;
            preview.PositionChanged -= OnPreviewPositionChanged;
            preview.Close();
        }

        previews.Clear();
    }

    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        if (closing)
        {
            return;
        }

        editor = null;
        registry.Changed -= OnRegistryChanged;
        ClosePreviews();
        RestoreRuntimeWindows(restore: true);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeRuntimeWindows();
    }

    private void SynchronizeRuntimeWindows()
    {
        if (editor is null)
        {
            return;
        }

        var snapshot = registry.Snapshot();
        var current = snapshot.Select(item => item.Window).ToHashSet();
        foreach (var stale in runtimeWindows.Keys
                     .Where(window => !current.Contains(window))
                     .ToArray())
        {
            DetachRuntimeWindow(stale);
        }

        foreach (var registered in snapshot)
        {
            if (runtimeWindows.ContainsKey(registered.Window))
            {
                if (registered.Window.IsVisible)
                {
                    SuppressRuntimeWindow(registered.Window);
                }

                continue;
            }

            EventHandler opened = (_, _) => Dispatcher.UIThread.Post(() =>
                SuppressRuntimeWindow(registered.Window));
            EventHandler closed = (_, _) => DetachRuntimeWindow(registered.Window);
            var state = new RuntimeWindowState(
                opened,
                closed,
                RestoreAfterEditing: registered.Window.IsVisible);
            runtimeWindows.Add(registered.Window, state);
            registered.Window.Opened += opened;
            registered.Window.Closed += closed;
            if (registered.Window.IsVisible)
            {
                registered.Window.Hide();
            }
        }
    }

    private void SuppressRuntimeWindow(Window window)
    {
        if (editor is null
            || !runtimeWindows.TryGetValue(window, out var state)
            || !window.IsVisible)
        {
            return;
        }

        state.RestoreAfterEditing = true;
        window.Hide();
    }

    private void DetachRuntimeWindow(Window window)
    {
        if (!runtimeWindows.Remove(window, out var state))
        {
            return;
        }

        window.Opened -= state.Opened;
        window.Closed -= state.Closed;
    }

    private void RestoreRuntimeWindows(bool restore)
    {
        var states = runtimeWindows.ToArray();
        foreach (var entry in states)
        {
            DetachRuntimeWindow(entry.Key);
        }

        if (!restore)
        {
            return;
        }

        foreach (var entry in states.Where(entry => entry.Value.RestoreAfterEditing))
        {
            try
            {
                entry.Key.Show();
            }
            catch (InvalidOperationException)
            {
                // The runtime coordinator closed the window while editing.
            }
        }
    }

    private sealed class RuntimeWindowState(
        EventHandler opened,
        EventHandler closed,
        bool RestoreAfterEditing)
    {
        public EventHandler Opened { get; } = opened;

        public EventHandler Closed { get; } = closed;

        public bool RestoreAfterEditing { get; set; } = RestoreAfterEditing;
    }
}
