using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Platform.Overlay;

public interface IOverlayPositionEditorHost : IDisposable
{
    event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

    event EventHandler<OverlayPreviewSizeChangedEventArgs>? PreviewSizeChanged;

    event EventHandler? Closed;

    bool Open(
        OverlayInteractionViewModel viewModel,
        OverlayPositionEditSession session,
        OverlayLayoutCategory category,
        PixelRect? preferredHostBounds
    );

    void ShowCategory(OverlayPositionEditSession session, OverlayLayoutCategory category);

    void RefreshPreviewOpacities(OverlayPositionEditSession session);

    void RefreshPreviewScales(OverlayPositionEditSession session);

    void RefreshPreviewTypography(OverlayPositionEditSession session);

    void RefreshPreviewSizes(OverlayPositionEditSession session);

    void RefreshPreviewPositions(OverlayPositionEditSession session);

    int SnapPreviewsToCenter(OverlayPositionEditSession session);

    void SetRuntimeOverlaysVisibleDuringEditing(bool visible);

    /// <summary>Finishes pending gestures and measurements before the view model snapshots changed fields.</summary>
    void PrepareSave(OverlayPositionEditSession session)
    {
        RefreshPreviewPositions(session);
    }

    void Close(bool restoreRuntimeWindows = true);
}

public sealed class OverlayPreviewMovedEventArgs(
    string plotterName,
    PixelPoint position,
    PixelSize previewSize,
    PixelRect hostBounds
) : EventArgs
{
    public string PlotterName { get; } = plotterName;

    public PixelPoint Position { get; } = position;

    public PixelSize PreviewSize { get; } = previewSize;

    public PixelRect HostBounds { get; } = hostBounds;
}

public sealed class OverlayPreviewSettingsRequestedEventArgs(string plotterName) : EventArgs
{
    public string PlotterName { get; } = plotterName;
}

public sealed class OverlayPreviewSizeChangedEventArgs(string plotterName, OverlayPanelSize size) : EventArgs
{
    public string PlotterName { get; } = plotterName;

    public OverlayPanelSize Size { get; } = size;
}

public sealed class AvaloniaOverlayPositionEditorHost : IOverlayPositionEditorHost
{
    private readonly IOverlayPlatformService platform;
    private readonly OverlayWindowRegistry registry;
    private readonly List<OverlayPositionPreviewWindow> previews = [];
    private readonly Dictionary<string, RuntimeOverlayGeometry> runtimePlacementReferences = new(
        StringComparer.Ordinal
    );
    private OverlayPositionEditorWindow? editor;
    private MiningCalibrationWindow? miningCalibration;
    private OverlayPositionEditSession? editSession;
    private PixelRect hostBounds;
    private double hostScaling = 1;
    private bool updatingPreviewLayout;
    private bool updatingReferenceFrame;
    private readonly Dictionary<string, PixelPoint> pendingGeometryOrigins = new(StringComparer.Ordinal);
    private DispatcherTimer? boundsTimer;
    private bool closing;
    private bool disposed;
    private OverlayInteractionViewModel? viewModel;
    private ColonizationCommodityOverlayViewModel? colonizationOverlay;

