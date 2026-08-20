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

    void RefreshPreviewOpacities(OverlayPositionEditSession session);

    void RefreshPreviewScales(OverlayPositionEditSession session);

    void RefreshPreviewPositions(OverlayPositionEditSession session);

    int SnapPreviewsToCenter(OverlayPositionEditSession session);

    void SetRuntimeOverlaysVisibleDuringEditing(bool visible);

    void Close(bool restoreRuntimeWindows = true);
}

public sealed record OverlayPreviewMovedEventArgs(
    string PlotterName,
    PixelPoint Position,
    PixelSize PreviewSize,
    PixelRect HostBounds);

public sealed record OverlayPreviewSettingsRequestedEventArgs(
    string PlotterName);

public sealed class AvaloniaOverlayPositionEditorHost : IOverlayPositionEditorHost
{
    private readonly IOverlayPlatformService platform;
    private readonly OverlayWindowRegistry registry;
    private readonly List<OverlayPositionPreviewWindow> previews = [];
    private readonly Dictionary<string, RuntimeOverlayGeometry>
        runtimePlacementReferences = new(StringComparer.Ordinal);
    private OverlayPositionEditorWindow? editor;
    private OverlayPositionEditSession? editSession;
    private PixelRect hostBounds;
    private double hostScaling = 1;
    private bool updatingPreviewLayout;
    private bool closing;
    private bool disposed;
    private OverlayInteractionViewModel? viewModel;

    public AvaloniaOverlayPositionEditorHost(
        IOverlayPlatformService platform,
        OverlayWindowRegistry? registry = null)
    {
        this.platform = platform
            ?? throw new ArgumentNullException(nameof(platform));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
    }

    public event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

    public event EventHandler? Closed;

    internal IReadOnlyList<OverlayPositionPreviewWindow> PreviewWindows =>
        previews;

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
        toolbar.Opened += OnEditorOpened;
        toolbar.Closed += OnEditorClosed;
        editor = toolbar;
        this.viewModel = viewModel;
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
        hostScaling = screen.Scaling;
        editSession = session;
        var keepRuntimeOverlaysVisible = viewModel.IsLiveInteractionEnabled;
        toolbar.SizeChanged += OnEditorSizeChanged;
        toolbar.Screens.Changed += OnScreensChanged;
        PositionEditorToolbar(toolbar);

        CaptureVisibleRuntimePlacements(registry.Snapshot());

        registry.Changed += OnRegistryChanged;
        registry.SetEditorSuppressed(!keepRuntimeOverlaysVisible);
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

        editSession = session;
        ClosePreviews();
        foreach (var definition in OverlayLayoutCatalog.ForCategory(category))
        {
            var preview = new OverlayPositionPreviewWindow(definition);
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(
                session.ScaleIndex,
                session.GetPlacement(definition.Name).ScaleIndex,
                hostScaling);
            var previewSize = preview.GetExpectedPixelSize(hostScaling);
            preview.Position = session.GetPosition(
                definition.Name,
                hostBounds,
                previewSize);
            preview.ConfigureOpacity(
                session.DefaultOpacity,
                session.GetPlacement(definition.Name).Opacity);
            preview.PointerPressed += OnPreviewPointerPressed;
            preview.SettingsRequested += OnPreviewSettingsRequested;
            preview.Opened += OnPreviewOpened;
            previews.Add(preview);
            preview.Show();
            PositionPreview(preview, session);
            preview.PositionChanged += OnPreviewPositionChanged;
        }

