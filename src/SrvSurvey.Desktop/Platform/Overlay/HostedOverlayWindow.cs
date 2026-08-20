using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal sealed record PassiveOverlayWindowDefinition(
    string PlotterName,
    Func<OverlayPlatformCapabilities, Window> CreateWindow,
    Func<PixelRect, PixelSize, PixelPoint> FallbackPlacement,
    Action<OverlayPreparationResult>? ObservePreparation = null)
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
}

internal enum OverlayHostHealth
{
    Healthy,
    Unsupported,
    PassivePreparationFailed,
    Faulted,
    Disposed,
}

internal enum OverlayHostPhase
{
    Hidden,
    Opening,
    Visible,
    Closing,
    Disposed,
}

internal sealed record OverlayHostDiagnostic(
    string PlotterName,
    OverlayHostPhase Phase,
    OverlayHostHealth Health,
    string Status,
    Exception? Exception = null);

internal sealed record OverlayPresentationSessionDependencies(
    Func<IOverlayPlatformService> CreatePlatform,
    Func<IGameWindowTracker> CreateGameWindowTracker,
    Func<TimeSpan, IHostedOverlayTimer> CreateTimer,
    LegacyOverlayLayout OverlayLayout,
    Action<OverlayHostDiagnostic>? ReportDiagnostic = null);

internal interface IHostedOverlayTimer : IDisposable
{
    event EventHandler? Tick;

    void Start();

    void Stop();
}

internal sealed class DispatcherHostedOverlayTimer : IHostedOverlayTimer
{
    private readonly OverlayDispatcherTimer timer;

    public DispatcherHostedOverlayTimer(TimeSpan interval)
    {
        timer = new OverlayDispatcherTimer
        {
            Interval = interval,
        };
    }

    public event EventHandler? Tick
    {
        add => timer.Tick += value;
        remove => timer.Tick -= value;
    }

    public void Start() => timer.Start();

    public void Stop() => timer.Stop();

    public void Dispose() => timer.Stop();
}

internal sealed class HostedOverlayWindow : IDisposable
{
    private readonly PassiveOverlayWindowDefinition definition;
    private readonly IOverlayPlatformService platform;
    private readonly IGameWindowTracker gameWindowTracker;
    private readonly LegacyOverlayLayout overlayLayout;
    private readonly IHostedOverlayTimer timer;
    private readonly Action<OverlayHostDiagnostic>? reportDiagnostic;
    private readonly Action<HostedOverlayWindow> removeFromSession;
    private readonly object reconciliationGate = new();
    private Window? window;
    private GameWindowSnapshot gameWindow = GameWindowSnapshot.Unavailable;
    private volatile bool wantsWindow;
    private volatile bool disposed;
    private bool reconciliationPosted;
    private bool isReconciling;
    private bool reconcileAgain;
    private bool isVisible;

    public HostedOverlayWindow(
        PassiveOverlayWindowDefinition definition,
        OverlayPresentationSessionDependencies dependencies,
        Action<HostedOverlayWindow> removeFromSession)
    {
        this.definition = Validate(definition);
        ArgumentNullException.ThrowIfNull(dependencies);
        this.removeFromSession = removeFromSession
            ?? throw new ArgumentNullException(nameof(removeFromSession));
        platform = dependencies.CreatePlatform();
        gameWindowTracker = dependencies.CreateGameWindowTracker();
        overlayLayout = dependencies.OverlayLayout;
        reportDiagnostic = dependencies.ReportDiagnostic;
        timer = dependencies.CreateTimer(definition.PollInterval);
        timer.Tick += OnTimerTick;
        timer.Start();
    }

    public event EventHandler? VisibilityChanged;

    public bool IsVisible => isVisible;

    public OverlayHostHealth Health { get; private set; } =
        OverlayHostHealth.Healthy;

    public void Reconcile(bool wantsWindow)
    {
        if (disposed)
        {
            return;
        }

        this.wantsWindow = wantsWindow;
        if (Dispatcher.UIThread.CheckAccess())
        {
            ReconcileOnUiThread();
            return;
        }

        lock (reconciliationGate)
        {
            if (disposed || reconciliationPosted)
            {
                return;
            }

            reconciliationPosted = true;
        }

        Dispatcher.UIThread.Post(RunPostedReconciliation);
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
        CloseWindow();
        timer.Dispose();
        gameWindowTracker.Dispose();
        platform.Dispose();
        Health = OverlayHostHealth.Disposed;
        removeFromSession(this);
    }