    public AvaloniaOverlayPositionEditorHost(IOverlayPlatformService platform, OverlayWindowRegistry? registry = null)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
    }

    public event EventHandler<OverlayPreviewMovedEventArgs>? PreviewMoved;

    public event EventHandler<OverlayPreviewSizeChangedEventArgs>? PreviewSizeChanged;

    public event EventHandler? Closed;

    internal IReadOnlyList<OverlayPositionPreviewWindow> PreviewWindows => previews;
    internal OverlayPositionEditorWindow? EditorToolbar => editor;
    internal MiningCalibrationWindow? MiningCalibration => miningCalibration;

    /// <summary>Prepares the position editor with the session's overlay window policy before showing its controls.</summary>
    public bool Open(
        OverlayInteractionViewModel viewModel,
        OverlayPositionEditSession session,
        OverlayLayoutCategory category,
        PixelRect? preferredHostBounds
    )
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
        viewModel.PropertyChanged += OnEditorControlsHeightChanged;
        colonizationOverlay = viewModel.ColonizationOverlay;
        colonizationOverlay?.PropertyChanged += OnColonizationPreferencesChanged;
        registry.PrepareWindow(toolbar);
        toolbar.Show();

        PixelRect? preferred = preferredHostBounds is { Width: > 0, Height: > 0 }
            ? preferredHostBounds.Value
            : (PixelRect?)null;
        Screen? gameScreen = preferred is { } gameBounds
            ? toolbar.Screens.ScreenFromBounds(gameBounds) ?? toolbar.Screens.Primary
            : null;
        MainWindowMonitor? fallbackMonitor = ResolveFallbackMonitor(
            MainWindowPlacement.DescribeScreens(toolbar.Screens.All),
            viewModel.OverlayBehavior?.PreferredMonitorId
        );
        if (gameScreen is null && fallbackMonitor is null)
        {
            Close(restoreRuntimeWindows: true);
            return false;
        }

        hostBounds = preferred ?? fallbackMonitor!.Bounds;
        hostScaling = gameScreen?.Scaling ?? fallbackMonitor!.Scaling;
        editSession = session;
        bool keepRuntimeOverlaysVisible = viewModel.IsLiveInteractionEnabled;
        toolbar.SizeChanged += OnEditorSizeChanged;
        toolbar.Screens.Changed += OnScreensChanged;
        PositionEditorToolbar(toolbar);

        CaptureVisibleRuntimePlacements(registry.Snapshot());

        registry.Changed += OnRegistryChanged;
        registry.SetEditorSuppressed(!keepRuntimeOverlaysVisible);
        SynchronizeRuntimeWindows();
        ShowCategory(session, category);
        toolbar.Activate();
        boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        boundsTimer.Tick += OnReferenceFrameTick;
        boundsTimer.Start();
        return true;
    }

    internal static MainWindowMonitor? ResolveFallbackMonitor(
        IReadOnlyList<MainWindowMonitor> monitors,
        string? preferredMonitorId
    )
    {
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return monitors.FirstOrDefault(monitor => string.Equals(monitor.Id, preferredMonitorId, comparison))
            ?? monitors.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? (monitors.Count > 0 ? monitors[0] : null);
    }

    /// <summary>Creates interactive previews under the same window-management policy as live panels.</summary>
    public void ShowCategory(OverlayPositionEditSession session, OverlayLayoutCategory category)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (disposed || editor is null || hostBounds.Width <= 0)
        {
            return;
        }

        editSession = session;
        ClosePreviews();
        if (category == OverlayLayoutCategory.Mining && viewModel?.MiningDetection is { } detection)
        {
            miningCalibration = new MiningCalibrationWindow(detection, hostBounds, hostScaling);
            detection.PropertyChanged += OnMiningDetectionChanged;
            miningCalibration.Show();
            _ = platform.PrepareInteractiveWindow(miningCalibration);
        }
        foreach (OverlayLayoutDefinition definition in OverlayLayoutCatalog.ForCategory(category))
        {
            var preview = new OverlayPositionPreviewWindow(definition);
            preview.ConfigureColonizationPreferences(
                viewModel?.ColonizationOverlay?.Preferences ?? ColonizationOverlayPreferences.Default
            );
            OverlayThemeResources.Apply(preview);
            preview.ApplyRuntimePresentationTheme();
            preview.ConfigureScale(session.ScaleIndex, session.GetPlacement(definition.Name).ScaleIndex, hostScaling);
            preview.ConfigureTypography(session.GetTypographyScale(definition.Name));
            preview.ConfigureSize(session.GetSizeOverride(definition.Name));
            PixelSize previewSize = preview.GetExpectedPixelSize(hostScaling);
            preview.Position = session.GetPosition(definition.Name, hostBounds, previewSize);
            if (definition.Name == "PlotRouteBio")
            {
                Screen? screen = editor.Screens.ScreenFromBounds(hostBounds) ?? editor.Screens.Primary;
                if (screen is not null)
                {
                    PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
                        new OverlayScreenGeometry(screen.Bounds, screen.WorkingArea),
                        editor
                            .Screens.All.Select(current => new OverlayScreenGeometry(
                                current.Bounds,
                                current.WorkingArea
                            ))
                            .ToArray()
                    );
                    PixelRect visibleBounds = OverlayWindowPlacement.GetUsableBounds(hostBounds, workingArea);
                    preview.MaxHeight = Math.Max(
                        1,
                        Math.Min(680, (visibleBounds.Bottom - preview.Position.Y - 8) / screen.Scaling)
                    );
                }
            }
            preview.ConfigureOpacity(session.DefaultOpacity, session.GetPlacement(definition.Name).Opacity);
            OverlayDragPolicy.SetOptionsFactory(preview, () => CreatePreviewDragOptions(preview));
            preview.PointerPressed += OnPreviewPointerPressed;
            preview.SettingsRequested += OnPreviewSettingsRequested;
            preview.PanelSizeChanged += OnPreviewSizeChanged;
            preview.SizeChanged += OnPreviewMeasured;
            preview.Opened += OnPreviewOpened;
            previews.Add(preview);
            registry.PrepareWindow(preview);
            preview.Show();
            PositionPreview(preview, session);
            Dispatcher.UIThread.Post(() => PositionPreviewAfterOpening(preview, session), DispatcherPriority.Loaded);
        }

        editor.Activate();
    }

    public void RefreshPreviewOpacities(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (OverlayPositionPreviewWindow preview in previews)
        {
            preview.ConfigureOpacity(session.DefaultOpacity, session.GetPlacement(preview.Definition.Name).Opacity);
        }
    }

    /// <summary>Changes scale while retaining each edited panel's screen origin.</summary>
    public void RefreshPreviewScales(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        updatingPreviewLayout = true;
        try
        {
            foreach (OverlayPositionPreviewWindow preview in previews)
            {
                RememberGeometryOrigin(preview, session);
                preview.ConfigureScale(
                    session.ScaleIndex,
                    session.GetPlacement(preview.Definition.Name).ScaleIndex,
                    hostScaling
                );
                if (!pendingGeometryOrigins.ContainsKey(preview.Definition.Name))
                {
                    PositionPreview(preview, session);
                }
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    /// <summary>Changes text geometry while retaining the selected panel origin.</summary>
    public void RefreshPreviewTypography(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        updatingPreviewLayout = true;
        try
        {
            foreach (OverlayPositionPreviewWindow preview in previews)
            {
                RememberGeometryOrigin(preview, session);
                preview.ConfigureTypography(session.GetTypographyScale(preview.Definition.Name));
                if (!pendingGeometryOrigins.ContainsKey(preview.Definition.Name))
                {
                    PositionPreview(preview, session);
                }
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    /// <summary>Updates size overrides and preserves their measured placement.</summary>
    public void RefreshPreviewSizes(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        updatingPreviewLayout = true;
        try
        {
            foreach (OverlayPositionPreviewWindow preview in previews)
            {
                RememberGeometryOrigin(preview, session);
                preview.ConfigureSize(session.GetSizeOverride(preview.Definition.Name));
                if (!pendingGeometryOrigins.ContainsKey(preview.Definition.Name))
                {
                    PositionPreview(preview, session);
                }
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    /// <summary>Adopts rebased sessions and reapplies positions against current panel geometry.</summary>
    public void RefreshPreviewPositions(OverlayPositionEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!ReferenceEquals(editSession, session))
        {
            editSession = session;
            runtimePlacementReferences.Clear();
        }
        updatingPreviewLayout = true;
        try
        {
            foreach (OverlayPositionPreviewWindow preview in previews)
            {
                PositionPreview(preview, session);
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    /// <summary>Retains the visible panel origin across a user-requested geometry change.</summary>
    private void RememberGeometryOrigin(OverlayPositionPreviewWindow preview, OverlayPositionEditSession session)
    {
        if (session.HasGeometryChanges(preview.Definition.Name))
        {
            pendingGeometryOrigins.TryAdd(preview.Definition.Name, preview.GetPanelScreenOrigin(preview.RenderScaling));
            runtimePlacementReferences.Remove(preview.Definition.Name);
        }
    }

    /// <summary>Measures final panel dimensions and translates the preserved origin into the saved anchors.</summary>
    private void OnPreviewMeasured(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (
            sender is OverlayPositionPreviewWindow preview
            && pendingGeometryOrigins.Remove(preview.Definition.Name, out PixelPoint origin)
        )
        {
            RecordMeasuredGeometry(preview, origin, preview.GetPanelMetrics(preview.RenderScaling).PanelSize);
        }
    }

    /// <summary>Publishes a panel's stable screen origin using its new size.</summary>
    private void RecordMeasuredGeometry(OverlayPositionPreviewWindow preview, PixelPoint origin, PixelSize size)
    {
        OverlayPreviewPanelMetrics metrics = preview.GetPanelMetrics(preview.RenderScaling);
        preview.Position = new PixelPoint(origin.X - metrics.OriginOffset.X, origin.Y - metrics.OriginOffset.Y);
        PreviewMoved?.Invoke(this, new OverlayPreviewMovedEventArgs(preview.Definition.Name, origin, size, hostBounds));
    }

    /// <summary>Flushes accepted movement and geometry updates before Save reads the session.</summary>
    public void PrepareSave(OverlayPositionEditSession session)
    {
        UpdateReferenceFrame();
        foreach (OverlayPositionPreviewWindow preview in previews)
        {
            ManagedOverlayWindowDragSession.Cancel(preview);
            if (pendingGeometryOrigins.Remove(preview.Definition.Name, out PixelPoint origin))
            {
                RecordMeasuredGeometry(preview, origin, preview.GetExpectedPixelSize(preview.RenderScaling));
            }
        }
        RefreshPreviewPositions(session);
    }

    /// <summary>Reconciles monitor changes and changes of the game's reference rectangle without discarding draft values.</summary>
    private void OnReferenceFrameTick(object? sender, EventArgs eventArgs)
    {
        try
        {
            UpdateReferenceFrame();
        }
        catch (Exception exception)
            when (LegacyOverlayLayoutStore.IsPersistenceFailure(exception) || exception is NotSupportedException)
        {
            viewModel?.HandleEditorFailure(exception);
        }
    }

    /// <summary>Rebuilds previews against the current screen or game frame while retaining proportional saved coordinates.</summary>
    internal void UpdateReferenceFrame()
    {
        if (updatingReferenceFrame)
        {
            return;
        }
        if (editor is not { } toolbar || viewModel is null || editSession is null)
        {
            return;
        }
        GameWindowSnapshot game = viewModel.EditorGameWindow;
        MainWindowMonitor? monitor = ResolveFallbackMonitor(
            MainWindowPlacement.DescribeScreens(toolbar.Screens.All),
            viewModel.OverlayBehavior?.PreferredMonitorId
        );
        PixelRect current = game.IsAvailable ? game.ClientBounds : monitor?.Bounds ?? hostBounds;
        double scaling = toolbar.Screens.ScreenFromBounds(current)?.Scaling ?? monitor?.Scaling ?? hostScaling;
        if (current == hostBounds && Math.Abs(scaling - hostScaling) < .0001)
        {
            return;
        }
        updatingReferenceFrame = true;
        try
        {
            hostBounds = current;
            hostScaling = scaling;
            foreach (OverlayPositionPreviewWindow preview in previews.ToArray())
            {
                ManagedOverlayWindowDragSession.Cancel(preview);
            }
            runtimePlacementReferences.Clear();
            pendingGeometryOrigins.Clear();
            ShowCategory(editSession, viewModel.SelectedCategory.Category);
            PositionEditorToolbar(toolbar);
        }
        finally
        {
            updatingReferenceFrame = false;
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
            foreach (OverlayPositionPreviewWindow preview in previews)
            {
                OverlayPreviewPanelMetrics metrics = preview.GetPanelMetrics(preview.RenderScaling);
                PixelSize referenceSize = GetReferenceSize(preview, metrics);
                var center = new PixelPoint(
                    hostBounds.X + ((hostBounds.Width - referenceSize.Width) / 2),
                    hostBounds.Y + ((hostBounds.Height - referenceSize.Height) / 2)
                );
                session.MoveWithDefaultAnchors(preview.Definition.Name, center, referenceSize, hostBounds);
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

    /// <summary>Closes previews and detaches their original settings sources before optionally restoring live overlays.</summary>
    public void Close(bool restoreRuntimeWindows = true)
    {
        StopBoundsTimer();
        if (closing)
        {
            return;
        }

        closing = true;
        registry.Changed -= OnRegistryChanged;
        ClosePreviews();
        OverlayPositionEditorWindow? toolbar = editor;
        editor = null;
        if (toolbar is not null)
        {
            toolbar.Opened -= OnEditorOpened;
            toolbar.Closed -= OnEditorClosed;
            toolbar.SizeChanged -= OnEditorSizeChanged;
            toolbar.Screens.Changed -= OnScreensChanged;
            TryCleanup(toolbar.Close);
        }

        viewModel?.PropertyChanged -= OnEditorControlsHeightChanged;
        colonizationOverlay?.PropertyChanged -= OnColonizationPreferencesChanged;
        colonizationOverlay = null;

        if (restoreRuntimeWindows)
        {
            TryCleanup(() => registry.SetEditorSuppressed(suppressed: false));
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

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (
            sender is not OverlayPositionPreviewWindow preview
            || !eventArgs.GetCurrentPoint(preview).Properties.IsLeftButtonPressed
        )
        {
            return;
        }

        // GNOME can ignore native move requests for the notification-style X11
        // windows used by editor previews. Track the pointer directly so every
        // desktop can reposition previews and cross display edges consistently.
        ManagedOverlayWindowDragSession.Begin(preview, eventArgs, position => OnPreviewDragged(preview, position));
        eventArgs.Handled = true;
    }

    /// <summary>Records accepted pointer positions using the current frame and panel size.</summary>
    private void OnPreviewDragged(OverlayPositionPreviewWindow preview, PixelPoint position)
    {
        if (updatingPreviewLayout || !previews.Contains(preview))
        {
            return;
        }

        UpdateReferenceFrame();
        if (!previews.Contains(preview))
        {
            return;
        }
        OverlayPreviewPanelMetrics metrics = preview.GetPanelMetrics(preview.RenderScaling);
        var panelPosition = new PixelPoint(position.X + metrics.OriginOffset.X, position.Y + metrics.OriginOffset.Y);
        PreviewMoved?.Invoke(
            this,
            new OverlayPreviewMovedEventArgs(
                preview.Definition.Name,
                panelPosition,
                GetReferenceSize(preview, metrics),
                hostBounds
            )
        );
    }

    private OverlayDragOptions CreatePreviewDragOptions(OverlayPositionPreviewWindow preview)
    {
        OverlayPreviewPanelMetrics metrics = preview.GetPanelMetrics(preview.RenderScaling);
        return OverlayDragPolicy.CreateOptions(
            preview,
            viewModel?.OverlayBehavior,
            hostBounds,
            metrics.PanelSize,
            metrics.OriginOffset
        );
    }

    private void OnPreviewSettingsRequested(object? sender, OverlayPreviewSettingsRequestedEventArgs eventArgs)
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

    private void OnPreviewSizeChanged(object? sender, OverlayPreviewSizeChangedEventArgs eventArgs)
    {
        if (sender is OverlayPositionPreviewWindow preview)
        {
            PreviewMoved?.Invoke(
                this,
                new OverlayPreviewMovedEventArgs(
                    eventArgs.PlotterName,
                    preview.GetPanelScreenOrigin(preview.RenderScaling),
                    preview.GetConfiguredPanelPixelSize(eventArgs.Size, preview.RenderScaling),
                    hostBounds
                )
            );
        }

        PreviewSizeChanged?.Invoke(this, eventArgs);
    }

    private void OnPreviewOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not OverlayPositionPreviewWindow preview)
        {
            return;
        }

        _ = platform.PrepareInteractiveWindow(preview);
    }

    /// <summary>Detaches measurements and safely closes every category preview.</summary>
    private void ClosePreviews()
    {
        if (miningCalibration is not null && viewModel?.MiningDetection is { } detection)
        {
            detection.PropertyChanged -= OnMiningDetectionChanged;
        }

        if (miningCalibration is { } calibration)
        {
            TryCleanup(calibration.Close);
        }
        miningCalibration = null;
        foreach (OverlayPositionPreviewWindow preview in previews)
        {
            preview.PointerPressed -= OnPreviewPointerPressed;
            preview.SettingsRequested -= OnPreviewSettingsRequested;
            preview.PanelSizeChanged -= OnPreviewSizeChanged;
            preview.SizeChanged -= OnPreviewMeasured;
            preview.Opened -= OnPreviewOpened;
            OverlayDragPolicy.SetOptionsFactory(preview, null);
            TryCleanup(preview.Close);
        }

        previews.Clear();
        pendingGeometryOrigins.Clear();
    }

    private void OnMiningDetectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (miningCalibration is null)
        {
            return;
        }

        bool testing = viewModel?.MiningDetection?.IsCalibrationTesting == true;
        foreach (OverlayPositionPreviewWindow preview in previews)
        {
            if (testing)
            {
                preview.Hide();
            }
            else if (!preview.IsVisible)
            {
                preview.Show();
            }
        }
    }

    /// <summary>Releases preview subscriptions and restores live overlays when the toolbar closes directly.</summary>
    private void OnEditorClosed(object? sender, EventArgs eventArgs)
    {
        StopBoundsTimer();
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

        viewModel?.PropertyChanged -= OnEditorControlsHeightChanged;
        colonizationOverlay?.PropertyChanged -= OnColonizationPreferencesChanged;
        colonizationOverlay = null;

        editor = null;
        viewModel = null;
        editSession = null;
        registry.Changed -= OnRegistryChanged;
        ClosePreviews();
        registry.SetEditorSuppressed(suppressed: false);
        runtimePlacementReferences.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Continues releasing editor resources after an expected native close or restoration failure.</summary>
    private static void TryCleanup(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
            when (LegacyOverlayLayoutStore.IsPersistenceFailure(exception) || exception is NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning("Overlay editor cleanup failed: {0}", exception);
        }
    }

    /// <summary>Detaches reference-frame polling at either editor cleanup point.</summary>
    private void StopBoundsTimer()
    {
        if (boundsTimer is null)
        {
            return;
        }
        boundsTimer.Stop();
        boundsTimer.Tick -= OnReferenceFrameTick;
        boundsTimer = null;
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
            // SizeChanged can arrive before Bounds reflects the new
            // SizeToContent measurement. Use the event size so the toolbar
            // stays fixed while settings rows expand upward above it.
            PositionEditorToolbar(toolbar, eventArgs.NewSize);
        }
    }

    /// <summary>Rebuilds the reference frame and toolbar when the screen configuration changes.</summary>
    private void OnScreensChanged(object? sender, EventArgs eventArgs)
    {
        UpdateReferenceFrame();
        if (editor is { } toolbar)
        {
            PositionEditorToolbar(toolbar);
        }
    }

    private void OnEditorControlsHeightChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs eventArgs)
    {
        if (
            eventArgs.PropertyName == nameof(OverlayInteractionViewModel.EditorControlsHeightPercent)
            && editor is { } toolbar
        )
        {
            PositionEditorToolbar(toolbar);
        }
    }

    /// <summary>Refreshes open colonization previews and their saved anchors when a presentation preference changes.</summary>
    private void OnColonizationPreferencesChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs eventArgs
    )
    {
        if (
            eventArgs.PropertyName != nameof(ColonizationCommodityOverlayViewModel.Preferences)
            || sender is not ColonizationCommodityOverlayViewModel source
            || editSession is not { } session
        )
        {
            return;
        }

        updatingPreviewLayout = true;
        try
        {
            foreach (
                OverlayPositionPreviewWindow preview in previews.Where(window =>
                    window.Definition.Name == "PlotBuildCommodities"
                )
            )
            {
                preview.ConfigureColonizationPreferences(source.Preferences);
                PositionPreview(preview, session);
                Dispatcher.UIThread.Post(() => PositionPreviewAfterOpening(preview, session));
            }
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    private void PositionEditorToolbar(Window toolbar, Size? measuredSize = null)
    {
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            return;
        }

        Screen? screen = toolbar.Screens.ScreenFromBounds(hostBounds) ?? toolbar.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        OverlayScreenGeometry[] screens = toolbar
            .Screens.All.Select(current => new OverlayScreenGeometry(current.Bounds, current.WorkingArea))
            .ToArray();
        PixelRect workingArea = OverlayWindowPlacement.GetReliableBottomWorkingArea(
            new OverlayScreenGeometry(screen.Bounds, screen.WorkingArea),
            screens
        );
        PixelRect usableBounds = OverlayWindowPlacement.GetUsableBounds(hostBounds, workingArea);
        double logicalWidth = measuredSize?.Width ?? (toolbar.Bounds.Width > 0 ? toolbar.Bounds.Width : toolbar.Width);
        double logicalHeight =
            measuredSize?.Height ?? (toolbar.Bounds.Height > 0 ? toolbar.Bounds.Height : toolbar.MinHeight);
        var toolbarSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(logicalWidth * screen.Scaling)),
            Math.Max(1, (int)Math.Ceiling(logicalHeight * screen.Scaling))
        );
        toolbar.Position = OverlayWindowPlacement.BottomCenterWithHeightAdjustment(
            usableBounds,
            screen.Bounds,
            toolbarSize,
            viewModel?.EditorControlsHeightPercent ?? 0,
            margin: 12
        );
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

        IReadOnlyList<RegisteredOverlayWindow> snapshot = registry.Snapshot();
        CaptureVisibleRuntimePlacements(snapshot);
    }

    /// <summary>Aligns a preview with live geometry only while that geometry remains unchanged.</summary>
    private void PositionPreview(OverlayPositionPreviewWindow preview, OverlayPositionEditSession session)
    {
        OverlayPreviewPanelMetrics metrics = preview.GetPanelMetrics(preview.RenderScaling);
        bool hasRuntimeReference = runtimePlacementReferences.TryGetValue(
            preview.Definition.Name,
            out RuntimeOverlayGeometry runtimeReference
        );
        PixelSize referenceSize = GetReferenceSize(preview, metrics);
        PixelPoint panelPosition =
            hasRuntimeReference && !HasPositionChange(session, preview.Definition.Name)
                ? runtimeReference.Position
                : session.GetPosition(preview.Definition.Name, hostBounds, referenceSize);
        NormalizeStatefulPanelAnchor(preview.Definition, session, panelPosition, referenceSize, hostBounds);
        preview.Position = new PixelPoint(
            panelPosition.X - metrics.OriginOffset.X,
            panelPosition.Y - metrics.OriginOffset.Y
        );
    }

    /// <summary>Repositions after layout has measured scaling and editor chrome.</summary>
    private void PositionPreviewAfterOpening(OverlayPositionPreviewWindow preview, OverlayPositionEditSession session)
    {
        if (
            disposed
            || !previews.Contains(preview)
            || !ReferenceEquals(editSession, session)
            || ManagedOverlayWindowDragSession.IsDragging(preview)
        )
        {
            return;
        }

        updatingPreviewLayout = true;
        try
        {
            PositionPreview(preview, session);
        }
        finally
        {
            updatingPreviewLayout = false;
        }
    }

    private static void NormalizeStatefulPanelAnchor(
        OverlayLayoutDefinition definition,
        OverlayPositionEditSession session,
        PixelPoint panelPosition,
        PixelSize referenceSize,
        PixelRect bounds
    )
    {
        if (
            definition.DefaultPlacement.Vertical == definition.MoveVerticalAnchor
            || session.GetPlacement(definition.Name).Vertical == definition.MoveVerticalAnchor
        )
        {
            return;
        }

        LegacyOverlayPlacement placement = session.GetPlacement(definition.Name) with
        {
            Vertical = definition.MoveVerticalAnchor,
        };
        session.SetPlacement(
            definition.Name,
            OverlayPositionEditSession.CreatePlacement(placement, panelPosition, referenceSize, bounds)
        );
    }

    /// <summary>Uses measured preview geometry after changes, otherwise retaining the opening live size.</summary>
    private PixelSize GetReferenceSize(OverlayPositionPreviewWindow preview, OverlayPreviewPanelMetrics metrics)
    {
        return
            editSession?.HasGeometryChanges(preview.Definition.Name) != true
            && runtimePlacementReferences.TryGetValue(
                preview.Definition.Name,
                out RuntimeOverlayGeometry runtimeReference
            )
            ? runtimeReference.Size
            : metrics.PanelSize;
    }

    private void CaptureVisibleRuntimePlacements(IReadOnlyList<RegisteredOverlayWindow> snapshot)
    {
        RegisteredOverlayWindow[] placementOwners = snapshot
            .Where(registered => registered.ParticipatesInPlacement)
            .ToArray();
        var currentPlotters = placementOwners
            .Select(registered => registered.PlotterName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (
            string? stale in runtimePlacementReferences
                .Keys.Where(plotterName => !currentPlotters.Contains(plotterName))
                .ToArray()
        )
        {
            runtimePlacementReferences.Remove(stale);
        }

        foreach (RegisteredOverlayWindow? registered in placementOwners.Where(candidate => candidate.IsVisible))
        {
            CaptureRuntimePlacement(registered);
        }
    }

    private void CaptureRuntimePlacement(RegisteredOverlayWindow registered)
    {
        runtimePlacementReferences[registered.PlotterName] = new RuntimeOverlayGeometry(
            registered.Window.Position,
            OverlayWindowMetrics.GetPixelSize(registered)
        );
    }

    private static bool HasPositionChange(OverlayPositionEditSession session, string plotterName)
    {
        LegacyOverlayPlacement current = session.GetPlacement(plotterName);
        LegacyOverlayPlacement original = session.GetOriginalPlacement(plotterName);
        return current.Horizontal != original.Horizontal
            || current.HorizontalOffset != original.HorizontalOffset
            || current.Vertical != original.Vertical
            || current.VerticalOffset != original.VerticalOffset;
    }

    private readonly record struct RuntimeOverlayGeometry(PixelPoint Position, PixelSize Size);
}