        editor.Activate();
    }

    public void RefreshPreviewOpacities(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var preview in previews)
        {
            preview.ConfigureOpacity(
                session.DefaultOpacity,
                session.GetPlacement(preview.Definition.Name).Opacity);
        }
    }

    public void RefreshPreviewScales(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        updatingPreviewLayout = true;
        try
        {
            foreach (var preview in previews)
            {
                preview.ConfigureScale(
                    session.ScaleIndex,
                    session.GetPlacement(preview.Definition.Name).ScaleIndex,
                    hostScaling);
                PositionPreview(preview, session);
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    public void RefreshPreviewPositions(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        updatingPreviewLayout = true;
        try
        {
            foreach (var preview in previews)
            {
                PositionPreview(preview, session);
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    public int SnapPreviewsToCenter(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            return 0;
        }

        updatingPreviewLayout = true;
        try
        {
            foreach (var preview in previews)
            {
                var metrics = preview.GetPanelMetrics(preview.RenderScaling);
                var referenceSize = GetReferenceSize(preview, metrics);
                var center = new PixelPoint(
                    hostBounds.X + ((hostBounds.Width - referenceSize.Width) / 2),
                    hostBounds.Y + ((hostBounds.Height - referenceSize.Height) / 2));
                session.MoveWithDefaultAnchors(
                    preview.Definition.Name,
                    center,
                    referenceSize,
                    hostBounds);
                PositionPreview(preview, session);
            }

            return previews.Count;
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    public void SetRuntimeOverlaysVisibleDuringEditing(bool visible)
    {
        if (editor is null)
        {
            return;
        }

        if (!visible)
        {
            CaptureVisibleRuntimePlacements(registry.Snapshot());
        }

        registry.SetEditorSuppressed(!visible);
        SynchronizeRuntimeWindows();
        if (visible)
        {
            CaptureVisibleRuntimePlacements(registry.Snapshot());
            if (editSession is not null)
            {
                RefreshPreviewPositions(editSession);
            }

            return;
        }
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
            toolbar.Opened -= OnEditorOpened;
            toolbar.Closed -= OnEditorClosed;
            toolbar.SizeChanged -= OnEditorSizeChanged;
            toolbar.Screens.Changed -= OnScreensChanged;
            toolbar.Close();
        }

        if (restoreRuntimeWindows)
        {
            registry.SetEditorSuppressed(suppressed: false);
        }

        closing = false;
        viewModel = null;
        editSession = null;
        runtimePlacementReferences.Clear();
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

    private static void OnPreviewPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (sender is not OverlayPositionPreviewWindow preview
            || !eventArgs.GetCurrentPoint(preview).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Native window-manager dragging keeps the top edge/title area on
        // screen. Editor previews are intentionally allowed to cross any
        // screen edge, so track the pointer and assign pixel positions
        // directly instead.
        ManagedOverlayWindowDragSession.Begin(preview, eventArgs);
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

        if (updatingPreviewLayout)
        {
            return;
        }

        var metrics = preview.GetPanelMetrics(preview.RenderScaling);
        var panelPosition = new PixelPoint(
            eventArgs.Point.X + metrics.OriginOffset.X,
            eventArgs.Point.Y + metrics.OriginOffset.Y);
        PreviewMoved?.Invoke(
            this,
            new OverlayPreviewMovedEventArgs(
                preview.Definition.Name,
                panelPosition,
                GetReferenceSize(preview, metrics),
                hostBounds));
    }

    private void OnPreviewSettingsRequested(
        object? sender,
        OverlayPreviewSettingsRequestedEventArgs eventArgs)
    {
        viewModel?.OpenOverlaySettings(eventArgs.PlotterName);
        if (editor is { } toolbar)
        {
            toolbar.Activate();
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(editor, toolbar))
                {
                    PositionEditorToolbar(toolbar);
                }
            });
        }
    }

    private void OnPreviewOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not OverlayPositionPreviewWindow preview)
        {
            return;
        }

        _ = platform.PrepareInteractiveWindow(preview);
    }

    private void ClosePreviews()
    {
        foreach (var preview in previews)
        {
            preview.PointerPressed -= OnPreviewPointerPressed;
            preview.PositionChanged -= OnPreviewPositionChanged;
            preview.SettingsRequested -= OnPreviewSettingsRequested;
            preview.Opened -= OnPreviewOpened;
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

        if (sender is Window toolbar)
        {
            toolbar.Opened -= OnEditorOpened;
            toolbar.SizeChanged -= OnEditorSizeChanged;
            toolbar.Screens.Changed -= OnScreensChanged;
        }

        editor = null;
        viewModel = null;
        editSession = null;
        registry.Changed -= OnRegistryChanged;
        ClosePreviews();
        registry.SetEditorSuppressed(suppressed: false);
        runtimePlacementReferences.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is Window toolbar)
        {
            _ = platform.PrepareInteractiveWindow(toolbar);
            PositionEditorToolbar(toolbar);
        }
    }

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (sender is Window toolbar)
        {
            PositionEditorToolbar(toolbar);
        }
    }

    private void OnScreensChanged(object? sender, EventArgs eventArgs)
    {
        if (editor is { } toolbar)
        {
            PositionEditorToolbar(toolbar);
        }
    }

    private void PositionEditorToolbar(Window toolbar)
    {
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            return;
        }

        var screen = toolbar.Screens.ScreenFromBounds(hostBounds)
            ?? toolbar.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var usableBounds = OverlayWindowPlacement.GetUsableBounds(
            hostBounds,
            screen.WorkingArea);
        var logicalWidth = toolbar.Bounds.Width > 0
            ? toolbar.Bounds.Width
            : toolbar.Width;
        var logicalHeight = toolbar.Bounds.Height > 0
            ? toolbar.Bounds.Height
            : toolbar.MinHeight;
        var toolbarSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(logicalWidth * screen.Scaling)),
            Math.Max(1, (int)Math.Ceiling(logicalHeight * screen.Scaling)));
        toolbar.Position = OverlayWindowPlacement.BottomCenter(
            usableBounds,
            toolbarSize,
            margin: 12);
    }

    private void OnRegistryChanged(object? sender, EventArgs eventArgs)
    {
        SynchronizeRuntimeWindows();
        if (editSession is not null)
        {
            RefreshPreviewPositions(editSession);
        }
    }

    private void SynchronizeRuntimeWindows()
    {
        if (editor is null)
        {
            return;
        }

        var snapshot = registry.Snapshot();
        CaptureVisibleRuntimePlacements(snapshot);
    }

    private void PositionPreview(
        OverlayPositionPreviewWindow preview,
        OverlayPositionEditSession session)
    {
        var metrics = preview.GetPanelMetrics(preview.RenderScaling);
        var hasRuntimeReference = runtimePlacementReferences.TryGetValue(
            preview.Definition.Name,
            out var runtimeReference);
        var referenceSize = hasRuntimeReference
            ? runtimeReference.Size
            : metrics.PanelSize;
        var panelPosition = hasRuntimeReference
            && !HasPositionChange(session, preview.Definition.Name)
                ? runtimeReference.Position
                : session.GetPosition(
                    preview.Definition.Name,
                    hostBounds,
                    referenceSize);
        NormalizeStatefulPanelAnchor(
            preview.Definition,
            session,
            panelPosition,
            referenceSize,
            hostBounds);
        preview.Position = new PixelPoint(
            panelPosition.X - metrics.OriginOffset.X,
            panelPosition.Y - metrics.OriginOffset.Y);
    }

    private static void NormalizeStatefulPanelAnchor(
        OverlayLayoutDefinition definition,
        OverlayPositionEditSession session,
        PixelPoint panelPosition,
        PixelSize referenceSize,
        PixelRect bounds)
    {
        if (definition.DefaultPlacement.Vertical
                == definition.MoveVerticalAnchor
            || session.GetPlacement(definition.Name).Vertical
                == definition.MoveVerticalAnchor)
        {
            return;
        }

        var placement = session.GetPlacement(definition.Name) with
        {
            Vertical = definition.MoveVerticalAnchor,
        };
        session.SetPlacement(
            definition.Name,
            OverlayInteractionViewModel.CreatePlacement(
                placement,
                panelPosition,
                referenceSize,
                bounds));
    }

    private PixelSize GetReferenceSize(
        OverlayPositionPreviewWindow preview,
        OverlayPreviewPanelMetrics metrics)
    {
        return runtimePlacementReferences.TryGetValue(
            preview.Definition.Name,
            out var runtimeReference)
                ? runtimeReference.Size
                : metrics.PanelSize;
    }

    private void CaptureVisibleRuntimePlacements(
        IReadOnlyList<RegisteredOverlayWindow> snapshot)
    {
        var currentPlotters = snapshot
            .Select(registered => registered.PlotterName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var stale in runtimePlacementReferences.Keys
                     .Where(plotterName => !currentPlotters.Contains(plotterName))
                     .ToArray())
        {
            runtimePlacementReferences.Remove(stale);
        }

        foreach (var registered in snapshot.Where(candidate => candidate.IsVisible))
        {
            CaptureRuntimePlacement(registered);
        }
    }

    private void CaptureRuntimePlacement(RegisteredOverlayWindow registered)
    {
        runtimePlacementReferences[registered.PlotterName] =
            new RuntimeOverlayGeometry(
                registered.Window.Position,
                OverlayWindowMetrics.GetPixelSize(registered));
    }

    private static bool HasPositionChange(
        OverlayPositionEditSession session,
        string plotterName)
    {
        var current = session.GetPlacement(plotterName);
        var original = session.GetOriginalPlacement(plotterName);
        return current.Horizontal != original.Horizontal
            || current.HorizontalOffset != original.HorizontalOffset
            || current.Vertical != original.Vertical
            || current.VerticalOffset != original.VerticalOffset;
    }

    private readonly record struct RuntimeOverlayGeometry(
        PixelPoint Position,
        PixelSize Size);
}
