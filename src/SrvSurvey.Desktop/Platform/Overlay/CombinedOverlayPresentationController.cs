using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal interface IOverlayPresentationControl
{
    void SetRuntimeOverlaysSuppressed(bool suppressed);
}

internal sealed class CombinedOverlayPresentationController : IDisposable
{
    private readonly IOverlayPlatformService nativePlatform;
    private readonly ICombinedOverlayNativeService nativeCombined;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly OverlayWindowRegistry registry;
    private readonly Dictionary<Window, Entry> entries = [];
    private readonly HashSet<Window> interactiveWindows = [];
    private readonly OverlayDispatcherTimer timer;
    private CombinedOverlayWindow? host;
    private PixelRect hostBounds;
    private OverlayPreparationResult? hostPreparation;
    private PixelRect[] appliedInputRegions = [];
    private OverlayInteractionResult? appliedInputResult;
    private DragState? drag;
    private bool runtimeOverlaysSuppressed;
    private bool disposed;

    public CombinedOverlayPresentationController(
        IOverlayPlatformService nativePlatform,
        IGameWindowTracker gameWindowTracker,
        OverlayWindowRegistry? registry = null)
    {
        this.nativePlatform = nativePlatform
            ?? throw new ArgumentNullException(nameof(nativePlatform));
        nativeCombined = nativePlatform as ICombinedOverlayNativeService
            ?? throw new ArgumentException(
                "The native platform cannot host combined overlays.",
                nameof(nativePlatform));
        this.gameWindowTracker = gameWindowTracker
            ?? throw new ArgumentNullException(nameof(gameWindowTracker));
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        timer = new OverlayDispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    public OverlayPlatformCapabilities Capabilities =>
        nativePlatform.Capabilities;

    public OverlayPreparationResult PreparePassiveWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!registry.TryGetPlotterName(window, out var plotterName))
        {
            return nativePlatform.PreparePassiveWindow(window);
        }

        if (!entries.TryGetValue(window, out var entry))
        {
            if (window.Content is not Control content)
            {
                return new OverlayPreparationResult(
                    IsPrepared: false,
                    IsClickThrough: false,
                    $"{plotterName} does not expose reusable overlay content.");
            }

            if (!nativeCombined.SuppressNativeWindow(window))
            {
                return new OverlayPreparationResult(
                    IsPrepared: false,
                    IsClickThrough: false,
                    $"The native {plotterName} source window could not be suppressed.");
            }

            var placeholder = new Border
            {
                Width = GetInitialPlaceholderLength(
                    window.Bounds.Width,
                    window.Width),
                Height = GetInitialPlaceholderLength(
                    window.Bounds.Height,
                    window.Height),
                IsHitTestVisible = false,
            };
            window.Content = placeholder;
            var presenter = new ContentControl
            {
                Content = content,
                DataContext = window.DataContext,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            };
            entry = new Entry(
                window,
                plotterName,
                presenter,
                placeholder);
            entries.Add(window, entry);
            window.PositionChanged += OnSourcePositionChanged;
            window.PropertyChanged += OnSourcePropertyChanged;
            window.Opened += OnSourceOpened;
            window.Closed += OnSourceClosed;
            presenter.LayoutUpdated += OnPresenterLayoutUpdated;
            presenter.PointerPressed += OnPresenterPointerPressed;
            presenter.PointerMoved += OnPresenterPointerMoved;
            presenter.PointerReleased += OnPresenterPointerReleased;
            presenter.PointerCaptureLost += OnPresenterPointerCaptureLost;
            registry.SetPresentationVisual(window, content);
            registry.SetPresentationVisible(
                window,
                !runtimeOverlaysSuppressed);
            EnsureHost();
            host?.Add(presenter);
        }
        else
        {
            _ = nativeCombined.SuppressNativeWindow(window);
        }

        UpdateEntry(entry);
        UpdateHost();
        if (hostPreparation is { IsClickThrough: false } failure)
        {
            return failure;
        }