    private static PassiveOverlayWindowDefinition Validate(
        PassiveOverlayWindowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.PlotterName);
        ArgumentNullException.ThrowIfNull(definition.CreateWindow);
        ArgumentNullException.ThrowIfNull(definition.FallbackPlacement);
        if (definition.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                "A hosted overlay requires a positive polling interval.");
        }

        _ = OverlayLayoutCatalog.GetRequired(definition.PlotterName);
        return definition;
    }

    private void OnTimerTick(object? sender, EventArgs eventArgs)
    {
        ReconcileOnUiThread();
    }

    private void RunPostedReconciliation()
    {
        lock (reconciliationGate)
        {
            reconciliationPosted = false;
            if (disposed)
            {
                return;
            }
        }

        ReconcileOnUiThread();
    }

    private void ReconcileOnUiThread()
    {
        if (disposed)
        {
            return;
        }

        if (isReconciling)
        {
            reconcileAgain = true;
            return;
        }

        isReconciling = true;
        try
        {
            ReconcileSafely();
            if (reconcileAgain && !disposed)
            {
                reconcileAgain = false;
                ReconcileSafely();
            }
        }
        finally
        {
            isReconciling = false;
            reconcileAgain = false;
        }
    }

    private void ReconcileSafely()
    {
        try
        {
            ReconcileCore();
        }
        catch (Exception exception)
        {
            LatchFault(
                window is null
                    ? OverlayHostPhase.Opening
                    : OverlayHostPhase.Visible,
                exception);
        }
    }

    private void ReconcileCore()
    {
        if (disposed || Health is OverlayHostHealth.PassivePreparationFailed
            or OverlayHostHealth.Faulted)
        {
            return;
        }

        gameWindow = gameWindowTracker.GetSnapshot();
        if (!platform.Capabilities.SupportsPassiveOverlay
            || !platform.Capabilities.SupportsClickThrough
            || !platform.Capabilities.SupportsGameWindowTracking)
        {
            Health = OverlayHostHealth.Unsupported;
            CloseWindow();
            return;
        }

        Health = OverlayHostHealth.Healthy;
        if (!wantsWindow
            || !gameWindow.IsAvailable
            || !gameWindow.IsVisible
            || !gameWindow.IsForeground)
        {
            CloseWindow();
            return;
        }

        if (window is not null)
        {
            PositionWindow(window, gameWindow.ClientBounds);
            return;
        }

        OpenWindow();
    }

    private void OpenWindow()
    {
        var overlay = definition.CreateWindow(platform.Capabilities)
            ?? throw new InvalidOperationException(
                $"The {definition.PlotterName} window factory returned null.");
        OverlayThemeResources.Apply(
            overlay,
            overlayLayout,
            definition.PlotterName);
        overlay.Opened += OnWindowOpened;
        overlay.Closed += OnWindowClosed;
        window = overlay;
        overlay.Show();
    }

    private void OnWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is not Window opened || !ReferenceEquals(window, opened))
        {
            return;
        }

        PositionWindow(opened, gameWindow.ClientBounds);
        var preparation = platform.PreparePassiveWindow(opened);
        definition.ObservePreparation?.Invoke(preparation);
        if (!preparation.IsClickThrough)
        {
            Health = OverlayHostHealth.PassivePreparationFailed;
            reportDiagnostic?.Invoke(new OverlayHostDiagnostic(
                definition.PlotterName,
                OverlayHostPhase.Opening,
                Health,
                preparation.Status));
            CloseWindow();
            return;
        }

        SetVisible(true);
    }

    private void OnWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not Window closed || !ReferenceEquals(window, closed))
        {
            return;
        }

        window = null;
        SetVisible(false);
    }

    private void PositionWindow(Window target, PixelRect gameBounds)
    {
        OverlayThemeResources.ApplyOpacity(
            target,
            overlayLayout,
            definition.PlotterName);
        var screen = target.Screens.ScreenFromBounds(gameBounds)
            ?? target.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var size = OverlayWindowMetrics.PrepareForPlacement(
            target,
            overlayLayout,
            definition.PlotterName,
            screen.Scaling);
        var position = overlayLayout.GetPosition(
                definition.PlotterName,
                gameBounds,
                size)
            ?? definition.FallbackPlacement(gameBounds, size);
        if (target.Position != position)
        {
            target.Position = position;
        }
    }

    private void CloseWindow()
    {
        var closing = window;
        if (closing is null)
        {
            return;
        }

        window = null;
        closing.Opened -= OnWindowOpened;
        closing.Closed -= OnWindowClosed;
        closing.Close();
        SetVisible(false);
    }

    private void SetVisible(bool value)
    {
        if (isVisible == value)
        {
            return;
        }

        isVisible = value;
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LatchFault(
        OverlayHostPhase phase,
        Exception exception)
    {
        Health = OverlayHostHealth.Faulted;
        try
        {
            CloseWindow();
        }
        catch
        {
            // Preserve the first lifecycle fault as the diagnostic cause.
        }

        reportDiagnostic?.Invoke(new OverlayHostDiagnostic(
            definition.PlotterName,
            phase,
            Health,
            exception.Message,
            exception));
    }
}