        return new OverlayPreparationResult(
            IsPrepared: true,
            IsClickThrough: true,
            "This overlay panel is sharing the combined native overlay window.");
    }

    public OverlayInteractionResult SetInteractive(
        Window window,
        bool interactive)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!entries.TryGetValue(window, out var entry))
        {
            return nativePlatform.SetInteractive(window, interactive);
        }

        if (interactive)
        {
            interactiveWindows.Add(window);
        }
        else
        {
            interactiveWindows.Remove(window);
            if (drag?.Entry == entry)
            {
                StopDrag(releasePointer: true);
            }
        }

        entry.Presenter.IsHitTestVisible = interactive
            && registry.ShouldPresent(window);
        var result = ApplyHostInputRegion();
        return new OverlayInteractionResult(
            result.IsPrepared,
            interactive && result.IsInteractive,
            result.Status);
    }

    public IDisposable? BeginVisibleCursorSession(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return nativePlatform.BeginVisibleCursorSession(host ?? window);
    }

    public void BeginMoveDrag(
        Window window,
        PointerPressedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(eventArgs);
        if (!entries.ContainsKey(window))
        {
            nativePlatform.BeginMoveDrag(window, eventArgs);
        }
    }

    public void SetRuntimeOverlaysSuppressed(bool suppressed)
    {
        if (disposed || runtimeOverlaysSuppressed == suppressed)
        {
            return;
        }

        runtimeOverlaysSuppressed = suppressed;
        foreach (var entry in entries.Values)
        {
            registry.SetPresentationVisible(
                entry.Window,
                !suppressed);
        }

        if (suppressed)
        {
            StopDrag(releasePointer: true);
        }

        UpdateHost();
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
        StopDrag(releasePointer: true);
        foreach (var window in entries.Keys.ToArray())
        {
            Remove(window);
        }

        var currentHost = host;
        host = null;
        if (currentHost is not null)
        {
            currentHost.Opened -= OnHostOpened;
            currentHost.Close();
        }

        gameWindowTracker.Dispose();
        nativePlatform.Dispose();
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        UpdateHost();
    }

    private void EnsureHost()
    {
        if (host is not null || disposed)
        {
            return;
        }

        var window = new CombinedOverlayWindow();
        OverlayThemeResources.Apply(window);
        window.Opened += OnHostOpened;
        host = window;
    }

    private void OnHostOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not CombinedOverlayWindow window)
        {
            return;
        }

        var result = nativePlatform.PreparePassiveWindow(window);
        hostPreparation = result;
        if (!result.IsClickThrough)
        {
            window.Hide();
            return;
        }

        foreach (var entry in entries.Values)
        {
            UpdateEntry(entry);
        }

        appliedInputResult = null;
        appliedInputRegions = [];
        ApplyHostInputRegion(force: true);
    }

    private void UpdateHost()
    {
        if (disposed || entries.Count == 0)
        {
            host?.Hide();
            return;
        }

        EnsureHost();
        var window = host;
        if (window is null)
        {
            return;
        }

        if (runtimeOverlaysSuppressed)
        {
            window.Hide();
            return;
        }

        var gameWindow = gameWindowTracker.GetSnapshot();
        if (!gameWindow.IsAvailable || !gameWindow.IsVisible)
        {
            window.Hide();
            return;
        }

        var screen = window.Screens.ScreenFromBounds(gameWindow.ClientBounds)
            ?? window.Screens.Primary;
        if (screen is null)
        {
            window.Hide();
            return;
        }

        hostBounds = gameWindow.ClientBounds;
        window.Position = hostBounds.Position;
        window.Width = hostBounds.Width / screen.Scaling;
        window.Height = hostBounds.Height / screen.Scaling;
        foreach (var entry in entries.Values)
        {
            UpdateEntry(entry);
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (interactiveWindows.Count > 0)
        {
            ApplyHostInputRegion();
        }
    }

    private void UpdateEntry(Entry entry)
    {
        var window = entry.Window;
        var presenter = entry.Presenter;
        presenter.DataContext = window.DataContext;
        presenter.Opacity = Math.Clamp(window.Opacity, 0, 1);
        presenter.MinWidth = NormalizeMinimum(window.MinWidth);
        presenter.MinHeight = NormalizeMinimum(window.MinHeight);
        presenter.MaxWidth = NormalizeMaximum(window.MaxWidth);
        presenter.MaxHeight = NormalizeMaximum(window.MaxHeight);
        presenter.Width = UsesContentWidth(window)
            ? double.NaN
            : NormalizeLength(window.Width);
        presenter.Height = UsesContentHeight(window)
            ? double.NaN
            : NormalizeLength(window.Height);

        if (host is null || hostBounds.Width <= 0)
        {
            return;
        }

        var size = GetLogicalSize(entry);
        var projection = CombinedOverlayProjection.Create(
            hostBounds,
            window.Position,
            size,
            host.RenderScaling);
        entry.Projection = projection;
        var shouldPresent = registry.ShouldPresent(window);
        presenter.IsVisible = projection is not null && shouldPresent;
        presenter.IsHitTestVisible = shouldPresent
            && interactiveWindows.Contains(window);
        if (projection is null)
        {
            return;
        }

        Canvas.SetLeft(presenter, projection.Left);
        Canvas.SetTop(presenter, projection.Top);
    }

    private OverlayInteractionResult ApplyHostInputRegion(bool force = false)
    {
        var window = host;
        if (window is null || !window.IsVisible)
        {
            return new OverlayInteractionResult(
                IsPrepared: entries.Count > 0,
                IsInteractive: interactiveWindows.Count > 0,
                "The combined overlay host is waiting for the Elite window.");
        }

        var regions = interactiveWindows
            .Select(source => entries.TryGetValue(source, out var entry)
                && registry.ShouldPresent(source)
                    ? entry.Projection?.InputRegion
                : null)
            .Where(region => region is not null)
            .Select(region => region!.Value)
            .ToArray();
        if (!force
            && appliedInputResult is not null
            && regions.SequenceEqual(appliedInputRegions))
        {
            return appliedInputResult;
        }

        var result = nativeCombined.SetInteractiveRegions(window, regions);
        appliedInputRegions = regions;
        appliedInputResult = result;
        return result;
    }

    private void OnSourceOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not Window window
            || !entries.TryGetValue(window, out var entry))
        {
            return;
        }

        _ = nativeCombined.SuppressNativeWindow(window);
        UpdateEntry(entry);
        UpdateHost();
    }

    private void OnSourceClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is Window window)
        {
            Remove(window);
        }
    }

    private void OnSourcePositionChanged(
        object? sender,
        PixelPointEventArgs eventArgs)
    {
        if (sender is Window window
            && entries.TryGetValue(window, out var entry))
        {
            UpdateEntry(entry);
            ApplyHostInputRegion();
        }
    }

    private void OnSourcePropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (sender is Window window
            && entries.TryGetValue(window, out var entry))
        {
            UpdateEntry(entry);
        }
    }

    private void OnPresenterLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        if (sender is not ContentControl presenter)
        {
            return;
        }

        var entry = entries.Values.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Presenter, presenter));
        if (entry is null)
        {
            return;
        }

        if (presenter.Bounds.Width > 0
            && presenter.Bounds.Height > 0)
        {
            entry.Placeholder.Width = presenter.Bounds.Width;
            entry.Placeholder.Height = presenter.Bounds.Height;
        }

        UpdateEntry(entry);
        if (interactiveWindows.Contains(entry.Window))
        {
            ApplyHostInputRegion();
        }
    }

    private void OnPresenterPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (sender is not ContentControl presenter
            || host is null
            || !eventArgs.GetCurrentPoint(presenter).Properties
                .IsLeftButtonPressed)
        {
            return;
        }

        var entry = entries.Values.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Presenter, presenter));
        if (entry is null || !interactiveWindows.Contains(entry.Window))
        {
            return;
        }

        StopDrag(releasePointer: true);
        drag = new DragState(
            entry,
            eventArgs.Pointer,
            entry.Window.Position,
            host.PointToScreen(eventArgs.GetPosition(host)));
        eventArgs.Pointer.Capture(presenter);
        eventArgs.Handled = true;
    }

    private void OnPresenterPointerMoved(
        object? sender,
        PointerEventArgs eventArgs)
    {
        var current = drag;
        if (current is null
            || host is null
            || !ReferenceEquals(current.Pointer, eventArgs.Pointer))
        {
            return;
        }

        var pointerPosition = host.PointToScreen(eventArgs.GetPosition(host));
        current.Entry.Window.Position = new PixelPoint(
            current.InitialWindowPosition.X
                + pointerPosition.X
                - current.InitialPointerPosition.X,
            current.InitialWindowPosition.Y
                + pointerPosition.Y
                - current.InitialPointerPosition.Y);
        eventArgs.Handled = true;
    }

    private void OnPresenterPointerReleased(
        object? sender,
        PointerReleasedEventArgs eventArgs)
    {
        if (drag is not null && ReferenceEquals(drag.Pointer, eventArgs.Pointer))
        {
            StopDrag(releasePointer: true);
            eventArgs.Handled = true;
        }
    }

    private void OnPresenterPointerCaptureLost(
        object? sender,
        PointerCaptureLostEventArgs eventArgs)
    {
        StopDrag(releasePointer: false);
    }

    private void StopDrag(bool releasePointer)
    {
        var current = drag;
        drag = null;
        if (releasePointer)
        {
            current?.Pointer.Capture(null);
        }
    }

    private void Remove(Window window)
    {
        if (!entries.Remove(window, out var entry))
        {
            return;
        }

        if (drag?.Entry == entry)
        {
            StopDrag(releasePointer: true);
        }

        interactiveWindows.Remove(window);
        window.PositionChanged -= OnSourcePositionChanged;
        window.PropertyChanged -= OnSourcePropertyChanged;
        window.Opened -= OnSourceOpened;
        window.Closed -= OnSourceClosed;
        entry.Presenter.LayoutUpdated -= OnPresenterLayoutUpdated;
        entry.Presenter.PointerPressed -= OnPresenterPointerPressed;
        entry.Presenter.PointerMoved -= OnPresenterPointerMoved;
        entry.Presenter.PointerReleased -= OnPresenterPointerReleased;
        entry.Presenter.PointerCaptureLost -= OnPresenterPointerCaptureLost;
        host?.Remove(entry.Presenter);
        entry.Presenter.Content = null;
        registry.SetPresentationVisual(window, null);
        UpdateHost();
        ApplyHostInputRegion();
    }

    private static Size GetLogicalSize(Entry entry)
    {
        if (entry.Presenter.Bounds.Width > 0
            && entry.Presenter.Bounds.Height > 0)
        {
            return entry.Presenter.Bounds.Size;
        }

        if (entry.Window.Bounds.Width > 0
            && entry.Window.Bounds.Height > 0)
        {
            return entry.Window.Bounds.Size;
        }

        var definition = OverlayLayoutCatalog.Supported.FirstOrDefault(item =>
            string.Equals(
                item.Name,
                entry.PlotterName,
                StringComparison.Ordinal));
        return definition is null
            ? new Size(1, 1)
            : new Size(
                definition.PreviewSize.Width
                    / Math.Max(0.1, entry.Window.RenderScaling),
                definition.PreviewSize.Height
                    / Math.Max(0.1, entry.Window.RenderScaling));
    }

    private static bool UsesContentWidth(Window window)
    {
        return window.SizeToContent is SizeToContent.Width
            or SizeToContent.WidthAndHeight;
    }

    private static bool UsesContentHeight(Window window)
    {
        return window.SizeToContent is SizeToContent.Height
            or SizeToContent.WidthAndHeight;
    }

    private static double NormalizeLength(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : double.NaN;
    }

    private static double NormalizeMinimum(double value)
    {
        return double.IsFinite(value) && value >= 0 ? value : 0;
    }

    private static double NormalizeMaximum(double value)
    {
        return double.IsFinite(value) && value > 0
            ? value
            : double.PositiveInfinity;
    }

    private static double GetInitialPlaceholderLength(
        double boundsLength,
        double configuredLength)
    {
        if (double.IsFinite(boundsLength) && boundsLength > 0)
        {
            return boundsLength;
        }

        return double.IsFinite(configuredLength) && configuredLength > 0
            ? configuredLength
            : 1;
    }

    private sealed class Entry(
        Window window,
        string plotterName,
        ContentControl presenter,
        Border placeholder)
    {
        public Window Window { get; } = window;

        public string PlotterName { get; } = plotterName;

        public ContentControl Presenter { get; } = presenter;

        public Border Placeholder { get; } = placeholder;

        public CombinedOverlayProjection? Projection { get; set; }
    }

    private sealed record DragState(
        Entry Entry,
        IPointer Pointer,
        PixelPoint InitialWindowPosition,
        PixelPoint InitialPointerPosition);
}
